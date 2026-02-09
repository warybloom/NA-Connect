using APS.WebApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace APS.WebApp.Services
{
    public class GrdInfo
    {
        public string TransmittalNumber { get; set; }
        public DateTime EmissionDate { get; set; }
        public string IssuedPurpose { get; set; }
    }

    public class GrdToLdpSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GrdToLdpSyncService> _logger;

        public GrdToLdpSyncService(AutodeskApiService autodeskApi, IConfiguration configuration, ILogger<GrdToLdpSyncService> logger)
        {
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> SyncGrdsToLdpAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation($"📂 [GRD SYNC] Iniciando rastreamento de correspondências para o projeto {projectId}...");

            // 1. Localizar Pasta de GRDs (Suporte a PT/EN)
            var pathsGrd = new[] {
                new[] { "02 - CORRESPONDENCE", "02.02 - OUTGOING TRANSMITTALS TO CLIENT" },
                new[] { "02 - CORRESPONDÊNCIA", "02.02 - GRD ENVIADA AO CLIENTE" }
            };

            string folderGrdId = null;
            foreach (var path in pathsGrd)
            {
                folderGrdId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
                if (!string.IsNullOrEmpty(folderGrdId)) break;
            }

            if (string.IsNullOrEmpty(folderGrdId))
            {
                return "⚠️ Pasta de GRDs não localizada. Verifique a estrutura de pastas 02/02.02.";
            }

            // 2. Mapear arquivos de GRD na pasta (Recursivo)
            var mapaGrdsNoAcc = new List<AccFolderItem>();
            await ListAllExcelsRecursiveAsync(projectId, folderGrdId, accessToken, mapaGrdsNoAcc);
            _logger.LogInformation($"🔍 Encontradas {mapaGrdsNoAcc.Count} planilhas de GRD para análise.");

            // 3. Dicionário de Consolidação (Chave: DocNumber, Valor: Dados da GRD mais recente)
            var consolidadoLdp = new Dictionary<string, GrdInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var grdFile in mapaGrdsNoAcc)
            {
                try
                {
                    await ProcessSingleGrdAsync(projectId, grdFile.VersionUrn, accessToken, consolidadoLdp);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"⚠️ Falha ao processar GRD {grdFile.Name}: {ex.Message}");
                }
            }

            if (consolidadoLdp.Count == 0) return "Nenhuma informação de documento encontrada nas GRDs.";

            // 4. Atualizar a LDP Única Vez
            return await UpdateLdpWithConsolidatedData(hubId, projectId, accessToken, consolidadoLdp);
        }

       private async Task ProcessSingleGrdAsync(string projectId, string versionUrn, string accessToken, Dictionary<string, GrdInfo> consolidado)
{
    using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
    using (var ms = new MemoryStream())
    {
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage(ms))
        {
            // 1. Localização da aba da GRD
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("GRD")) 
                     ?? package.Workbook.Worksheets[0];
            
            _logger.LogInformation($"      🔍 Analisando aba: {ws.Name}");

            // 2. Captura e Concatenação do Transmittal (G3+H3+I3)
            string rawT = $"{ws.Cells[3, 7].Text}{ws.Cells[3, 8].Text}{ws.Cells[3, 9].Text}".Trim(); 
            string rawDate = ws.Cells[4, 7].Text?.Trim();

            if (string.IsNullOrEmpty(rawT) || !rawT.Contains("GRD", StringComparison.OrdinalIgnoreCase)) 
            {
                _logger.LogWarning($"      ⚠️ Planilha em G3 não contém 'GRD'. Ignorando.");
                return;
            }

            // Isola o sufixo (ex: GRD-001)
            string cleanTransmittal = rawT.Substring(rawT.IndexOf("GRD", StringComparison.OrdinalIgnoreCase));

            // Conversão de Data
            if (!DateTime.TryParse(rawDate, out DateTime emissionDate)) 
            {
                _logger.LogWarning($"      ⚠️ Data '{rawDate}' inválida em G4. Ignorando.");
                return;
            }

            // 3. Varredura de Documentos (Definição de Variáveis de Escopo)
            int colDoc = 3;      // Coluna C (N&A DOCUMENT NUMBER)
            int colPurpose = 7;  // Coluna G (ISSUED POUPURSE)
            int startRow = 24;   // Início da lista de documentos
            int countProcessadosNestaGrd = 0;

            int lastRow = ws.Dimension?.End.Row ?? 500;

            for (int row = startRow; row <= lastRow; row++)
            {
                string docNumber = ws.Cells[row, colDoc].Text?.Trim();
                string purpose = ws.Cells[row, colPurpose].Text?.Trim();

                if (string.IsNullOrEmpty(docNumber)) continue;

                var novaInfo = new GrdInfo 
                { 
                    TransmittalNumber = cleanTransmittal, 
                    EmissionDate = emissionDate, 
                    IssuedPurpose = purpose 
                };

                // 4. Lógica de Sobreposição (Data Recente + Desempate por Número da GRD)
                if (consolidado.TryGetValue(docNumber, out var existente))
                {
                    bool ehMaisRecente = novaInfo.EmissionDate > existente.EmissionDate;
                    
                    // Se a data for igual, desempata pelo final do número da GRD (ex: 002 > 001)
                    if (!ehMaisRecente && novaInfo.EmissionDate == existente.EmissionDate)
                    {
                        ehMaisRecente = string.Compare(novaInfo.TransmittalNumber, existente.TransmittalNumber, StringComparison.OrdinalIgnoreCase) > 0;
                    }

                    if (ehMaisRecente)
                    {
                        consolidado[docNumber] = novaInfo;
                        _logger.LogInformation($"      🔄 SOBREPOSIÇÃO: {docNumber} atualizado para {cleanTransmittal}.");
                    }
                }
                else
                {
                    consolidado[docNumber] = novaInfo;
                }
                
                countProcessadosNestaGrd++;
            }
            _logger.LogInformation($"      ✅ {countProcessadosNestaGrd} documentos processados na planilha {cleanTransmittal}.");
        }
    }
}

        private async Task<string> UpdateLdpWithConsolidatedData(string hubId, string projectId, string accessToken, Dictionary<string, GrdInfo> dados)
{
    string tempLdpPath = Path.Combine(Path.GetTempPath(), $"LDP_GRD_UPDATE_{Guid.NewGuid()}.xlsx");
    
    try
    {
        // 1. Localiza a LDP de Controle (Pasta 09.10)
        var folderLdpId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" }, accessToken);
        if (string.IsNullOrEmpty(folderLdpId)) 
            folderLdpId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }, accessToken);

        string ldpNameSearch = _configuration[$"ProjectSettings:{projectId}:LdpFileName"] ?? "LDP";
        var (ldpId, actualName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderLdpId, ldpNameSearch, accessToken);

        // 2. Download da LDP para processamento local
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpId, accessToken))
        using (var fs = new FileStream(tempLdpPath, FileMode.Create)) 
            await dl.CopyToAsync(fs);

        // Variável de contagem declarada apenas UMA vez no escopo do método
        int totalUpdates = 0;

        using (var package = new ExcelPackage(new FileInfo(tempLdpPath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            
            int colDoc = 4; // Coluna D (N&A DOCUMENT NUMBER)
            int colGrdNum = -1, colGrdDate = -1, colGrdPurpose = -1;

            // 3. Identificação Dinâmica das Colunas (Linha 7)
            for (int c = 1; c <= ws.Dimension.End.Column; c++)
            {
                string header = ws.Cells[7, c].Text?.ToUpper().Trim();
                
                // Mapeamento exato conforme imagem enviada
                if (header == "EMISSION DATA") colGrdDate = c;
                if (header == "TRANMISTAL NUMBER") colGrdNum = c; 
                if (header == "ISSUED POUPURSE") colGrdPurpose = c; 
            }

            // 4. Preenchimento da LDP (Linha 8 em diante)
            for (int row = 8; row <= ws.Dimension.End.Row; row++)
            {
                string doc = ws.Cells[row, colDoc].Text?.Trim();
                if (string.IsNullOrEmpty(doc)) continue;

                if (dados.TryGetValue(doc, out var info))
                {
                    // Atualiza as colunas se foram localizadas
                    if (colGrdDate > 0) ws.Cells[row, colGrdDate].Value = info.EmissionDate.ToString("dd/MM/yyyy");
                    if (colGrdNum > 0) ws.Cells[row, colGrdNum].Value = info.TransmittalNumber;
                    if (colGrdPurpose > 0) ws.Cells[row, colGrdPurpose].Value = info.IssuedPurpose;
                    
                    totalUpdates++;
                }
            }
            package.Save();
        }

        // 5. Upload da nova versão para o ACC
        using (var ul = new FileStream(tempLdpPath, FileMode.Open))
            await _autodeskApi.CreateNewVersionAsync(projectId, ldpId, folderLdpId, actualName, ul, accessToken);

        return $"✅ Sucesso! {totalUpdates} documentos atualizados com dados de GRD.";
    }
    catch (Exception ex)
    {
        _logger.LogError($"❌ Erro ao atualizar LDP: {ex.Message}");
        throw;
    }
    finally 
    { 
        if (File.Exists(tempLdpPath)) File.Delete(tempLdpPath); 
    }
}

        private async Task ListAllExcelsRecursiveAsync(string projectId, string folderId, string token, List<AccFolderItem> results)
        {
            // Obtém o conteúdo da pasta atual (arquivos e pastas)
            var contents = await _autodeskApi.GetFolderContentsAsync(projectId, folderId, token);
            
            foreach (var item in contents)
            {
                if (item.Type == "folders") 
                {
                    // Se encontrou uma subpasta (ex: "GRD-001"), entra nela recursivamente
                    _logger.LogInformation($"   Subindo um nível: Entrando na pasta '{item.Name}'");
                    await ListAllExcelsRecursiveAsync(projectId, item.Id, token, results);
                }
                else if (item.Type == "items" && (item.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || item.Name.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))) 
                {
                    // Se encontrou um Excel, adiciona à lista de análise
                    _logger.LogInformation($"   Arquivo encontrado para análise: {item.Name}");
                    results.Add(item);
                }
            }
        }
    }
}