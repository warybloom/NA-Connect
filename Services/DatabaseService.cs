using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using NA_Connect.Models; // 🎯 Referência ao modelo de descoberta

namespace NA_Connect.Services
{
    /// <summary>
    /// Serviço responsável pela persistência de dados no SQL Server LocalDB.
    /// Parte integrante da automação N&A Connect para gestão de BIM e AEC.
    /// </summary>
    public class DatabaseService
    {
        // Endereço estável do LocalDB para garantir a conexão pelo Visual Studio
        private readonly string _connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=DB_NA_CONNECT;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;";

        #region 🏗️ Módulo 01: Hubs e Projetos Base

        /// <summary>
        /// Busca todos os Hubs cadastrados para iniciar a sincronização com o ACC.
        /// </summary>
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

        // Módulo: NA_Connect.Services/DatabaseService.cs

/// <summary>
/// Registra ou atualiza os metadados vitais do projeto (Etapa 0).
/// </summary>
// Módulo: NA_Connect.Services/DatabaseService.cs

// Módulo: NA_Connect.Services/DatabaseService.cs

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
            cmd.Parameters.AddWithValue("@lang", idioma); // 🎯 Novo
            cmd.Parameters.AddWithValue("@std", padrao);   // 🎯 Novo

            conn.Open();
            cmd.ExecuteNonQuery();
        }
    }
}
// Em NA_Connect.Services/DatabaseService.cs

public string ObterColunaChaveProjeto(string projectId)
{
    using (var conn = new SqlConnection(_connectionString))
    {
        // Remove prefixo 'b.' se vier, para garantir compatibilidade
        string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
        
        // Tenta achar com ou sem prefixo
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

            // Se não tiver nada no banco, retorna o padrão do sistema
            return !string.IsNullOrEmpty(valor) ? valor : "N&A DOCUMENT NUMBER";
        }
    }
}



        /// <summary>
        /// Salva ou atualiza um projeto vindo da API da Autodesk no banco de dados local.
        /// </summary>
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

        /// <summary>
        /// Retorna os projetos marcados como ativos para processamento pelo Worker.
        /// </summary>
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

        /// <summary>
        /// Recupera o contexto (Padrão e Idioma) salvo no banco para evitar re-descoberta.
        /// </summary>
        public ProjectDiscoveryResult ObterContextoProjeto(string projectId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Busca as configurações de padrão de nome e idioma do projeto
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

        /// <summary>
        /// Persiste a descoberta do padrão do projeto no banco de dados.
        /// </summary>
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

        /// <summary>
        /// Busca o ID do arquivo LDP armazenado no banco para economia de chamadas à API.
        /// </summary>
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

        /// <summary>
        /// Atualiza o cache do ID do arquivo LDP no banco de dados.
        /// </summary>
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

        #region 📅 Módulo 03: Cronograma e Power BI (Etapa 8/9)

        /// <summary>
        /// Persiste os dados extraídos do MS Project (MPP) no SQL. 
        /// Crucial para o funcionamento do Gráfico de Gantt no Power BI.
        /// </summary>
        public void SalvarTarefaCronograma(
            string projectIdAcc, string uid, string docNum, string name, 
            decimal phys, decimal pred, string wbsPct, 
            DateTime? start, DateTime? baseline, DateTime? finish, 
            string edt, string projeto)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Query modular ajustada para os nomes comerciais solicitados (NA_DOCUMENT_NUMBER, CR_OS, etc.)
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Cronograma_Projetos WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid)
                    BEGIN
                        INSERT INTO Cronograma_Projetos (
                            Project_ID_ACC, UID_Tarefa, NA_DOCUMENT_NUMBER, TASK_NAME, 
                            PERCENT_PHYSICAL_COMPLETED, PERCENT_PREDICTED_PHYSICAL, WBS_PERCENT,
                            PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE, 
                            EDT_WBS, CR_OS)
                        VALUES (@pid, @uid, @docNum, @name, @phys, @pred, @wbsPct, @start, @baseline, @finish, @edt, @projeto)
                    END
                    ELSE
                    BEGIN
                        UPDATE Cronograma_Projetos SET 
                            NA_DOCUMENT_NUMBER = @docNum, TASK_NAME = @name, 
                            PERCENT_PHYSICAL_COMPLETED = @phys, PERCENT_PREDICTED_PHYSICAL = @pred, 
                            WBS_PERCENT = @wbsPct, PLANNED_DATE_START = @start, 
                            PLANNED_DATA_BASELINE = @baseline, REPLANNED_DATE = @finish, 
                            EDT_WBS = @edt, CR_OS = @projeto,
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


