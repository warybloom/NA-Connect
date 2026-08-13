using System;
using Microsoft.Data.SqlClient; // Referência moderna instalada via dotnet add package
using Microsoft.Extensions.Configuration;
using System.Data;

namespace ForgeSync.Services
{
    public class SaaSService
    {
        private readonly string _connectionString;

        public SaaSService(IConfiguration configuration)
        {
            // Puxando a conexão do banco ForgeSync (seu banco de gestão local)
            _connectionString = configuration.GetConnectionString("LocalConnection");
        }

        /// <summary>
        /// Módulo de Validação de Licença (Etapas 6 e 7)
        /// Este método impede que o robô processe projetos se:
        /// 1. O trial de 30 dias expirou.
        /// 2. O limite de projetos contratados foi atingido.
        /// 3. O Admin (Você) bloqueou a empresa manualmente.
        /// </summary>
        public bool ValidarAcessoEmpresa(int empresaId)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("A string de conexão 'LocalConnection' não foi configurada no appsettings.json do ForgeSync.");
            }

            try 
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    
                    // Query de Gestão SaaS:
                    // Verifica data de expiração (Etapa 7)
                    // Verifica se o contador de projetos ativos excedeu o limite (Etapa 6)
                    string query = @"
                        SELECT 
                            CASE 
                                WHEN Data_Expiracao < GETDATE() THEN 0
                                WHEN Status_Assinatura = 'BLOQUEADO' THEN 0
                                WHEN (SELECT COUNT(*) FROM Metadados_Projetos WHERE EmpresaID = @id) >= Limite_Projetos THEN 0
                                ELSE 1 
                            END as TemAcesso
                        FROM Empresas_SaaS 
                        WHERE EmpresaID = @id";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        // Uso de parâmetros para evitar SQL Injection (Segurança nível Premium)
                        cmd.Parameters.Add("@id", SqlDbType.Int).Value = empresaId;
                        
                        object result = cmd.ExecuteScalar();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            bool temAcesso = Convert.ToInt32(result) == 1;
                            
                            if(!temAcesso)
                            {
                                Console.WriteLine($"[AVISO FORGESYNC]: Empresa {empresaId} tentou rodar o robô mas está bloqueada ou sem limite.");
                            }
                            
                            return temAcesso;
                        }
                        
                        return false; // Empresa não existe no cadastro
                    }
                }
            }
            catch (SqlException ex)
            {
                // Log detalhado para sua tela de Admin (Etapa 8)
                Console.WriteLine($"[ERRO DE BANCO DE DADOS]: {ex.Message}");
                return false; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CRÍTICO NO SAASSERVICE]: {ex.Message}");
                return false;
            }
        }
    }
}