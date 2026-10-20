using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

public sealed record RedistributionDestination(
    string DocumentCode,
    string? Title,
    decimal PlannedHours,
    decimal Weight,
    decimal Hours,
    decimal Cost);

public sealed record RedistributionPreview(
    string ProjectId,
    string SourceDocumentCode,
    string? Discipline,
    decimal SourceHours,
    decimal SourceCost,
    decimal TotalDestinationPlannedHours,
    bool CanConfirm,
    string? BlockingReason,
    IReadOnlyList<RedistributionDestination> Destinations)
{
    public string? SourceActivityCode { get; init; }
    public bool ActivityMappingRequired { get; init; }
    public string? DisciplineResolution { get; init; }
}

public sealed record RedistributionConfirmRequest(string ProjectId, string DocumentCode);

public sealed record RedistributionBatchRequest(string ProjectId, IReadOnlyList<string> DocumentCodes);

public sealed record RedistributionBatchPreviewItem(
    string DocumentCode,
    RedistributionPreview? Preview,
    string? Error);

public sealed record RedistributionBatchConfirmItem(
    string DocumentCode,
    bool Success,
    RedistributionDecision? Decision,
    string? Error);

public sealed record ActivityDisciplineMappingRequest(
    string ProjectId,
    string ActivityCode,
    string Discipline);

public sealed record ActivityDisciplineMapping(
    string ProjectId,
    string ActivityCode,
    string Type,
    string Discipline,
    string UpdatedBy,
    DateTime UpdatedAtUtc);

public sealed record RedistributionDecision(
    Guid Id,
    string ProjectId,
    string SourceDocumentCode,
    string Discipline,
    decimal SourceHours,
    decimal SourceCost,
    string Status,
    string ConfirmedBy,
    DateTime ConfirmedAtUtc,
    IReadOnlyList<RedistributionDestination> Items);

internal sealed record RedistributionTargetSource(
    string DocumentCode,
    string? Title,
    decimal PlannedHours);

public sealed class InformationQualityRedistributionService(IConfiguration configuration)
{
    private const decimal Tolerance = 0.000001m;

