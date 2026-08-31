// Caminho: Controllers/WebhooksController.cs (VERSÃO FINAL E COMPLETA)
#nullable disable
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using APS.WebApp.Services;
using System.Collections.Generic;
using System.Linq;
using OfficeOpenXml;
using Microsoft.Extensions.Configuration; // Adicione este using

namespace APS.WebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhooksController : ControllerBase
    {
        private readonly TokenManager _tokenManager;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration; // Adicione a configuração
        private const string LdpFileName = "LDP DE CONTROLE.xlsx";

        public WebhooksController(TokenManager tokenManager, AutodeskApiService autodeskApi, IConfiguration configuration)
        {
            _tokenManager = tokenManager;
            _autodeskApi = autodeskApi;
            _configuration = configuration; // Adicione a configuração
        }

        [HttpPost("callback")]
        public async Task<IActionResult> HandleWebhookEvent()
        {
            // Responde ao "ping" de verificação da Autodesk ao criar o Webhook
            if (Request.Headers.ContainsKey("x-adsk-hook-token"))
            {
                return Ok();
            }

            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync();
            var webhookPayload = JsonDocument.Parse(requestBody).RootElement;

            var eventType = webhookPayload.GetProperty("event").GetString();
            if (eventType != "dm.version.added")
            {
                return Ok("Evento ignorado: não é 'dm.version.added'.");
            }

            var payload = webhookPayload.GetProperty("payload");
            var versionUrn = payload.GetProperty("version").GetProperty("id").GetString();
            var fileName = payload.GetProperty("version").GetProperty("attributes").GetProperty("name").GetString();
            
            // Verifica se o ficheiro atualizado é a LDP que nos interessa
            if (!fileName.Equals(LdpFileName, StringComparison.OrdinalIgnoreCase))
            {
                return Ok($"Evento ignorado: o ficheiro '{fileName}' não é a LDP de Controle.");
            }
            
            Console.WriteLine($"--> Webhook recebido: Nova versão da LDP! A iniciar sincronização para a versão: {versionUrn}");

            // Executa a sincronização em segundo plano para responder rapidamente à Autodesk
            _ = Task.Run(async () => {
                try
                {
                    var appToken = await _tokenManager.GetTokenAsync();
                    
                    var itemData = payload.GetProperty("version").GetProperty("relationships").GetProperty("item").GetProperty("data");
                    var projectUrn = payload.GetProperty("version").GetProperty("relationships").GetProperty("project").GetProperty("data").GetProperty("id").GetString();
                    string projectId = projectUrn; // O URN do projeto já vem com o "b."
                    
                    // O hubId é necessário para a nossa função de busca global
                    var hubId = _configuration["APS:AccountId"];

                    var ldpData = new List<Dictionary<string, string>>();
                    using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, appToken))
                    {
                        using (var package = new ExcelPackage(stream))
                        {
                            ExcelWorksheet worksheet = package.Workbook.Worksheets.FirstOrDefault();
                            if (worksheet == null || worksheet.Dimension == null)
                            {
                                Console.WriteLine("Erro na sincronização automática: A planilha LDP descarregada está vazia.");
                                return;
                            }
                            // Lógica completa para ler o Excel
                            int headerRow = 1;
                            bool headerFound = false;
                            for (int r = 1; r <= worksheet.Dimension.Rows && r <= 20; r++)
                            {
                                var cellValue = worksheet.Cells[r, 1].Value?.ToString().Trim();
                                if (cellValue != null && (cellValue.Equals("Nome", StringComparison.OrdinalIgnoreCase) || cellValue.Equals("Nome do Arquivo", StringComparison.OrdinalIgnoreCase))) { headerRow = r; headerFound = true; break; }
                            }
                            if (!headerFound) { Console.WriteLine("Erro na sincronização automática: Cabeçalho não encontrado na LDP."); return; }
                            var colCount = worksheet.Dimension.Columns;
                            var headers = Enumerable.Range(1, colCount).Select(col => worksheet.Cells[headerRow, col].Value?.ToString().Trim()).ToList();
                            for (int row = headerRow + 1; row <= worksheet.Dimension.Rows; row++)
                            {
                                var firstCell = worksheet.Cells[row, 1].Value?.ToString();
                                if (string.IsNullOrWhiteSpace(firstCell)) continue;
                                var rowData = new Dictionary<string, string>();
                                for (int col = 1; col <= headers.Count; col++) { if(headers[col - 1] != null) rowData[headers[col - 1]] = worksheet.Cells[row, col].Value?.ToString().Trim() ?? string.Empty; }
                                ldpData.Add(rowData);
                            }
                        }
                    }

                    if (ldpData.Any())
                    {
                        var result = await _autodeskApi.SyncLdpAttributesAsync(hubId, projectId, ldpData, appToken, "IGNORAR_WEBHOOK");
                        Console.WriteLine($"--> Sincronização automática concluída: {result}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"--> ERRO na sincronização automática via Webhook: {ex.ToString()}");
                }
            });

            return Accepted();
        }
    }
}