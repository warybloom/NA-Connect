using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using System.Linq;
using NA_Connect.Services;


namespace APS.WebApp.Services
{
    public class Plant3DReportService
    {
        private readonly ILogger<Plant3DReportService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly DatabaseService _dbService;
        private readonly Plant3DDataFetcher _fetcher;
        private readonly Plant3DExcelGenerator _excelGenerator;

        public Plant3DReportService(
            ILogger<Plant3DReportService> logger, 
            AutodeskApiService autodeskApi, 
            DatabaseService dbService,
            Plant3DDataFetcher fetcher,
            Plant3DExcelGenerator excelGenerator)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _dbService = dbService;
            _fetcher = fetcher;
            _excelGenerator = excelGenerator;
        }

        // ==============================================================================
        // ORQUESTRADOR PRINCIPAL: GERA QUALQUER RELATÓRIO P&ID
        // ==============================================================================
        public async Task<(byte[] FileBytes, string FileName)> GenerateReportAsync(string projectId, string reportType)
        {
            string tempDcfPath = null;
            string tempTemplatePath = null;

            try
            {
                _logger.LogInformation($"[PLANT 3D] Iniciando geração de relatório: {reportType} para o projeto {projectId}");

                // 1. Validar Configuração no Banco de Dados (Qual é a pasta DCF?)
                // (Nota: Precisaremos adicionar o método ObterConfiguracaoPlant3D no DatabaseService depois)
                var config = _dbService.ObterConfiguracaoLdp(projectId); // Provisório até criarmos a config específica do Plant
                if (config == null || string.IsNullOrEmpty(config.Folder_ID_LDP))
                    throw new Exception("Projeto não possui pasta configurada para os arquivos do Plant 3D.");

                string folderUrn = config.Folder_ID_LDP; // Assumindo que o DCF está na mesma pasta (ou usaremos a do Plant)
                string token = await _autodeskApi.GetInternalTokenAsync();

                // 2. Localizar e Baixar o ProcessPower.dcf
                string dcfUrn = await _autodeskApi.FindFileByNameRecursiveAsync(projectId, folderUrn, "ProcessPower", token, false);
                if (string.IsNullOrEmpty(dcfUrn)) throw new Exception("Arquivo ProcessPower.dcf não encontrado no ACC.");

                tempDcfPath = await _autodeskApi.DownloadFileToTempPathAsync(projectId, dcfUrn, token);

                // 3. Extrair os Dados via SQLite usando o Dapper
                List<Dictionary<string, object>> dataDict = new List<Dictionary<string, object>>();

                if (reportType == "VALVE LIST")
                {
                    var valves = await _fetcher.ExtrairValvulasPIDAsync(tempDcfPath);
                    dataDict = valves.Select(v => ToDictionary(v)).ToList();
                }
                else if (reportType == "INSTRUMENTATION")
                {
                    var inst = await _fetcher.ExtrairInstrumentosPIDAsync(tempDcfPath);
                    // Aplica a regra do MergeKey (Area-Loop)
                    dataDict = inst.Select(i => 
                    {
                        var dict = ToDictionary(i);
                        dict["MergeKey"] = i.Tag.Contains("-") ? string.Join("-", i.Tag.Split('-').Skip(1)) : i.Tag;
                        return dict;
                    }).ToList();
                }
                else if (reportType == "LINE LIST")
                {
                    var lines = await _fetcher.ExtrairLinhasPIDAsync(tempDcfPath);
                    dataDict = lines.Select(l => ToDictionary(l)).ToList();
                }

                if (!dataDict.Any()) throw new Exception("Nenhum dado encontrado para gerar o relatório.");

                // 4. Localizar e baixar o Template (Fixo na raiz do projeto por enquanto para teste)
                string templateUrn = await _autodeskApi.FindFileByNameRecursiveAsync(projectId, folderUrn, "TEMPLATE", token, false);
                if (string.IsNullOrEmpty(templateUrn)) throw new Exception("Arquivo de Template Excel não encontrado no ACC.");
                
                tempTemplatePath = await _autodeskApi.DownloadFileToTempPathAsync(projectId, templateUrn, token);
                byte[] templateBytes = await File.ReadAllBytesAsync(tempTemplatePath);

                // 5. Injetar dados no Excel em Memória
                byte[] excelBytes = _excelGenerator.GerarRelatorioExcel(templateBytes, reportType, dataDict);
                string fileName = $"{reportType.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                _logger.LogInformation($"[PLANT 3D] Relatório {fileName} gerado com sucesso na memória.");
                
                return (excelBytes, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PLANT 3D] Erro ao gerar {reportType}: {ex.Message}");
                throw;
            }
            finally
            {
                // Limpeza absoluta dos ficheiros temporários do servidor
                if (!string.IsNullOrEmpty(tempDcfPath) && File.Exists(tempDcfPath)) File.Delete(tempDcfPath);
                if (!string.IsNullOrEmpty(tempTemplatePath) && File.Exists(tempTemplatePath)) File.Delete(tempTemplatePath);
            }
        }

        // Helper: Converte os DTOs do C# para Dictionary para a geração genérica do Excel funcionar perfeitamente
        private Dictionary<string, object> ToDictionary(object obj)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obj.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(obj);
            }
            return dict;
        }
    }
}