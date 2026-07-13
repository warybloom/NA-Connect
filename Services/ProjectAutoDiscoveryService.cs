// Modulo: APS.WebApp.Services/ProjectAutoDiscoveryService.cs
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NA_Connect.Models;

namespace APS.WebApp.Services
{
    public class ProjectAutoDiscoveryService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly ILogger<ProjectAutoDiscoveryService> _logger;

        public ProjectAutoDiscoveryService(AutodeskApiService autodeskApi, ILogger<ProjectAutoDiscoveryService> logger)
        {
            _autodeskApi = autodeskApi;
            _logger = logger;
        }

        public async Task<ProjectDiscoveryResult> ExecuteDiscoveryAsync(string projectId, string ldpPath, string accessToken)
        {
            _logger.LogInformation("🔍 [DNA] INICIANDO AUTO-DETECÇÃO NA ABA 'LDP'...");
            
            // Inicializa o objeto com o nome do ficheiro ldpPath
            var result = new ProjectDiscoveryResult 
            { 
                NamingStandard = "NA", 
                Language = "PT",
                LdpName = Path.GetFileName(ldpPath) // 🎯 CAPTURANDO O NOME AQUI
            };

            try 
            {
                using (var package = new ExcelPackage(new FileInfo(ldpPath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) 
                             ?? package.Workbook.Worksheets[0];

                    // 1. Deteção de Idioma (Linha 7, Coluna 4)
                    string headerDocNumber = ws.Cells[7, 4].Text?.Trim().ToUpper(); 
                    result.Language = headerDocNumber.Contains("NUMBER") ? "EN" : "PT";

                    // 2. Deteção de Padrão (Amostragem de 5 documentos)
                    int matchNA = 0;
                    int matchClient = 0;

                    for (int row = 8; row <= 12; row++) 
                    {
                        string naCode = ws.Cells[row, 4].Text?.Trim();
                        string clientCode = ws.Cells[row, 5].Text?.Trim();

                        if (string.IsNullOrEmpty(naCode)) continue;
                        
                        // Valida se o código N&A existe no ACC
                        if (await CheckFileExists(projectId, naCode, accessToken)) matchNA++;
                        
                        // Valida se o código do Cliente existe no ACC
                        if (!string.IsNullOrEmpty(clientCode) && await CheckFileExists(projectId, clientCode, accessToken)) 
                            matchClient++;
                    }

                    // MODO TESTE: Forçando o padrão independente do match das colunas
// Depois do teste, volte para: result.NamingStandard = matchClient > matchNA ? "CLIENT" : "NA";

result.NamingStandard = "CLIENT"; 

_logger.LogWarning("⚠️ [TESTE] NAMING_STANDARD FORÇADO PARA 'CLIENT' VIA CÓDIGO.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro durante a varredura física do DNA: {ex.Message}");
                return null;
            }

            return result;
        }

        // Módulo: Services/ProjectAutoDiscoveryService.cs

        private async Task<bool> CheckFileExists(string proj, string name, string token)
        {
            // 🎯 CORREÇÃO: Usa a Busca Global aqui também para não quebrar o DNA
            var res = await _autodeskApi.SearchFileInProjectAsync(proj, name, token);
            return !string.IsNullOrEmpty(res.FileId);
        }
    }
}