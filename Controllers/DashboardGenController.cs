using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Threading.Tasks;
using System;

namespace APS.WebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardGenController : ControllerBase
    {
        // Usando a exata string de conexão do seu servidor VPN
        private readonly string _connectionString = @"Server=100.76.16.82,14333;Database=DB_NA_CONNECT;User Id=usuario_bi;Password=SenhaBI@2026;TrustServerCertificate=True;";

        [HttpGet("kpis")]
        public async Task<IActionResult> GetKpis([FromQuery] string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) 
                return BadRequest(new { sucesso = false, mensagem = "Envie o ID do Projeto (ACC ou N&A)." });

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 🎯 Dapper batendo direto na sua View, filtrando até o dia de hoje (Data Date)
                    string sql = @"
                        SELECT 
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS Total_HH_Previsto,
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS Total_HH_Realizado,
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Total_Custo_Previsto,
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Total_Custo_Realizado
                        FROM VW_Custo_Diario_Documento
                        WHERE Project_ID_ACC = @projectId
                          AND Data_Date <= CAST(GETDATE() AS DATE)";

                    var kpis = await conn.QueryFirstOrDefaultAsync(sql, new { projectId });

                    if (kpis == null)
                        return Ok(new { sucesso = false, mensagem = "Projeto não encontrado ou sem dados." });

                    // 🎯 Cálculos de EVM (Earned Value Management) para os Cards
                    double hhPrevisto = Convert.ToDouble(kpis.Total_HH_Previsto);
                    double hhRealizado = Convert.ToDouble(kpis.Total_HH_Realizado);
                    double custoPrevisto = Convert.ToDouble(kpis.Total_Custo_Previsto);
                    double custoRealizado = Convert.ToDouble(kpis.Total_Custo_Realizado);

                    // SPI: Se o Realizado for maior que o Previsto, SPI > 1 (Adiantado)
                    double spi = hhPrevisto > 0 ? Math.Round(hhRealizado / hhPrevisto, 2) : 0; 
                    
                    // CPI: Se o que você entregou (Previsto/Agregado) for maior que o que você gastou (Realizado), CPI > 1 (Lucro)
                    double cpi = custoRealizado > 0 ? Math.Round(custoPrevisto / custoRealizado, 2) : 0; 

                    return Ok(new 
                    { 
                        sucesso = true, 
                        dados = new {
                            hh_previsto = hhPrevisto,
                            hh_realizado = hhRealizado,
                            custo_previsto = custoPrevisto,
                            custo_realizado = custoRealizado,
                            spi = spi,
                            cpi = cpi
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { sucesso = false, mensagem = $"Erro ao buscar KPIs: {ex.Message}" });
            }
        }
    }
}