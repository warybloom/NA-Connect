using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;

namespace LDP_System.Modules.Admin.Controllers
{
    [ApiController]
    [Route("api/admin/projeto")]
    public class ProjetoAdminController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        // Injetando a configuração para acessar o banco local
        public ProjetoAdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Criando um DTO simples apenas para esta requisição
        public class VincularLdpSimplesRequest
        {
            public int ProjetoId { get; set; }
            public int? ConfigLDPID { get; set; } // Pode ser nulo se o usuário escolher "Personalizado/Nenhuma"
        }

        [HttpPost("vincular-config-ldp")]
        public async Task<IActionResult> VincularConfigLdp([FromBody] VincularLdpSimplesRequest request)
        {
            if (request == null || request.ProjetoId == 0) 
                return BadRequest("Dados de projeto inválidos.");

            try 
            {
                using (var conexao = new SqlConnection(_configuration.GetConnectionString("LocalConnection")))
                {
                    await conexao.OpenAsync();

                    // Atualiza a tabela principal Projetos
                    var sqlUpdateProjeto = @"
                        UPDATE [dbo].[Projetos] 
                        SET [ConfigLDPID] = @ConfigLDPID 
                        WHERE [ProjetoID] = @ProjetoId";

                    // Atualiza a tabela de Metadados (se ela existir e precisar do vínculo)
                    var sqlUpdateMetadados = @"
                        UPDATE M
                        SET M.[ConfigLDPID] = @ConfigLDPID
                        FROM [dbo].[Metadados_Projetos] M
                        INNER JOIN [dbo].[Projetos] P ON M.[Project_ID_ACC] = P.[Project_ID_ACC]
                        WHERE P.[ProjetoID] = @ProjetoId";

                    var sqlUpdateConfigLdp = @"
                        UPDATE C
                        SET C.[Project_ID_ACC] = P.[Project_ID_ACC]
                        FROM [dbo].[ConfiguracoesLDP] C 
                        INNER JOIN [dbo].[Projetos] P ON P.[ProjetoID] = @ProjetoId
                        WHERE C.[ConfigLDPID] = @ConfigLDPID";

                    await conexao.ExecuteAsync(sqlUpdateConfigLdp, new { ConfigLDPID = request.ConfigLDPID, ProjetoId = request.ProjetoId });

                    await conexao.ExecuteAsync(sqlUpdateProjeto, new { ConfigLDPID = request.ConfigLDPID, ProjetoId = request.ProjetoId });
                    
                    // Executa o update de metadados de forma segura (ignora se a tabela não existir/falhar)
                    try { await conexao.ExecuteAsync(sqlUpdateMetadados, new { ConfigLDPID = request.ConfigLDPID, ProjetoId = request.ProjetoId }); } catch { }
                }

                return Ok(new { Message = "Vínculo salvo com sucesso!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro interno ao processar vínculo: {ex.Message}");
            }
        }
    }
}