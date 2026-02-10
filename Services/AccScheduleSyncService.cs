using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using System.Globalization;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Importações do MPXJ (Prevenção de Ambiguidade entre tipos de Task)
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;

// 🎯 IMPORTANTE: Usando o namespace correto dos modelos para evitar ambiguidade
using NA_Connect.Services;
using NA_Connect.Models; 

namespace APS.WebApp.Services
{
    public class AccScheduleSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly DatabaseService _dbService;
        private readonly ILogger<AccScheduleSyncService> _logger;
        private readonly IConfiguration _config;
        private readonly ProjectAutoDiscoveryService _discoveryService;

        public AccScheduleSyncService(
            AutodeskApiService autodeskApi, 
            DatabaseService dbService, 
            ILogger<AccScheduleSyncService> logger, 
            IConfiguration config,
            ProjectAutoDiscoveryService discoveryService)
        {
            _autodeskApi = autodeskApi;
            _dbService = dbService;
            _logger = logger;
            _config = config;
            _discoveryService = discoveryService;
        }

        private readonly List<(string[] LdpNames, string SchKey)> _columnMappings = new()
        {
            (new[] { "%PHYSICAL COMPLETED", "%FÍSICO REALIZADO", "% PHYSICAL COMPLETED" }, "PERCENT_COMPLETE"),
            (new[] { "WBS (%)", "WBS (%)" }, "WBS_PERCENT"),
            (new[] { "PLANNED DATA (BASE LINE)", "DATA PREVISTA LINHA DE BASE 0", "PLANNED DATE" }, "BASELINE_FINISH"),
            (new[] { "REPLANNED DATE", "DATA REPROGRAMADA", "RE-PLANNED DATE" }, "CURRENT_FINISH"),
            (new[] { "%PREDICTED PHYSICAL", "%FÍSICO PREVISTO", "% PREDICTED PHYSICAL" }, "TEXT4")
        };

        /// <summary>
        /// Sincroniza o cronograma do ACC para a planilha LDP e persiste no SQL para o Power BI.
        /// </summary>
       public async Task<string> SyncScheduleToLdpAsync(string hubId, string projectId, string accessToken, string ldpFileId)
{
    _logger.LogInformation($"📅 [CRONOGRAMA] INICIANDO SINCRONIZAÇÃO COMPLETA PARA {projectId}...");

    string configKey = projectId.StartsWith("b.") ? projectId : "b." + projectId;
    string mppPath = Path.Combine(Path.GetTempPath(), $"SCH_{Guid.NewGuid()}.mpp");
    string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_MASTER_{Guid.NewGuid()}.xlsx");

    try
    {
        // -----------------------------------------------------------------------
        // 1. VACINA: RECUPERA DETALHES DA LDP (Versão e PASTA)
        // -----------------------------------------------------------------------
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
        if (itemDetails == null) throw new Exception("Detalhes da LDP não encontrados.");

        string ldpVersionUrn = itemDetails.TipVersionUrn;
        string ldpParentFolderId = itemDetails.ParentFolderId; // 🎯 OBRIGATÓRIO PARA O UPLOAD
        string ldpRealName = itemDetails.DisplayName;

        // -----------------------------------------------------------------------
        // 2. BUSCA O CRONOGRAMA (.mpp) - Esse ainda precisa procurar a pasta
        // -----------------------------------------------------------------------
        var folderSchId = await FindFolderByPaths(hubId, projectId, new[] {
            new[] { "01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO" },
            new[] { "01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES" }
        }, accessToken);

        if (string.IsNullOrEmpty(folderSchId)) return "Falha: Pasta de Cronograma não encontrada.";

        var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
        if (string.IsNullOrEmpty(schRes.FileId)) return "Falha: Arquivo .mpp não localizado.";

        // -----------------------------------------------------------------------
        // 3. DOWNLOADS (MPP + LDP)
        // -----------------------------------------------------------------------
        
        // MPP (Pega detalhes para garantir a versão correta também)
        var mppDetails = await _autodeskApi.GetItemDetailsAsync(projectId, schRes.FileId, accessToken);
        string mppVersionUrn = mppDetails?.TipVersionUrn ?? schRes.FileId; // Fallback se falhar

        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, mppVersionUrn, accessToken))
        using (var fs = new FileStream(mppPath, FileMode.Create)) await dl.CopyToAsync(fs);

