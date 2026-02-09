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

        /// <summary>
        /// Busca todos os Hubs cadastrados para iniciar a sincronização com o ACC.
        /// </summary>
        /// <returns>DataTable com as informações dos Hubs.</returns>
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
                        conn.Open(); // Se houver erro de logon, o erro aparecerá no console para diagnóstico
                        using (var da = new SqlDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                    catch (SqlException ex)
                    {
                        // Lançamos a exceção para que o Program.cs capture o erro real de logon
                        throw new Exception($"Falha de conexão com o SQL Server: {ex.Message}", ex);
                    }

                    return dt;
                }
            }
        }

        public void SalvarMetadado(string projectIdAcc, string numeroNA, string descricao)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Query modular que insere ou atualiza o metadado do projeto
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
                    // Busca projetos que não possuem entrada na tabela de Metadados_Projetos
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

        /// <summary>
        /// Salva ou atualiza um projeto vindo da API da Autodesk no banco de dados local.
        /// </summary>
        /// <param name="hubContaId">ID interno do Hub no banco.</param>
        /// <param name="nomeProjeto">Nome do projeto no ACC.</param>
        /// <param name="projectIdAcc">ID único do projeto na Autodesk.</param>
        public void SalvarProjeto(int hubContaId, string nomeProjeto, string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Query modular para evitar duplicidade de projetos
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
            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                // Esta query busca apenas os projetos que você marcou como Is_Ativo = 1 no SSMS
                string query = @"
                    SELECT P.Project_ID_ACC, P.Nome_Projeto, H.Account_ID_ACC 
                    FROM Projetos P
                    JOIN Hubs_Conta H ON P.HubContaID = H.HubContaID
                    WHERE P.Is_Ativo = 1";

                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(query, conn))
                {
                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new Microsoft.Data.SqlClient.SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }




    }
}