using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace NA_Connect.Services
{
    /// <summary>
    /// Serviço responsável pela persistência de dados no SQL Server LocalDB.
    /// Parte integrante da automação N&A Connect.
    /// </summary>
    public class DatabaseService
    {
        // Usando o endereço estável do LocalDB para garantir a conexão pelo Visual Studio
        private readonly string _connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=DB_NA_CONNECT;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;";

        #region Módulo de Hubs e Projetos Base

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
                        throw new Exception($"Falha de conexão com o SQL Server: {ex.Message}", ex);
                    }

                    return dt;
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
                        INSERT INTO Projetos (HubContaID, Nome_Projeto, Project_ID_ACC)
                        VALUES (@hubContaId, @nomeProjeto, @projectIdAcc)
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

        #region Módulo de Metadados e LDP (Cache Inteligente)

        /// <summary>
        /// Busca o ID do arquivo LDP armazenado no banco para evitar chamadas excessivas à API.
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
        /// Salva o ID do arquivo LDP no banco após a descoberta dinâmica.
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

        public void SalvarMetadado(string projectIdAcc, string numeroNA, string descricao)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @projectIdAcc)
                    BEGIN
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, Numero_NA, Descricao_Corporativa)
                        VALUES (@projectIdAcc, @numeroNA, @descricao)
                    END
                    ELSE
                    BEGIN
                        UPDATE Metadados_Projetos 
                        SET Numero_NA = @numeroNA, 
                            Descricao_Corporativa = @descricao,
                            Data_Ultima_Atualizacao = GETDATE()
                        WHERE Project_ID_ACC = @projectIdAcc
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

        #endregion

        #region Módulo de Cronograma (Etapa 8)

        /// <summary>
        /// Persiste os dados extraídos do MS Project (MPP) no SQL.
        /// </summary>
public void SalvarTarefaCronograma(
    string projectIdAcc, string uid, string docNum, string name, 
    decimal phys, decimal pred, string wbsPct, 
    DateTime? start, DateTime? baseline, DateTime? finish, 
    string edt, string projeto)
{
    using (var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
    {
        // Query modular com os nomes de colunas oficiais
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

        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(query, conn))
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
    }
}