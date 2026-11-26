using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Dedup;

namespace APS.WebApp.Services.OperationalAudit.Persistence
{
    /// <inheritdoc cref="IAuditRepository"/>
    public sealed class AuditRepository : IAuditRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<AuditRepository> _logger;

        public AuditRepository(IConfiguration configuration, OperationalAuditOptions options, ILogger<AuditRepository> logger)
        {
            _connectionString = AuditConnectionStringResolver.Resolver(
                configuration, options.RepositoryConnectionStringName, "persistência no schema audit");
            _logger = logger;
        }

        public async Task<long> IniciarAuditRunAsync(
            Guid executionId,
            string triggeredBy,
            IReadOnlyList<string> stagesRequested,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO audit.AuditRun (ExecutionId, StartedAtUtc, Status, TriggeredBy, StagesRequested)
                OUTPUT INSERTED.AuditRunId
                VALUES (@ExecutionId, @StartedAtUtc, @Status, @TriggeredBy, @StagesRequested);";
            command.Parameters.AddWithValue("@ExecutionId", executionId);
            command.Parameters.AddWithValue("@StartedAtUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@Status", OperationalAuditRunStatus.Running.ToString());
            command.Parameters.AddWithValue("@TriggeredBy", (object?)triggeredBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@StagesRequested", stagesRequested.Count == 0 ? DBNull.Value : (object)string.Join(",", stagesRequested));

            var id = await command.ExecuteScalarAsync(cancellationToken);
            long auditRunId = Convert.ToInt64(id);

            _logger.LogInformation("[AUDITORIA][INICIO] AuditRunId={AuditRunId} | ExecutionId={ExecutionId}", auditRunId, executionId);
            return auditRunId;
        }

        public async Task<AuditProjectPersistResult> PersistirProjetoAsync(
            long auditRunId,
            OperationalAuditProjectData dados,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                long auditProjectRunId = await InserirProjectRunAsync(connection, transaction, auditRunId, dados, cancellationToken);

                foreach (var stage in dados.StageResults)
                {
                    await InserirStageResultAsync(connection, transaction, auditProjectRunId, stage, cancellationToken);
                }

                var requerimentosPorCodigo = new HashSet<(string Etapa, string Codigo)>();
                foreach (var requisito in dados.Requirements)
                {
                    long catalogId = await ObterOuCriarRequirementCatalogAsync(connection, transaction, requisito.Etapa, requisito.RequirementCode, bloqueiaSeFalho: false, cancellationToken);
                    await InserirRequirementResultAsync(connection, transaction, auditProjectRunId, catalogId, requisito, cancellationToken);
                    requerimentosPorCodigo.Add((requisito.Etapa, requisito.RequirementCode));
                }

                var dedupHashesEncontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var finding in dados.Findings)
                {
                    string dedupHash = finding.DedupHash
                        ?? throw new InvalidOperationException($"[OperationalAudit] Finding sem DedupHash calculado (RegraId={finding.RegraId}) — o orquestrador precisa calcular antes de persistir.");
                    dedupHashesEncontrados.Add(dedupHash);

                    await UpsertIssueEOcorrenciaAsync(connection, transaction, auditRunId, finding, cancellationToken);
                }

                var codigosAprovadosNestaExecucao = dados.Requirements
                    .Where(r => r.Status == OperationalRequirementStatus.Approved)
                    .Select(r => r.RequirementCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                await ResolverIssuesAusentesAsync(connection, transaction, dados.ProjectIdAcc, dedupHashesEncontrados, codigosAprovadosNestaExecucao, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "[AUDITORIA][RECONCILIACAO] ProjectIdAcc={ProjectIdAcc} | Findings={Findings} | Requisitos={Requisitos} | Etapas={Etapas}",
                    dados.ProjectIdAcc, dados.Findings.Count, dados.Requirements.Count, dados.StageResults.Count);

                return AuditProjectPersistResult.Ok(auditProjectRunId);
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "[AUDITORIA][ERRO] Falha ao reverter transação do projeto {ProjectIdAcc} — verificar estado manualmente.", dados.ProjectIdAcc);
                }

