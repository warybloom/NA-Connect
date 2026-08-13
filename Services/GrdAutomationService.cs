using APS.WebApp.Models;
using NA_Connect.Services;
using System.Data;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using iText.Kernel.Pdf; 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using iText.Layout;
using Microsoft.Data.SqlClient;
using Dapper;

namespace APS.WebApp.Services
{
    public class GrdAutomationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationService> _logger;
        private readonly string _connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";

        public GrdAutomationService(IServiceProvider serviceProvider, ILogger<GrdAutomationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Orquestra a execução completa das 11 etapas do motor de automação ForgeSync.
        /// </summary>
        public async Task ExecuteFullProjectSyncAsync(string projectIdAcc, string hubId, string projName)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var autodeskApi = scope.ServiceProvider.GetRequiredService<AutodeskApiService>();
            var ldpUpdateService = scope.ServiceProvider.GetRequiredService<LdpUpdateService>();
            var tsDumpService = scope.ServiceProvider.GetRequiredService<TimesheetDumpService>();
            var pdfMetadataService = scope.ServiceProvider.GetRequiredService<PdfMetadataService>();
            var grdSyncService = scope.ServiceProvider.GetRequiredService<GrdToLdpSyncService>();
            var ldpToCustoService = scope.ServiceProvider.GetRequiredService<LdpToCustoSyncService>();
            var scheduleService = scope.ServiceProvider.GetRequiredService<AccScheduleSyncService>();
            var ocrService = scope.ServiceProvider.GetService<IOcrService>(); 

            string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_{projectIdAcc}_{Guid.NewGuid()}.xlsx");
            string formattedProjectId = projectIdAcc.StartsWith("b.") ? projectIdAcc : $"b.{projectIdAcc}";
            
            try
            {
                _logger.LogInformation($"--- 🏗️ Motor Forge Sync: Iniciando Ciclo para {projName} ---");

                var ldpConfig = dbService.ObterConfiguracaoLdp(projectIdAcc);
                if (ldpConfig == null) 
                {
                    _logger.LogWarning($"⚠️ Projeto {projName} ignorado: Sem Template LDP vinculado no banco de dados.");
                    return; 
                }

                string robotToken = await autodeskApi.Get2LeggedTokenAsync();

                _logger.LogInformation($"🧹 Limpando dados antigos do projeto {projName}...");
                dbService.LimparDadosProjetoParaNovoCiclo(projectIdAcc);
                dbService.SalvarLogProcessamento(projectIdAcc, projName, "INICIO", "Banco limpo para novo ciclo.");

                // Captura o padrão de arquivo dinâmico gerido de forma relacional pelo SQL Server
                string padraoNomeFiltro = !string.IsNullOrEmpty(ldpConfig.File_Name_Pattern)
                    ? ldpConfig.File_Name_Pattern.Replace("%", "").Trim()
                    : "LD"; 

                string ldpFileId = dbService.GetLdpFileIdFromCache(projectIdAcc);
                if (string.IsNullOrEmpty(ldpFileId))
                {
                    _logger.LogInformation($">>> Buscando arquivo contendo '{padraoNomeFiltro}' na pasta/projeto: {ldpConfig.Folder_ID_LDP}");
                    ldpFileId = await autodeskApi.FindLdpFileUrnAsync(hubId, projectIdAcc, robotToken, ldpConfig.Folder_ID_LDP, padraoNomeFiltro);
                }
                
                if (string.IsNullOrEmpty(ldpFileId)) throw new Exception($"Nenhum ficheiro Excel contendo '{padraoNomeFiltro}' no nome foi encontrado na pasta do ACC.");

                using (var dl = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, ldpFileId, robotToken))
                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);
                
                var discovery = await scheduleService.GetOrDiscoverProjectContext(projectIdAcc, ldpPath, robotToken);

                dbService.RegistrarDnaProjeto(
                    projectIdAcc,                                           
                    ldpFileId,                                              
                    hubId,                                                  
                    discovery?.LdpName ?? projName,         
                    discovery?.Language ?? "PT",            
                    discovery?.NamingStandard ?? "PADRAO",  
                    null,   
                    null,   
                    "Sincronização via Motor Sync"          
                );