// ==================================================================================
        // MÉTODO DE INSERÇÃO NA TABELA Ldp_Unificada (CORRIGIDO E BLINDADO)
        // ==================================================================================
        public void SalvarDadosLdpUnificada(string projectId, Dictionary<int, object> rowData)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Query EXATA com as colunas que você mandou
                string sql = @"
                    INSERT INTO [dbo].[Ldp_Unificada] (
                        [Project_ID_ACC], 
                        [Item], [Schedule_ID], [Discipline], 
                        [NA_Document_Number], [Client_Document_Number], [Document_Title], 
                        [Format], [No_Pages], 
                        [Planned_Date_Baseline], [Replanned_Date], [Emission_Date], 
                        [Transmittal_Number], [Issued_Purpose], [Revision], [Document_Status], 
                        [WBS_Percent], [Status_In_Flow], [Situation], 
                        [Date_Comments_Received], [Comment_Status], 
                        [Percent_Planned_Physical], [Percent_Physical_Completed], 
                        [Inclusion_Date], [CR], [CR_OS], 
                        [Area], [Discipline_Activity], [Doc_Type_Code], [Seq], 
                        [Planned_HH], [Actual_HH], [Remaining_HH], 
                        [Planned_Cost], [Actual_Cost], [Remaining_Cost], 
                        [Data_Sincronizacao]
                    ) VALUES (
                        @ProjectID, 
                        @Item, @SchID, @Disc, 
                        @NADoc, @CliDoc, @Title, 
                        @Format, @Pages, 
                        @DtPlan, @DtReplan, @DtEmiss, 
                        @Transmittal, @Purpose, @Rev, @Status, 
                        @WBS, @Flow, @Sit, 
                        @DtComm, @CommSt, 
                        @PctPlan, @PctReal, 
                        @DtInc, @CR, @CROS, 
                        @Area, @Activity, @DocType, @Seq, 
                        @HHPlan, @HHReal, @HHRem, 
                        @CostPlan, @CostReal, @CostRem, 
                        GETDATE()
                    )";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    // --- PARÂMETROS GERAIS ---
                    cmd.Parameters.AddWithValue("@ProjectID", projectId);

                    // --- COLUNAS DE TEXTO (1 a 8) ---
                    cmd.Parameters.AddWithValue("@Item", GetString(rowData, 1));
                    cmd.Parameters.AddWithValue("@SchID", GetString(rowData, 2));
                    cmd.Parameters.AddWithValue("@Disc", GetString(rowData, 3));
                    cmd.Parameters.AddWithValue("@NADoc", GetString(rowData, 4));
                    cmd.Parameters.AddWithValue("@CliDoc", GetString(rowData, 5));
                    cmd.Parameters.AddWithValue("@Title", GetString(rowData, 6));
                    cmd.Parameters.AddWithValue("@Format", GetString(rowData, 7));
                    
                    // --- NÚMERO INTEIRO (Páginas) ---
                    cmd.Parameters.AddWithValue("@Pages", GetInt(rowData, 8));

                    // --- DATAS (Onde costuma dar erro) - Colunas 9, 10, 11 ---
                    // Usa a função TratarData para converter texto em Data SQL
                    cmd.Parameters.AddWithValue("@DtPlan", TratarData(rowData, 9));
                    cmd.Parameters.AddWithValue("@DtReplan", TratarData(rowData, 10));
                    cmd.Parameters.AddWithValue("@DtEmiss", TratarData(rowData, 11));

                    // --- TEXTOS (12 a 15) ---
                    cmd.Parameters.AddWithValue("@Transmittal", GetString(rowData, 12));
                    cmd.Parameters.AddWithValue("@Purpose", GetString(rowData, 13));
                    cmd.Parameters.AddWithValue("@Rev", GetString(rowData, 14));
                    cmd.Parameters.AddWithValue("@Status", GetString(rowData, 15));

                    // --- NUMÉRICO (Porcentagem WBS) ---
                    cmd.Parameters.AddWithValue("@WBS", GetDecimal(rowData, 16));

                    // --- TEXTOS (17 a 18) ---
                    cmd.Parameters.AddWithValue("@Flow", GetString(rowData, 17));
                    cmd.Parameters.AddWithValue("@Sit", GetString(rowData, 18));

                    // --- DATA COMO TEXTO (Pedido específico) ---
                    cmd.Parameters.AddWithValue("@DtComm", GetString(rowData, 19));
                    cmd.Parameters.AddWithValue("@CommSt", GetString(rowData, 20));

                    // --- NUMÉRICOS (Físico) ---
                    cmd.Parameters.AddWithValue("@PctPlan", GetDecimal(rowData, 21));
                    cmd.Parameters.AddWithValue("@PctReal", GetDecimal(rowData, 22));

                    // --- DATA COMO TEXTO ---
                    cmd.Parameters.AddWithValue("@DtInc", GetString(rowData, 23));

                    // --- TEXTOS DIVERSOS ---
                    cmd.Parameters.AddWithValue("@CR", GetString(rowData, 24));
                    cmd.Parameters.AddWithValue("@CROS", GetString(rowData, 25));
                    cmd.Parameters.AddWithValue("@Area", GetString(rowData, 26));
                    cmd.Parameters.AddWithValue("@Activity", GetString(rowData, 27));
                    cmd.Parameters.AddWithValue("@DocType", GetString(rowData, 28));
                    cmd.Parameters.AddWithValue("@Seq", GetString(rowData, 29));

                    // --- HH (Numéricos) ---
                    cmd.Parameters.AddWithValue("@HHPlan", GetDecimal(rowData, 30));
                    cmd.Parameters.AddWithValue("@HHReal", GetDecimal(rowData, 31));
                    cmd.Parameters.AddWithValue("@HHRem", GetDecimal(rowData, 32));

                    // --- CUSTOS (Numéricos) ---
                    cmd.Parameters.AddWithValue("@CostPlan", GetDecimal(rowData, 33));
                    cmd.Parameters.AddWithValue("@CostReal", GetDecimal(rowData, 34));
                    cmd.Parameters.AddWithValue("@CostRem", GetDecimal(rowData, 35));

                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ==================================================================================
        // FUNÇÕES AUXILIARES (HELPERS) - PARA NÃO QUEBRAR O SQL
        // ==================================================================================

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
            string val = row[index].ToString().Trim()
                .Replace("R$", "")
                .Replace("%", "")
                .Trim();
            
            // Tenta converter (aceita ponto ou virgula)
            if (decimal.TryParse(val, out decimal result)) return result;
            if (decimal.TryParse(val, new System.Globalization.CultureInfo("pt-BR"), out decimal resBr)) return resBr;
            
            return DBNull.Value;
        }

        private object TratarData(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string valor = row[index].ToString().Trim().ToUpper();

            // Lista de lixos
            if (string.IsNullOrWhiteSpace(valor) || valor == "-" || valor == "N/A" || valor == "TBD") return DBNull.Value;

            // 1. Tenta PT-BR
            if (DateTime.TryParse(valor, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out DateTime data))
            {
                if (data.Year < 1900 || data.Year > 2100) return DBNull.Value;
                return data;
            }

            // 2. Tenta Serial Excel
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
    } 
    #endregion
}