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

        // Mapeamento mantido exatamente como no seu código
        // No arquivo AccScheduleSyncService.cs, altere este bloco:

        private readonly List<(string[] LdpNames, string SchKey)> _columnMappings = new()
        {
            (new[] { "%Realizado" }, "Texto2"),
            (new[] { "WBS (%)", "WBS_PERCENT" }, "WBS_PERCENT"),
            (new[] { "Término da linha de base", "PLANNED DATA (BASE LINE)" }, "Planned_Date_Baseline"),
            
            // AJUSTE AQUI: O primeiro array é o que o robô procura no EXCEL (LDP)
            // O segundo parâmetro ("Replanned_Date") é a chave que usamos internamente para a data de tendência
            (new[] { "REPLANNED DATE", "Término tendência" }, "Replanned_Date"), 
            
            (new[] { "%Previsto" }, "Texto3")
        };

        // ====================================================================================
        // MÉTODO RESTAURADO: Descoberta de DNA (Necessário para a Etapa 0 do Worker)
        // ====================================================================================
        public async Task<NA_Connect.Models.ProjectDiscoveryResult> GetOrDiscoverProjectContext(string projectId, string ldpPath, string accessToken)
        {
            var cachedContext = _dbService.ObterContextoProjeto(projectId);
            if (cachedContext != null && !string.IsNullOrEmpty(cachedContext.NamingStandard)) return cachedContext;
            
            var discovery = await _discoveryService.ExecuteDiscoveryAsync(projectId, ldpPath, accessToken);
            _dbService.SalvarContextoProjeto(projectId, discovery.NamingStandard, discovery.Language);
            
            return discovery;
        }

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

                if (string.IsNullOrEmpty(folderSchId)) return "Falha: Pasta de Cronograma não encontrada.";

                var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
                if (string.IsNullOrEmpty(schRes.FileId)) return "Falha: Arquivo .mpp não localizado.";

                var mppDetails = await _autodeskApi.GetItemDetailsAsync(projectId, schRes.FileId, accessToken);
                string mppVersionUrn = mppDetails?.TipVersionUrn ?? schRes.FileId;

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, mppVersionUrn, accessToken))
                using (var fs = new FileStream(mppPath, FileMode.Create)) await dl.CopyToAsync(fs);

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpVersionUrn, accessToken))
                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

                var projectFile = LoadMppFile(mppPath);

                int totalUpdates = 0;
                string projetoCodigo = "";

                using (var package = new ExcelPackage(new FileInfo(ldpPath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
                    projetoCodigo = ws.Cells[8, 25].Text?.Trim();
                    if (string.IsNullOrEmpty(projetoCodigo)) projetoCodigo = configKey.Replace("b.", "");

                    // 1. Persistência no SQL com datas blindadas
                    PersistToSql(projectId, projectFile, projetoCodigo);

                    // 2. Recuperação do Banco
                    var scheduleDataDoBanco = _dbService.GetDadosCronogramaDoBanco(projectId);

                    var ldpCols = MapLdpHeaders(ws);
                    int matchColIndex = ldpCols.FirstOrDefault(x => x.Value.Contains("N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase)).Key;
                    if (matchColIndex == 0) matchColIndex = 4;

                    for (int row = 8; row <= ws.Dimension.End.Row; row++)
                    {
                        string docCodeExcel = ws.Cells[row, matchColIndex].Text?.Trim();
                        if (string.IsNullOrEmpty(docCodeExcel)) continue;

                        string key = NormalizeDoc(docCodeExcel);

                        if (scheduleDataDoBanco.TryGetValue(key, out var mppValues))
                        {
                            bool lineUpdated = false;
                            foreach (var map in _columnMappings)
                            {
                                int targetLdpCol = FindLdpCol(ldpCols, map.LdpNames);
                                if (targetLdpCol > 0)
                                {
                                    mppValues.TryGetValue(map.SchKey, out string? val);
                                    
                                    // Prevenção contra datas vazias/lixo ("Término da linha de base", etc)
                                    if (string.IsNullOrEmpty(val) && (map.SchKey.Contains("Date") || map.SchKey.Contains("Replanned")))
                                    {
                                        val = "ND";
                                    }

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

                if (totalUpdates > 0)
                {
                    using (var ul = new FileStream(ldpPath, FileMode.Open))
                        await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, ldpParentFolderId, ldpRealName, ul, accessToken);
                }

                return "✅ Sucesso! LDP atualizada rigorosamente via MPXJ e SQL.";
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

        private void PersistToSql(string projectIdAcc, ProjectFile project, string projetoCodigo)
        {
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

                decimal physicalDone = ParsePercentage(task.get(TaskField.TEXT2));
                decimal predictedDone = ParsePercentage(task.get(TaskField.TEXT3));
                string wbsPercent = task.get(TaskField.TEXT3)?.ToString() ?? "";

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

                DateTime? trendDate = null;
                if (physicalDone >= 100 && task.getActualFinish() != null)
                {
                    trendDate = ConvertMpxjDate(task.getActualFinish());
                }
                else
                {
                    trendDate = ConvertMpxjDate(task.getFinish());
                }

                _dbService.SalvarTarefaCronograma(
                    projectIdAcc, uid, docNumber, taskName,
                    physicalDone, predictedDone, wbsPercent,
                    start, baseline, trendDate, edtWbs, projetoCodigo,
                    trabalhoMpp
                );
            }
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

        private int FindLdpCol(Dictionary<int, string> cols, string[] options)
        {
            var match = cols.FirstOrDefault(c => options.Any(opt => c.Value.Trim().Equals(opt.Trim(), StringComparison.OrdinalIgnoreCase)));
            return match.Key;
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