    public async Task<RedistributionPreview> PreviewAsync(
        string projectId,
        string documentCode,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);
        return await BuildPreviewAsync(connection, null, NormalizeProject(projectId), NormalizeDocument(documentCode), ct);
    }

    public async Task<IReadOnlyList<RedistributionBatchPreviewItem>> PreviewBatchAsync(
        string projectId,
        IEnumerable<string> documentCodes,
        CancellationToken ct)
    {
        var normalizedProject = NormalizeProject(projectId);
        var codes = NormalizeDocuments(documentCodes);
        var result = new List<RedistributionBatchPreviewItem>(codes.Count);

        foreach (var code in codes)
        {
            try
            {
                var preview = await PreviewAsync(normalizedProject, code, ct);
                result.Add(new(code, preview, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Add(new(code, null, ex.Message));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<RedistributionBatchConfirmItem>> ConfirmBatchAsync(
        string projectId,
        IEnumerable<string> documentCodes,
        string confirmedBy,
        CancellationToken ct)
    {
        var normalizedProject = NormalizeProject(projectId);
        var codes = NormalizeDocuments(documentCodes);
        var result = new List<RedistributionBatchConfirmItem>(codes.Count);

        foreach (var code in codes)
        {
            try
            {
                var decision = await ConfirmAsync(normalizedProject, code, confirmedBy, ct);
                result.Add(new(code, true, decision, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Add(new(code, false, null, ex.Message));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<string>> ActivityDisciplineOptionsAsync(
        string projectId,
        CancellationToken ct)
    {
        var normalizedProject = NormalizeProject(projectId);
        const string sql = """
            ;WITH NormaProjeto AS
            (
                SELECT TOP (1) NormaId
                FROM coordenacao.ProjetoNorma
                WHERE Project_ID_ACC = @projectId
                ORDER BY Data_Atualizacao DESC
            )
            SELECT DISTINCT LTRIM(RTRIM(nm.Disciplina)) AS Disciplina
            FROM coordenacao.NormaMapeamento nm
            CROSS JOIN NormaProjeto np
            WHERE nm.NormaId = np.NormaId
              AND NULLIF(LTRIM(RTRIM(nm.Disciplina)), N'') IS NOT NULL
            ORDER BY Disciplina;
            """;

        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = normalizedProject;

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<ActivityDisciplineMapping> SaveActivityDisciplineMappingAsync(
        string projectId,
        string activityCode,
        string discipline,
        string actor,
        CancellationToken ct)
    {
        var normalizedProject = NormalizeProject(projectId);
        var normalizedActivity = NormalizeDocument(activityCode);
        var normalizedDiscipline = (discipline ?? string.Empty).Trim();
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "usuario-autenticado" : actor.Trim();

        if (string.IsNullOrWhiteSpace(normalizedActivity))
            throw new InvalidOperationException("A atividade é obrigatória.");
        if (string.IsNullOrWhiteSpace(normalizedDiscipline))
            throw new InvalidOperationException("A disciplina é obrigatória.");

        const string sql = """
            IF NOT EXISTS
            (
                SELECT 1
                FROM coordenacao.NormaMapeamento nm
                WHERE nm.NormaId =
                (
                    SELECT TOP (1) pn.NormaId
                    FROM coordenacao.ProjetoNorma pn
                    WHERE pn.Project_ID_ACC = @projectId
                    ORDER BY pn.Data_Atualizacao DESC
                )
                  AND UPPER(LTRIM(RTRIM(nm.Disciplina))) = UPPER(LTRIM(RTRIM(@discipline)))
            )
            BEGIN
                THROW 50001, N'A disciplina escolhida não existe na Norma de Coordenação atual do projeto.', 1;
            END;

            UPDATE coordenacao.AtividadeDisciplinaProjeto
               SET Tipo = N'AT',
                   Disciplina = @discipline,
                   Ativo = 1,
                   AlteradoPor = @actor,
                   AlteradoEm = SYSUTCDATETIME()
             WHERE Project_ID_ACC = @projectId
               AND UPPER(LTRIM(RTRIM(Atividade))) = @activity;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO coordenacao.AtividadeDisciplinaProjeto
                    (Project_ID_ACC, Atividade, Tipo, Disciplina, Ativo, AlteradoPor, AlteradoEm)
                VALUES
                    (@projectId, @activity, N'AT', @discipline, 1, @actor, SYSUTCDATETIME());
            END;

            SELECT TOP (1)
                Project_ID_ACC, Atividade, Tipo, Disciplina, AlteradoPor, AlteradoEm
            FROM coordenacao.AtividadeDisciplinaProjeto
            WHERE Project_ID_ACC = @projectId
              AND UPPER(LTRIM(RTRIM(Atividade))) = @activity
              AND Ativo = 1;
            """;

        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = normalizedProject;
        command.Parameters.Add("@activity", SqlDbType.NVarChar, 255).Value = normalizedActivity;
        command.Parameters.Add("@discipline", SqlDbType.NVarChar, 255).Value = normalizedDiscipline;
        command.Parameters.Add("@actor", SqlDbType.NVarChar, 255).Value = normalizedActor;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException("Não foi possível gravar a associação AT → disciplina.");
        }

        var mapping = new ActivityDisciplineMapping(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));

        await reader.DisposeAsync();
        await transaction.CommitAsync(ct);
        return mapping;
    }

    public async Task<IReadOnlyList<RedistributionDecision>> ListAsync(
        string projectId,
        CancellationToken ct)
    {
        var normalizedProject = NormalizeProject(projectId);
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT
                h.Id,
                h.Project_ID_ACC,
                h.Documento_Origem,
                h.Disciplina,
                h.HH_Origem,
                h.Custo_Origem,
                h.Status,
                h.ConfirmadoPor,
                h.ConfirmadoEm,
                i.Documento_Destino,
                i.Titulo_Destino,
                i.Planned_HH_Destino,
                i.Peso,
                i.HH_Redistribuida,
                i.Custo_Redistribuido
            FROM gestao.AuditoriaRedistribuicaoDocumento h
            LEFT JOIN gestao.AuditoriaRedistribuicaoItem i
                ON i.Redistribuicao_Id = h.Id
            WHERE h.Project_ID_ACC = @projectId
              AND h.Ativo = 1
            ORDER BY h.ConfirmadoEm DESC, h.Documento_Origem, i.Documento_Destino;
            """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = normalizedProject;

        var headers = new Dictionary<Guid, (string ProjectId, string Source, string Discipline, decimal Hours, decimal Cost, string Status, string User, DateTime At, List<RedistributionDestination> Items)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!headers.TryGetValue(id, out var entry))
            {
                entry = (
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc),
                    []);
                headers[id] = entry;
            }

            if (!reader.IsDBNull(9))
            {
                entry.Items.Add(new(
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.GetDecimal(14)));
            }
        }

        return headers
            .Select(x => new RedistributionDecision(
                x.Key,
                x.Value.ProjectId,
                x.Value.Source,
                x.Value.Discipline,
                x.Value.Hours,
                x.Value.Cost,
                x.Value.Status,
                x.Value.User,
                x.Value.At,
                x.Value.Items))
            .OrderByDescending(x => x.ConfirmedAtUtc)
            .ThenBy(x => x.SourceDocumentCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RedistributionDecision> ConfirmAsync(
        string projectId,
        string documentCode,
        string confirmedBy,
        CancellationToken ct)
    {
        var normalizedProject = NormalizeProject(projectId);
        var normalizedDocument = NormalizeDocument(documentCode);
        var actor = string.IsNullOrWhiteSpace(confirmedBy) ? "usuario-autenticado" : confirmedBy.Trim();

        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            const string requiredSetOptions = """
                SET ANSI_NULLS ON;
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_PADDING ON;
                SET ANSI_WARNINGS ON;
                SET ARITHABORT ON;
                SET CONCAT_NULL_YIELDS_NULL ON;
                SET NUMERIC_ROUNDABORT OFF;
                """;

            await using (var setOptions = new SqlCommand(requiredSetOptions, connection, transaction))
                await setOptions.ExecuteNonQueryAsync(ct);

            var existing = await LoadActiveDecisionAsync(connection, transaction, normalizedProject, normalizedDocument, ct);
            if (existing is not null)
            {
                await transaction.CommitAsync(ct);
                return existing;
            }

            var preview = await BuildPreviewAsync(connection, transaction, normalizedProject, normalizedDocument, ct);
            if (!preview.CanConfirm)
                throw new InvalidOperationException(preview.BlockingReason ?? "Redistribuição não pode ser confirmada.");

            var isActivityAssignment = string.Equals(
                preview.DisciplineResolution,
                "AT_MANUAL",
                StringComparison.OrdinalIgnoreCase);

            if (!isActivityAssignment && preview.Destinations.Count == 0)
                throw new InvalidOperationException("Nenhum documento de destino elegível.");

            var id = Guid.NewGuid();

            const string insertHeader = """
                INSERT INTO gestao.AuditoriaRedistribuicaoDocumento
                (
                    Id, Project_ID_ACC, Documento_Origem, Disciplina,
                    HH_Origem, Custo_Origem, Status, Status_Documento, ConfirmadoPor,
                    ConfirmadoEm, Ativo, Motivo
                )
                VALUES
                (
                    @id, @projectId, @source, @discipline,
                    @hours, @cost, N'CONFIRMADO', @statusDocumento, @user,
                    SYSUTCDATETIME(), 1, @motivo
                );
                """;

            await using (var header = new SqlCommand(insertHeader, connection, transaction) { CommandTimeout = 30 })
            {
                header.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                header.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = normalizedProject;
                header.Parameters.Add("@source", SqlDbType.NVarChar, 255).Value = normalizedDocument;
                header.Parameters.Add("@discipline", SqlDbType.NVarChar, 255).Value = preview.Discipline!;
                header.Parameters.Add("@hours", SqlDbType.Decimal).ConfigureDecimal(18, 6).Value = preview.SourceHours;
                header.Parameters.Add("@cost", SqlDbType.Decimal).ConfigureDecimal(18, 6).Value = preview.SourceCost;
                header.Parameters.Add("@statusDocumento", SqlDbType.NVarChar, 30).Value =
                    isActivityAssignment ? "AT" : "CANCELADO";
                header.Parameters.Add("@motivo", SqlDbType.NVarChar, 500).Value =
                    isActivityAssignment
                        ? $"Atividade {preview.SourceActivityCode ?? "AT"} classificada manualmente; HH e custo permanecem no projeto e são atribuídos à disciplina {preview.Discipline}, sem documento de destino."
                        : "Documento cancelado; HH e custo redistribuídos entre documentos ativos da mesma disciplina.";
                header.Parameters.Add("@user", SqlDbType.NVarChar, 255).Value = actor;
                await header.ExecuteNonQueryAsync(ct);
            }

            const string insertItem = """
                INSERT INTO gestao.AuditoriaRedistribuicaoItem
                (
                    Redistribuicao_Id, Documento_Destino, Titulo_Destino,
                    Planned_HH_Destino, Peso, HH_Redistribuida, Custo_Redistribuido
                )
                VALUES
                (
                    @id, @destination, @title,
                    @planned, @weight, @hours, @cost
                );
                """;

            foreach (var item in preview.Destinations)
            {
                await using var command = new SqlCommand(insertItem, connection, transaction) { CommandTimeout = 30 };
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                command.Parameters.Add("@destination", SqlDbType.NVarChar, 255).Value = item.DocumentCode;
                command.Parameters.Add("@title", SqlDbType.NVarChar, 500).Value = (object?)item.Title ?? DBNull.Value;
                command.Parameters.Add("@planned", SqlDbType.Decimal).ConfigureDecimal(18, 6).Value = item.PlannedHours;
                command.Parameters.Add("@weight", SqlDbType.Decimal).ConfigureDecimal(18, 12).Value = item.Weight;
                command.Parameters.Add("@hours", SqlDbType.Decimal).ConfigureDecimal(18, 6).Value = item.Hours;
                command.Parameters.Add("@cost", SqlDbType.Decimal).ConfigureDecimal(18, 6).Value = item.Cost;
                await command.ExecuteNonQueryAsync(ct);
            }

            if (!isActivityAssignment)
            {
                var sumHours = preview.Destinations.Sum(x => x.Hours);
                var sumCost = preview.Destinations.Sum(x => x.Cost);
                if (Math.Abs(sumHours - preview.SourceHours) > Tolerance || Math.Abs(sumCost - preview.SourceCost) > Tolerance)
                    throw new InvalidOperationException("Invariante violada: HH/custo redistribuídos não fecham com a origem.");
            }

            await transaction.CommitAsync(ct);

            return new(
                id,
                normalizedProject,
                normalizedDocument,
                preview.Discipline!,
                preview.SourceHours,
                preview.SourceCost,
                "CONFIRMADO",
                actor,
                DateTime.UtcNow,
                preview.Destinations);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    internal static IReadOnlyList<RedistributionDestination> CalculateAllocations(
        decimal sourceHours,
        decimal sourceCost,
        IReadOnlyList<RedistributionTargetSource> targets)
    {
        var ordered = targets
            .Where(x => x.PlannedHours > 0)
            .OrderBy(x => x.DocumentCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
            return [];

        var totalPlanned = ordered.Sum(x => x.PlannedHours);
        if (totalPlanned <= 0)
            return [];

        var result = new List<RedistributionDestination>(ordered.Count);
        decimal allocatedHours = 0;
        decimal allocatedCost = 0;
        decimal allocatedWeight = 0;

        for (var index = 0; index < ordered.Count; index++)
        {
            var target = ordered[index];
            var isLast = index == ordered.Count - 1;
            var rawWeight = target.PlannedHours / totalPlanned;
            var weight = isLast
                ? 1m - allocatedWeight
                : Math.Round(rawWeight, 12, MidpointRounding.AwayFromZero);
            var hours = isLast
                ? sourceHours - allocatedHours
                : Math.Round(sourceHours * rawWeight, 6, MidpointRounding.AwayFromZero);
            var cost = isLast
                ? sourceCost - allocatedCost
                : Math.Round(sourceCost * rawWeight, 6, MidpointRounding.AwayFromZero);

            result.Add(new(
                target.DocumentCode,
                target.Title,
                target.PlannedHours,
                weight,
                hours,
                cost));

            allocatedWeight += weight;
            allocatedHours += hours;
            allocatedCost += cost;
        }

        return result;
    }

    private async Task<RedistributionPreview> BuildPreviewAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string projectId,
        string documentCode,
        CancellationToken ct)
    {
        var existing = await LoadActiveDecisionAsync(connection, transaction, projectId, documentCode, ct);
        if (existing is not null)
        {
            return new(
                projectId,
                documentCode,
                existing.Discipline,
                existing.SourceHours,
                existing.SourceCost,
                existing.Items.Sum(x => x.PlannedHours),
                false,
                "Este documento já possui redistribuição confirmada.",
                existing.Items);
        }

        const string sourceSql = """
            ;WITH Fonte AS
            (
                SELECT
                    CAST(ISNULL(SUM(ISNULL(HorasTrabalhadas, 0)), 0) AS decimal(18,6)) AS HH,
                    CAST(ISNULL(SUM(ISNULL(CustoCalculado, 0)), 0) AS decimal(18,6)) AS Custo,
                    MAX(NULLIF(LTRIM(RTRIM(Doc3)), '')) AS Doc3
                FROM gestao.TimesheetSnapshotAtual
                WHERE Project_ID_ACC = @projectId
                  AND UPPER(LTRIM(RTRIM(DocumentoCodigo))) = @documentCode
            ),
            NormaProjeto AS
            (
                SELECT TOP (1) NormaId
                FROM coordenacao.ProjetoNorma
                WHERE Project_ID_ACC = @projectId
                ORDER BY Data_Atualizacao DESC
            )
            SELECT
                f.HH,
                f.Custo,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(at.Disciplina)), ''),
                    NULLIF(LTRIM(RTRIM(nm.Disciplina)), '')
                ) AS Disciplina,
                NULLIF(LTRIM(RTRIM(f.Doc3)), '') AS Atividade,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(at.Disciplina)), '') IS NOT NULL THEN N'AT_MANUAL'
                    WHEN NULLIF(LTRIM(RTRIM(nm.Disciplina)), '') IS NOT NULL THEN N'NORMA'
                    ELSE N'NAO_RESOLVIDA'
                END AS ResolucaoDisciplina
            FROM Fonte f
            LEFT JOIN NormaProjeto np ON 1 = 1
            LEFT JOIN coordenacao.NormaMapeamento nm
                ON nm.NormaId = np.NormaId
               AND UPPER(LTRIM(RTRIM(nm.Sigla))) = UPPER(LTRIM(RTRIM(f.Doc3)))
            LEFT JOIN coordenacao.AtividadeDisciplinaProjeto at
                ON at.Project_ID_ACC = @projectId
               AND UPPER(LTRIM(RTRIM(at.Atividade))) = UPPER(LTRIM(RTRIM(f.Doc3)))
               AND at.Ativo = 1;

            ;WITH LdpAtual AS
            (
                SELECT L.*
                FROM dbo.Ldp_Unificada L
                WHERE L.Project_ID_ACC = @projectId
                  AND (
                        (
                            EXISTS
                            (
                                SELECT 1
                                FROM dbo.Projetos P
                                WHERE P.Project_ID_ACC = @projectId
                                  AND P.Tipo_LDP = 2
                            )
                            AND L.Data_Sincronizacao =
                            (
                                SELECT MAX(T2.Data_Sincronizacao)
                                FROM dbo.Ldp_Unificada T2
                                WHERE T2.Project_ID_ACC = @projectId
                            )
                        )
                        OR
                        (
                            NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.Projetos P
                                WHERE P.Project_ID_ACC = @projectId
                                  AND P.Tipo_LDP = 2
                            )
                            AND L.Data_Sincronizacao >= ISNULL
                            (
                                (
                                    SELECT MAX(G.Data_Sincronizacao)
                                    FROM
                                    (
                                        SELECT
                                            X.Data_Sincronizacao,
                                            DATEDIFF
                                            (
                                                SECOND,
                                                LAG(X.Data_Sincronizacao) OVER
                                                (
                                                    ORDER BY X.Data_Sincronizacao, X.ID_Registro
                                                ),
                                                X.Data_Sincronizacao
                                            ) AS Gap
                                        FROM dbo.Ldp_Unificada X
                                        WHERE X.Project_ID_ACC = @projectId
                                    ) G
                                    WHERE G.Gap > 30
                                ),
                                CAST('19000101' AS DATETIME)
                            )
                        )
                    )
            )
            SELECT
                COUNT_BIG(*) AS QtdLdp,
                SUM(CASE
                    WHEN UPPER(LTRIM(RTRIM(ISNULL(Document_Status, '')))) IN
                         ('DOCUMENTO RETIRADO DA LDP', 'CANCELADO', 'CANCELLED')
                    THEN 1 ELSE 0 END) AS QtdCancelada,
                (
                    SELECT COUNT_BIG(*)
                    FROM gestao.TimesheetDocumentoDePara_Espelho dp
                    WHERE dp.Project_ID_ACC = @projectId
                      AND UPPER(LTRIM(RTRIM(dp.Documento_Origem))) = @documentCode
                      AND dp.Ativo = 1
                ) AS QtdDeParaAtivo
            FROM LdpAtual
            WHERE UPPER(LTRIM(RTRIM(NA_Document_Number))) = @documentCode
            OPTION (RECOMPILE);
            """;

        decimal sourceHours;
        decimal sourceCost;
        string? discipline;
        string? sourceActivityCode;
        string disciplineResolution;
        long ldpRows;
        long cancelledRows;
        long activeDeParaRows;

        await using (var command = new SqlCommand(sourceSql, connection, transaction) { CommandTimeout = 45 })
        {
            command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            command.Parameters.Add("@documentCode", SqlDbType.NVarChar, 255).Value = documentCode;

            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            sourceHours = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
            sourceCost = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
            discipline = reader.IsDBNull(2) ? null : reader.GetString(2);
            sourceActivityCode = reader.IsDBNull(3) ? null : reader.GetString(3);
            disciplineResolution = reader.IsDBNull(4) ? "NAO_RESOLVIDA" : reader.GetString(4);

            await reader.NextResultAsync(ct);
            await reader.ReadAsync(ct);
            ldpRows = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
            cancelledRows = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1));
            activeDeParaRows = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
        }

        if (sourceHours <= 0)
            return Blocked(projectId, documentCode, discipline, sourceHours, sourceCost,
                "O documento não possui HH realizado no Timesheet atual.", sourceActivityCode, disciplineResolution);

        if (activeDeParaRows > 0)
            return Blocked(projectId, documentCode, discipline, sourceHours, sourceCost,
                "Este documento possui De/Para ativo. A redistribuição não pode ser aplicada sobre uma correção documental já existente.",
                sourceActivityCode, disciplineResolution);

        if (ldpRows > 1)
            return Blocked(projectId, documentCode, discipline, sourceHours, sourceCost,
                "O número do documento possui mais de um registro atual na LDP. Resolva a duplicidade documental antes da redistribuição.",
                sourceActivityCode, disciplineResolution);

        if (ldpRows == 1 && cancelledRows == 0)
            return Blocked(projectId, documentCode, discipline, sourceHours, sourceCost,
                "O documento ainda está ativo na LDP e não pode ser tratado como cancelado pela redistribuição.",
                sourceActivityCode, disciplineResolution);

        if (string.IsNullOrWhiteSpace(discipline))
        {
            var activityText = string.IsNullOrWhiteSpace(sourceActivityCode)
                ? "a atividade do lançamento"
                : $"a atividade {sourceActivityCode}";
            return Blocked(
                projectId,
                documentCode,
                null,
                sourceHours,
                sourceCost,
                $"Não foi possível resolver a disciplina pela Norma de Coordenação. Classifique {activityText} como AT e atribua a disciplina responsável.",
                sourceActivityCode,
                disciplineResolution,
                activityMappingRequired: !string.IsNullOrWhiteSpace(sourceActivityCode));
        }

        if (string.Equals(disciplineResolution, "AT_MANUAL", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                projectId,
                documentCode,
                discipline,
                sourceHours,
                sourceCost,
                0,
                true,
                null,
                Array.Empty<RedistributionDestination>())
            {
                SourceActivityCode = sourceActivityCode,
                ActivityMappingRequired = false,
                DisciplineResolution = "AT_MANUAL"
            };
        }

        const string targetsSql = """
            ;WITH LdpAtual AS
            (
                SELECT L.*
                FROM dbo.Ldp_Unificada L
                WHERE L.Project_ID_ACC = @projectId
                  AND (
                        (
                            EXISTS
                            (
                                SELECT 1
                                FROM dbo.Projetos P
                                WHERE P.Project_ID_ACC = @projectId
                                  AND P.Tipo_LDP = 2
                            )
                            AND L.Data_Sincronizacao =
                            (
                                SELECT MAX(T2.Data_Sincronizacao)
                                FROM dbo.Ldp_Unificada T2
                                WHERE T2.Project_ID_ACC = @projectId
                            )
                        )
                        OR
                        (
                            NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.Projetos P
                                WHERE P.Project_ID_ACC = @projectId
                                  AND P.Tipo_LDP = 2
                            )
                            AND L.Data_Sincronizacao >= ISNULL
                            (
                                (
                                    SELECT MAX(G.Data_Sincronizacao)
                                    FROM
                                    (
                                        SELECT
                                            X.Data_Sincronizacao,
                                            DATEDIFF
                                            (
                                                SECOND,
                                                LAG(X.Data_Sincronizacao) OVER
                                                (
                                                    ORDER BY X.Data_Sincronizacao, X.ID_Registro
                                                ),
                                                X.Data_Sincronizacao
                                            ) AS Gap
                                        FROM dbo.Ldp_Unificada X
                                        WHERE X.Project_ID_ACC = @projectId
                                    ) G
                                    WHERE G.Gap > 30
                                ),
                                CAST('19000101' AS DATETIME)
                            )
                        )
                    )
            ),
            NormaProjeto AS
            (
                SELECT TOP (1) NormaId
                FROM coordenacao.ProjetoNorma
                WHERE Project_ID_ACC = @projectId
                ORDER BY Data_Atualizacao DESC
            ),
            Destinos AS
            (
                SELECT
                    UPPER(LTRIM(RTRIM(d.NA_Document_Number))) AS Documento,
                    MAX(NULLIF(LTRIM(RTRIM(d.Document_Title)), '')) AS Titulo,
                    CAST(MAX(ISNULL(d.Planned_HH, 0)) AS decimal(18,6)) AS PlannedHH,
                    COUNT_BIG(*) AS Ocorrencias
                FROM LdpAtual d
                CROSS JOIN NormaProjeto np
                INNER JOIN coordenacao.NormaMapeamento nm
                    ON nm.NormaId = np.NormaId
                   AND UPPER(LTRIM(RTRIM(nm.Sigla))) = UPPER(LTRIM(RTRIM(d.Discipline_Activity)))
                WHERE UPPER(LTRIM(RTRIM(nm.Disciplina))) = UPPER(LTRIM(RTRIM(@discipline)))
                  AND NULLIF(LTRIM(RTRIM(d.NA_Document_Number)), '') IS NOT NULL
                  AND UPPER(LTRIM(RTRIM(d.NA_Document_Number))) <> @source
                  AND ISNULL(d.Planned_HH, 0) > 0
                  AND UPPER(LTRIM(RTRIM(ISNULL(d.Doc_Type_Code, '')))) <> 'AT'
                  AND UPPER(LTRIM(RTRIM(ISNULL(d.Document_Status, '')))) NOT IN
                      ('DOCUMENTO RETIRADO DA LDP', 'CANCELADO', 'CANCELLED')
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM gestao.AuditoriaRedistribuicaoDocumento r
                      WHERE r.Project_ID_ACC = d.Project_ID_ACC
                        AND UPPER(LTRIM(RTRIM(r.Documento_Origem))) = UPPER(LTRIM(RTRIM(d.NA_Document_Number)))
                        AND r.Ativo = 1
                        AND r.Status = N'CONFIRMADO'
                  )
                GROUP BY UPPER(LTRIM(RTRIM(d.NA_Document_Number)))
            )
            SELECT Documento, Titulo, PlannedHH
            FROM Destinos
            WHERE Ocorrencias = 1
              AND PlannedHH > 0
            ORDER BY Documento
            OPTION (RECOMPILE);
            """;

        var targets = new List<RedistributionTargetSource>();
        await using (var command = new SqlCommand(targetsSql, connection, transaction) { CommandTimeout = 45 })
        {
            command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            command.Parameters.Add("@discipline", SqlDbType.NVarChar, 255).Value = discipline;
            command.Parameters.Add("@source", SqlDbType.NVarChar, 255).Value = documentCode;

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                targets.Add(new(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetDecimal(2)));
            }
        }

        if (targets.Count == 0)
            return Blocked(projectId, documentCode, discipline, sourceHours, sourceCost,
                "Não existem documentos ativos elegíveis com HH planejado na mesma disciplina.",
                sourceActivityCode, disciplineResolution,
                activityMappingRequired: !string.IsNullOrWhiteSpace(sourceActivityCode));

        var allocations = CalculateAllocations(sourceHours, sourceCost, targets);
        var totalPlanned = targets.Sum(x => x.PlannedHours);
        if (allocations.Count == 0 || totalPlanned <= 0)
            return Blocked(projectId, documentCode, discipline, sourceHours, sourceCost,
                "A disciplina não possui HH planejado suficiente para formar os pesos da redistribuição.",
                sourceActivityCode, disciplineResolution,
                activityMappingRequired: !string.IsNullOrWhiteSpace(sourceActivityCode));

        return new(
            projectId,
            documentCode,
            discipline,
            sourceHours,
            sourceCost,
            totalPlanned,
            true,
            null,
            allocations)
        {
            SourceActivityCode = sourceActivityCode,
            ActivityMappingRequired = false,
            DisciplineResolution = disciplineResolution
        };
    }

    private async Task<RedistributionDecision?> LoadActiveDecisionAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string projectId,
        string documentCode,
        CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                h.Id,
                h.Project_ID_ACC,
                h.Documento_Origem,
                h.Disciplina,
                h.HH_Origem,
                h.Custo_Origem,
                h.Status,
                h.ConfirmadoPor,
                h.ConfirmadoEm
            FROM gestao.AuditoriaRedistribuicaoDocumento h WITH (UPDLOCK, HOLDLOCK)
            WHERE h.Project_ID_ACC = @projectId
              AND UPPER(LTRIM(RTRIM(h.Documento_Origem))) = @documentCode
              AND h.Ativo = 1
            ORDER BY h.ConfirmadoEm DESC;
            """;

        Guid id;
        string source;
        string discipline;
        decimal hours;
        decimal cost;
        string status;
        string user;
        DateTime confirmedAt;

        await using (var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 })
        {
            command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            command.Parameters.Add("@documentCode", SqlDbType.NVarChar, 255).Value = documentCode;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            id = reader.GetGuid(0);
            source = reader.GetString(2);
            discipline = reader.GetString(3);
            hours = reader.GetDecimal(4);
            cost = reader.GetDecimal(5);
            status = reader.GetString(6);
            user = reader.GetString(7);
            confirmedAt = DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc);
        }

        const string itemSql = """
            SELECT Documento_Destino, Titulo_Destino, Planned_HH_Destino, Peso, HH_Redistribuida, Custo_Redistribuido
            FROM gestao.AuditoriaRedistribuicaoItem
            WHERE Redistribuicao_Id = @id
            ORDER BY Documento_Destino;
            """;

        var items = new List<RedistributionDestination>();
        await using (var command = new SqlCommand(itemSql, connection, transaction) { CommandTimeout = 30 })
        {
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(new(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5)));
            }
        }

        return new(id, projectId, source, discipline, hours, cost, status, user, confirmedAt, items);
    }

    private static RedistributionPreview Blocked(
        string projectId,
        string documentCode,
        string? discipline,
        decimal hours,
        decimal cost,
        string reason,
        string? sourceActivityCode = null,
        string? disciplineResolution = null,
        bool activityMappingRequired = false)
        => new(projectId, documentCode, discipline, hours, cost, 0, false, reason, [])
        {
            SourceActivityCode = sourceActivityCode,
            ActivityMappingRequired = activityMappingRequired,
            DisciplineResolution = disciplineResolution
        };

    private string ConnectionString()
        => configuration.GetConnectionString("LocalConnection")
           ?? throw new InvalidOperationException("Conexão LocalConnection indisponível.");

    private static string NormalizeProject(string value)
        => value?.Trim() ?? string.Empty;

    private static string NormalizeDocument(string value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static IReadOnlyList<string> NormalizeDocuments(IEnumerable<string>? values)
        => (values ?? [])
            .Select(NormalizeDocument)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
}

internal static class SqlParameterRedistributionExtensions
{
    internal static SqlParameter ConfigureDecimal(this SqlParameter parameter, byte precision, byte scale)
    {
        parameter.Precision = precision;
        parameter.Scale = scale;
        return parameter;
    }
}
