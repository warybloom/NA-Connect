using APS.WebApp.Services;
using com.sun.corba.se.impl.orbutil.concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace NA_Connect.Services
{
    public class QualityInfo
    {
        public string Cliente { get; set; }   
        public string Cr { get; set; }        
        public string GRD { get; set; }     
        public string CodNea { get; set; }       
        public string CodCliente { get; set; }  
        public string Desc { get; set; }              
        public string disciplina { get; set; }       
        public string Revisao { get; set; }           
        public string Nvia { get; set; }  
        public string Mes { get; set; }  
        public string DataEnvio { get; set; }   
        public string Emitent { get; set; }   
    }

    public class QualitySyncService
    {
        private readonly ILogger<QualitySyncService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly AccScheduleSyncService _scheduleService;
        private readonly DatabaseService _dbService;

        public QualitySyncService(
            ILogger<QualitySyncService> logger,
            AutodeskApiService autodeskApi,
            IConfiguration configuration,
            AccScheduleSyncService scheduleService,
            DatabaseService dbService)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _scheduleService = scheduleService;
            _dbService = dbService;
 
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public async Task<string> SyncQualitySyncToDbAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation("Iniciando busca da pasta de QUALITY no ACC...");
 
            string folderId = await _scheduleService.FindFolderByPaths(
                hubId,
                projectId,
                new[] {
                    // Procuar a pasta no ACC
                    new[] { "05 - QUALIDADE", "05.06 - RELATÓRIO DE NÃO CONFORMIDADE DA NCR" },
                    new[] { "05 - QUALITY", "01.15 - NCR NONCONFORMITY REPORT" }
                },
                accessToken);
 
            if (string.IsNullOrEmpty(folderId))
            {
                return "⚠️ Pasta não encontrada para o QUALITY.";
            }
 
            var listaAcumuladora = new List<AccFolderItem>();
            await ListAllExcelsRecursiveAsync(projectId, folderId, accessToken, listaAcumuladora);
 
            _logger.LogInformation($"Encontradas {listaAcumuladora.Count} planilhas Excel.");
 
            if (!listaAcumuladora.Any())
            {
                return "⚠️ Nenhuma planilha encontrada na pasta de QUALITY.";
            }

            // Pegar a primeira planilha da pasta
            var excelFile = listaAcumuladora.First();
            // Criar uma pasta temporária 
            string tempPath = Path.Combine(Path.GetTempPath(), $"QUALITY_SYNC_{Guid.NewGuid()}.xlsx");
 
            try
            {
                int importados = await ProcessExcelFileAsync(projectId, excelFile.Id, folderId, excelFile.Name, accessToken, tempPath);
                return $"✅ Sincronização concluída com sucesso! {importados} registros importados/atualizados.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha fatal ao rodar sincronização do QUALITY.");
                return $"❌ Erro na sincronização: {ex.Message}";
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Não foi possível deletar o arquivo temporário em {tempPath}");
                    }
                }
            }
        }

        private async Task ListAllExcelsRecursiveAsync(
            string projectId,
            string folderId,
            string token,
            List<AccFolderItem> listaAcumuladora)
        {
            var folderContents = await _autodeskApi.GetFolderContentsAsync(projectId, folderId, token);
            if (folderContents == null) return;
 
            foreach (var item in folderContents)
            {
                if (item.Type == "items")
                {
                    if (item.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                        item.Name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                    {
                        listaAcumuladora.Add(item);
                    }
                }
                else if (item.Type == "folders")
                {
                    await ListAllExcelsRecursiveAsync(projectId, item.Id, token, listaAcumuladora);
                }
            }
        }

        
        private async Task<int> ProcessExcelFileAsync(
            string projectId, 
            string itemId, 
            string parentFolderId, 
            string realFileName, 
            string accessToken, 
            string tempPath)
        {
            // 1. Download do arquivo
            var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, itemId, accessToken);
            if (itemDetails == null) throw new FileNotFoundException("Falha ao obter detalhes do item do ACC.");
 
            string versionUrn = itemDetails.TipVersionUrn;
 
            using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await dl.CopyToAsync(fs);
            }
            
 
            int atualizados = 0;
 
            // 2. Leitura dos dados usando EPPlus
            using (var package = new ExcelPackage(new FileInfo(tempPath)))
            {
                var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("BASE LANÇAMENTO"))
                         ?? package.Workbook.Worksheets[1];
 
                _logger.LogInformation($"Processando planilha: {realFileName}, aba: {ws.Name}");
 
                int startRow = 3; 
                int endRow = ws.Dimension?.End.Row ?? 0;
 
                for (int r = startRow; r <= endRow; r++)
                {
                    string CodNea = ws.Cells[r, 4].Text?.Trim();      
                       
 
                    if (string.IsNullOrEmpty(CodNea))
                    {
                        continue; 
                    }
 
                   
                        var rowData = new Dictionary<int, object>
                        {
                            { 1, ws.Cells[r, 1].Text?.Trim() },  // A - CLIENTE
                            { 2, ws.Cells[r, 2].Text?.Trim() },  // B - CR
                            { 3, ws.Cells[r, 3].Text?.Trim() },  // C - GRD
                            { 4, CodNea },                       // D - CÓDIGO N&A
                            { 5, ws.Cells[r, 5].Text?.Trim() },  // E - CÓDIGO CLIENTE
                            { 6, ws.Cells[r, 6].Text?.Trim() },  // F - DESCRIÇÃO
                            { 7, ws.Cells[r, 7].Text?.Trim() },  // G - DISCIPLINA
                            { 8, ws.Cells[r, 8].Text?.Trim() },  // H - REVISÃO
                            { 9, ws.Cells[r, 9].Text?.Trim() },  // I - Nº VIAS
                            { 10, ws.Cells[r, 10].Text?.Trim() },// J - MÊS 👈 CORRIGIDO
                            { 11, ws.Cells[r, 11].Text?.Trim() },// K - DATA ENVIO 👈 CORRIGIDO
                            { 12, ws.Cells[r, 12].Text?.Trim() } // L - EMITENTE
                        };
 
                    try
                    {
                        //Salvar dados no banco de dados
                        _dbService.SalvarDadosQuality(projectId, rowData);
                        atualizados++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erro ao salvar registro da linha {r} no banco de dados.");
                    }
                }
 
                _logger.LogInformation($"Processados {atualizados} registros válidos de Hold Control.");
 
                if (atualizados > 0)
                {
                    package.Save();
                }
            }
 
            // 3. Atualiza versão no ACC caso alterado
            if (atualizados > 0)
            {
                using (var ul = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                {
                    await _autodeskApi.CreateNewVersionAsync(projectId, itemId, parentFolderId, realFileName, ul, accessToken);
                    _logger.LogInformation($"✅ Nova versão da planilha criada no ACC: {realFileName}");
                }
            }
 
            return atualizados;
        }
        private int? ParseNullableInt(object value)
        {
            if (value == null) return null;
            return int.TryParse(value.ToString(), out int result) ? result : null;
        }
    }
}