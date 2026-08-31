#nullable disable
using APS.WebApp.Services;
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
    // 1. O MODELO (DTO): Representa as colunas físicas do Excel do Hold Control
    public class HoldControlInfo
    {
        public int? Item { get; set; }               // Coluna A (Pode ser nulo ou texto, tratamos como int anulável)
        public string NumeroHold { get; set; }       // Coluna B (Nº HOLD)
        public string DocumentoNeA { get; set; }     // Coluna C (Nº N&A DO DOCUMENTO)
        public string Disciplina { get; set; }       // Coluna D (DISCIPLINA)
        public string TituloDocumento { get; set; }  // Coluna E (TÍTULO DO DOCUMENTO)
        public string Rev { get; set; }              // Coluna F (REV)
        public string MotivoHold { get; set; }       // Coluna G (MOTIVO DO HOLD)
        public string Status { get; set; }           // Coluna H (STATUS)
        public string Responsavel { get; set; }      // Coluna I (RESPONSÁVEL)
    }
 
    // 2. O SERVIÇO: Classe responsável por rodar a lógica do robô
    public class HoldControlSyncService
    {
        private readonly ILogger<HoldControlSyncService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly AccScheduleSyncService _scheduleService;
        private readonly DatabaseService _dbService;
 
        // Construtor: O ASP.NET Core injeta os serviços aqui
        public HoldControlSyncService(
            ILogger<HoldControlSyncService> logger,
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
 
            // Configura o EPPlus para uso Não-Comercial obrigatório
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }
 
        // Método de Execução Principal
        public async Task<string> SyncHoldControlToDbAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation("Iniciando busca da pasta de Hold Control no ACC...");
 
            string folderId = await _scheduleService.FindFolderByPaths(
                hubId,
                projectId,
                new[] {
                    new[] { "01 - PLANEJAMENTO E CONTROLE", "01.15 - HOLD CONTROL" },
                    new[] { "01 - PROJETO", "01.15 - HOLD CONTROL" }
                },
                accessToken);
 
            if (string.IsNullOrEmpty(folderId))
            {
                return "⚠️ Pasta não encontrada para o Hold Control.";
            }
 
            var listaAcumuladora = new List<AccFolderItem>();
            await ListAllExcelsRecursiveAsync(projectId, folderId, accessToken, listaAcumuladora);
 
            _logger.LogInformation($"Encontradas {listaAcumuladora.Count} planilhas Excel.");
 
            if (!listaAcumuladora.Any())
            {
                return "⚠️ Nenhuma planilha encontrada na pasta de Hold Control.";
            }
 
            // Seleciona a primeira planilha para processar
            var excelFile = listaAcumuladora.First();
            string tempPath = Path.Combine(Path.GetTempPath(), $"HOLD_SYNC_{Guid.NewGuid()}.xlsx");
 
            try
            {
                int importados = await ProcessExcelFileAsync(projectId, excelFile.Id, folderId, excelFile.Name, accessToken, tempPath);
                return $"✅ Sincronização concluída com sucesso! {importados} registros importados/atualizados.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha fatal ao rodar sincronização do Hold Control.");
                return $"❌ Erro na sincronização: {ex.Message}";
            }
            finally
            {
                // Limpeza crucial do arquivo temporário para evitar estouro de disco no servidor
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
 
        // Método recursivo para listar planilhas
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
 
        /// <summary>
        /// Faz o download do arquivo Excel do ACC e processa os dados salvando no Banco de Dados.
        /// </summary>
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
                var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("HOLD"))
                         ?? package.Workbook.Worksheets[0];
 
                _logger.LogInformation($"Processando planilha: {realFileName}, aba: {ws.Name}");
 
                // Cabeçalho físico real de acordo com as colunas do HoldControlInfo (A até I)
                int startRow = 2; // Dados começam na linha 2 (linha 1 assumida como cabeçalho)
                int endRow = ws.Dimension?.End.Row ?? 0;
 
                for (int r = startRow; r <= endRow; r++)
                {
                    string numeroHold = ws.Cells[r, 2].Text?.Trim();      // Coluna B (Nº HOLD)
                    string documentoNeA = ws.Cells[r, 3].Text?.Trim();    // Coluna C (Nº N&A DO DOCUMENTO)
 
                    // Validação de segurança correta para o layout de Hold Control
                    if (string.IsNullOrEmpty(numeroHold) && string.IsNullOrEmpty(documentoNeA))
                    {
                        continue; // Pula linhas vazias reais
                    }
 
                    // Montagem do dicionário de gravação compatível com a tabela de destino do banco
                    var rowData = new Dictionary<int, object>
                    {
                        { 1, ws.Cells[r, 1].Text?.Trim() }, // ITEM (Coluna A)
                        { 2, numeroHold },                  // Nº HOLD (Coluna B)
                        { 3, documentoNeA },                // Nº N&A DO DOCUMENTO (Coluna C)
                        { 4, ws.Cells[r, 4].Text?.Trim() }, // DISCIPLINA (Coluna D)
                        { 5, ws.Cells[r, 5].Text?.Trim() }, // TÍTULO DO DOCUMENTO (Coluna E)
                        { 6, ws.Cells[r, 6].Text?.Trim() }, // REV (Coluna F)
                        { 7, ws.Cells[r, 7].Text?.Trim() }, // MOTIVO DO HOLD (Coluna G)
                        { 8, ws.Cells[r, 8].Text?.Trim() }, // STATUS (Coluna H)
                        { 9, ws.Cells[r, 9].Text?.Trim() }  // RESPONSÁVEL (Coluna I)
                    };
 
                    try
                    {
                        // Envia para o repositório de banco de dados
                        _dbService.SalvarDadosHoldControl(projectId, rowData);
                        atualizados++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erro ao salvar registro da linha {r} no banco de dados.");
                    }
                }
 
                _logger.LogInformation($"Processados {atualizados} registros válidos de Hold Control.");
 
                // Salva alterações de volta no ACC caso tenha alterado algo localmente na planilha
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