                _logger.LogError(ex, "[AUDITORIA][ERRO] Falha ao persistir achados do projeto {ProjectIdAcc} — rollback aplicado, dados operacionais não afetados.", dados.ProjectIdAcc);
                return AuditProjectPersistResult.Falha(ex.Message);
            }
        }

        public async Task FinalizarAuditRunAsync(
            long auditRunId,
            OperationalAuditRunStatus status,
            int totalProjetos,
            int totalFindings,
            int totalRequisitos,
            string? erro,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE audit.AuditRun
                SET FinishedAtUtc = @FinishedAtUtc,
                    Status = @Status,
                    TotalProjects = @TotalProjects,
                    TotalFindings = @TotalFindings,
                    TotalRequirementsEvaluated = @TotalRequirementsEvaluated,
                    ErrorMessage = @ErrorMessage
                WHERE AuditRunId = @AuditRunId;";
            command.Parameters.AddWithValue("@FinishedAtUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@Status", status.ToString());
            command.Parameters.AddWithValue("@TotalProjects", totalProjetos);
            command.Parameters.AddWithValue("@TotalFindings", totalFindings);
            command.Parameters.AddWithValue("@TotalRequirementsEvaluated", totalRequisitos);
            command.Parameters.AddWithValue("@ErrorMessage", (object?)erro ?? DBNull.Value);
            command.Parameters.AddWithValue("@AuditRunId", auditRunId);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("[AUDITORIA][FIM] AuditRunId={AuditRunId} | Status={Status}", auditRunId, status);
        }

        // -----------------------------------------------------------------------------------------------
        // Helpers privados — cada um assume que já está DENTRO da transação do projeto (exceto os dois
        // métodos públicos que abrem/fecham conexão própria: IniciarAuditRunAsync e FinalizarAuditRunAsync,
        // que não tocam achados, só o cabeçalho da execução).
        // -----------------------------------------------------------------------------------------------

        private static async Task<long> InserirProjectRunAsync(
            SqlConnection connection, SqlTransaction transaction, long auditRunId, OperationalAuditProjectData dados, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO audit.AuditProjectRun (AuditRunId, ProjectIdAcc, AccountIdAcc, ProjectName, StartedAtUtc, FinishedAtUtc, Status, PersistenceStatus)
                OUTPUT INSERTED.AuditProjectRunId
                VALUES (@AuditRunId, @ProjectIdAcc, @AccountIdAcc, @ProjectName, @StartedAtUtc, @FinishedAtUtc, @Status, @PersistenceStatus);";
            command.Parameters.AddWithValue("@AuditRunId", auditRunId);
            command.Parameters.AddWithValue("@ProjectIdAcc", dados.ProjectIdAcc);
            command.Parameters.AddWithValue("@AccountIdAcc", (object?)dados.AccountIdAcc ?? DBNull.Value);
            command.Parameters.AddWithValue("@ProjectName", (object?)dados.ProjectName ?? DBNull.Value);
            command.Parameters.AddWithValue("@StartedAtUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@FinishedAtUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@Status", "COMPLETED");
            command.Parameters.AddWithValue("@PersistenceStatus", OperationalAuditProjectPersistenceStatus.Ok.ToString());

            var id = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt64(id);
        }

        private static async Task InserirStageResultAsync(
            SqlConnection connection, SqlTransaction transaction, long auditProjectRunId, OperationalStageResult stage, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO audit.AuditStageResult
                    (AuditProjectRunId, StageNumber, StageName, Result, TotalRequirements, Approved, Failed, Warnings,
                     OperationalErrors, AutomationErrors, Inconclusive, Blockers, CompliancePercent, StartedAtUtc, FinishedAtUtc)
                VALUES
                    (@AuditProjectRunId, @StageNumber, @StageName, @Result, @TotalRequirements, @Approved, @Failed, @Warnings,
                     @OperationalErrors, @AutomationErrors, @Inconclusive, @Blockers, @CompliancePercent, @StartedAtUtc, @FinishedAtUtc);";
            command.Parameters.AddWithValue("@AuditProjectRunId", auditProjectRunId);
            command.Parameters.AddWithValue("@StageNumber", stage.Etapa);
            command.Parameters.AddWithValue("@StageName", stage.NomeEtapa);
            command.Parameters.AddWithValue("@Result", stage.Resultado.ToString());
            command.Parameters.AddWithValue("@TotalRequirements", stage.TotalRequisitos);
            command.Parameters.AddWithValue("@Approved", stage.Aprovados);
            command.Parameters.AddWithValue("@Failed", stage.Falhos);
            command.Parameters.AddWithValue("@Warnings", stage.Avisos);
            command.Parameters.AddWithValue("@OperationalErrors", stage.ErrosOperacionais);
            command.Parameters.AddWithValue("@AutomationErrors", stage.ErrosAutomacao);
            command.Parameters.AddWithValue("@Inconclusive", stage.Inconclusivos);
            command.Parameters.AddWithValue("@Blockers", stage.Bloqueadores);
            command.Parameters.AddWithValue("@CompliancePercent", (object?)stage.PercentualConformidade ?? DBNull.Value);
            command.Parameters.AddWithValue("@StartedAtUtc", stage.IniciadoEmUtc);
            command.Parameters.AddWithValue("@FinishedAtUtc", (object?)stage.FinalizadoEmUtc ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Get-or-create determinístico da chave natural (StageNumber, RequirementCode) — a mesma chave da
        /// constraint única <c>UQ_AuditRequirementCatalog_Stage_Code</c> (seção 2.4 do schema). Duas camadas
        /// de defesa contra a corrida SELECT-não-encontrou-INSERT entre transações concorrentes de projetos
        /// diferentes: (1) o SELECT usa <c>WITH (UPDLOCK, HOLDLOCK)</c> na própria chave natural, serializando
        /// quem mais tentar o mesmo SELECT dentro da mesma janela; (2) mesmo assim, se o INSERT ainda colidir
        /// (ex.: outra transação não passou pelo mesmo SELECT protegido), a violação de chave única
        /// (SqlException 2601/2627) NUNCA aborta o projeto — recupera o Id da linha que a transação
        /// concorrente de fato criou e continua normalmente.
        /// </summary>
        private static async Task<long> ObterOuCriarRequirementCatalogAsync(
            SqlConnection connection, SqlTransaction transaction, string etapa, string requirementCode, bool bloqueiaSeFalho, CancellationToken ct)
        {
            long? existente = await SelecionarRequirementCatalogIdAsync(connection, transaction, etapa, requirementCode, ct);
            if (existente.HasValue) return existente.Value;

            try
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO audit.AuditRequirementCatalog (StageNumber, RequirementCode, Description, IsBlocking)
                    OUTPUT INSERTED.AuditRequirementCatalogId
                    VALUES (@StageNumber, @RequirementCode, @Description, @IsBlocking);";
                insert.Parameters.AddWithValue("@StageNumber", etapa);
                insert.Parameters.AddWithValue("@RequirementCode", requirementCode);
                insert.Parameters.AddWithValue("@Description", requirementCode);
                insert.Parameters.AddWithValue("@IsBlocking", bloqueiaSeFalho);
                var novoId = await insert.ExecuteScalarAsync(ct);
                return Convert.ToInt64(novoId);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                long? existenteAposCorrida = await SelecionarRequirementCatalogIdAsync(connection, transaction, etapa, requirementCode, ct);
                if (existenteAposCorrida.HasValue) return existenteAposCorrida.Value;

                throw new InvalidOperationException(
                    $"[OperationalAudit] Violação de chave única ao criar AuditRequirementCatalog (Etapa={etapa}, RequirementCode={requirementCode}), " +
                    "mas o SELECT de recuperação pós-corrida não encontrou a linha — estado inesperado, investigar manualmente.", ex);
            }
        }

        private static async Task<long?> SelecionarRequirementCatalogIdAsync(
            SqlConnection connection, SqlTransaction transaction, string etapa, string requirementCode, CancellationToken ct)
        {
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = @"
                SELECT AuditRequirementCatalogId
                FROM audit.AuditRequirementCatalog WITH (UPDLOCK, HOLDLOCK)
                WHERE StageNumber = @StageNumber AND RequirementCode = @RequirementCode;";
            select.Parameters.AddWithValue("@StageNumber", etapa);
            select.Parameters.AddWithValue("@RequirementCode", requirementCode);
            var existente = await select.ExecuteScalarAsync(ct);
            return existente == null ? (long?)null : Convert.ToInt64(existente);
        }

        private static async Task InserirRequirementResultAsync(
            SqlConnection connection, SqlTransaction transaction, long auditProjectRunId, long catalogId, OperationalRequirementResult requisito, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO audit.AuditRequirementResult
                    (AuditProjectRunId, AuditRequirementCatalogId, StageNumber, RequirementCode, ObjectEvaluated, Status, ValueExpected, ValueFound, EvaluatedAtUtc)
                VALUES
                    (@AuditProjectRunId, @AuditRequirementCatalogId, @StageNumber, @RequirementCode, @ObjectEvaluated, @Status, @ValueExpected, @ValueFound, @EvaluatedAtUtc);";
            command.Parameters.AddWithValue("@AuditProjectRunId", auditProjectRunId);
            command.Parameters.AddWithValue("@AuditRequirementCatalogId", catalogId);
            command.Parameters.AddWithValue("@StageNumber", requisito.Etapa);
            command.Parameters.AddWithValue("@RequirementCode", requisito.RequirementCode);
            command.Parameters.AddWithValue("@ObjectEvaluated", (object?)requisito.ObjetoAvaliado ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", requisito.Status.ToString());
            command.Parameters.AddWithValue("@ValueExpected", (object?)requisito.ValorEsperado ?? DBNull.Value);
            command.Parameters.AddWithValue("@ValueFound", (object?)requisito.ValorEncontrado ?? DBNull.Value);
            command.Parameters.AddWithValue("@EvaluatedAtUtc", requisito.DataHoraUtc == default ? DateTime.UtcNow : requisito.DataHoraUtc);

            await command.ExecuteNonQueryAsync(ct);
        }

        private static async Task UpsertIssueEOcorrenciaAsync(
            SqlConnection connection, SqlTransaction transaction, long auditRunId, OperationalFinding finding, CancellationToken ct)
        {
            var agora = DateTime.UtcNow;
            AuditIssueSnapshot? existente = null;
            long? auditIssueId = null;

            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = @"
                    SELECT AuditIssueId, CurrentStatus, FirstSeenAtUtc, LastSeenAtUtc, OccurrenceCount
                    FROM audit.AuditIssue WITH (UPDLOCK, HOLDLOCK)
                    WHERE ProjectIdAcc = @ProjectIdAcc AND DedupHash = @DedupHash;";
                select.Parameters.AddWithValue("@ProjectIdAcc", finding.ProjectIdAcc);
                select.Parameters.AddWithValue("@DedupHash", finding.DedupHash);

                await using var reader = await select.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    auditIssueId = reader.GetInt64(0);
                    existente = new AuditIssueSnapshot
                    {
                        Status = Enum.Parse<OperationalIssueStatus>(reader.GetString(1)),
                        FirstSeenAtUtc = reader.GetDateTime(2),
                        LastSeenAtUtc = reader.GetDateTime(3),
                        OccurrenceCount = reader.GetInt32(4)
                    };
                }
            }

            var decisao = AuditIssueDedupEngine.DecidirParaOcorrenciaEncontrada(existente, agora);

            if (decisao.EhNovo)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO audit.AuditIssue
                        (ProjectIdAcc, DedupHash, StageNumber, ProblemCode, ErrorDomain, Classification, Severity, Title, Description,
                         ProbableOwner, DocumentNA, DocumentClient, FileName, FoundFolder, FoundWorksheet, ExcelRow, FieldName,
                         CustomAttributeName, CustomAttributeIdAcc, CustomAttributeState, IsCustomAttributeHidden, ExpectedFolder, ConfigurationKey,
                         BlocksStage, AutomationCanFix, CurrentStatus, FirstSeenAtUtc, LastSeenAtUtc, OccurrenceCount)
                    OUTPUT INSERTED.AuditIssueId
                    VALUES
                        (@ProjectIdAcc, @DedupHash, @StageNumber, @ProblemCode, @ErrorDomain, @Classification, @Severity, @Title, @Description,
                         @ProbableOwner, @DocumentNA, @DocumentClient, @FileName, @FoundFolder, @FoundWorksheet, @ExcelRow, @FieldName,
                         @CustomAttributeName, @CustomAttributeIdAcc, @CustomAttributeState, @IsCustomAttributeHidden, @ExpectedFolder, @ConfigurationKey,
                         @BlocksStage, @AutomationCanFix, @CurrentStatus, @FirstSeenAtUtc, @LastSeenAtUtc, @OccurrenceCount);";
                AdicionarParametrosIssue(insert, finding, decisao);
                var novoId = await insert.ExecuteScalarAsync(ct);
                auditIssueId = Convert.ToInt64(novoId);
            }
            else
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"
                    UPDATE audit.AuditIssue
                    SET CurrentStatus = @CurrentStatus, LastSeenAtUtc = @LastSeenAtUtc, OccurrenceCount = @OccurrenceCount,
                        Title = @Title, Description = @Description, Severity = @Severity, ErrorDomain = @ErrorDomain, Classification = @Classification
                    WHERE AuditIssueId = @AuditIssueId;";
                update.Parameters.AddWithValue("@CurrentStatus", decisao.NovoStatus.ToString());
                update.Parameters.AddWithValue("@LastSeenAtUtc", decisao.LastSeenAtUtc);
                update.Parameters.AddWithValue("@OccurrenceCount", decisao.OccurrenceCount);
                update.Parameters.AddWithValue("@Title", finding.Titulo);
                update.Parameters.AddWithValue("@Description", (object?)finding.Descricao ?? DBNull.Value);
                update.Parameters.AddWithValue("@Severity", finding.Severidade.ToString());
                update.Parameters.AddWithValue("@ErrorDomain", finding.ErrorDomain.ToString());
                update.Parameters.AddWithValue("@Classification", finding.Classificacao.ToString());
                update.Parameters.AddWithValue("@AuditIssueId", auditIssueId!.Value);
                await update.ExecuteNonQueryAsync(ct);
            }

            finding.Status = decisao.NovoStatus;
            finding.PrimeiraOcorrencia = decisao.FirstSeenAtUtc;
            finding.UltimaOcorrencia = decisao.LastSeenAtUtc;
            finding.QuantidadeOcorrencias = decisao.OccurrenceCount;

            // Seção 9, passo 5: impedir ocorrência duplicada usando OccurrenceHash — a constraint única
            // (AuditIssueId, OccurrenceHash) é a garantia definitiva; a violação é tratada como "já
            // registrada nesta execução", nunca como falha.
            try
            {
                await using var insertOcorrencia = connection.CreateCommand();
                insertOcorrencia.Transaction = transaction;
                insertOcorrencia.CommandText = @"
                    INSERT INTO audit.AuditOccurrence
                        (AuditIssueId, AuditRunId, OccurrenceHash, DetectedAtUtc, EvidenceSource, SourceLogFile, LogLineStart, LogLineEnd, ValueExpected, ValueFound, Evidence)
                    VALUES
                        (@AuditIssueId, @AuditRunId, @OccurrenceHash, @DetectedAtUtc, @EvidenceSource, @SourceLogFile, @LogLineStart, @LogLineEnd, @ValueExpected, @ValueFound, @Evidence);";
                insertOcorrencia.Parameters.AddWithValue("@AuditIssueId", auditIssueId!.Value);
                insertOcorrencia.Parameters.AddWithValue("@AuditRunId", auditRunId);
                insertOcorrencia.Parameters.AddWithValue("@OccurrenceHash", (object?)finding.OccurrenceHash ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@DetectedAtUtc", agora);
                insertOcorrencia.Parameters.AddWithValue("@EvidenceSource", (object?)finding.FonteEvidencia ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@SourceLogFile", (object?)finding.ArquivoLog ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@LogLineStart", (object?)finding.LinhaLogInicial ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@LogLineEnd", (object?)finding.LinhaLogFinal ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@ValueExpected", (object?)finding.ValorEsperado ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@ValueFound", (object?)finding.ValorEncontrado ?? DBNull.Value);
                insertOcorrencia.Parameters.AddWithValue("@Evidence", (object?)finding.Evidencia ?? DBNull.Value);
                await insertOcorrencia.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Violação da constraint única (AuditIssueId, OccurrenceHash) — mesma evidência já
                // registrada nesta execução; intencionalmente ignorado, não é uma falha.
            }
        }

        private static void AdicionarParametrosIssue(SqlCommand command, OperationalFinding finding, AuditIssueDecision decisao)
        {
            command.Parameters.AddWithValue("@ProjectIdAcc", finding.ProjectIdAcc);
            command.Parameters.AddWithValue("@DedupHash", finding.DedupHash);
            command.Parameters.AddWithValue("@StageNumber", finding.Etapa);
            command.Parameters.AddWithValue("@ProblemCode", finding.CodigoProblema);
            command.Parameters.AddWithValue("@ErrorDomain", finding.ErrorDomain.ToString());
            command.Parameters.AddWithValue("@Classification", finding.Classificacao.ToString());
            command.Parameters.AddWithValue("@Severity", finding.Severidade.ToString());
            command.Parameters.AddWithValue("@Title", finding.Titulo);
            command.Parameters.AddWithValue("@Description", (object?)finding.Descricao ?? DBNull.Value);
            command.Parameters.AddWithValue("@ProbableOwner", (object?)finding.ResponsavelProvavel ?? DBNull.Value);
            command.Parameters.AddWithValue("@DocumentNA", (object?)finding.DocumentoNa ?? DBNull.Value);
            command.Parameters.AddWithValue("@DocumentClient", (object?)finding.DocumentoCliente ?? DBNull.Value);
            command.Parameters.AddWithValue("@FileName", (object?)finding.NomeArquivo ?? DBNull.Value);
            command.Parameters.AddWithValue("@FoundFolder", (object?)finding.PastaEncontrada ?? DBNull.Value);
            command.Parameters.AddWithValue("@FoundWorksheet", (object?)finding.AbaEncontrada ?? DBNull.Value);
            command.Parameters.AddWithValue("@ExcelRow", (object?)finding.Linha ?? DBNull.Value);
            command.Parameters.AddWithValue("@FieldName", (object?)finding.Campo ?? DBNull.Value);
            command.Parameters.AddWithValue("@CustomAttributeName", (object?)finding.CustomAttributeName ?? DBNull.Value);
            command.Parameters.AddWithValue("@CustomAttributeIdAcc", (object?)finding.CustomAttributeIdAcc ?? DBNull.Value);
            command.Parameters.AddWithValue("@CustomAttributeState", finding.CustomAttributeState.HasValue ? finding.CustomAttributeState.Value.ToString() : (object)DBNull.Value);
            command.Parameters.AddWithValue("@IsCustomAttributeHidden", (object?)finding.IsCustomAttributeHidden ?? DBNull.Value);
            command.Parameters.AddWithValue("@ExpectedFolder", (object?)finding.PastaEsperada ?? DBNull.Value);
            command.Parameters.AddWithValue("@ConfigurationKey", (object?)finding.ConfigurationKey ?? DBNull.Value);
            command.Parameters.AddWithValue("@BlocksStage", finding.BloqueiaEtapa);
            command.Parameters.AddWithValue("@AutomationCanFix", finding.AutomacaoPodeCorrigir);
            command.Parameters.AddWithValue("@CurrentStatus", decisao.NovoStatus.ToString());
            command.Parameters.AddWithValue("@FirstSeenAtUtc", decisao.FirstSeenAtUtc);
            command.Parameters.AddWithValue("@LastSeenAtUtc", decisao.LastSeenAtUtc);
            command.Parameters.AddWithValue("@OccurrenceCount", decisao.OccurrenceCount);
        }

        private static async Task ResolverIssuesAusentesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string projectIdAcc,
            HashSet<string> dedupHashesEncontrados,
            HashSet<string> codigosAprovadosNestaExecucao,
            CancellationToken ct)
        {
            var abertos = new List<(long Id, string ProblemCode, string DedupHash, OperationalIssueStatus Status)>();

            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = @"
                    SELECT AuditIssueId, ProblemCode, DedupHash, CurrentStatus
                    FROM audit.AuditIssue
                    WHERE ProjectIdAcc = @ProjectIdAcc AND CurrentStatus IN ('Aberto', 'Regressao', 'PossivelmenteResolvido');";
                select.Parameters.AddWithValue("@ProjectIdAcc", projectIdAcc);

                await using var reader = await select.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    abertos.Add((
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        Enum.Parse<OperationalIssueStatus>(reader.GetString(3))));
                }
            }

            foreach (var issue in abertos)
            {
                if (dedupHashesEncontrados.Contains(issue.DedupHash)) continue; // encontrado nesta execução — já tratado em UpsertIssueEOcorrenciaAsync.

                bool requisitoPositivoComprovado = codigosAprovadosNestaExecucao.Contains(issue.ProblemCode);
                var novoStatus = AuditIssueDedupEngine.DecidirParaAusenciaNestaExecucao(issue.Status, requisitoPositivoComprovado);
                if (novoStatus == issue.Status) continue;

                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE audit.AuditIssue SET CurrentStatus = @CurrentStatus WHERE AuditIssueId = @AuditIssueId;";
                update.Parameters.AddWithValue("@CurrentStatus", novoStatus.ToString());
                update.Parameters.AddWithValue("@AuditIssueId", issue.Id);
                await update.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
