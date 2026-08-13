using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using ForgeSync.Modules.Plant3D.Services;

namespace ForgeSync.Modules.Plant3D.Controllers
{
    [ApiController]
    [Route("api/plant")]
    public class PlantMappingController : ControllerBase
    {
        private readonly PlantMappingService _mappingService;
        

        public PlantMappingController()
        {
            // Em um cenário real, isso seria injetado via Injeção de Dependência (DI) no Program.cs
            _mappingService = new PlantMappingService();
        }

        [HttpGet("colunas-disponiveis/{projectId}")]
        public async Task<IActionResult> GetColunasDisponiveis(string projectId)
        {
            try
            {
                // Aqui você vai buscar no seu banco de dados ou regra de negócio onde os arquivos
                // deste projeto específico foram baixados temporariamente pelo Robô.
                // Exemplo simulado de diretórios:
                string basePath = $@"C:\ForgeSync_Cache\Projetos\{projectId}";
                string pidDirectory = $@"{basePath}\PID_Extractions";
                string p3dDirectory = $@"{basePath}\3D_Extractions";

                // Retorna o dicionário com as duas listas (PID e 3D)
                var columnsDict = await _mappingService.GetDynamicColumnsAsync(pidDirectory, p3dDirectory);

                // Se ambas as listas vierem vazias, significa que os JSONs não foram baixados/processados ainda
                if (columnsDict["pidColumns"].Count == 0 && columnsDict["p3dColumns"].Count == 0)
                {
                    return NotFound(new { message = "Arquivos de dados da Autodesk não encontrados no cache do servidor. Certifique-se de que a sincronização primária foi executada." });
                }

                return Ok(columnsDict);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro interno ao processar as colunas do dicionário de dados.", details = ex.Message });
            }
        }
    }
}