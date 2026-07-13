using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using System.Globalization;
using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Importações do MPXJ (Prevenção de Ambiguidade entre tipos de Task)
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;

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

        // 🎯 MAPEAMENTO INTELIGENTE (Padrão Forge Sync)
        private readonly List<(string[] LdpKeywords, string SchKey)> _columnMappings = new()
        {
            (new[] { "PHYSICAL COMPLETED", "REALIZADO" }, "%PHYSICAL COMPLETED"),
            (new[] { "PLANNED PHYSICAL", "PREVISTO" }, "%PLANNED PHYSICAL"),
            (new[] { "REPLANNED DATE", "REPROGRAMADA", "REPLANEJADA" }, "REPLANNED DATE"),
            (new[] { "BASE LINE", "BASELINE", "LINHA DE BASE" }, "PLANNED DATA (BASE LINE)"),
            (new[] { "WBS" }, "WBS_PERCENT")
        };

        public async Task<string> SyncScheduleToLdpAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation($"📅 [CRONOGRAMA] INICIANDO SINCRONIZAÇÃO COMPLETA PARA {projectId}...");

            string configKey = projectId.StartsWith("b.") ? projectId : "b." + projectId;
            string mppPath = Path.Combine(Path.GetTempPath(), $"SCH_{Guid.NewGuid()}.mpp");
            string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_MASTER_{Guid.NewGuid()}.xlsx");

            try
            {
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Detalhes da LDP não encontrados.");

                string ldpVersionUrn = itemDetails.TipVersionUrn;
                string ldpParentFolderId = itemDetails.ParentFolderId; 
                string ldpRealName = itemDetails.DisplayName;

                var folderSchId = await FindFolderByPaths(hubId, projectId, new[] {
                    new[] { "01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO" },
                    new[] { "01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES" }
                }, accessToken);

                if (string.IsNullOrEmpty(folderSchId)) return "Falha: Pasta de Cronograma não encontrada no ACC.";

                var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
                if (string.IsNullOrEmpty(schRes.FileId)) return "Falha: Arquivo .mpp não localizado na pasta de planejamento.";

                var mppDetails = await _autodeskApi.GetItemDetailsAsync(projectId, schRes.FileId, accessToken);
                string mppVersionUrn = mppDetails?.TipVersionUrn ?? schRes.FileId; 

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, mppVersionUrn, accessToken))
                using (var fs = new FileStream(mppPath, FileMode.Create)) await dl.CopyToAsync(fs);

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpVersionUrn, accessToken))
                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

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
                    
                    int matchColIndex = ldpCols.FirstOrDefault(x => x.Value.Contains("DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase)).Key;
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
                                int targetLdpCol = FindLdpCol(ldpCols, map.LdpKeywords);
                                if (targetLdpCol > 0 && mppValues.TryGetValue(map.SchKey, out string? val)) 
                                {
                                    if (string.IsNullOrEmpty(val) && (map.SchKey.Contains("DATE") || map.SchKey.Contains("LINE")))
                                    {
                                        val = "-";
                                    }

                                    string currentVal = ws.Cells[row, targetLdpCol].Text?.Trim();
                                    if (!string.Equals(currentVal, val, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogInformation($"✏️ [CRONOGRAMA] Linha {row} | Coluna {targetLdpCol} atualizada: '{currentVal}' ➡️ '{val}'");
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

                // 🎯 Persistência Blindada com 13 parâmetros (Incluindo trabalhoMpp)
                PersistToSql(projectId, projectFile, projetoCodigo);

                if (totalUpdates > 0)
                {
                    _logger.LogInformation($"⬆️ Subindo nova versão da LDP atualizada com {totalUpdates} linhas sincronizadas.");
                    using (var ul = new FileStream(ldpPath, FileMode.Open))
                        await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, ldpParentFolderId, ldpRealName, ul, accessToken);
                }

                return "✅ Sucesso! Dados do Cronograma gravados na LDP Master.";
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

        public async Task<ProjectDiscoveryResult> GetOrDiscoverProjectContext(string projectId, string ldpPath, string accessToken)
        {
            var cachedContext = _dbService.ObterContextoProjeto(projectId);
            if (cachedContext != null && !string.IsNullOrEmpty(cachedContext.NamingStandard)) return cachedContext;
            
            var discovery = await _discoveryService.ExecuteDiscoveryAsync(projectId, ldpPath, accessToken);
            _dbService.SalvarContextoProjeto(projectId, discovery.NamingStandard, discovery.Language);
            return discovery;
        }

        // 🎯 AJUSTADO: Retorno com os 13 argumentos exigidos pelo N&A Connect
        private void PersistToSql(string projectIdAcc, ProjectFile project, string projetoCodigo)
        {
            FieldType realField = GetFieldTypeByAliasKeywords(project, "Realizado") ?? TaskField.TEXT2;
            FieldType prevField = GetFieldTypeByAliasKeywords(project, "Previsto") ?? TaskField.TEXT3;
            FieldType trendField = GetFieldTypeByAliasKeywords(project, "tendencia") ?? TaskField.TEXT4;

            var tasks = project.getTasks();
            var iterTasks = tasks.iterator();
            while (iterTasks.hasNext())
            {
                MpxjTask task = (MpxjTask)iterTasks.next();
                if (task == null || task.getID() == null || task.getSummary()) continue;

                string uid = task.getGUID()?.ToString() ?? task.getUniqueID()?.ToString() ?? "";
                string docNumber = task.get(TaskField.TEXT1)?.ToString() ?? "";
                string taskName = task.getName() ?? "Sem Nome";
                string edtWbs = task.getWBS() ?? "";

                decimal physicalDone = ParsePercentage(task.get(realField));
                decimal predictedDone = ParsePercentage(task.get(prevField));
                string wbsPercent = task.get(TaskField.TEXT3)?.ToString() ?? "";

                // 🎯 LÓGICA DO N&A CONNECT RESTAURADA: Extração do TrabalhoMpp
                decimal trabalhoMpp = 0;
                var rawWork = task.getWork();
                if (rawWork != null)
                {
                    try { trabalhoMpp = Convert.ToDecimal(rawWork.getDuration()); } catch { }
                }

                DateTime? start = ConvertMpxjDate(task.getStart());
                
                DateTime? baseline = null;
                if (task.getBaselineFinish() != null)
                {
                    baseline = ConvertMpxjDate(task.getBaselineFinish());
                }

                DateTime? trendDate = ConvertMpxjDate(task.get(trendField));
                if (trendDate == null)
                {
                    trendDate = (physicalDone >= 100 && task.getActualFinish() != null)
                        ? ConvertMpxjDate(task.getActualFinish())
                        : ConvertMpxjDate(task.getFinish());
                }

                // 🎯 Chamada com os 13 parâmetros exigidos
                _dbService.SalvarTarefaCronograma(
                    projectIdAcc, uid, docNumber, taskName,
                    physicalDone, predictedDone, wbsPercent,
                    start, baseline, trendDate, edtWbs, projetoCodigo,
                    trabalhoMpp 
                );
            }
        }

        private FieldType? GetFieldTypeByAliasKeywords(ProjectFile file, string keyword)
        {
            try
            {
                var customFields = file.getCustomFields();
                if (customFields != null)
                {
                    var iterator = customFields.iterator();
                    while (iterator.hasNext())
                    {
                        var field = (net.sf.mpxj.CustomField)iterator.next();
                        string? aliasName = field.getAlias()?.ToString();
                        if (!string.IsNullOrEmpty(aliasName) && AliasMatchesKeyword(aliasName, keyword))
                        {
                            return field.getFieldType();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private Dictionary<string, Dictionary<string, string>> ExtractMppData(ProjectFile project)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            
            FieldType realField = GetFieldTypeByAliasKeywords(project, "Realizado") ?? TaskField.TEXT2;
            FieldType prevField = GetFieldTypeByAliasKeywords(project, "Previsto") ?? TaskField.TEXT3;
            FieldType trendField = GetFieldTypeByAliasKeywords(project, "tendencia") ?? TaskField.TEXT4;

            var tasks = project.getTasks();
            var iterator = tasks.iterator();

            while (iterator.hasNext())
            {
                MpxjTask task = (MpxjTask)iterator.next();
                if (task == null) continue;
                
                string? code = task.get(TaskField.TEXT1)?.ToString();
                if (string.IsNullOrWhiteSpace(code)) code = task.getName(); 
                if (string.IsNullOrWhiteSpace(code)) continue;

                string key = NormalizeDoc(code);
                var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var realizadoVal = task.get(realField);
                var previstoVal = task.get(prevField);
                var tendenciaVal = task.get(trendField);

                vals["%PHYSICAL COMPLETED"] = FormatPercentValue(realizadoVal);
                vals["%PLANNED PHYSICAL"] = FormatPercentValue(previstoVal);
                
                string formattedDate = FormatProjectDate(tendenciaVal);
                if (string.IsNullOrEmpty(formattedDate)) formattedDate = FormatProjectDate(task.getFinish());

                vals["REPLANNED DATE"] = formattedDate;
                vals["PLANNED DATA (BASE LINE)"] = formattedDate;
                vals["WBS_PERCENT"] = task.get(TaskField.TEXT3)?.ToString() ?? "";

                if (!result.ContainsKey(key)) result[key] = vals;
            }
            return result;
        }

        private bool AliasMatchesKeyword(string? alias, string keyword)
        {
            if (string.IsNullOrEmpty(alias)) return false;
            return NormalizeString(alias).Contains(NormalizeString(keyword));
        }

        private string NormalizeString(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        private decimal ParsePercentage(object? val)
        {
            if (val == null) return 0;
            string cleanVal = val.ToString().Replace("%", "").Trim().Replace(",", ".");
            if (decimal.TryParse(cleanVal, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res))
                return res;
            return 0;
        }

        private DateTime? ConvertMpxjDate(object? mpxjDate)
        {
            if (mpxjDate == null) return null;
            if (mpxjDate is java.util.Date javaDate)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(javaDate.getTime()).DateTime;
            }
            try { return DateTime.Parse(mpxjDate.ToString()); }
            catch { return null; }
        }

        private string FormatPercentValue(object? val)
        {
            if (val == null) return "0%";
            if (val is java.lang.Number num)
            {
                double dbl = num.doubleValue();
                if (dbl <= 1.0 && dbl > 0) dbl = dbl * 100.0;
                return dbl.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
            string s = val.ToString()?.Trim() ?? "0%";
            if (!string.IsNullOrEmpty(s) && !s.Contains("%")) s += "%";
            return s;
        }

        private ProjectFile LoadMppFile(string path)
        {
            try { java.lang.System.setProperty("log4j2.loggerContextFactory", "org.apache.logging.log4j.simple.SimpleLoggerContextFactory"); } catch { }
            return new UniversalProjectReader().read(path);
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

        private int FindLdpCol(Dictionary<int, string> cols, string[] keywords)
        {
            var normalizedKeywords = keywords.Select(k => NormalizeString(k).ToUpper()).ToList();
            var match = cols.FirstOrDefault(c => 
                normalizedKeywords.Any(kw => NormalizeString(c.Value).ToUpper().Contains(kw))
            );
            return match.Key;
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

        public async Task<string> FindFolderByPaths(string hub, string proj, string[][] paths, string token)
        {
            foreach (var pathArray in paths)
            {
                try
                {
                    var id = await _autodeskApi.FindFolderIdByPathAsync(hub, proj, pathArray, token);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                catch { }
            }
            return null;
        }

        private string NormalizeDoc(string s) => Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");
    }
}