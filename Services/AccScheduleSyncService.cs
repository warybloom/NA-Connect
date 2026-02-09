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
using NA_Connect.Services;

namespace APS.WebApp.Services
{
    public class AccScheduleSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly DatabaseService _dbService;
        private readonly ILogger<AccScheduleSyncService> _logger;
        private readonly IConfiguration _config;

        public AccScheduleSyncService(
            AutodeskApiService autodeskApi, 
            DatabaseService dbService, 
            ILogger<AccScheduleSyncService> logger, 
            IConfiguration config)
        {
            _autodeskApi = autodeskApi;
            _dbService = dbService;
            _logger = logger;
            _config = config;
        }

        private readonly List<(string[] LdpNames, string SchKey)> _columnMappings = new()
        {
            (new[] { "%PHYSICAL COMPLETED", "%FÍSICO REALIZADO", "% PHYSICAL COMPLETED" }, "PERCENT_COMPLETE"),
            (new[] { "WBS (%)", "WBS (%)" }, "WBS_PERCENT"),
            (new[] { "PLANNED DATA (BASE LINE)", "DATA PREVISTA LINHA DE BASE 0", "PLANNED DATE" }, "BASELINE_FINISH"),
            (new[] { "REPLANNED DATE", "DATA REPROGRAMADA", "RE-PLANNED DATE" }, "CURRENT_FINISH"),
            (new[] { "%PREDICTED PHYSICAL", "%FÍSICO PREVISTO", "% PREDICTED PHYSICAL" }, "TEXT4")
        };

        public async Task<string> SyncScheduleToLdpAsync(string hubId, string projectId, string accessToken)
{
    _logger.LogInformation($"📅 [CRONOGRAMA] INICIANDO SINCRONIZAÇÃO COMPLETA PARA {projectId}...");

    // 1. Preparação de caminhos e chaves
    string configKey = projectId.StartsWith("b.") ? projectId : "b." + projectId;
    string cachedLdpFileId = _dbService.GetLdpFileIdFromCache(projectId); 
    string ldpFileNameSearch = _config[$"ProjectSettings:{configKey}:LdpFileName"] ?? "LDP";

    string mppPath = Path.Combine(Path.GetTempPath(), $"SCH_{Guid.NewGuid()}.mpp");
    string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_MASTER_{Guid.NewGuid()}.xlsx");

    try
    {
        // 2. Localização de Pastas e Downloads (Mantendo lógica bilíngue)
        var folderSchId = await FindFolderByPaths(hubId, projectId, new[] {
            new[] { "01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO" },
            new[] { "01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES" }
        }, accessToken);

        var folderLdpId = await FindFolderByPaths(hubId, projectId, new[] {
            new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },
            new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }
        }, accessToken);

        if (string.IsNullOrEmpty(folderSchId)) return "Falha: Pasta de Cronograma não encontrada.";

        // Gerenciamento de Cache LDP
        string finalLdpFileId = cachedLdpFileId;
        if (string.IsNullOrEmpty(finalLdpFileId))
        {
            var ldpRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderLdpId, ldpFileNameSearch, accessToken);
            if (string.IsNullOrEmpty(ldpRes.FileId)) ldpRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderLdpId, ".xlsx", accessToken);
            
            if (!string.IsNullOrEmpty(ldpRes.FileId)) {
                finalLdpFileId = ldpRes.FileId;
                _dbService.AtualizarIdLdpProjeto(projectId, finalLdpFileId);
            }
        }

        var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
        if (string.IsNullOrEmpty(schRes.FileId) || string.IsNullOrEmpty(finalLdpFileId)) return "Falha: Arquivos não localizados.";

        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, schRes.FileId, accessToken))
        using (var fs = new FileStream(mppPath, FileMode.Create)) await dl.CopyToAsync(fs);

        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, finalLdpFileId, accessToken))
        using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

        // 3. Extração de Dados (MPP) - Sem filtros para o Gantt
        var projectFile = LoadMppFile(mppPath);
        var scheduleData = ExtractMppData(projectFile); 

        int totalUpdates = 0;
        string projetoCodigo = ""; 

        // --- 🏗️ ETAPA 9: ATUALIZAÇÃO EXCEL E CAPTURA DO CR-OS (Y8) ---
        using (var package = new ExcelPackage(new FileInfo(ldpPath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            
            // 🎯 AJUSTE CRÍTICO: Pegando o valor na Linha 8 (Primeira linha de dados)
            projetoCodigo = ws.Cells[8, 25].Text?.Trim(); // Coluna Y = 25

            if (string.IsNullOrEmpty(projetoCodigo)) {
                projetoCodigo = configKey.Replace("b.", ""); 
                _logger.LogWarning($">>> [AVISO] Célula Y8 vazia. Usando fallback: {projetoCodigo}");
            } else {
                _logger.LogInformation($">>> [SUCESSO] CR-OS Capturado: {projetoCodigo}");
            }

            var ldpCols = MapLdpHeaders(ws);
            int matchColIndex = ldpCols.FirstOrDefault(x => x.Value.Contains("N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase)).Key;
            if (matchColIndex == 0) matchColIndex = 4;

            // Loop para atualizar os percentuais no Excel
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

        // --- 🏗️ ETAPA 8: PERSISTÊNCIA SQL COMPLETA (Para o Gráfico de Gantt) ---
        // Aqui removemos o filtro N&A para trazer a árvore hierárquica completa
        PersistToSql(projectId, projectFile, projetoCodigo);

        if (totalUpdates > 0)
        {
            using (var ul = new FileStream(ldpPath, FileMode.Open))
                await _autodeskApi.CreateNewVersionAsync(projectId, finalLdpFileId, folderLdpId, "LDP_Atualizada.xlsx", ul, accessToken);
        }

        return "✅ Sucesso! Cronograma Completo e CR-OS corrigidos no SQL.";
    }
    catch (Exception ex)
    {
        _logger.LogError($"❌ Erro Crítico: {ex.Message}");
        return $"Falha: {ex.Message}";
    }
    finally
    {
        if (File.Exists(mppPath)) File.Delete(mppPath);
        if (File.Exists(ldpPath)) File.Delete(ldpPath);
    }
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

        // % FÍSICO REAL
        decimal physicalDone = 0;
        var rawPercent = task.getPercentageComplete();
        if (rawPercent != null)
            physicalDone = (decimal)((java.lang.Number)rawPercent).doubleValue() / 10m;

        // 🎯 % PREVISTO (Captura do campo Número1 do Project)
        decimal predictedDone = 0;
        var rawPredicted = task.get(TaskField.NUMBER1); 
        if (rawPredicted != null)
            predictedDone = (decimal)((java.lang.Number)rawPredicted).doubleValue();

        // 🎯 WBS % (Captura do campo Texto3 do Project)
        string wbsPercent = task.get(TaskField.TEXT3)?.ToString() ?? ""; 

        // Datas para o Gráfico de Gantt
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
        
        // No dicionário do Excel, ainda precisamos de uma chave. 
        // Se não tem código N&A, usamos o nome da tarefa para não dar erro no TryGetValue.
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

        private async Task<string> FindFolderByPaths(string hub, string proj, string[][] paths, string token)
        {
            foreach (var pathArray in paths) {
                try {
                    var id = await _autodeskApi.FindFolderIdByPathAsync(hub, proj, pathArray, token);
                    if (!string.IsNullOrEmpty(id)) return id;
                } catch { }
            }
            return null;
        }

        private string NormalizeDoc(string s) => Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");
    }
}