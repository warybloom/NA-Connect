using APS.WebApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace APS.WebApp.Services
{
    public class PdfMetadataService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PdfMetadataService> _logger;
        private Dictionary<string, TitleBlockConfig> _templatesCache;

        public PdfMetadataService(AutodeskApiService autodeskApi, IConfiguration configuration, ILogger<PdfMetadataService> logger)
        {
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
        }

       public async Task<string> SyncPdfRevisionsToAccAsync(string hubId, string projectId, string accessToken)
{
    _logger.LogInformation("🕵️ [ETAPA 8] Iniciando Fluxo Final (Adaptação para Método Genérico)...");
    LoadTemplateConfigs(projectId);

    // Lista para armazenar o log detalhado
    List<string> logProcessamento = new List<string>();
    logProcessamento.Add($"=== LOG DE PROCESSAMENTO OCR - PROJETO: {projectId} - {DateTime.Now} ===");

    string tempLdpPath = Path.Combine(Path.GetTempPath(), $"LDP_SCAN_{Guid.NewGuid()}.xlsx");
    int totalAtualizados = 0;

    try
    {
        string rootFolderId = await GetProjectRootIdSafeAsync(hubId, projectId, accessToken);
        string projectFilesId = await GetProjectFilesFolderId(projectId, rootFolderId, accessToken);
        if (string.IsNullOrEmpty(projectFilesId)) return "❌ Erro Fatal: Não encontrei a pasta 'Project Files'.";

        var definitions = await _autodeskApi.GetFolderAttributeDefinitionsAsync(projectId, projectFilesId, accessToken);
        var revDef = definitions.FirstOrDefault(d => d.Name.Equals("REV", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("REV.", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("REVISÃO", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("Revision", StringComparison.OrdinalIgnoreCase));

        if (revDef == null) return "❌ Erro Fatal: Atributo 'REV' não encontrado.";

        string revAttributeIdString = revDef.Id.ToString();
        string folderLdpId = await NavigateToLdpFolderAsync(projectId, projectFilesId, accessToken);
        string ldpFileName = GetLdpFileNameFromConfig(projectId);
        string ldpSearchKey = Path.GetFileNameWithoutExtension(ldpFileName);

        var (ldpItemId, _) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderLdpId, ldpSearchKey, accessToken);
        if (string.IsNullOrEmpty(ldpItemId)) return "❌ Erro: LDP não encontrada.";

        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpItemId, accessToken))
        using (var fs = new FileStream(tempLdpPath, FileMode.Create)) await dl.CopyToAsync(fs);

        // 4. MAPEAR (Ajustado para pastas 01 e 04)
            _logger.LogInformation("🗺️ Mapeando pastas específicas (01 e 04)...");
            var mapaArquivos = new Dictionary<string, (string LineageId, string VersionId)>(StringComparer.OrdinalIgnoreCase);

            var foldersToScan = new[] { "01", "04" };

            foreach (var prefix in foldersToScan)
            {
                // Localiza o ID da pasta (ex: "01 - PLANEJAMENTO" ou "04 - ENGENHARIA")
                string targetFolderId = await FindSubFolderWithLogAsync(projectId, projectFilesId, prefix, accessToken);
                
                if (!string.IsNullOrEmpty(targetFolderId))
                {
                    _logger.LogInformation($"   📂 Escaneando subpasta com prefixo {prefix}...");
                    await ScanFolderRecursiveWithVersionAsync(projectId, targetFolderId, accessToken, mapaArquivos, 0);
                }
                else
                {
                    _logger.LogWarning($"   ⚠️ Pasta com prefixo {prefix} não encontrada no diretório raiz.");
                    // Registra no log de arquivo também para você saber por que não achou nada
                    logProcessamento.Add($"⚠️ PASTA NÃO LOCALIZADA: Prefixo {prefix}");
                }
            }
        
        using (var package = new ExcelPackage(new FileInfo(tempLdpPath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            int lastRow = ws.Dimension.End.Row;

            for (int row = 8; row <= lastRow; row++)
            {
                string docNumber = ws.Cells[row, 4].Text?.Trim();
                string formato = ws.Cells[row, 7].Text?.Trim().ToUpper();
                if (formato == "AT") continue;
                if (string.IsNullOrEmpty(docNumber)) continue;

                // Registro se o arquivo não existe no ACC
                if (!mapaArquivos.TryGetValue(docNumber + ".pdf", out var ids))
                {
                    if (!mapaArquivos.TryGetValue(docNumber, out ids))
                    {
                        logProcessamento.Add($"⚠️ NÃO ENCONTRADO NO ACC: {docNumber} (Formato na LDP: {formato})");
                        continue;
                    }
                }

                // Registro se o formato não tem template configurado
                if (!_templatesCache.TryGetValue(formato, out var config))
                {
                    logProcessamento.Add($"⚠️ SEM TEMPLATE CONFIGURADO: {docNumber} (Formato lido: {formato})");
                    continue;
                }

                string revisaoLida = await ExtrairRevisaoDoPdf(projectId, ids.VersionId, accessToken, config, docNumber);

                if (!string.IsNullOrEmpty(revisaoLida))
                {
                    if (long.TryParse(revAttributeIdString, out long revIdNumeric))
                    {
                        var attributesPayload = new { id = revIdNumeric, value = revisaoLida };

                        try 
                        {
                            await _autodeskApi.UpdateFileAttributesAsync(projectId, ids.VersionId, attributesPayload, accessToken);
                            
                            if (mapaArquivos.TryGetValue(docNumber + ".dwg", out var dwgIds))
                                await _autodeskApi.UpdateFileAttributesAsync(projectId, dwgIds.VersionId, attributesPayload, accessToken);

                            totalAtualizados++;
                            logProcessamento.Add($"✅ ATUALIZADO: {docNumber} | Rev: {revisaoLida} | Formato: {formato}");
                        }
                        catch (Exception ex) when (ex.Message.Contains("423") || ex.Message.Contains("locked"))
                        {
                            logProcessamento.Add($"❌ ARQUIVO TRAVADO (LOCKED): {docNumber} | Formato: {formato}");
                        }
                        catch (Exception ex)
                        {
                            logProcessamento.Add($"❌ ERRO API: {docNumber} | {ex.Message}");
                        }
                    }
                }
                else
                {
                    logProcessamento.Add($"🔍 OCR VAZIO: {docNumber} | Não foi possível ler o selo no formato {formato}");
                }
            }
        }

        // Grava o arquivo de log na pasta da aplicação
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Log_OCR_{projectId}_{DateTime.Now:yyyyMMdd_HHmm}.txt");
        File.WriteAllLines(logPath, logProcessamento);
        _logger.LogInformation($"📄 Log detalhado gerado em: {logPath}");

        return $"Sucesso! {totalAtualizados} atualizados. Log gerado na pasta raiz.";
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Erro Fatal");
        return $"Erro: {ex.Message}";
    }
    finally { if (File.Exists(tempLdpPath)) File.Delete(tempLdpPath); }
}
        // --- MANTENHA OS MÉTODOS AUXILIARES ABAIXO EXATAMENTE COMO ESTAVAM ---
        // (ScanFolderRecursiveWithVersionAsync, ExtrairRevisaoDoPdf, GetProjectRootIdSafeAsync, etc.)
        // Vou omiti-los aqui para economizar espaço, mas você DEVE mantê-los no arquivo.
        
        // ... (Cole aqui os métodos auxiliares: ExtrairRevisaoDoPdf, ScanFolder, GetRoot, etc.)
        
        // Só para garantir que você não perca o OCR corrigido:
        private async Task<string> ExtrairRevisaoDoPdf(string projectId, string urn, string token, TitleBlockConfig config, string docName)
        {
            try {
                using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, urn, token))
                using (var ms = new MemoryStream()) {
                    await stream.CopyToAsync(ms);
                    ms.Position = 0;
                    using (var pdf = PdfDocument.Open(ms)) {
                        var page = pdf.GetPage(1);
                        double mmToPoints = 72.0 / 25.4;
                        double boxW = config.BoxWidth * mmToPoints;
                        double boxH = config.BoxHeight * mmToPoints;
                        double mR = config.MarginRight * mmToPoints;
                        double mB = config.MarginBottom * mmToPoints;
                        
                        double startX = page.Width - mR - boxW;
                        double startY = mB; 
                        double endX = page.Width - mR;
                        double endY = mB + boxH;
                        if (startX < 0) startX = 0;

                        var letters = page.Letters.Where(l => l.GlyphRectangle.Left >= startX && l.GlyphRectangle.Right <= endX && l.GlyphRectangle.Bottom >= startY && l.GlyphRectangle.Top <= endY);
                        string raw = string.Join("", letters.Select(l => l.Value)).Trim();
                        string clean = raw.Replace("REV", "", StringComparison.OrdinalIgnoreCase).Replace("SCALE", "", StringComparison.OrdinalIgnoreCase);
                        if (clean.Contains(":")) clean = clean.Split(':').Last();
                        if (clean.Contains(".")) clean = clean.Split('.').Last();
                        clean = clean.Trim();
                        _logger.LogInformation($"      [OCR] Raw: '{raw}' -> Limpo: '{clean}'");
                        var match = Regex.Match(clean, @"([A-Z0-9]{1,3})$");
                        return match.Success ? match.Value : null;
                    }
                }
            } catch { return null; }
        }

        private async Task ScanFolderRecursiveWithVersionAsync(string projectId, string folderId, string token, Dictionary<string, (string, string)> map, int depth)
        {
            if (depth > 8) return; 
            try
            {
                string safeProj = projectId.StartsWith("b.") ? projectId : "b." + projectId;
                string url = $"https://developer.api.autodesk.com/data/v1/projects/{safeProj}/folders/{folderId}/contents";
                using (var client = new HttpClient()) {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode) return;
                    var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if(json.RootElement.TryGetProperty("data", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array) {
                        foreach (var item in items.EnumerateArray()) {
                            string type = item.GetProperty("type").GetString();
                            string id = item.GetProperty("id").GetString();
                            string name = item.GetProperty("attributes").GetProperty("displayName").GetString();
                            if (type == "folders") {
                                await ScanFolderRecursiveWithVersionAsync(projectId, id, token, map, depth + 1);
                            } else if (type == "items") {
                                if (item.TryGetProperty("relationships", out var rel) && rel.TryGetProperty("tip", out var tip) && tip.TryGetProperty("data", out var tipData)) {
                                    string tipVersionId = tipData.GetProperty("id").GetString();
                                    map[name] = (id, tipVersionId);
                                    string nameNoExt = Path.GetFileNameWithoutExtension(name);
                                    if (!map.ContainsKey(nameNoExt)) map[nameNoExt] = (id, tipVersionId);
                                }
                            }
                        }
                    }
                }
            } catch { }
        }

        private async Task<string> GetProjectRootIdSafeAsync(string hubId, string projectId, string token) 
        {
             try {
                string safeProj = projectId.StartsWith("b.") ? projectId : "b." + projectId;
                string url = $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects/{safeProj}";
                using(var client = new HttpClient()) {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    var res = await client.GetAsync(url);
                    var json = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    return json.RootElement.GetProperty("data").GetProperty("relationships").GetProperty("rootFolder").GetProperty("data").GetProperty("id").GetString();
                }
            } catch { return null; }
        }

        private async Task<string> GetProjectFilesFolderId(string projectId, string rootId, string token)
        {
             var id = await FindSubFolderWithLogAsync(projectId, rootId, "Project Files", token);
             if (id == null) id = await FindSubFolderWithLogAsync(projectId, rootId, "Arquivos de Projeto", token);
             return id;
        }

        private async Task<string> NavigateToLdpFolderAsync(string projectId, string baseFolderId, string token)
        {
             string idNivel1 = await FindSubFolderWithLogAsync(projectId, baseFolderId, "09", token); 
             if (idNivel1 == null) return null;
             return await FindSubFolderWithLogAsync(projectId, idNivel1, "09.10", token); 
        }

        private async Task<string> FindSubFolderWithLogAsync(string projectId, string parentFolderId, string prefixToFind, string token)
        {
            try {
                string safeProj = projectId.StartsWith("b.") ? projectId : "b." + projectId;
                string url = $"https://developer.api.autodesk.com/data/v1/projects/{safeProj}/folders/{parentFolderId}/contents";
                using(var client = new HttpClient()){
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    var res = await client.GetAsync(url);
                    var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    if(doc.RootElement.TryGetProperty("data", out var data)) {
                        foreach(var item in data.EnumerateArray()) {
                            if(item.GetProperty("type").GetString() == "folders" && 
                               item.GetProperty("attributes").GetProperty("displayName").GetString().Trim().StartsWith(prefixToFind, StringComparison.OrdinalIgnoreCase))
                               return item.GetProperty("id").GetString();
                        }
                    }
                }
            } catch {}
            return null;
        }

        private string GetLdpFileNameFromConfig(string projectId)
        {
            var name = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];
            if (string.IsNullOrEmpty(name) && projectId.StartsWith("b.")) name = _configuration[$"ProjectSettings:{projectId.Substring(2)}:LdpFileName"];
            if (string.IsNullOrEmpty(name) && !projectId.StartsWith("b.")) name = _configuration[$"ProjectSettings:b.{projectId}:LdpFileName"];
            return name ?? "LDP.xlsx";
        }

        private void LoadTemplateConfigs(string projectId)
        {
            _templatesCache = new Dictionary<string, TitleBlockConfig>(StringComparer.OrdinalIgnoreCase);
            var section = _configuration.GetSection($"TitleBlockTemplates:{projectId}");
            if (!section.Exists() && !projectId.StartsWith("b.")) section = _configuration.GetSection($"TitleBlockTemplates:b.{projectId}");
            if (!section.Exists() && projectId.StartsWith("b.")) section = _configuration.GetSection($"TitleBlockTemplates:{projectId.Substring(2)}");
            if (!section.Exists()) section = _configuration.GetSection("TitleBlockTemplates:Default");
            foreach (var child in section.GetChildren()) {
                _templatesCache[child.Key] = new TitleBlockConfig {
                    BoxWidth = child.GetValue<double>("BoxWidth"),
                    BoxHeight = child.GetValue<double>("BoxHeight"),
                    MarginRight = child.GetValue<double>("MarginRight"),
                    MarginBottom = child.GetValue<double>("MarginBottom")
                };
            }
        }
    }

    public class TitleBlockConfig
    {
        public double BoxWidth { get; set; }
        public double BoxHeight { get; set; }
        public double MarginRight { get; set; }
        public double MarginBottom { get; set; }
    }
}