        // LDP (Usa a versão correta descoberta no passo 1)
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpVersionUrn, accessToken))
        using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

        // -----------------------------------------------------------------------
        // 4. PROCESSAMENTO (MPXJ e EPPlus) - Lógica Mantida
        // -----------------------------------------------------------------------
        var projectFile = LoadMppFile(mppPath);
        var scheduleData = ExtractMppData(projectFile); 

        int totalUpdates = 0;
        string projetoCodigo = ""; 

        using (var package = new ExcelPackage(new FileInfo(ldpPath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            
            projetoCodigo = ws.Cells[8, 25].Text?.Trim();
            if (string.IsNullOrEmpty(projetoCodigo)) projetoCodigo = configKey.Replace("b.", ""); 

            var ldpCols = MapLdpHeaders(ws);
            int matchColIndex = ldpCols.FirstOrDefault(x => x.Value.Contains("N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase)).Key;
            if (matchColIndex == 0) matchColIndex = 4;

            for (int row = 8; row <= ws.Dimension.End.Row; row++)
            {
                string docCodeExcel = ws.Cells[row, matchColIndex].Text?.Trim();
                if (string.IsNullOrEmpty(docCodeExcel)) continue;

                string key = NormalizeDoc(docCodeExcel);
                if (scheduleData.TryGetValue(key, out var mppValues))
                {
                    bool lineUpdated = false;
                    foreach (var map in _columnMappings)
                    {
                        int targetLdpCol = FindLdpCol(ldpCols, map.LdpNames);
                        if (targetLdpCol > 0 && mppValues.TryGetValue(map.SchKey, out string val)) 
                        {
                            string currentVal = ws.Cells[row, targetLdpCol].Text?.Trim();
                            if (!string.Equals(currentVal, val, StringComparison.OrdinalIgnoreCase))
                            {
                                ws.Cells[row, targetLdpCol].Value = val;
                                lineUpdated = true;
                            }
                        }
                    }
                    if (lineUpdated) totalUpdates++;
                }
            }
            package.Save();
        }

        // Salva no SQL (opcional, mantido)
        PersistToSql(projectId, projectFile, projetoCodigo);

        // -----------------------------------------------------------------------
        // 5. UPLOAD CORRIGIDO (LDP)
        // -----------------------------------------------------------------------
        if (totalUpdates > 0)
        {
            using (var ul = new FileStream(ldpPath, FileMode.Open))
                // 🎯 Passa ldpParentFolderId para evitar erro BAD_INPUT
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, ldpParentFolderId, ldpRealName, ul, accessToken);
        }

        return "✅ Sucesso! Cronograma Completo e CR-OS corrigidos.";
    }
    catch (Exception ex)
    {
        _logger.LogError($"❌ Erro Crítico Cronograma: {ex.Message}");
        return $"Falha: {ex.Message}";
    }
    finally
    {
        if (File.Exists(mppPath)) File.Delete(mppPath);
        if (File.Exists(ldpPath)) File.Delete(ldpPath);
    }
}

        /// <summary>
        /// Gerencia a memória do projeto (Padrão de Nome e Idioma) via Banco ou Descoberta.
        /// </summary>
        public async Task<NA_Connect.Models.ProjectDiscoveryResult> GetOrDiscoverProjectContext(string projectId, string ldpPath, string accessToken)
        {
            // 🎯 CORREÇÃO: Usando o tipo explícito do namespace Models para evitar conflitos
            var cachedContext = _dbService.ObterContextoProjeto(projectId);

            if (cachedContext != null && !string.IsNullOrEmpty(cachedContext.NamingStandard))
            {
                _logger.LogInformation($">>> [MEMÓRIA] Contexto recuperado do Banco: {cachedContext.NamingStandard} | {cachedContext.Language}");
                return cachedContext;
            }

            _logger.LogWarning("🔍 Contexto não encontrado no banco. Iniciando Auto-Detecção...");
            
            // Certifique-se que o método ExecuteDiscoveryAsync também retorna o tipo NA_Connect.Models.ProjectDiscoveryResult
            var discovery = await _discoveryService.ExecuteDiscoveryAsync(projectId, ldpPath, accessToken);

            _dbService.SalvarContextoProjeto(projectId, discovery.NamingStandard, discovery.Language);
            
            _logger.LogInformation($">>> [SUCESSO] Contexto salvo no Banco para uso futuro.");
            
            return discovery;
        }

        private void PersistToSql(string projectIdAcc, ProjectFile project, string projetoCodigo)
        {
            var tasks = project.getTasks();
            var iterTasks = tasks.iterator();
            while (iterTasks.hasNext())
            {
                MpxjTask task = (MpxjTask)iterTasks.next();
                if (task == null || task.getID() == null) continue;

                string uid = task.getGUID()?.ToString() ?? task.getUniqueID()?.ToString() ?? "";
                string docNumber = task.get(TaskField.TEXT1)?.ToString() ?? ""; 
                string taskName = task.getName() ?? "Sem Nome";
                string edtWbs = task.getWBS() ?? ""; 

                decimal physicalDone = 0;
                var rawPercent = task.getPercentageComplete();
                if (rawPercent != null)
                    physicalDone = (decimal)((java.lang.Number)rawPercent).doubleValue() / 10m;

                decimal predictedDone = 0;
                var rawPredicted = task.get(TaskField.NUMBER1); 
                if (rawPredicted != null)
                    predictedDone = (decimal)((java.lang.Number)rawPredicted).doubleValue();

                string wbsPercent = task.get(TaskField.TEXT3)?.ToString() ?? ""; 

                DateTime? start = task.getStart() != null ? DateTime.Parse(task.getStart().toString()) : null;
                DateTime? baseline = task.getBaselineFinish() != null ? DateTime.Parse(task.getBaselineFinish().toString()) : null;
                DateTime? finish = task.getFinish() != null ? DateTime.Parse(task.getFinish().toString()) : null;

                _dbService.SalvarTarefaCronograma(
                    projectIdAcc, uid, docNumber, taskName, 
                    physicalDone, predictedDone, wbsPercent, 
                    start, baseline, finish, edtWbs, projetoCodigo
                );
            }
        }

        private Dictionary<string, Dictionary<string, string>> ExtractMppData(ProjectFile project)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            var tasks = project.getTasks();
            var iterTasks = tasks.iterator();

            while (iterTasks.hasNext())
            {
                MpxjTask task = (MpxjTask)iterTasks.next();
                
                string? code = task.get(TaskField.TEXT1)?.ToString();
                if (string.IsNullOrWhiteSpace(code)) code = task.getName(); 
                if (string.IsNullOrWhiteSpace(code)) continue;

                string key = NormalizeDoc(code);
                var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                
                double rawPct = 0;
                var mpxjPct = task.getPercentageComplete();
                if (mpxjPct != null) rawPct = ((java.lang.Number)mpxjPct).doubleValue() / 10.0;
                
                vals["PERCENT_COMPLETE"] = rawPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                vals["TEXT4"] = (task.get(TaskField.TEXT4)?.ToString() ?? "") + (task.get(TaskField.TEXT4)?.ToString().Contains("%") == true ? "" : "%");
                vals["WBS_PERCENT"] = task.get(TaskField.TEXT3)?.ToString() ?? "";
                vals["BASELINE_FINISH"] = FormatProjectDate(task.getBaselineFinish());
                vals["CURRENT_FINISH"] = FormatProjectDate(task.getFinish());

                if (!result.ContainsKey(key)) result[key] = vals;
            }
            return result;
        }

        private ProjectFile LoadMppFile(string path)
        {
            try { java.lang.System.setProperty("log4j2.loggerContextFactory", "org.apache.logging.log4j.simple.SimpleLoggerContextFactory"); } catch { }
            return new UniversalProjectReader().read(path);
        }

        private string FormatProjectDate(object? dateObj)
        {
            if (dateObj == null) return "";
            if (DateTime.TryParse(dateObj.ToString(), out DateTime dt))
            {
                var culture = new CultureInfo("pt-BR");
                string dayName = culture.DateTimeFormat.GetAbbreviatedDayName(dt.DayOfWeek).ToUpper();
                if (dayName.Length > 3) dayName = dayName.Substring(0, 3);
                return $"{dayName} {dt:dd/MM/yy}";
            }
            return "";
        }

        private Dictionary<int, string> MapLdpHeaders(ExcelWorksheet ws)
        {
            var map = new Dictionary<int, string>();
            for (int c = 1; c <= 100; c++) {
                string txt = ws.Cells[7, c].Text?.Trim().ToUpper();
                if (!string.IsNullOrEmpty(txt)) map[c] = txt;
            }
            return map;
        }

        private int FindLdpCol(Dictionary<int, string> cols, string[] options)
        {
            var match = cols.FirstOrDefault(c => options.Any(opt => c.Value.Trim().Equals(opt.Trim(), StringComparison.OrdinalIgnoreCase)));
            return match.Key;
        }

        // Modulo: Services/AccScheduleSyncService.cs

// 🎯 ALTERAÇÃO: Mude de 'private' para 'public'
public async Task<string> FindFolderByPaths(string hub, string proj, string[][] paths, string token)
{
    foreach (var pathArray in paths) 
    {
        try 
        {
            // Este método chama a API para navegar nas subpastas
            var id = await _autodeskApi.FindFolderIdByPathAsync(hub, proj, pathArray, token);
            if (!string.IsNullOrEmpty(id)) return id;
        } 
        catch 
        { 
            // Se não encontrar em um caminho, tenta o próximo (PT ou EN)
        }
    }
    return null;
}

        private string NormalizeDoc(string s) => Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");
    }
}