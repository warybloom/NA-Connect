using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using APS.WebApp.Services;
using NA_Connect.Services;
using NA_Connect.Models;

namespace APS.WebApp.Services
{
    public class GrdAutomationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationWorker> _logger;
        private readonly TimeSpan _period = TimeSpan.FromSeconds(5);

        public GrdAutomationWorker(IServiceProvider serviceProvider, ILogger<GrdAutomationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🤖 Robô de Processos N&A (Controle via SQL) INICIADO.");

            using var timer = new PeriodicTimer(_period);

            while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
            {
                var stopwatch = Stopwatch.StartNew();
                AutodeskApiService.ResetCounter();

                try
                {
                    _logger.LogInformation($"🤖 Robô acordou às {DateTime.Now:HH:mm:ss}.");

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                        var autodeskApi = scope.ServiceProvider.GetRequiredService<AutodeskApiService>();
                        var ldpUpdateService = scope.ServiceProvider.GetRequiredService<LdpUpdateService>();
                        var tsDumpService = scope.ServiceProvider.GetRequiredService<TimesheetDumpService>();
                        var pdfMetadataService = scope.ServiceProvider.GetRequiredService<PdfMetadataService>();
                        var grdSyncService = scope.ServiceProvider.GetRequiredService<GrdToLdpSyncService>();
                        var ldpToCustoService = scope.ServiceProvider.GetRequiredService<LdpToCustoSyncService>();
                        var scheduleService = scope.ServiceProvider.GetRequiredService<AccScheduleSyncService>();
                        
                        _logger.LogInformation("🔍 Consultando SQL para identificar projetos ativos...");
                        DataTable dtAtivos = dbService.GetProjetosAtivos();

                        if (dtAtivos.Rows.Count == 0)
                        {
                            _logger.LogWarning("⚠️ Nenhum projeto marcado como 'Is_Ativo = 1' no banco de dados.");
                            continue;
                        }

                        _logger.LogInformation($"✅ {dtAtivos.Rows.Count} projetos encontrados para processamento.");
                        string robotToken = await autodeskApi.Get2LeggedTokenAsync();

                        foreach (DataRow projectRow in dtAtivos.Rows)
                        {
                            string projName = projectRow["Nome_Projeto"].ToString();
                            string projectId = projectRow["Project_ID_ACC"].ToString(); // ID original para SQL
                            string hubId = projectRow["Account_ID_ACC"].ToString();
                            string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_{projectId}_{Guid.NewGuid()}.xlsx");

                            // 🎯 AJUSTE APS: Garante o prefixo 'b.' para as chamadas de API da Autodesk
                            string formattedProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";

                            _logger.LogInformation($"--- 🏗️ Processando Projeto: {projName} ---");

                            try
                            {
                                // ==============================================================================
                                // PREPARAÇÃO OBRIGATÓRIA: LOCALIZAÇÃO E DESCOBERTA DA LDP
                                // ==============================================================================
                                string ldpFileId = dbService.GetLdpFileIdFromCache(projectId);
                                string ldpRealName = "";

                                // Se não tiver no cache, inicia o processo de descoberta profunda
                                if (string.IsNullOrEmpty(ldpFileId))
                                {
                                    _logger.LogInformation($"📂 [DESCOBERTA] Localizando pasta de Gestão...");

                                    var folderLdpId = await scheduleService.FindFolderByPaths(hubId, formattedProjectId, new[] {
                                        new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },
                                        new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }
                                    }, robotToken);

                                    if (!string.IsNullOrEmpty(folderLdpId))
                                    {
                                        _logger.LogInformation($"🔎 Pasta encontrada. Varrendo arquivos Excel...");

                                        // 🎯 TENTATIVA: Pegar o primeiro Excel da pasta, independente do nome (LDP, BML, etc)
                                        var fileInfo = await autodeskApi.GetFirstExcelInFolderAsync(formattedProjectId, folderLdpId, robotToken);

                                        if (!string.IsNullOrEmpty(fileInfo.FileId))
                                        {
                                            ldpFileId = fileInfo.FileId;
                                            ldpRealName = fileInfo.FileName;
                                            _logger.LogInformation($"✅ LDP Identificada: [{ldpRealName}].");
                                            
                                            // Nota: O registro no banco será feito na Etapa 0 junto com o DNA completo
                                        }
                                    }
                                }

                                // VALIDAÇÃO FINAL ANTES DE PROSSEGUIR
                                if (string.IsNullOrEmpty(ldpFileId))
                                {
                                    _logger.LogCritical($"❌ [ERRO] Nenhum arquivo Excel encontrado na pasta de Gestão para o projeto {projName}. Pulando...");
                                    continue;
                                }

                                // DOWNLOAD FÍSICO COM O ID DESCOBERTO
                                using (var dl = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, ldpFileId, robotToken))
                                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);
                                
                                // ==============================================================================
                                // 🧬 ETAPA 0/11: DNA DO PROJETO (METADADOS E CONTEXTO)
                                // ==============================================================================
                                _logger.LogInformation($"🔍 [0/11] Validando DNA do Projeto (Padrão, Idioma e Banco de Dados)...");

                                // Analisa o arquivo baixado para descobrir PT/EN e NA/CLIENT
                                var discovery = await scheduleService.GetOrDiscoverProjectContext(projectId, ldpPath, robotToken);

                                if (discovery == null || string.IsNullOrEmpty(discovery.NamingStandard))
                                {
                                    _logger.LogCritical("❌ [ETAPA 0] FALHA CRÍTICA: DNA não identificado. Abortando execução para evitar danos.");
                                    continue;
                                }
                                
                                // Se o nome veio vazio da descoberta (cache), usa o nome descoberto na varredura
                                if (string.IsNullOrEmpty(ldpRealName)) ldpRealName = discovery.LdpName ?? "LDP_Desconhecida.xlsx";

                                // 💾 SALVAMENTO DO DNA COMPLETO NO BANCO
                                _logger.LogInformation($"💾 Gravando DNA completo no Banco de Dados...");
                                dbService.RegistrarDnaProjeto(projectId, ldpFileId, hubId, ldpRealName, discovery.Language, discovery.NamingStandard);

                                _logger.LogInformation($"   ✅ Etapa 0: Padrão [{discovery.NamingStandard}] | Idioma [{discovery.Language}] | LDP [{ldpRealName}]");


                                // ==============================================================================
                                // ETAPAS DE PROCESSAMENTO - AGORA RECEBEM O ldpFileId DIRETO DO ROBÔ
                                // ==============================================================================
                            
                                // ETAPA 1/11: TIMESHEET
                                _logger.LogInformation($"🧹 [1/11] Reconstruindo aba TIMESHEET via SQL Bruto...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result0 = await tsDumpService.ResetAndDumpTimesheetAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 1: {result0}");

                                // ETAPA 2/11: HORAS E REVISÃO
                                _logger.LogInformation($"💾 [2/11] Atualizando LDP: Horas do Banco e Atributo REV...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result1 = await ldpUpdateService.UpdateLdpFromDatabaseAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 2: {result1.SummaryMessage}");
                                    
                                // ETAPA 3/11: OCR
                                _logger.LogInformation($"📄 [3/11] Extraindo Revisões de PDFs (Selo/OCR)...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result2 = await pdfMetadataService.SyncPdfRevisionsToAccAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 3: {result2}");
                                    
                                // ETAPA 4/11: GRDs
                                _logger.LogInformation($"✉️ [4/11] Rastreando informações de envio (GRDs)...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var resultGrd = await grdSyncService.SyncGrdsToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 4: {resultGrd}");

                                // ETAPA 5/11: WORKFLOW
                                _logger.LogInformation($"🌊 [5/11] Atualizando LDP: Etapa do Fluxo (ACC Review)...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result4 = await ldpUpdateService.UpdateWorkflowStatusAsync(
                                    hubId, 
                                    formattedProjectId, 
                                    robotToken, 
                                    "WDV284PFQTJDQZEG", // Este é o userId (ou contexto do fluxo)
                                    ldpFileId           // 🎯 O ID do arquivo Excel VAI POR ÚLTIMO
                                );
                                _logger.LogInformation($"   ✅ Etapa 5: {result4}");

                                // ETAPA 6/11: CUSTO
                                _logger.LogInformation($"📊 [6/11] Sincronizando informações da LDP para aba CUSTO...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result5 = await ldpToCustoService.ExecuteFullSyncProcessAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 6: {result5}");

                                await Task.Delay(2000, stoppingToken);
                                
                                // ETAPA 7/11: ATRIBUTOS ACC
                                _logger.LogInformation($"⚡ [7/11] Sincronizando Atributos no ACC (Metadados)...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result6 = await ldpUpdateService.SyncAccAttributesFromLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 7: {result6}");
                                
                                // ETAPA 8/11: VEREDITOS
                                _logger.LogInformation($"🏁 [8/11] Sincronizando Vereditos de Aprovação...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                // 🎯 Correct order: User/Workflow ID first, then File ID
                                var result7 = await ldpUpdateService.RunApprovalVerdictSyncAsync(
                                    hubId, 
                                    formattedProjectId, 
                                    robotToken, 
                                    "WDV284PFQTJDQZEG", // userId / workflow context
                                    ldpFileId           // ldpFileId (Item ID)
                                );
                                _logger.LogInformation($"   ✅ Etapa 8: {result7}");

                                // ETAPA 9/11: CRONOGRAMA
                                _logger.LogInformation($"📅 [9/11] Sincronizando dados do Cronograma para LDP...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var resSch = await scheduleService.SyncScheduleToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 9: {resSch}");

                                // ETAPA 10/11: ESPELHAMENTO
                                _logger.LogInformation($"🪞 [10/11] Espelhando LDP para pasta pública...");
                                // 🎯 CORREÇÃO: Passando ldpFileId
                                var result9 = await ldpUpdateService.MirrorLdpControlToProjectAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 10: {result9}");
                                
                                // ==============================================================================
                                // 🏗️ ETAPA 11/11: PERSISTÊNCIA NA LDP UNIFICADA (SQL SERVER)
                                // ==============================================================================
                                _logger.LogInformation($"🗄️ [11/11] Sincronizando LDP Unificada para o SQL Server...");

                                if (File.Exists(ldpPath))
                                {
                                    using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(ldpPath)))
                                    {
                                        var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
                                        int totalRowsSaved = 0;

                                        for (int excelRow = 8; excelRow <= ws.Dimension.End.Row; excelRow++)
                                        {
                                            var rowData = new Dictionary<int, object>();
                                            for (int col = 1; col <= 35; col++)
                                            {
                                                rowData[col] = ws.Cells[excelRow, col].Value;
                                            }

                                            // Só salva se houver código N&A ou Cliente
                                            if (rowData[4] != null || rowData[2] != null)
                                            {
                                                dbService.SalvarDadosLdpUnificada(projectId, rowData);
                                                totalRowsSaved++;
                                            }
                                        }
                                        _logger.LogInformation($"   ✅ Etapa 11: {totalRowsSaved} linhas persistidas no SQL.");
                                    }
                                }