                _logger.LogInformation("🗄️ Indexando documentos do Excel no banco...");
                PersistirLdpNoBanco(ldpPath, projectIdAcc, dbService, ldpConfig);

                _logger.LogInformation("📄 Extraindo revisões exclusivas dos PDFs ativos...");
                var arquivosAcc = await autodeskApi.GetProjectFilesAsync(formattedProjectId, robotToken); 
                
                foreach (var file in arquivosAcc.Where(f => f.Name.ToLower().EndsWith(".pdf")))
                {
                    string tempPdfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{file.Name}");
                    using (var stream = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, file.FileId, robotToken))
                    using (var fs = new FileStream(tempPdfPath, FileMode.Create)) await stream.CopyToAsync(fs);

                    await ProcessarOCRMetadados(tempPdfPath, projectIdAcc, file.FileId, dbService, ocrService);
                    
                    if (File.Exists(tempPdfPath)) File.Delete(tempPdfPath);
                }

                // Sincronização Sequencial das Etapas do Pipeline
                await ldpUpdateService.UpdateLdpFromDatabaseAsync(hubId, formattedProjectId, robotToken, ldpFileId); 
                await ldpUpdateService.SyncAccAttributesFromSqlAsync(hubId, formattedProjectId, robotToken);
                await tsDumpService.ResetAndDumpTimesheetAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                await pdfMetadataService.SyncPdfRevisionsToAccAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                await grdSyncService.SyncGrdsToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                await ldpUpdateService.UpdateWorkflowStatusAsync(hubId, formattedProjectId, robotToken, "WDV284PFQTJDQZEG", ldpFileId);
                await ldpToCustoService.ExecuteFullSyncProcessAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                await ldpUpdateService.RunApprovalVerdictSyncAsync(hubId, formattedProjectId, robotToken, "WDV284PFQTJDQZEG", ldpFileId);
                await scheduleService.SyncScheduleToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                await ldpUpdateService.MirrorLdpControlToProjectAsync(hubId, formattedProjectId, robotToken, ldpFileId);

                dbService.SalvarLogProcessamento(projectIdAcc, projName, "SUCESSO", "Ciclo finalizado com sucesso.");
                _logger.LogInformation($"🏁 Ciclo FINALIZADO com sucesso para {projName}.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro no motor: {ex.Message}");
                dbService.SalvarLogProcessamento(projectIdAcc, projName, "FALHA_CICLO", ex.Message);
                throw;
            }
            finally { if (File.Exists(ldpPath)) File.Delete(ldpPath); }
        }

        public async Task ExecuteOnlyScheduleSyncAsync(string projectIdAcc, string hubId, string projName)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var autodeskApi = scope.ServiceProvider.GetRequiredService<AutodeskApiService>();
            var scheduleService = scope.ServiceProvider.GetRequiredService<AccScheduleSyncService>();

            string formattedProjectId = projectIdAcc.StartsWith("b.") ? projectIdAcc : $"b.{projectIdAcc}";
            
            try
            {
                _logger.LogInformation($"--- 🧪 Iniciando Teste Isolado de Cronograma para: {projName} ---");

                var ldpConfig = dbService.ObterConfiguracaoLdp(projectIdAcc);
                if (ldpConfig == null) 
                {
                    _logger.LogWarning($"⚠️ Projeto {projName} sem configuração vinculada no banco.");
                    return; 
                }

                string robotToken = await autodeskApi.Get2LeggedTokenAsync();
                string padraoNomeFiltro = !string.IsNullOrEmpty(ldpConfig.File_Name_Pattern)
                    ? ldpConfig.File_Name_Pattern.Replace("%", "").Trim()
                    : "LD";

                string ldpFileId = dbService.GetLdpFileIdFromCache(projectIdAcc);
                if (string.IsNullOrEmpty(ldpFileId))
                {
                    ldpFileId = await autodeskApi.FindLdpFileUrnAsync(hubId, projectIdAcc, robotToken, ldpConfig.Folder_ID_LDP, padraoNomeFiltro);
                }
                
                if (string.IsNullOrEmpty(ldpFileId)) throw new Exception("Arquivo LDP de controle não localizado para o teste.");

                _logger.LogInformation("⏳ Disparando processamento restrito do arquivo .mpp...");
                await scheduleService.SyncScheduleToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro crítico executando teste isolado de cronograma: {ex.Message}");
            }
        }

        public void AtualizarRevisaoOcrNaLdp(string projectIdAcc, string docNumber, string revision)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                const string sql = @"
                    UPDATE [dbo].[Ldp_Unificada] 
                    SET [Revision] = @rev, [Data_Sincronizacao] = GETDATE()
                    WHERE [Project_ID_ACC] = @pid AND [NA_Document_Number] = @doc";
                
                if (conn.State == ConnectionState.Closed) conn.Open();
                conn.Execute(sql, new { rev = revision, pid = projectIdAcc, doc = docNumber });
            }
        }

        private async Task ProcessarOCRMetadados(string filePath, string projectIdAcc, string fileIdAcc, DatabaseService db, IOcrService ocr)
        {
            if (ocr == null) return;

            try 
            {
                if (filePath.ToLower().EndsWith(".xlsx")) return;

                string tamanhoFolha = DetectarTamanhoFolhaPDF(filePath); 
                var coordenadas = db.ObterCoordenadasOCR(projectIdAcc, tamanhoFolha);

                if (coordenadas == null || !coordenadas.Any()) return;

                var metadados = new Dictionary<string, string>();
                foreach (var coord in coordenadas)
                {
                    var texto = await ocr.LerAreaAsync(filePath, coord.X, coord.Y, coord.Width, coord.Height);
                    if (!string.IsNullOrEmpty(texto)) metadados.Add(coord.CampoDestino, texto.Trim());
                }

                if (metadados.Any()) 
                {
                    db.SalvarMetadadosTemporarios(projectIdAcc, fileIdAcc, metadados);
                }
            }
            catch (Exception ex) { _logger.LogError($"[OCR Erro]: {ex.Message}"); }
        }

        private string DetectarTamanhoFolhaPDF(string path)
        {
            try 
            {
                using var reader = new PdfReader(path);
                using var pdfDoc = new PdfDocument(reader);
                var pageSize = pdfDoc.GetPage(1).GetPageSize();
                float widthMm = pageSize.GetWidth() * 0.352778f;
                float heightMm = pageSize.GetHeight() * 0.352778f;
                float maiorLado = Math.Max(widthMm, heightMm);

                if (maiorLado > 1179 && maiorLado < 1199) return "A0";
                if (maiorLado > 831 && maiorLado < 851)   return "A1";
                if (maiorLado > 584 && maiorLado < 604)   return "A2";
                if (maiorLado > 410 && maiorLado < 430)   return "A3";
                if (maiorLado > 287 && maiorLado < 307)   return "A4";

                if (maiorLado > 1108 && maiorLado < 1128) return "ANSI_E";
                if (maiorLado > 854 && maiorLado < 874)   return "ANSI_D";
                if (maiorLado > 549 && maiorLado < 569)   return "ANSI_C";
                if (maiorLado > 422 && maiorLado < 442)   return "ANSI_B";
                if (maiorLado > 269 && maiorLado < 289)   return "ANSI_A";

                return "A1";
            }
            catch { return "A1"; }
        }

        private void PersistirLdpNoBanco(string path, string projectIdAcc, DatabaseService db, ConfigLdpContext config)
        {
            using var package = new ExcelPackage(new FileInfo(path));
            
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(config.SheetName, StringComparison.OrdinalIgnoreCase)) 
                  ?? package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) 
                  ?? package.Workbook.Worksheets[0];
            
            int startRow = config.HeaderRow + 1;

            for (int r = startRow; r <= ws.Dimension.End.Row; r++)
            {
                var rowData = new Dictionary<int, object>();
                for (int c = 1; c <= 35; c++) rowData[c] = ws.Cells[r, c].Value;

                if (path.ToLower().EndsWith(".xlsx")) 
                {
                    rowData[14] = null; 
                }

                if (rowData.ContainsKey(4) && rowData[4] != null || rowData.ContainsKey(2) && rowData[2] != null) 
                    db.SalvarDadosLdpUnificada(projectIdAcc, rowData);
            }
        }
    }
}   