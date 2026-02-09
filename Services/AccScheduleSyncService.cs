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

// Importações do MPXJ
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;

namespace APS.WebApp.Services
{
    public class AccScheduleSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly ILogger<AccScheduleSyncService> _logger;
        private readonly IConfiguration _config;

        public AccScheduleSyncService(AutodeskApiService autodeskApi, ILogger<AccScheduleSyncService> logger, IConfiguration config)
        {
            _autodeskApi = autodeskApi;
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
            _logger.LogInformation("📅 [CRONOGRAMA] INICIANDO SINCRONIZAÇÃO...");

            string configKey = projectId.StartsWith("b.") ? projectId : "b." + projectId;
            string ldpFileNameSearch = _config[$"ProjectSettings:{configKey}:LdpFileName"] ?? "LDP";

            string mppPath = Path.Combine(Path.GetTempPath(), $"SCH_{Guid.NewGuid()}.mpp");
            string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_MASTER_{Guid.NewGuid()}.xlsx");

            try
            {
                // 1. Localiza Pastas
                var folderSchId = await FindFolderByPaths(hubId, projectId, new[] {
                    new[] { "01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO" },
                    new[] { "01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES" }
                }, accessToken);

                var folderLdpId = await FindFolderByPaths(hubId, projectId, new[] {
                    new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },
                    new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }
                }, accessToken);

                // 2. Arquivos
                var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
                _logger.LogInformation($"   📄 Cronograma encontrado: {schRes.FileName}");

                var ldpRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderLdpId, ldpFileNameSearch, accessToken);
                _logger.LogInformation($"   📄 LDP encontrada: {ldpRes.FileName}");

                // 3. Download
                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, schRes.FileId, accessToken))
                using (var fs = new FileStream(mppPath, FileMode.Create)) await dl.CopyToAsync(fs);

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpRes.FileId, accessToken))
                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

                // 4. Extração MPP
                var scheduleData = ExtractMppData(mppPath);
                _logger.LogInformation($"   📊 Dados extraídos do MPP: {scheduleData.Count} tarefas identificadas.");

                if (scheduleData.Count == 0) return "Nenhuma tarefa encontrada no MPP.";

                // 5. Atualização Excel
                int totalUpdates = 0;
                using (var package = new ExcelPackage(new FileInfo(ldpPath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
                    var ldpCols = MapLdpHeaders(ws);

                    string keyColConfig = "N&A DOCUMENT NUMBER";
                    int matchColIndex = ldpCols.FirstOrDefault(x => x.Value.Contains(keyColConfig, StringComparison.OrdinalIgnoreCase)).Key;

                    if (matchColIndex == 0)
                    {
                        matchColIndex = ldpCols.FirstOrDefault(x => x.Value.Contains("N&A NUMBER", StringComparison.OrdinalIgnoreCase)).Key;
                        if (matchColIndex == 0) matchColIndex = 4;
                    }

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

                                if (targetLdpCol > 0 && mppValues.TryGetValue(map.SchKey, out string? val))
                                {
                                    string currentVal = ws.Cells[row, targetLdpCol].Text?.Trim();

                                    // Compara valores normalizados para evitar update falso
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

                if (totalUpdates > 0)
                {
                    using (var ul = new FileStream(ldpPath, FileMode.Open))
                        await _autodeskApi.CreateNewVersionAsync(projectId, ldpRes.FileId, folderLdpId, ldpRes.FileName, ul, accessToken);
                    return $"✅ Sucesso! {totalUpdates} linhas do cronograma atualizadas.";
                }

                return "Finalizado sem alterações.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro Cronograma: {ex.Message}");
                return $"Falha no Cronograma: {ex.Message}";
            }
            finally
            {
                if (File.Exists(mppPath)) File.Delete(mppPath);
                if (File.Exists(ldpPath)) File.Delete(ldpPath);
            }
        }

        private Dictionary<string, Dictionary<string, string>> ExtractMppData(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                try { java.lang.System.setProperty("log4j2.loggerContextFactory", "org.apache.logging.log4j.simple.SimpleLoggerContextFactory"); } catch { }

                var reader = new UniversalProjectReader();
                ProjectFile project = reader.read(path);

                FieldType matchField = null;
                var customFields = project.getCustomFields();
                var iterCF = customFields.iterator();
                while (iterCF.hasNext())
                {
                    var cf = (CustomField)iterCF.next();
                    string alias = cf.getAlias()?.ToUpper() ?? "";
                    if (alias.Contains("N&A") || alias.Contains("DOCUMENTO"))
                    {
                        matchField = cf.getFieldType();
                        break;
                    }
                }

                var tasks = project.getTasks();
                var iterTasks = tasks.iterator();
                while (iterTasks.hasNext())
                {
                    MpxjTask task = (MpxjTask)iterTasks.next();

                    string? code = null;
                    if (matchField != null) code = task.get(matchField)?.ToString();
                    if (string.IsNullOrWhiteSpace(code)) code = task.getName();
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    string key = NormalizeDoc(code);
                    if (key.Length < 5) continue;

                    var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // 🎯 1. FÍSICO REALIZADO (Ajuste de Escala)
                    // Se o Project mandar 40, dividimos por 10 -> 4. Adicionamos % -> "4%".
                    // Se o Project mandar 1000, dividimos por 10 -> 100. Adicionamos % -> "100%".
                    // Matematicamente isso equivale a dividir o bruto por 1000 para achar o decimal (0.04)
                    // e depois multiplicar por 100 para achar o texto da porcentagem (4).
                    double rawPct = Convert.ToDouble(task.getPercentageComplete()?.ToString() ?? "0");
                    double fixedPct = rawPct / 10.0;
                    vals["PERCENT_COMPLETE"] = fixedPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";

                    // 🎯 2. FÍSICO PREVISTO (TEXT4)
                    string text4 = task.get(TaskField.TEXT4)?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(text4) && !text4.Contains("%"))
                    {
                        text4 += "%";
                    }
                    vals["TEXT4"] = text4;

                    vals["WBS_PERCENT"] = task.get(TaskField.TEXT3)?.ToString() ?? "";
                    vals["BASELINE_FINISH"] = FormatProjectDate(task.getBaselineFinish());
                    vals["CURRENT_FINISH"] = FormatProjectDate(task.getFinish());

                    if (!result.ContainsKey(key)) result[key] = vals;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro lendo arquivo MPP: {ex.Message}");
            }
            return result;
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
            for (int c = 1; c <= 100; c++)
            {
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
            foreach (var p in paths)
            {
                var id = await _autodeskApi.FindFolderIdByPathAsync(hub, proj, p, token);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            throw new Exception("Pasta não localizada.");
        }

        private string NormalizeDoc(string s) => Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");
    }
}