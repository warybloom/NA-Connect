using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions; // 🎯 Necessário para normalizar o código do documento
using System.Globalization; // 🎯 Necessário para a formatação da data
using NA_Connect.Models;

namespace NA_Connect.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            // Agora ele busca especificamente o seu banco local
            _connectionString = configuration.GetConnectionString("LocalConnection");
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
                    WHERE Project_ID_ACC = @pid";

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
                    cmd.Parameters.AddWithValue("@DtComm", GetString(rowData, 19));
                    cmd.Parameters.AddWithValue("@CommSt", GetString(rowData, 20));
                    cmd.Parameters.AddWithValue("@PctPlan", GetDecimal(rowData, 21));
                    cmd.Parameters.AddWithValue("@PctReal", GetDecimal(rowData, 22));
                    cmd.Parameters.AddWithValue("@DtInc", GetString(rowData, 23));
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
        public void SalvarDadosHoldControl(string projectId, Dictionary<int, object> rowData)
{
    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();
        
        // Correção: Removida a vírgula do início e adicionados os parâmetros correspondentes no VALUES
        string sql = @"
            INSERT INTO [dbo].[tb_HoldControl] (
                [Project_ID_ACC],
                [Data_Sincronizacao],
                [ITEM],
                [N_HOLD],
                [N_NA_DOCUMENTO],
                [DISCIPLINA],
                [TITULO_DOCUMENTO],
                [REV],
                [MOTIVO_HOLD],
                [STATUS],
                [RESPONSAVEL]
            ) VALUES (
                @Project_ID_ACC, 
                GETDATE(), -- Insere a data e hora atual do servidor SQL de forma automática
                @Item, 
                @NumeroHold, 
                @DocumentoNeA, 
                @Disciplina, 
                @TituloDocumento, 
                @Rev, 
                @MotivoHold, 
                @Status, 
                @Responsavel
            )";

        using (var cmd = new SqlCommand(sql, conn))
        {
            // Vinculando os parâmetros corretamente
            cmd.Parameters.AddWithValue("@Project_ID_ACC", projectId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Item", GetString(rowData, 1) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@NumeroHold", GetString(rowData, 2) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@DocumentoNeA", GetString(rowData, 3) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Disciplina", GetString(rowData, 4) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TituloDocumento", GetString(rowData, 5) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Rev", GetString(rowData, 6) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@MotivoHold", GetString(rowData, 7) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", GetString(rowData, 8) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Responsavel", GetString(rowData, 9) ?? (object)DBNull.Value);

            cmd.ExecuteNonQuery();
        }
    }
}
public void SalvarDadosQuality(string projectId, Dictionary<int, object> rowData)
{
    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();
        
        string sql = @"
            INSERT INTO [dbo].[NCR NONCONFORMITY REPORT] (
                [Cliente], [GRD], [CodigoNA], [CodigoCliente], [Descricao],
                [Disciplina], [Revisao], [NumeroVias], [Mes], [DataEnvio],
                [Emitente], [DataCriacao], [DataAtualizacao], [CR]
            ) VALUES (
                @Cliente, @GRD, @CodigoNA, @CodigoCliente, @Descricao,
                @Disciplina, @Revisao, @NumeroVias, @Mes, @DataEnvio,
                @Emitente, GETDATE(), GETDATE(), @CR
            )";

        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@Cliente", GetLimitedString(rowData, 1, 255));
            cmd.Parameters.AddWithValue("@CR", GetLimitedString(rowData, 2, 100));
            cmd.Parameters.AddWithValue("@GRD", GetLimitedString(rowData, 3, 100));
            cmd.Parameters.AddWithValue("@CodigoNA", GetLimitedString(rowData, 4, 100));
            cmd.Parameters.AddWithValue("@CodigoCliente", GetLimitedString(rowData, 5, 100));
            cmd.Parameters.AddWithValue("@Descricao", GetLimitedString(rowData, 6, 2000));
            cmd.Parameters.AddWithValue("@Disciplina", GetLimitedString(rowData, 7, 255));
            cmd.Parameters.AddWithValue("@Revisao", GetLimitedString(rowData, 8, 50));
            
            // Tratamento preventivo para NumeroVias (caso seja INT)
            string nViaTexto = GetLimitedString(rowData, 9, 50)?.ToString();
            if (int.TryParse(nViaTexto, out int nViasInt)) {
                cmd.Parameters.AddWithValue("@NumeroVias", nViasInt);
            } else {
                cmd.Parameters.AddWithValue("@NumeroVias", nViaTexto ?? (object)DBNull.Value);
            }

            // 🌟 CORREÇÃO AQUI: Converte "MAIO" ou "JAN" para número (tinyint)
            string mesTexto = GetLimitedString(rowData, 10, 50)?.ToString();
            byte? mesNumero = ConverterMesParaByte(mesTexto);
            cmd.Parameters.AddWithValue("@Mes", mesNumero.HasValue ? (object)mesNumero.Value : DBNull.Value);

            cmd.Parameters.AddWithValue("@DataEnvio", GetLimitedString(rowData, 11, 100));  
            cmd.Parameters.AddWithValue("@Emitente", GetLimitedString(rowData, 12, 255));

            cmd.ExecuteNonQuery();
        }
    }
}

// 🧭 Função auxiliar para converter o nome do mês em número (tinyint / byte)
private byte? ConverterMesParaByte(string mesTexto)
{
    if (string.IsNullOrWhiteSpace(mesTexto)) return null;

    string mesProcurado = mesTexto.Trim().ToUpper();

    // Se já for um número na planilha (ex: "5" ou "05"), apenas converte direto
    if (byte.TryParse(mesProcurado, out byte resultadoNumero))
    {
        return resultadoNumero;
    }

    // Se for texto por extenso ou abreviado, faz o de/para:
    if (mesProcurado.StartsWith("JAN")) return 1;
    if (mesProcurado.StartsWith("FEV") || mesProcurado.StartsWith("FEB")) return 2;
    if (mesProcurado.StartsWith("MAR")) return 3;
    if (mesProcurado.StartsWith("ABR") || mesProcurado.StartsWith("APR")) return 4;
    if (mesProcurado.StartsWith("MAI")) return 5; // Trata "MAIO" ou "MAI"
    if (mesProcurado.StartsWith("JUN")) return 6;
    if (mesProcurado.StartsWith("JUL")) return 7;
    if (mesProcurado.StartsWith("AGO") || mesProcurado.StartsWith("AUG")) return 8;
    if (mesProcurado.StartsWith("SET") || mesProcurado.StartsWith("SEP")) return 9;
    if (mesProcurado.StartsWith("OUT") || mesProcurado.StartsWith("OCT")) return 10;
    if (mesProcurado.StartsWith("NOV")) return 11;
    if (mesProcurado.StartsWith("DEZ") || mesProcurado.StartsWith("DEC")) return 12;

    return null; // Caso venha algum texto inválido
}

private object GetLimitedString(Dictionary<int, object> rowData, int index, int maxLength)
{
    if (rowData != null && rowData.TryGetValue(index, out var value) && value != null)
    {
        string text = value.ToString().Trim();
        if (text.Length > maxLength)
        {
            return text.Substring(0, maxLength);
        }
        return text;
    }
    return null;
}




        







        #endregion
    }
}