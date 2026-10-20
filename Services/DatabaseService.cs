using System;
using System.Data;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions; // 🎯 Necessário para normalizar o código do documento
using System.Globalization; // 🎯 Necessário para a formatação da data
using NA_Connect.Models;
using APS.WebApp.Models;

namespace NA_Connect.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(Microsoft.Extensions.Configuration.IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _configuration = configuration;
            // Agora ele busca especificamente o seu banco local
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }
        #region 🏗️ Módulo 01: Hubs e Projetos Base

        public DataTable GetHubs()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT HubContaID, Nome_Hub, Account_ID_ACC FROM Hubs_Conta";
                    var dt = new DataTable();
                    try 
                    {
                        conn.Open();
                        using (var da = new SqlDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                    catch (SqlException ex)
                    {
                        throw new Exception($"Falha de conexão com o SQL Server (Hubs): {ex.Message}", ex);
                    }
                    return dt;
                }
            }
        }

        public void RegistrarDnaProjeto(string projectId, string ldpFileId, string hubId, string ldpName, string idioma, string padrao)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @pid)
                        UPDATE Metadados_Projetos 
                        SET LDP_File_ID = @fid, 
                            Account_ID_ACC = @hub, 
                            Nome_LDP = @name, 
                            Idioma = @lang, 
                            Padrao_Nomenclatura = @std,
                            Data_Update = GETDATE()
                        WHERE Project_ID_ACC = @pid
                    ELSE
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, LDP_File_ID, Account_ID_ACC, Nome_LDP, Idioma, Padrao_Nomenclatura, Data_Update)
                        VALUES (@pid, @fid, @hub, @name, @lang, @std, GETDATE())";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    cmd.Parameters.AddWithValue("@fid", ldpFileId);
                    cmd.Parameters.AddWithValue("@hub", hubId);
                    cmd.Parameters.AddWithValue("@name", ldpName);
                    cmd.Parameters.AddWithValue("@lang", idioma);
                    cmd.Parameters.AddWithValue("@std", padrao);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public string ObterColunaChaveProjeto(string projectId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
                string query = @"
                    SELECT TOP 1 Coluna_Chave 
                    FROM Metadados_Projetos 
                    WHERE Project_ID_ACC = @pid OR Project_ID_ACC = @cleanPid";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    cmd.Parameters.AddWithValue("@cleanPid", cleanId);
                    conn.Open();
                    var result = cmd.ExecuteScalar();
                    string valor = result != null && result != DBNull.Value ? result.ToString() : null;
                    return !string.IsNullOrEmpty(valor) ? valor : "N&A DOCUMENT NUMBER";
                }
            }
        }

        public void SalvarProjeto(int hubContaId, string nomeProjeto, string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Projetos WHERE Project_ID_ACC = @projectIdAcc)
                    BEGIN
                        INSERT INTO Projetos (HubContaID, Nome_Projeto, Project_ID_ACC, Is_Ativo)
                        VALUES (@hubContaId, @nomeProjeto, @projectIdAcc, 1)
                    END
                    ELSE
                    BEGIN
                        UPDATE Projetos 
                        SET Nome_Projeto = @nomeProjeto 
                        WHERE Project_ID_ACC = @projectIdAcc
                    END";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@hubContaId", hubContaId);
                    cmd.Parameters.AddWithValue("@nomeProjeto", nomeProjeto);
                    cmd.Parameters.AddWithValue("@projectIdAcc", projectIdAcc);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public DataTable GetProjetosAtivos()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT P.Project_ID_ACC, P.Nome_Projeto, H.Account_ID_ACC
                    FROM Projetos P
                    JOIN Hubs_Conta H ON P.HubContaID = H.HubContaID
                    WHERE P.Is_Ativo = 1";

                using (var cmd = new SqlCommand(query, conn))
                {
                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }

        /// <summary>
        /// Filtro de teste exclusivo (seção 22 do pedido): devolve UM único projeto pelo Project_ID_ACC,
        /// independentemente de Is_Ativo — o diagnóstico controlado das Etapas 12/13/14/16/17 não deve
        /// depender de alguém marcar/desmarcar Is_Ativo manualmente para restringir o lote.
        /// </summary>
        public DataTable GetProjetoPorId(string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT P.Project_ID_ACC, P.Nome_Projeto, H.Account_ID_ACC
                    FROM Projetos P
                    JOIN Hubs_Conta H ON P.HubContaID = H.HubContaID
                    WHERE P.Project_ID_ACC = @projectId";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.Add("@projectId", SqlDbType.NVarChar).Value = projectIdAcc;

                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }

        /// <summary>
        /// Lista completa de dbo.Projetos para a tela de Configuração (ControlPanel.tsx, aba "Gestão de
        /// Projetos") — ao contrário de GetProjetosAtivos(), não filtra por Is_Ativo (a tela precisa
        /// mostrar e permitir ativar/desativar). Inclui Tipo_LDP/Naming_Standard/Project_Language/
        /// File_ID_LDP_ACC, que hoje só eram lidos internamente pelo Worker.
        /// </summary>
        public DataTable GetTodosProjetosParaAdmin()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT P.ProjetoID, P.Nome_Projeto, P.Project_ID_ACC, P.Is_Ativo,
                           P.Tipo_LDP, P.Naming_Standard, P.Project_Language, P.File_ID_LDP_ACC
                    FROM Projetos P
                    ORDER BY P.Nome_Projeto";

                using (var cmd = new SqlCommand(query, conn))
                {
                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }

        /// <summary>
        /// Atualização parcial de dbo.Projetos pela tela de Configuração — cada parâmetro nulo mantém o
        /// valor atual (COALESCE), permitindo que a UI envie só o campo que o usuário efetivamente
        /// alterou (ex.: só Is_Ativo, ou só Naming_Standard). Naming_Standard usa os mesmos valores já
        /// consumidos por LdpUpdateService/WorkerStageRouting/AccScheduleSyncService: "NA" ou "CLIENT".
        /// Project_Language: auditado 17/09/2026 contra os dados reais — só "EN"/"PT" existem hoje na
        /// tabela (nenhum outro valor foi inventado). File_ID_LDP_ACC não tem escrita aqui de propósito
        /// (fica somente leitura — checkpoint 15/16: "sem contrato seguro de edição manual").
        /// </summary>
        public void AtualizarConfiguracaoProjeto(int projetoId, bool? isAtivo, byte? tipoLdp, string namingStandard, string projectLanguage)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    UPDATE Projetos SET
                        Is_Ativo = COALESCE(@isAtivo, Is_Ativo),
                        Tipo_LDP = COALESCE(@tipoLdp, Tipo_LDP),
                        Naming_Standard = COALESCE(@namingStandard, Naming_Standard),
                        Project_Language = COALESCE(@projectLanguage, Project_Language)
                    WHERE ProjetoID = @projetoId";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projetoId", projetoId);
                    cmd.Parameters.AddWithValue("@isAtivo", isAtivo.HasValue ? (object)isAtivo.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@tipoLdp", tipoLdp.HasValue ? (object)tipoLdp.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@namingStandard", string.IsNullOrEmpty(namingStandard) ? (object)DBNull.Value : namingStandard);
                    cmd.Parameters.AddWithValue("@projectLanguage", string.IsNullOrEmpty(projectLanguage) ? (object)DBNull.Value : projectLanguage);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region 🔍 Módulo 02: DNA do Projeto (Contexto e Cache)

        public ProjectDiscoveryResult ObterContextoProjeto(string projectId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT Naming_Standard, Project_Language FROM Projetos WHERE Project_ID_ACC = @pid";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new ProjectDiscoveryResult
                            {
                                NamingStandard = reader["Naming_Standard"]?.ToString(),
                                Language = reader["Project_Language"]?.ToString()
                            };
                        }
                    }
                }
            }
            return null;
        }

        public void SalvarContextoProjeto(string projectId, string standard, string lang)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    UPDATE Projetos SET 
                        Naming_Standard = @standard, 
                        Project_Language = @lang 
                    WHERE Project_ID_ACC = @pid";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    cmd.Parameters.AddWithValue("@standard", standard ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@lang", lang ?? (object)DBNull.Value);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public string GetLdpFileIdFromCache(string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT File_ID_LDP_ACC FROM Projetos WHERE Project_ID_ACC = @projectId";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectIdAcc);
                    conn.Open();
                    var result = cmd.ExecuteScalar();
                    return (result != null && result != DBNull.Value) ? result.ToString() : null;
                }
            }
        }

        public void AtualizarIdLdpProjeto(string projectIdAcc, string fileIdLdp)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE Projetos SET File_ID_LDP_ACC = @fileId WHERE Project_ID_ACC = @projectId";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@fileId", fileIdLdp ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@projectId", projectIdAcc);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Registro somente-leitura de dbo.Projetos para o canário de diagnóstico da descoberta da
        /// LDP ([DISCOVERY][PROJECT] em GrdAutomationWorker) — nunca escreve nada, nunca influencia
        /// nenhuma decisão do fluxo, é usado só para log. Tolera a coluna Tipo_LDP ainda não existir
        /// no banco (mesmo padrão de LdpProjetoRepository.ColunaTipoLdpProvavelmenteAusente): se a
        /// primeira consulta falhar com "Invalid column name" para Tipo_LDP, refaz sem essa coluna e
        /// marca TipoLdpRaw como "(coluna ausente)".
        /// </summary>
        public sealed class ProjetoDiagnostico
        {
            public string ProjectIdAcc { get; set; }
            public string NomeProjeto { get; set; }
            public string HubContaId { get; set; }
            public bool? IsAtivo { get; set; }
            public string TipoLdpRaw { get; set; }
            public string FileIdLdpAcc { get; set; }
        }

        public ProjetoDiagnostico GetProjetoDiagnostico(string projectIdAcc)
        {
            var diag = new ProjetoDiagnostico { ProjectIdAcc = projectIdAcc };

            void Preencher(SqlConnection conn, bool comTipoLdp)
            {
                string colTipo = comTipoLdp ? ", P.Tipo_LDP" : "";
                string query = $@"
                    SELECT P.Nome_Projeto, P.HubContaID, P.Is_Ativo, P.File_ID_LDP_ACC{colTipo}
                    FROM Projetos P
                    WHERE P.Project_ID_ACC = @projectId";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectIdAcc);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return;

                        diag.NomeProjeto = reader["Nome_Projeto"] as string;
                        diag.HubContaId = reader["HubContaID"] == DBNull.Value ? null : reader["HubContaID"].ToString();
                        diag.IsAtivo = reader["Is_Ativo"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(reader["Is_Ativo"]);
                        diag.FileIdLdpAcc = reader["File_ID_LDP_ACC"] as string;
                        if (comTipoLdp)
                        {
                            diag.TipoLdpRaw = reader["Tipo_LDP"] == DBNull.Value ? "(vazio)" : reader["Tipo_LDP"].ToString();
                        }
                    }
                }
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    try
                    {
                        Preencher(conn, comTipoLdp: true);
                    }
                    catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 207))
                    {
                        diag.TipoLdpRaw = "(coluna Tipo_LDP ausente)";
                        Preencher(conn, comTipoLdp: false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DISCOVERY][PROJECT] Falha ao ler diagnóstico do projeto {ProjectIdAcc} (somente log, não interrompe o ciclo).", projectIdAcc);
            }

            return diag;
        }

        #endregion

        #region 📅 Módulo 03: Cronograma e Power BI

        public void SalvarTarefaCronograma(
            string projectIdAcc, string uid, string docNum, string name, 
            decimal phys, decimal pred, string wbsPct, 
            DateTime? start, DateTime? baseline, DateTime? finish, 
            string edt, string projeto, decimal trabalhoMpp) // <-- 13º argumento adicionado aqui
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Cronograma_Projetos WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid)
                    BEGIN
                        INSERT INTO Cronograma_Projetos (
                            Project_ID_ACC, UID_Tarefa, NA_DOCUMENT_NUMBER, TASK_NAME, 
                            PERCENT_PHYSICAL_COMPLETED, PERCENT_PREDICTED_PHYSICAL, WBS_PERCENT,
                            PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE, 
                            EDT_WBS, CR_OS, Trabalho) -- <-- Coluna nova no INSERT
                        VALUES (@pid, @uid, @docNum, @name, @phys, @pred, @wbsPct, @start, @baseline, @finish, @edt, @projeto, @trabalho)
                    END
                    ELSE
                    BEGIN
                        UPDATE Cronograma_Projetos SET 
                            NA_DOCUMENT_NUMBER = @docNum, TASK_NAME = @name, 
                            PERCENT_PHYSICAL_COMPLETED = @phys, PERCENT_PREDICTED_PHYSICAL = @pred, 
                            WBS_PERCENT = @wbsPct, PLANNED_DATE_START = @start, 
                            PLANNED_DATA_BASELINE = @baseline, REPLANNED_DATE = @finish, 
                            EDT_WBS = @edt, CR_OS = @projeto, 
                            Trabalho = @trabalho, -- <-- Coluna nova no UPDATE
                            Data_Sincronizacao = GETDATE()
                        WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid
                    END";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectIdAcc);
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Parameters.AddWithValue("@docNum", docNum ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@phys", phys);
                    cmd.Parameters.AddWithValue("@pred", pred);
                    cmd.Parameters.AddWithValue("@wbsPct", wbsPct ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@start", (object)start ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@baseline", (object)baseline ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@finish", (object)finish ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@edt", edt);
                    cmd.Parameters.AddWithValue("@projeto", projeto);
                    cmd.Parameters.AddWithValue("@trabalho", trabalhoMpp); // <-- Parâmetro passado para o SQL

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Persiste o snapshot inteiro do Cronograma Tipo 1 de UM projeto em uma ÚNICA SqlConnection/
        /// SqlTransaction (seção 8 da manutenção técnica) — nunca mais tarefa por tarefa com catch
        /// individual. A regra de upsert por linha (UID_Tarefa) é a mesma já usada por
        /// SalvarTarefaCronograma; a diferença é que agora TODAS as linhas do projeto entram na mesma
        /// transação: se qualquer uma falhar, nenhuma fica persistida (rollback do lote inteiro — nunca
        /// snapshot parcial). Não executa DDL nem altera schema.
        /// </summary>
        public int SalvarSnapshotCronogramaTipo1Transacional(string projectIdAcc, IReadOnlyCollection<RegistroCronogramaTipo1Preparado> registros)
        {
            if (registros.Count == 0 || registros.Any(r => r.ProjectIdAcc != projectIdAcc || string.IsNullOrWhiteSpace(r.TaskUid)
                    || string.IsNullOrWhiteSpace(r.AnalyticsJson))
                || registros.Select(r => r.TaskUid).Distinct(StringComparer.Ordinal).Count() != registros.Count)
                throw new InvalidOperationException("Snapshot Tipo 1 vazio, identidade ausente ou UID duplicado.");
            var cronometro = Stopwatch.StartNew();
            _logger.LogInformation("[CRONOGRAMA][SQL][INICIO] Project_ID_ACC={ProjectIdAcc} | RegistrosPreparados={Preparados}", projectIdAcc, registros.Count);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var transacao = conn.BeginTransaction())
                {
                    try
                    {
                        // Serialize snapshots of this project and require its authoritative Tipo_LDP.
                        using (var projectLock = new SqlCommand("SELECT Tipo_LDP FROM dbo.Projetos WITH (UPDLOCK,HOLDLOCK) WHERE Project_ID_ACC=@pid", conn, transacao))
                        {
                            projectLock.Parameters.AddWithValue("@pid", projectIdAcc);
                            if (Convert.ToInt32(projectLock.ExecuteScalar()) != 1)
                                throw new InvalidOperationException("Persistencia Tipo 1 requer projeto Tipo_LDP=1.");
                        }
                        var snapshotTime = DateTime.UtcNow;
                        int registrosInseridos = 0;

                        foreach (var registro in registros)
                        {
                            using var cmd = CriarComandoSnapshotTipo1(registro, snapshotTime, conn, transacao);
                            cmd.ExecuteNonQuery();
                            registrosInseridos++;
                        }

                        // A snapshot replaces membership too: deleted MPP tasks must not survive as current facts.
                        using (var prune = new SqlCommand(@"DELETE FROM dbo.Cronograma_Projetos
                            WHERE Project_ID_ACC=@pid AND Tipo_LDP=1
                            AND (Data_Leitura_Cronograma IS NULL OR Data_Leitura_Cronograma<>@snapshot)", conn, transacao))
                        {
                            prune.Parameters.AddWithValue("@pid", projectIdAcc);
                            prune.Parameters.Add("@snapshot", SqlDbType.DateTime2).Value = snapshotTime;
                            prune.ExecuteNonQuery();
                        }
                        transacao.Commit();
                        cronometro.Stop();

                        _logger.LogInformation("[CRONOGRAMA][ANALYTICS][COMMIT] Project_ID_ACC={ProjectId} | Snapshot={Snapshot} | Resumos={Summaries} | DisciplinaPreenchida={Disciplines} | FolhasSemDocumento={NonDocuments}",
                            projectIdAcc, snapshotTime, registros.Count(r => r.EhTarefaResumo),
                            registros.Count(r => !string.IsNullOrWhiteSpace(r.Disciplina)),
                            registros.Count(r => !r.EhTarefaResumo && !r.CandidatoDocumento));

                        _logger.LogInformation(
                            "[CRONOGRAMA][SQL][COMMIT] Project_ID_ACC={ProjectIdAcc} | RegistrosPreparados={Preparados} | RegistrosInseridos={Inseridos} | DuracaoMs={DuracaoMs}",
                            projectIdAcc, registros.Count, registrosInseridos, cronometro.Elapsed.TotalMilliseconds);

                        return registrosInseridos;
                    }
                    catch (Exception ex)
                    {
                        transacao.Rollback();
                        cronometro.Stop();

                        string codigoErro = (ex as SqlException)?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ex.GetType().Name;

                        _logger.LogError(ex,
                            "[CRONOGRAMA][SQL][ROLLBACK] Project_ID_ACC={ProjectIdAcc} | RegistrosPreparados={Preparados} | DuracaoMs={DuracaoMs} | RollbackExecutado=sim | CodigoErro={CodigoErro}",
                            projectIdAcc, registros.Count, cronometro.Elapsed.TotalMilliseconds, codigoErro);

                        throw;
                    }
                }
            }
        }

        internal static SqlCommand CriarComandoSnapshotTipo1(RegistroCronogramaTipo1Preparado registro,
            DateTime snapshotTime, SqlConnection? connection = null, SqlTransaction? transaction = null)
        {
            var cmd = new SqlCommand { Connection = connection, Transaction = transaction };
            cmd.CommandText = @"
                IF NOT EXISTS (SELECT 1 FROM Cronograma_Projetos WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid AND Tipo_LDP=1)
                BEGIN
                    INSERT INTO Cronograma_Projetos (
                        Project_ID_ACC, UID_Tarefa, NA_DOCUMENT_NUMBER, TASK_NAME,
                        PERCENT_PHYSICAL_COMPLETED, PERCENT_PREDICTED_PHYSICAL, WBS_PERCENT,
                        PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE,
                        EDT_WBS, CR_OS, Trabalho, Tipo_LDP, Eh_Tarefa_Resumo, Disciplina_Cronograma, Campos_Customizados_JSON, Data_Leitura_Cronograma, Data_Sincronizacao)
                    VALUES (@pid, @uid, @docNum, @name, @phys, @pred, @wbsPct, @start, @baseline, @finish, @edt, @projeto, @trabalho, 1, @resumo, @disciplina, @analytics, @snapshot, @snapshot)
                END
                ELSE
                BEGIN
                    UPDATE Cronograma_Projetos SET
                        NA_DOCUMENT_NUMBER = @docNum, TASK_NAME = @name,
                        PERCENT_PHYSICAL_COMPLETED = @phys, PERCENT_PREDICTED_PHYSICAL = @pred,
                        WBS_PERCENT = @wbsPct, PLANNED_DATE_START = @start,
                        PLANNED_DATA_BASELINE = @baseline, REPLANNED_DATE = @finish,
                        EDT_WBS = @edt, CR_OS = @projeto,
                        Trabalho = @trabalho, Eh_Tarefa_Resumo=@resumo, Disciplina_Cronograma=@disciplina,
                        Campos_Customizados_JSON=@analytics, Data_Leitura_Cronograma=@snapshot,
                        Data_Sincronizacao = @snapshot
                    WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid AND Tipo_LDP=1
                END";

            cmd.Parameters.AddWithValue("@pid", registro.ProjectIdAcc);
            cmd.Parameters.AddWithValue("@uid", registro.TaskUid);
            cmd.Parameters.AddWithValue("@docNum", (object)registro.DocNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", registro.TaskName);
            cmd.Parameters.AddWithValue("@phys", (object?)registro.PhysicalDone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pred", (object?)registro.PredictedDone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@wbsPct", (object)registro.WbsPercent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@start", (object)registro.Start ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@baseline", (object)registro.Baseline ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@finish", (object)registro.TrendDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@edt", registro.EdtWbs);
            cmd.Parameters.AddWithValue("@projeto", (object?)registro.ProjetoCodigo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@trabalho", (object?)registro.TrabalhoMpp ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@resumo", registro.EhTarefaResumo);
            cmd.Parameters.AddWithValue("@disciplina", (object?)registro.Disciplina ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@analytics", (object?)registro.AnalyticsJson ?? DBNull.Value);
            cmd.Parameters.Add("@snapshot", SqlDbType.DateTime2).Value = snapshotTime;
            return cmd;
        }

        public int GetTotalTarefasSincronizadas(string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM Cronograma_Projetos WHERE Project_ID_ACC = @projectIdAcc";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectIdAcc", projectIdAcc);
                    conn.Open();
                    return (int)cmd.ExecuteScalar();
                }
            }
        }

        // 🎯 NOVO MÉTODO: Lê a Fonte da Verdade (Banco de Dados) e monta o Dicionário para a LDP
        public Dictionary<string, Dictionary<string, string>> GetDadosCronogramaDoBanco(string projectIdAcc)
        {
            var dict = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT NA_DOCUMENT_NUMBER, 
                           PERCENT_PHYSICAL_COMPLETED, 
                           PERCENT_PREDICTED_PHYSICAL,
                           WBS_PERCENT, 
                           PLANNED_DATA_BASELINE, 
                           REPLANNED_DATE
                    FROM Cronograma_Projetos 
                    WHERE Project_ID_ACC = @pid AND Tipo_LDP=1 AND ISNULL(Eh_Tarefa_Resumo,0)=0";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectIdAcc);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string docCode = reader["NA_DOCUMENT_NUMBER"]?.ToString();
                            if (string.IsNullOrWhiteSpace(docCode)) continue;

                            // Normalização da chave idêntica à da LDP para dar "Match" perfeito
                            string key = Regex.Replace(docCode.Trim().ToUpper(), @"[^A-Z0-9]", "");
                            var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                            // Exemplo de como deve ser montado o dicionário vindo do banco (dentro do loop do reader)
                            if (reader["PERCENT_PHYSICAL_COMPLETED"] != DBNull.Value)
                            {
                                decimal phys = Convert.ToDecimal(reader["PERCENT_PHYSICAL_COMPLETED"]);
                                // Chave casando com o mapeamento (Texto2)
                                vals["Texto2"] = phys.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                            }

                            if (reader["PERCENT_PREDICTED_PHYSICAL"] != DBNull.Value)
                            {
                                decimal pred = Convert.ToDecimal(reader["PERCENT_PREDICTED_PHYSICAL"]);
                                // Chave casando com o mapeamento (Texto3)
                                vals["Texto3"] = pred.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                            }

                            if (reader["WBS_PERCENT"] != DBNull.Value)
                                vals["WBS_PERCENT"] = reader["WBS_PERCENT"].ToString();

                            if (reader["PLANNED_DATA_BASELINE"] != DBNull.Value)
                            {
                                DateTime baseline = Convert.ToDateTime(reader["PLANNED_DATA_BASELINE"]);
                                vals["Planned_Date_Baseline"] = FormatarDataParaLDP(baseline);
                            }

                            if (reader["REPLANNED_DATE"] != DBNull.Value)
                            {
                                DateTime replanned = Convert.ToDateTime(reader["REPLANNED_DATE"]);
                                vals["Replanned_Date"] = FormatarDataParaLDP(replanned);
                            }

                            dict[key] = vals;
                        }
                    }
                }
            }
            return dict;
        }

        // 🎯 NOVA FUNÇÃO AUXILIAR: Transforma a Data do Banco no modelo "SEX 06/03/26" exigido na LDP
        private string FormatarDataParaLDP(DateTime dt)
        {
            var culture = new CultureInfo("pt-BR");
            string dayName = culture.DateTimeFormat.GetAbbreviatedDayName(dt.DayOfWeek).ToUpper();
            if (dayName.Length > 3) dayName = dayName.Substring(0, 3);
            return $"{dayName} {dt:dd/MM/yy}";
        }

        /// <summary>
        /// Lê (somente leitura, nunca DDL) o esquema real das colunas de uma tabela via sys.columns/
        /// sys.types — usado pela validação preventiva de valores DECIMAL/NUMERIC do Cronograma Tipo 1
        /// antes do envio ao SQL, para identificar a linha causadora de um estouro aritmético (erro 8115)
        /// sem truncar, arredondar ou zerar o valor. tableName deve ser o nome da tabela sem schema/
        /// colchetes (ex.: "Cronograma_Projetos") — OBJECT_ID resolve o schema padrão da conexão.
        /// </summary>
        public List<ColunaEsquemaSql> ObterEsquemaColunas(string tableName)
        {
            var colunas = new List<ColunaEsquemaSql>();

            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT
                        c.name AS Coluna,
                        t.name AS Tipo,
                        c.precision,
                        c.scale,
                        c.max_length,
                        c.is_nullable
                    FROM sys.columns c
                    INNER JOIN sys.types t
                        ON t.user_type_id = c.user_type_id
                    WHERE c.object_id = OBJECT_ID(@Tabela)
                    ORDER BY c.column_id;";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Tabela", tableName);
                    conn.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            colunas.Add(new ColunaEsquemaSql
                            {
                                Coluna = reader["Coluna"].ToString(),
                                Tipo = reader["Tipo"].ToString(),
                                Precision = Convert.ToInt32(reader["precision"]),
                                Scale = Convert.ToInt32(reader["scale"]),
                                MaxLength = Convert.ToInt32(reader["max_length"]),
                                IsNullable = Convert.ToBoolean(reader["is_nullable"])
                            });
                        }
                    }
                }
            }

            return colunas;
        }

        #endregion

        #region 📄 Módulo 04: Metadados e OCR

        public void SalvarMetadado(string projectIdAcc, string numeroNA, string descricao)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @projectIdAcc AND Numero_NA = @numeroNA)
                    BEGIN
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, Numero_NA, Descricao_Corporativa)
                        VALUES (@projectIdAcc, @numeroNA, @descricao)
                    END
                    ELSE
                    BEGIN
                        UPDATE Metadados_Projetos 
                        SET Descricao_Corporativa = @descricao,
                            Data_Ultima_Atualizacao = GETDATE()
                        WHERE Project_ID_ACC = @projectIdAcc AND Numero_NA = @numeroNA
                    END";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectIdAcc", projectIdAcc);
                    cmd.Parameters.AddWithValue("@numeroNA", numeroNA);
                    cmd.Parameters.AddWithValue("@descricao", descricao);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public DataTable GetProjetosPendentes()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT P.Nome_Projeto, P.Project_ID_ACC 
                    FROM Projetos P
                    LEFT JOIN Metadados_Projetos M ON P.Project_ID_ACC = M.Project_ID_ACC
                    WHERE M.Project_ID_ACC IS NULL";

                using (var cmd = new SqlCommand(query, conn))
                {
                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }

        public void SalvarDadosLdpUnificada(string projectId, Dictionary<int, object> rowData)
        {
            LdpEtapa11Diagnostico.Log(_logger, _configuration, "MODEL_ROW_DATA", projectId, rowData);
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = @"
                    INSERT INTO [dbo].[Ldp_Unificada] (
                        [Project_ID_ACC], [Item], [Schedule_ID], [Discipline], 
                        [NA_Document_Number], [Client_Document_Number], [Document_Title], 
                        [Format], [No_Pages], [Planned_Date_Baseline], [Replanned_Date], [Emission_Date], 
                        [Transmittal_Number], [Issued_Purpose], [Revision], [Document_Status], 
                        [WBS_Percent], [Status_In_Flow], [Situation], [Date_Comments_Received], [Comment_Status], 
                        [Percent_Planned_Physical], [Percent_Physical_Completed], [Inclusion_Date], [CR], [CR_OS], 
                        [Area], [Discipline_Activity], [Doc_Type_Code], [Seq], [Planned_HH], [Actual_HH], [Remaining_HH], 
                        [Planned_Cost], [Actual_Cost], [Remaining_Cost], [Data_Sincronizacao]
                    ) VALUES (
                        @ProjectID, @Item, @SchID, @Disc, @NADoc, @CliDoc, @Title, @Format, @Pages, 
                        @DtPlan, @DtReplan, @DtEmiss, @Transmittal, @Purpose, @Rev, @Status, @WBS, @Flow, @Sit, 
                        @DtComm, @CommSt, @PctPlan, @PctReal, @DtInc, @CR, @CROS, @Area, @Activity, @DocType, @Seq, 
                        @HHPlan, @HHReal, @HHRem, @CostPlan, @CostReal, @CostRem, GETDATE()
                    )";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ProjectID", projectId);
                    cmd.Parameters.AddWithValue("@Item", GetString(rowData, 1));
                    cmd.Parameters.AddWithValue("@SchID", GetString(rowData, 2));
                    cmd.Parameters.AddWithValue("@Disc", GetString(rowData, 3));
                    cmd.Parameters.AddWithValue("@NADoc", GetString(rowData, 4));
                    cmd.Parameters.AddWithValue("@CliDoc", GetString(rowData, 5));
                    cmd.Parameters.AddWithValue("@Title", GetString(rowData, 6));
                    cmd.Parameters.AddWithValue("@Format", GetString(rowData, 7));
                    cmd.Parameters.AddWithValue("@Pages", GetInt(rowData, 8));
                    cmd.Parameters.AddWithValue("@DtPlan", TratarData(rowData, 9));
                    cmd.Parameters.AddWithValue("@DtReplan", TratarData(rowData, 10));
                    cmd.Parameters.AddWithValue("@DtEmiss", TratarData(rowData, 11));
                    cmd.Parameters.AddWithValue("@Transmittal", GetString(rowData, 12));
                    cmd.Parameters.AddWithValue("@Purpose", GetString(rowData, 13));
                    cmd.Parameters.AddWithValue("@Rev", GetString(rowData, 14));
                    cmd.Parameters.AddWithValue("@Status", GetString(rowData, 15));
                    cmd.Parameters.AddWithValue("@WBS", GetDecimal(rowData, 16));
                    cmd.Parameters.AddWithValue("@Flow", GetString(rowData, 17));
                    cmd.Parameters.AddWithValue("@Sit", GetString(rowData, 18));
                    // Date_Comments_Received e Inclusion_Date são DATETIME no SQL.
                    // Nunca enviar texto e depender de conversão implícita do SQL Server: alguns projetos
                    // trazem datas em formatos/culturas diferentes e isso abortava TODA a Etapa 11.
                    cmd.Parameters.AddWithValue("@DtComm", TratarData(rowData, 19));
                    cmd.Parameters.AddWithValue("@CommSt", GetString(rowData, 20));
                    cmd.Parameters.AddWithValue("@PctPlan", GetDecimal(rowData, 21));
                    cmd.Parameters.AddWithValue("@PctReal", GetDecimal(rowData, 22));
                    cmd.Parameters.AddWithValue("@DtInc", TratarData(rowData, 23));
                    cmd.Parameters.AddWithValue("@CR", GetString(rowData, 24));
                    cmd.Parameters.AddWithValue("@CROS", GetString(rowData, 25));
                    cmd.Parameters.AddWithValue("@Area", GetString(rowData, 26));
                    cmd.Parameters.AddWithValue("@Activity", GetString(rowData, 27));
                    cmd.Parameters.AddWithValue("@DocType", GetString(rowData, 28));
                    cmd.Parameters.AddWithValue("@Seq", GetString(rowData, 29));
                    cmd.Parameters.AddWithValue("@HHPlan", GetDecimal(rowData, 30));
                    cmd.Parameters.AddWithValue("@HHReal", GetDecimal(rowData, 31));
                    cmd.Parameters.AddWithValue("@HHRem", GetDecimal(rowData, 32));
                    cmd.Parameters.AddWithValue("@CostPlan", GetDecimal(rowData, 33));
                    cmd.Parameters.AddWithValue("@CostReal", GetDecimal(rowData, 34));
                    cmd.Parameters.AddWithValue("@CostRem", GetDecimal(rowData, 35));

                    LdpEtapa11Diagnostico.Log(_logger, _configuration, "ANTES_INSERT_PARAMETROS", projectId,
                        new Dictionary<int, object>
                        {
                            [4] = cmd.Parameters["@NADoc"].Value,
                            [30] = cmd.Parameters["@HHPlan"].Value, [31] = cmd.Parameters["@HHReal"].Value,
                            [32] = cmd.Parameters["@HHRem"].Value, [33] = cmd.Parameters["@CostPlan"].Value,
                            [34] = cmd.Parameters["@CostReal"].Value, [35] = cmd.Parameters["@CostRem"].Value
                        });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        private object GetString(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string val = row[index].ToString().Trim();
            return string.IsNullOrEmpty(val) ? DBNull.Value : val;
        }

        private object GetInt(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string val = row[index].ToString().Trim();
            if (int.TryParse(val, out int result)) return result;
            return DBNull.Value;
        }

        private object GetDecimal(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string val = row[index].ToString().Trim().Replace("R$", "").Replace("%", "").Trim();
            if (decimal.TryParse(val, out decimal result)) return result;
            if (decimal.TryParse(val, new CultureInfo("pt-BR"), out decimal resBr)) return resBr;
            return DBNull.Value;
        }

        private object TratarData(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string valor = row[index].ToString().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(valor) || valor == "-" || valor == "N/A" || valor == "TBD") return DBNull.Value;

            if (valor.Length > 8 && char.IsLetter(valor[0]))
            {
                int firstSpace = valor.IndexOf(' ');
                if (firstSpace != -1) valor = valor.Substring(firstSpace + 1);
            }

            if (DateTime.TryParse(valor, new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime data))
            {
                if (data.Year < 1900 || data.Year > 2100) return DBNull.Value;
                return data;
            }

            if (double.TryParse(valor, out double serial))
            {
                try {
                     var d = DateTime.FromOADate(serial);
                     if (d.Year < 1900 || d.Year > 2100) return DBNull.Value;
                     return d;
                } catch {}
            }

            return DBNull.Value;
        }

        #endregion

        #region 🗄️ Módulo 05: Cache Persistente de Sincronização de Atributos ACC

        /// <summary>
        /// Carrega, em UMA ÚNICA consulta, todos os estados de sincronização de atributos já gravados
        /// para este projeto (nunca um SELECT por arquivo). A tabela dbo.ACC_Attribute_Sync_State ainda
        /// não tem índice único composto (Project_ID_ACC, VersionUrn, ChaveConfiguracao) — se existirem
        /// registros duplicados para a mesma combinação, fica com o mais recente por
        /// DataUltimaTentativa DESC, Id DESC (via ROW_NUMBER). A chave do dicionário em memória combina
        /// VersionUrn + ChaveConfiguracao (ver LdpUpdateService.MontarChaveEstado) porque o mesmo
        /// VersionUrn pode, em tese, aparecer em mais de uma configuração.
        /// </summary>
        public Dictionary<string, AccAttributeSyncState> ObterEstadosSincronizacaoAtributos(string projectIdAcc)
        {
            var resultado = new Dictionary<string, AccAttributeSyncState>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    WITH EstadosOrdenados AS
                    (
                        SELECT
                            Id, Project_ID_ACC, VersionUrn, ChaveConfiguracao, PayloadHash,
                            FileName, PastaEncontrada, UltimaTentativaSucesso,
                            DataUltimaTentativa, DataUltimoSucesso, UltimoErro,
                            ROW_NUMBER() OVER
                            (
                                PARTITION BY Project_ID_ACC, VersionUrn, ChaveConfiguracao
                                ORDER BY DataUltimaTentativa DESC, Id DESC
                            ) AS rn
                        FROM dbo.ACC_Attribute_Sync_State
                        WHERE Project_ID_ACC = @ProjectIdAcc
                    )
                    SELECT
                        Id, Project_ID_ACC, VersionUrn, ChaveConfiguracao, PayloadHash,
                        FileName, PastaEncontrada, UltimaTentativaSucesso,
                        DataUltimaTentativa, DataUltimoSucesso, UltimoErro
                    FROM EstadosOrdenados
                    WHERE rn = 1;";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ProjectIdAcc", projectIdAcc);

                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var estado = new AccAttributeSyncState
                            {
                                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                                ProjectIdAcc = reader["Project_ID_ACC"] as string,
                                VersionUrn = (reader["VersionUrn"] as string)?.Trim(),
                                ChaveConfiguracao = (reader["ChaveConfiguracao"] as string)?.Trim(),
                                PayloadHash = (reader["PayloadHash"] as string)?.Trim(),
                                FileName = reader["FileName"] as string,
                                PastaEncontrada = reader["PastaEncontrada"] as string,
                                UltimaTentativaSucesso = reader.GetBoolean(reader.GetOrdinal("UltimaTentativaSucesso")),
                                DataUltimaTentativa = reader.GetDateTime(reader.GetOrdinal("DataUltimaTentativa")),
                                DataUltimoSucesso = reader["DataUltimoSucesso"] == DBNull.Value ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("DataUltimoSucesso")),
                                UltimoErro = reader["UltimoErro"] as string
                            };

                            string chave = $"{estado.VersionUrn}{estado.ChaveConfiguracao}";
                            resultado[chave] = estado;
                        }
                    }
                }
            }

            return resultado;
        }

        /// <summary>
        /// Grava (upsert) o estado de sincronização de uma VersionUrn + ChaveConfiguracao. Como a tabela
        /// ainda não tem índice único, o upsert é protegido por transação com UPDLOCK/HOLDLOCK: tenta
        /// UPDATE primeiro; se nenhuma linha for afetada, faz o INSERT (e recupera o Id gerado). Em caso
        /// de erro, a transação é revertida — o chamador decide como tratar essa falha (ver comentários em
        /// LdpUpdateService: uma falha ao GRAVAR o cache depois de um POST bem-sucedido no ACC não deve
        /// travar a sincronização, só significa que o arquivo pode ser reenviado na próxima execução).
        /// </summary>
        public AccAttributeSyncState SalvarEstadoSincronizacaoAtributos(AccAttributeSyncState estado)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var transacao = conn.BeginTransaction())
                {
                    try
                    {
                        int linhasAfetadas;
                        using (var cmdUpdate = conn.CreateCommand())
                        {
                            cmdUpdate.Transaction = transacao;
                            cmdUpdate.CommandText = @"
                                UPDATE dbo.ACC_Attribute_Sync_State WITH (UPDLOCK, HOLDLOCK)
                                SET
                                    PayloadHash = @PayloadHash,
                                    FileName = @FileName,
                                    PastaEncontrada = @PastaEncontrada,
                                    UltimaTentativaSucesso = @UltimaTentativaSucesso,
                                    DataUltimaTentativa = @DataUltimaTentativa,
                                    DataUltimoSucesso = @DataUltimoSucesso,
                                    UltimoErro = @UltimoErro
                                WHERE
                                    Project_ID_ACC = @ProjectIdAcc
                                    AND VersionUrn = @VersionUrn
                                    AND ChaveConfiguracao = @ChaveConfiguracao;";

                            AdicionarParametrosEstadoSincronizacao(cmdUpdate, estado);
                            linhasAfetadas = cmdUpdate.ExecuteNonQuery();
                        }

                        if (linhasAfetadas == 0)
                        {
                            using (var cmdInsert = conn.CreateCommand())
                            {
                                cmdInsert.Transaction = transacao;
                                cmdInsert.CommandText = @"
                                    INSERT INTO dbo.ACC_Attribute_Sync_State
                                    (
                                        Project_ID_ACC, VersionUrn, ChaveConfiguracao, PayloadHash,
                                        FileName, PastaEncontrada, UltimaTentativaSucesso,
                                        DataUltimaTentativa, DataUltimoSucesso, UltimoErro
                                    )
                                    VALUES
                                    (
                                        @ProjectIdAcc, @VersionUrn, @ChaveConfiguracao, @PayloadHash,
                                        @FileName, @PastaEncontrada, @UltimaTentativaSucesso,
                                        @DataUltimaTentativa, @DataUltimoSucesso, @UltimoErro
                                    );
                                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                                AdicionarParametrosEstadoSincronizacao(cmdInsert, estado);
                                var novoId = cmdInsert.ExecuteScalar();
                                if (novoId != null && novoId != DBNull.Value) estado.Id = Convert.ToInt64(novoId);
                            }
                        }

                        transacao.Commit();
                    }
                    catch
                    {
                        transacao.Rollback();
                        throw;
                    }
                }
            }

            return estado;
        }

        private static void AdicionarParametrosEstadoSincronizacao(SqlCommand cmd, AccAttributeSyncState estado)
        {
            cmd.Parameters.AddWithValue("@ProjectIdAcc", (object)estado.ProjectIdAcc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@VersionUrn", (object)estado.VersionUrn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ChaveConfiguracao", (object)estado.ChaveConfiguracao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PayloadHash", (object)estado.PayloadHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FileName", (object)estado.FileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PastaEncontrada", (object)estado.PastaEncontrada ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UltimaTentativaSucesso", estado.UltimaTentativaSucesso);
            cmd.Parameters.AddWithValue("@DataUltimaTentativa", estado.DataUltimaTentativa);
            cmd.Parameters.AddWithValue("@DataUltimoSucesso", (object)estado.DataUltimoSucesso ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UltimoErro", (object)estado.UltimoErro ?? DBNull.Value);
        }

        #endregion

        #region 🌊 ACC_REVIEW_STAGE — Snapshot da etapa atual dos ACC Reviews

        /// <summary>
        /// Persiste o snapshot inteiro de [dbo].[ACC_Review_Current_Stage] de UM projeto em uma ÚNICA
        /// SqlConnection/SqlTransaction (seção 11 da funcionalidade "ACC_REVIEW_STAGE"), no mesmo padrão
        /// de QualidadeRepository.PersistirAsync: 1) conta registros anteriores do projeto, 2) DELETE
        /// somente do projeto atual (nunca DELETE global), 3) SqlBulkCopy do DataTable já preparado pelo
        /// chamador (AccReviewStageSnapshotService — esta camada nunca decide consolidação/conflito,
        /// só persiste). Qualquer exceção provoca rollback único: o snapshot anterior do projeto é
        /// preservado intacto, nenhum resultado parcial é gravado.
        /// </summary>
        public ResultadoPersistenciaSnapshotEtapaReview SalvarSnapshotEtapasReviewTransacional(string projectIdAcc, DataTable snapshot)
        {
            var cronometro = Stopwatch.StartNew();
            var resultado = new ResultadoPersistenciaSnapshotEtapaReview();

            _logger.LogInformation(
                "[ACC_REVIEW_STAGE][SNAPSHOT][INICIO] Project_ID_ACC={ProjectIdAcc} | RegistrosPreparados={Preparados}",
                projectIdAcc, snapshot.Rows.Count);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var transacao = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmdCount = conn.CreateCommand())
                        {
                            cmdCount.Transaction = transacao;
                            cmdCount.CommandText = "SELECT COUNT(*) FROM [dbo].[ACC_Review_Current_Stage] WHERE [Project_ID_ACC] = @pid";
                            cmdCount.Parameters.AddWithValue("@pid", projectIdAcc);
                            resultado.RegistrosAnteriores = (int)cmdCount.ExecuteScalar();
                        }

                        using (var cmdDelete = conn.CreateCommand())
                        {
                            cmdDelete.Transaction = transacao;
                            cmdDelete.CommandText = "DELETE FROM [dbo].[ACC_Review_Current_Stage] WHERE [Project_ID_ACC] = @pid";
                            cmdDelete.Parameters.AddWithValue("@pid", projectIdAcc);
                            int removidos = cmdDelete.ExecuteNonQuery();

                            _logger.LogInformation(
                                "[ACC_REVIEW_STAGE][SNAPSHOT][DELETE] Project_ID_ACC={ProjectIdAcc} | RegistrosRemovidos={Removidos}",
                                projectIdAcc, removidos);
                        }

                        using (var bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, transacao))
                        {
                            bulkCopy.DestinationTableName = "[dbo].[ACC_Review_Current_Stage]";
                            bulkCopy.BulkCopyTimeout = 300;
                            bulkCopy.BatchSize = 1000;

                            // Mapeamento explícito NOMEADO — cada coluna do DataTable já é nomeada como a
                            // coluna SQL real (ver AccReviewStageSnapshotService). Nunca por posição/ordinal.
                            foreach (DataColumn coluna in snapshot.Columns)
                            {
                                bulkCopy.ColumnMappings.Add(coluna.ColumnName, coluna.ColumnName);
                            }

                            bulkCopy.WriteToServer(snapshot);
                        }

                        resultado.RegistrosInseridos = snapshot.Rows.Count;

                        transacao.Commit();
                        resultado.Sucesso = true;
                        cronometro.Stop();

                        _logger.LogInformation(
                            "[ACC_REVIEW_STAGE][SNAPSHOT][COMMIT] Project_ID_ACC={ProjectIdAcc} | RegistrosAnteriores={Anteriores} | RegistrosInseridos={Inseridos} | DuracaoMs={DuracaoMs}",
                            projectIdAcc, resultado.RegistrosAnteriores, resultado.RegistrosInseridos, cronometro.Elapsed.TotalMilliseconds);

                        return resultado;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            transacao.Rollback();
                            resultado.RollbackExecutado = true;
                        }
                        catch (Exception exRollback)
                        {
                            _logger.LogError(exRollback, "[ACC_REVIEW_STAGE][SNAPSHOT][ROLLBACK] Falha adicional ao executar o rollback.");
                        }

                        resultado.Sucesso = false;
                        resultado.Erro = ex.Message;
                        cronometro.Stop();

                        _logger.LogError(
                            ex,
                            "[ACC_REVIEW_STAGE][SNAPSHOT][ROLLBACK] Project_ID_ACC={ProjectIdAcc} | RegistrosPreparados={Preparados} | DuracaoMs={DuracaoMs} | RollbackExecutado={RollbackExecutado} | Snapshot anterior preservado.",
                            projectIdAcc, snapshot.Rows.Count, cronometro.Elapsed.TotalMilliseconds, resultado.RollbackExecutado ? "sim" : "não");

                        return resultado;
                    }
                }
            }
        }

        #endregion
    }
}