                                _logger.LogInformation($"🏁 Ciclo concluído com sucesso para {projName}.");

                                
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"❌ Erro ao processar projeto {projName}: {ex.Message}");
                            }
                            finally
                            {
                                if (File.Exists(ldpPath))
                                {
                                    File.Delete(ldpPath);
                                    _logger.LogInformation("🧹 Arquivo temporário removido.");
                                }
                            }

                            
                            // ✅ MOVA PARA CÁ (Ainda dentro do foreach):
                            var telemetry = AutodeskApiService.GetTelemetry();
                            _logger.LogInformation($@"
                            📊 [PERFORMANCE - {projName}]
                            ⏱️ Tempo de Execução: {stopwatch.Elapsed:mm\:ss}
                            📞 Chamadas à API Autodesk: {telemetry.Calls}
                            📉 Rate Limit Atual: {telemetry.Limit}
                            __________________________________________________________");
                            
                        }
                    }
                    
                    stopwatch.Stop();
                    _logger.LogInformation("----------------------------------------------------------");
                    _logger.LogInformation($"🏁 RELATÓRIO DE EXECUÇÃO FINALIZADO em {stopwatch.Elapsed.Minutes}m {stopwatch.Elapsed.Seconds}s");
                    _logger.LogInformation("💤 Robô dormindo...");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Erro fatal no Loop do Robô: {ex.Message}");
                }
            }
        }

        // =========================================================================================
        // 🛡️ MÉTODO BLINDADO PARA TRATAR DATAS DO EXCEL
        // =========================================================================================
        private object TratarData(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return DBNull.Value;

            string v = valor.Trim().ToUpper();
            // Lista de "sujeiras" comuns no Excel
            if (v == "-" || v == "N/A" || v == "NA" || v == "TBD" || v == "A DEFINIR" || v == "S/D") return DBNull.Value;

            // 1. Tenta converter formato PT-BR (dd/MM/yyyy)
            if (DateTime.TryParse(valor, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out DateTime data))
            {
                if (data.Year < 1900 || data.Year > 2100) return DBNull.Value; // Data inválida para SQL
                return data;
            }

            // 2. Tenta converter número serial do Excel (ex: 45321)
            if (double.TryParse(valor, out double serial))
            {
                try 
                { 
                    var dataSerial = DateTime.FromOADate(serial);
                    if (dataSerial.Year < 1900 || dataSerial.Year > 2100) return DBNull.Value;
                    return dataSerial;
                } 
                catch { }
            }

            return DBNull.Value; // Se nada funcionar, salva NULL e não trava
        }
    }
}