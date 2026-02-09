using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data;
using APS.WebApp.Services;
using NA_Connect.Services; // Namespace do seu DatabaseService

namespace APS.WebApp.Services
{
    public class GrdAutomationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationWorker> _logger;
        //private readonly TimeSpan _period = TimeSpan.FromMinutes(100);
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
                        // Injeção dos serviços necessários
                        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                        var autodeskApi = scope.ServiceProvider.GetRequiredService<AutodeskApiService>();
                        var ldpUpdateService = scope.ServiceProvider.GetRequiredService<LdpUpdateService>();
                        var tsDumpService = scope.ServiceProvider.GetRequiredService<TimesheetDumpService>();
                        var pdfMetadataService = scope.ServiceProvider.GetRequiredService<PdfMetadataService>();
                        var grdSyncService = scope.ServiceProvider.GetRequiredService<GrdToLdpSyncService>();
                        var ldpToCustoService = scope.ServiceProvider.GetRequiredService<LdpToCustoSyncService>();
                        var scheduleService = scope.ServiceProvider.GetRequiredService<AccScheduleSyncService>();

                        // 1. BUSCA PROJETOS ATIVOS NO BANCO (Substitui a lista manual anterior)
                        _logger.LogInformation("🔍 Consultando SQL para identificar projetos ativos...");
                        DataTable dtAtivos = dbService.GetProjetosAtivos();

                        if (dtAtivos.Rows.Count == 0)
                        {
                            _logger.LogWarning("⚠️ Nenhum projeto marcado como 'Is_Ativo = 1' no banco de dados.");
                            continue;
                        }

                        _logger.LogInformation($"✅ {dtAtivos.Rows.Count} projetos encontrados para processamento.");

                        string robotToken = await autodeskApi.Get2LeggedTokenAsync();

                        // 2. LOOP DE PROCESSAMENTO BASEADO NO SQL
                        foreach (DataRow row in dtAtivos.Rows)
                        {
                            string projName = row["Nome_Projeto"].ToString();
                            string hubId = row["Account_ID_ACC"].ToString();
                            string projectId = row["Project_ID_ACC"].ToString();

                            _logger.LogInformation($"--- 🏗️ Processando Projeto: {projName} ---");

                            // ==============================================================================
                            // ETAPA 0/9: RESET E CARGA DA ABA TIMESHEET (SQL BRUTO)
                            // ==============================================================================
                            _logger.LogInformation($"🧹 [0/9] Reconstruindo aba TIMESHEET via SQL Bruto...");
                            try
                            {
                                var result0 = await tsDumpService.ResetAndDumpTimesheetAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 0: {result0}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 0: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 1/9: BANCO DE DADOS E REVISÃO (HH + REV.)
                            // ==============================================================================
                            _logger.LogInformation($"💾 [1/9] Atualizando LDP: Horas do Banco e Atributo REV...");
                            try
                            {
                                var result1 = await ldpUpdateService.UpdateLdpFromDatabaseAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 1: {result1.SummaryMessage}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 1: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 2/9: OCR DE PDF (REVISÃO DO SELO -> ACC)
                            // ==============================================================================
                            _logger.LogInformation($"📄 [2/9] Extraindo Revisões de PDFs (Selo/OCR) para o ACC...");
                            try
                            {
                                var result2 = await pdfMetadataService.SyncPdfRevisionsToAccAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 2: {result2}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 2: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 3/9: SINCRONIZAÇÃO DE GRD (CORRESPONDÊNCIA -> LDP)
                            // ==============================================================================
                            _logger.LogInformation($"✉️ [3/9] Rastreando informações de envio (GRDs) para LDP...");
                            try
                            {
                                var resultGrd = await grdSyncService.SyncGrdsToLdpAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 3: {resultGrd}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 3: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 4/9: MÓDULO DE REVISÃO (STATUS IN THE FLOW)
                            // ==============================================================================
                            _logger.LogInformation($"🌊 [4/9] Atualizando LDP: Etapa do Fluxo (ACC Review Workflow)...");
                            try
                            {
                                var result4 = await ldpUpdateService.UpdateWorkflowStatusAsync(hubId, projectId, robotToken, "WDV284PFQTJDQZEG");
                                _logger.LogInformation($"   ✅ Etapa 4: {result4}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 4: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 5/9: SINCRONIZAÇÃO INTERNA (LDP -> CUSTO)
                            // ==============================================================================
                            _logger.LogInformation($"📊 [5/9] Sincronizando informações da LDP para aba CUSTO...");
                            try
                            {
                                var result5 = await ldpToCustoService.ExecuteFullSyncProcessAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 5: {result5}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 5: {ex.Message}"); }

                            // Pausa técnica para indexação do ACC
                            await Task.Delay(5000, stoppingToken);

                            // ==============================================================================
                            // ETAPA 6/9: SINCRONIZAÇÃO DE ATRIBUTOS (LDP -> ACC)
                            // ==============================================================================
                            _logger.LogInformation($"⚡ [6/9] Sincronizando Atributos no ACC (Metadados)...");
                            try
                            {
                                var result6 = await ldpUpdateService.SyncAccAttributesFromLdpAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 6: {result6}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 6: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 7/9: VEREDITOS FINAIS
                            // ==============================================================================
                            _logger.LogInformation($"🏁 [7/9] Sincronizando Vereditos de Aprovação...");
                            try
                            {
                                var result7 = await ldpUpdateService.RunApprovalVerdictSyncAsync(hubId, projectId, robotToken, "WDV284PFQTJDQZEG");
                                _logger.LogInformation($"   ✅ Etapa 7: {result7}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 7: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 8/9: SINCRONIZAÇÃO DE CRONOGRAMA (AVANÇO FÍSICO)
                            // ==============================================================================
                            _logger.LogInformation($"📅 [8/9] Sincronizando dados do Cronograma para LDP...");
                            try
                            {
                                var resSch = await scheduleService.SyncScheduleToLdpAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 8: {resSch}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 8: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 9/9: ESPELHAMENTO (CONTROLE -> PROJETO)
                            // ==============================================================================
                            _logger.LogInformation($"🪞 [9/9] Espelhando LDP para pasta pública de Projeto...");
                            try
                            {
                                var result9 = await ldpUpdateService.MirrorLdpControlToProjectAsync(hubId, projectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 9: {result9}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 9: {ex.Message}"); }

                            _logger.LogInformation($"🏁 Ciclo concluído para {projName}.");
                        }
                    }

                    stopwatch.Stop();
                    TimeSpan ts = stopwatch.Elapsed;

                    _logger.LogInformation("----------------------------------------------------------");
                    _logger.LogInformation($"🏁 RELATÓRIO DE EXECUÇÃO FINALIZADO");
                    _logger.LogInformation($"⏱️ Tempo Total Decorrido: {ts.Minutes}m {ts.Seconds}s");
                    _logger.LogInformation($"📊 Telemetria APS: {AutodeskApiService.TotalApiCallsInCurrentCycle} chamadas de API.");
                    
                    _logger.LogInformation(AutodeskApiService.TotalApiCallsInCurrentCycle > 200 
                        ? "⚠️ ALERTA: Volume de tráfego elevado. Risco de Rate Limit." 
                        : "🟢 Consumo de tráfego dentro dos limites de segurança.");

                    _logger.LogInformation("💤 Robô finalizou tarefas. Dormindo...");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Erro fatal no Loop do Robô: {ex.Message}");
                }
            }
        }
    }
}