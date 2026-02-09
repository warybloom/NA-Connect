using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using APS.WebApp.Services;

namespace APS.WebApp.Services
{
    public class GrdAutomationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationWorker> _logger;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(100);

        // Altere de 1 minuto para 10 segundos apenas para validar o início

        //private readonly TimeSpan _period = TimeSpan.FromSeconds(10);

        public GrdAutomationWorker(IServiceProvider serviceProvider, ILogger<GrdAutomationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🤖 Robô de Processos N&A (10 Etapas) INICIADO.");

            using var timer = new PeriodicTimer(_period);

            while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
            {
                // Inicia o cronômetro de ciclo e zera telemetria
                var stopwatch = Stopwatch.StartNew();
                AutodeskApiService.ResetCounter();

                try
                {
                    _logger.LogInformation($"🤖 Robô acordou às {DateTime.Now:HH:mm:ss}.");

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var autodeskApi = scope.ServiceProvider.GetRequiredService<AutodeskApiService>();
                        var ldpUpdateService = scope.ServiceProvider.GetRequiredService<LdpUpdateService>();
                        var tsDumpService = scope.ServiceProvider.GetRequiredService<TimesheetDumpService>();
                        var pdfMetadataService = scope.ServiceProvider.GetRequiredService<PdfMetadataService>();
                        var grdSyncService = scope.ServiceProvider.GetRequiredService<GrdToLdpSyncService>();
                        var ldpToCustoService = scope.ServiceProvider.GetRequiredService<LdpToCustoSyncService>();
                        var scheduleService = scope.ServiceProvider.GetRequiredService<AccScheduleSyncService>();

                        string robotToken = await autodeskApi.Get2LeggedTokenAsync();

                        var projetosMonitorados = new List<(string Name, string HubId, string ProjectId)>
                        {
                            ("BML011.13 - MODULAR PROJECT", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.c3e20d9d-779e-423c-9381-abec38cf11cb"),
                            ("BML011.12 - O-015.1- MABOUNIE TANTALEX", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.5cb8ea9b-0898-4527-a0df-1cb383134f67"),
                            ("BML011.09 - O-013.1- SPENT CATALYST (ARAMCO)", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.5f6f39c3-6d20-4cdd-9370-b1f68b3b8608"),
                            ("BML011.08 - O-012.1- WA 1 (NIOBIUN)", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.1642f72c-6feb-403d-8697-cccf52692d59"),
                            ("BML011.07 - O-011.1- ALMONTY (TUNGSTEN)", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.bd8ede2c-c65e-45b8-bd9f-c8631d15ec7f"),
                            ("BML011.06 - O-010.1- FREEPORT MCMORAN", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.f8b4ea4a-991a-4281-a604-647da62a0465"),
                            ("BML011.05 - O-009.1- CODELCO", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.6005289e-5b45-43ae-ad9d-9bc93023508d"),
                            ("BML011.04 - O-008.1-VALE ONCA PUMA - COBALT / NICKEL", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.b49e1862-1f4b-43bd-aec5-914d9c0cfece"),
                            ("BML011.03 - STUDY SCOPE_NIOBEC", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.e80ba8cf-05cb-4973-bdee-9a30bdf3aad7"),
                            ("BML011.02 - STUDY SCOPE_SPENT CATALYST", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.1e3dc8ab-657c-4d34-a4f8-cd4e90f6151f"),
                            ("BML011.01 - STUDY SCOPE_ERAMET", "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "b.83db73f0-b438-4330-aee4-ce219eb7a22c")
                            
                        };

                        foreach (var proj in projetosMonitorados)
                        {
                            
                            _logger.LogInformation($"--- 🏗️ Processando Projeto: {proj.Name} ---");

                            // ==============================================================================
                            // ETAPA 0/9: RESET E CARGA DA ABA TIMESHEET (SQL BRUTO)
                            // ==============================================================================
                            _logger.LogInformation($"🧹 [0/9] Reconstruindo aba TIMESHEET via SQL Bruto...");
                            try
                            {
                                var result0 = await tsDumpService.ResetAndDumpTimesheetAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 0: {result0}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 0: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 1/9: BANCO DE DADOS E REVISÃO (HH + REV.)
                            // ==============================================================================
                            _logger.LogInformation($"💾 [1/9] Atualizando LDP: Horas do Banco e Atributo REV...");
                            try
                            {
                                var result1 = await ldpUpdateService.UpdateLdpFromDatabaseAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 1: {result1.SummaryMessage}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 1: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 2/9: OCR DE PDF (REVISÃO DO SELO -> ACC)
                            // ==============================================================================
                            _logger.LogInformation($"📄 [2/9] Extraindo Revisões de PDFs (Selo/OCR) para o ACC...");
                            try
                            {
                                var result2 = await pdfMetadataService.SyncPdfRevisionsToAccAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 2: {result2}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 2: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 3/9: SINCRONIZAÇÃO DE GRD (CORRESPONDÊNCIA -> LDP)
                            // ==============================================================================
                            _logger.LogInformation($"✉️ [3/9] Rastreando informações de envio (GRDs) para LDP...");
                            try
                            {
                                var resultGrd = await grdSyncService.SyncGrdsToLdpAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 3: {resultGrd}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 3: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 4/9: MÓDULO DE REVISÃO (STATUS IN THE FLOW)
                            // ==============================================================================
                            _logger.LogInformation($"🌊 [4/9] Atualizando LDP: Etapa do Fluxo (ACC Review Workflow)...");
                            try
                            {
                                var result4 = await ldpUpdateService.UpdateWorkflowStatusAsync(proj.HubId, proj.ProjectId, robotToken, "WDV284PFQTJDQZEG");
                                _logger.LogInformation($"   ✅ Etapa 4: {result4}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 4: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 5/9: SINCRONIZAÇÃO INTERNA (LDP -> CUSTO)
                            // ==============================================================================
                            _logger.LogInformation($"📊 [5/9] Sincronizando informações da LDP para aba CUSTO...");
                            try
                            {
                                var result5 = await ldpToCustoService.ExecuteFullSyncProcessAsync(proj.HubId, proj.ProjectId, robotToken);
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
                                var result6 = await ldpUpdateService.SyncAccAttributesFromLdpAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 6: {result6}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 6: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 7/9: VEREDITOS FINAIS
                            // ==============================================================================
                            _logger.LogInformation($"🏁 [7/9] Sincronizando Vereditos de Aprovação...");
                            try
                            {
                                var result7 = await ldpUpdateService.RunApprovalVerdictSyncAsync(proj.HubId, proj.ProjectId, robotToken, "WDV284PFQTJDQZEG");
                                _logger.LogInformation($"   ✅ Etapa 7: {result7}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 7: {ex.Message}"); }

                            // ==============================================================================
                            // ETAPA 8/9: SINCRONIZAÇÃO DE CRONOGRAMA (AVANÇO FÍSICO)
                            // ==============================================================================
                            _logger.LogInformation($"📅 [8/9] Sincronizando dados do Cronograma para LDP...");
                            try
                            {
                                var resSch = await scheduleService.SyncScheduleToLdpAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 8: {resSch}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 8: {ex.Message}"); }

                            

                            // ==============================================================================
                            // ETAPA 9/9: ESPELHAMENTO (CONTROLE -> PROJETO)
                            // ==============================================================================
                            _logger.LogInformation($"🪞 [9/9] Espelhando LDP para pasta pública de Projeto...");
                            try
                            {
                                var result9 = await ldpUpdateService.MirrorLdpControlToProjectAsync(proj.HubId, proj.ProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 9: {result9}");
                            }
                            catch (Exception ex) { _logger.LogError($"   ❌ Falha na Etapa 9: {ex.Message}"); }

                            _logger.LogInformation($"🏁 Ciclo concluído para {proj.Name}.");
                        }
                    }

                    stopwatch.Stop();
                    TimeSpan ts = stopwatch.Elapsed;

                    // RELATÓRIO FINAL DE PERFORMANCE
                    _logger.LogInformation("----------------------------------------------------------");
                    _logger.LogInformation($"🏁 RELATÓRIO DE EXECUÇÃO FINALIZADO");
                    _logger.LogInformation($"⏱️ Tempo Total Decorrido: {ts.Minutes}m {ts.Seconds}s");
                    _logger.LogInformation($"📊 Telemetria APS: {AutodeskApiService.TotalApiCallsInCurrentCycle} chamadas de API.");

                    if (AutodeskApiService.TotalApiCallsInCurrentCycle > 200)
                        _logger.LogWarning("⚠️ ALERTA: Volume de tráfego elevado. Risco de Rate Limit.");
                    else
                        _logger.LogInformation("🟢 Consumo de tráfego dentro dos limites de segurança.");

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