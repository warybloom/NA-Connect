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
using System.Text;
using APS.WebApp.Services;
using NA_Connect.Services;
using NA_Connect.Models;

namespace APS.WebApp.Services
{
    // =========================================================================================
    // 🛠️ CLONE DE CONSOLE: Captura tudo da tela preta e joga para o arquivo TXT em tempo real
    // =========================================================================================
    public class ConsoleOutputInterceptor : TextWriter
    {
        private readonly TextWriter _originalConsoleOutput;
        private readonly StreamWriter _fileWriter;

        public ConsoleOutputInterceptor(string filePath, TextWriter originalConsoleOutput)
        {
            _originalConsoleOutput = originalConsoleOutput;
            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            
            _fileWriter = new StreamWriter(filePath, true) { AutoFlush = true };
        }

        public override Encoding Encoding => _originalConsoleOutput.Encoding;

        public override void Write(char value)
        {
            _originalConsoleOutput.Write(value);
            _fileWriter.Write(value);
        }

        public override void WriteLine(string value)
        {
            _originalConsoleOutput.WriteLine(value);
            
            // Limpa códigos de cor ANSI que deixam o TXT sujo
            string cleanLine = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\x1B\[[^@-~]*[@-~]", "");
            _fileWriter.WriteLine(cleanLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileWriter.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class GrdAutomationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationWorker> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;

        public GrdAutomationWorker(IServiceProvider serviceProvider, ILogger<GrdAutomationWorker> logger, IHostApplicationLifetime hostApplicationLifetime)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        // =========================================================================================
        // ⏰ INICIALIZAÇÃO PARA TESTES (RODA 5 SEGUNDOS APÓS O INÍCIO)
        // =========================================================================================
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🤖 Robô de Processos N&A INICIADO EM MODO DE TESTE.");

            try
            {
                _logger.LogInformation("⏳ MODO TESTE ATIVO: Aguardando 5 segundos para disparar o motor principal...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await ProcessarCicloCompleto(stoppingToken);
                    _logger.LogInformation("🛑 MODO TESTE: Execução única finalizada. Reinicie a aplicação para testar o próximo projeto.");
                }
            }
            catch (TaskCanceledException) 
            { 
                _logger.LogInformation("Execução cancelada antes de iniciar.");
            }
        }

        // =========================================================================================
        // 🚀 MÉTODO DE PROCESSAMENTO DO LOTE
        // =========================================================================================
        private async Task ProcessarCicloCompleto(CancellationToken stoppingToken)
        {
            var stopwatch = Stopwatch.StartNew();
            AutodeskApiService.ResetCounter();

            // 🎯 INICIA A INTERCEPTAÇÃO DE TELA PARA O LOG COMPLETO
            string folderPath = @"C:\Dev\N-A-Connect\Logs";
            string fileName = $"Execucao_Completa_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string fullLogPath = Path.Combine(folderPath, fileName);
            
            var originalConsole = Console.Out;
            using var interceptor = new ConsoleOutputInterceptor(fullLogPath, originalConsole);
            Console.SetOut(interceptor);

            Console.WriteLine("==========================================================");
            Console.WriteLine($"🚀 EXECUÇÃO LOTE INICIADA EM: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            Console.WriteLine("==========================================================");

            try
            {
                _logger.LogInformation($"🤖 Robô acordou às {DateTime.Now:HH:mm:ss}. Executando lote de projetos...");

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
                    }
                    else
                    {
                        _logger.LogInformation($"✅ {dtAtivos.Rows.Count} projetos encontrados para processamento.");
                        
                        string robotToken = await autodeskApi.Get2LeggedTokenAsync();

                        foreach (DataRow projectRow in dtAtivos.Rows)
                        {
                            string projName = projectRow["Nome_Projeto"].ToString();
                            string projectId = projectRow["Project_ID_ACC"].ToString(); 
                            string hubId = projectRow["Account_ID_ACC"].ToString();
                            string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_{projectId}_{Guid.NewGuid()}.xlsx");

                            string formattedProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";

                            _logger.LogInformation($"--- 🏗️ Processando Projeto: {projName} ---");

                            try
                            {
                                string ldpFileId = dbService.GetLdpFileIdFromCache(projectId);
                                string ldpRealName = "";

                                if (string.IsNullOrEmpty(ldpFileId))
                                {
                                    _logger.LogInformation($"📂 [DESCOBERTA] Localizando pasta de Gestão...");
                                    var folderLdpId = await scheduleService.FindFolderByPaths(hubId, formattedProjectId, new[] {
                                        new[] { "09 - DESENVOLVIMENTO", "09.14 - GESTÃO" },
                                        new[] { "09 - DEVELOPING", "09.14 - MANAGEMENT" },
                                        new[] { "09 - MODEL DEVELOPING", "09.14 - MANAGEMENT" }
                                    }, robotToken);

                                    if (!string.IsNullOrEmpty(folderLdpId))
                                    {
                                        _logger.LogInformation($"🔎 Pasta encontrada. Varrendo arquivos Excel...");
                                        var fileInfo = await autodeskApi.GetFirstExcelInFolderAsync(formattedProjectId, folderLdpId, robotToken);

                                        if (!string.IsNullOrEmpty(fileInfo.FileId))
                                        {
                                            ldpFileId = fileInfo.FileId;
                                            ldpRealName = fileInfo.FileName;
                                            _logger.LogInformation($"✅ LDP Identificada: [{ldpRealName}].");
                                        }
                                    }
                                }

                                if (string.IsNullOrEmpty(ldpFileId))
                                {
                                    _logger.LogCritical($"❌ [ERRO] Nenhum arquivo Excel encontrado na pasta de Gestão para o projeto {projName}. Pulando...");
                                    continue;
                                }

                                using (var dl = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, ldpFileId, robotToken))
                                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);
                                
                                _logger.LogInformation($"🔍 [0/11] Validando DNA do Projeto (Padrão, Idioma e Banco de Dados)...");
                                var discovery = await scheduleService.GetOrDiscoverProjectContext(projectId, ldpPath, robotToken);

                                if (discovery == null || string.IsNullOrEmpty(discovery.NamingStandard))
                                {
                                    _logger.LogCritical("❌ [ETAPA 0] FALHA CRÍTICA: DNA não identificado. Abortando execução para evitar danos.");
                                    continue;
                                }
                                
                                if (string.IsNullOrEmpty(ldpRealName)) ldpRealName = discovery.LdpName ?? "LDP_Desconhecida.xlsx";

                                _logger.LogInformation($"💾 Gravando DNA completo no Banco de Dados...");
                                dbService.RegistrarDnaProjeto(projectId, ldpFileId, hubId, ldpRealName, discovery.Language, discovery.NamingStandard);
                                _logger.LogInformation($"   ✅ Etapa 0: Padrão [{discovery.NamingStandard}] | Idioma [{discovery.Language}] | LDP [{ldpRealName}]");

                                // ETAPA 1/11: TIMESHEET
                                _logger.LogInformation($"🧹 [1/11] Reconstruindo aba TIMESHEET via SQL Bruto...");
                                var result0 = await tsDumpService.ResetAndDumpTimesheetAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 1: {result0}");

                                // ETAPA 2/11: HORAS E REVISÃO
                                _logger.LogInformation($"💾 [2/11] Atualizando LDP: Horas do Banco e Atributo REV...");
                                var result1 = await ldpUpdateService.UpdateLdpFromDatabaseAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 2: {result1.SummaryMessage}");
                                
                                // ETAPA 4/11: GRDs
                                _logger.LogInformation($"✉️ [4/11] Rastreando informações de envio (GRDs)...");
                                var resultGrd = await grdSyncService.SyncGrdsToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 4: {resultGrd}");

                                // ETAPA 5/11: WORKFLOW
                                _logger.LogInformation($"🌊 [5/11] Atualizando LDP: Etapa do Fluxo (ACC Review)...");
                                var result4 = await ldpUpdateService.UpdateWorkflowStatusAsync(hubId, formattedProjectId, robotToken, "WDV284PFQTJDQZEG", ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 5: {result4}");

                                // ETAPA 6/11: CUSTO
                                _logger.LogInformation($"📊 [6/11] Sincronizando informações da LDP para aba CUSTO...");
                                var result5 = await ldpToCustoService.ExecuteFullSyncProcessAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 6: {result5}");

                                await Task.Delay(2000, stoppingToken);
                            
                                // ETAPA 7/11: ATRIBUTOS ACC
                                _logger.LogInformation($"⚡ [7/11] Sincronizando Atributos no ACC (Metadados via SQL)...");
                                var result6 = await ldpUpdateService.SyncAccAttributesFromSqlAsync(hubId, formattedProjectId, robotToken);
                                _logger.LogInformation($"   ✅ Etapa 7: {result6}");
                                    
                                // 🛡️ VACINA 1: RENOVA O TOKEN ANTES DA ETAPA 8
                                _logger.LogInformation("🔄 Renovando token de acesso da Autodesk após carga da Etapa 7...");
                                robotToken = await autodeskApi.Get2LeggedTokenAsync();

                                // ETAPA 8/11: VEREDITOS
                                _logger.LogInformation($"🏁 [8/11] Sincronizando Vereditos de Aprovação...");
                                var result7 = await ldpUpdateService.RunApprovalVerdictSyncAsync(hubId, formattedProjectId, robotToken, "WDV284PFQTJDQZEG", ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 8: {result7}");

                                // 🛡️ VACINA 2: RENOVA O TOKEN ANTES DO CRONOGRAMA
                                _logger.LogInformation("🔄 Renovando token para o módulo de Cronograma...");
                                robotToken = await autodeskApi.Get2LeggedTokenAsync();

                                // ETAPA 9/11: CRONOGRAMA
                                _logger.LogInformation($"📅 [9/11] Sincronizando dados do Cronograma para LDP...");
                                var resSch = await scheduleService.SyncScheduleToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 9: {resSch}");

                                // ETAPA 10/11: ESPELHAMENTO
                                _logger.LogInformation($"🪞 [10/11] Espelhando LDP para pasta pública...");
                                var result9 = await ldpUpdateService.MirrorLdpControlToProjectAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 10: {result9}");
                                
                                // ETAPA 11/11: PERSISTÊNCIA NA LDP UNIFICADA (SQL SERVER BLINDADA)
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
                                                var valorBruto = ws.Cells[excelRow, col].Value;

                                                if (InsinuaSerData(valorBruto))
                                                {
                                                    rowData[col] = TratarData(valorBruto);
                                                }
                                                else
                                                {
                                                    if (valorBruto != null && (valorBruto.ToString().Trim() == "-" || valorBruto.ToString().Trim().ToUpper() == "N/A"))
                                                    {
                                                        rowData[col] = DBNull.Value;
                                                    }
                                                    else
                                                    {
                                                        rowData[col] = valorBruto ?? DBNull.Value;
                                                    }
                                                }
                                            }

                                            if (rowData[4] != null && rowData[4] != DBNull.Value || rowData[2] != null && rowData[2] != DBNull.Value)
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
                                _logger.LogError($"❌ Erro ao processar projeto {projName}:\n{ex.ToString()}");
                            }
                            finally
                            {
                                if (File.Exists(ldpPath))
                                {
                                    File.Delete(ldpPath);
                                    _logger.LogInformation("🧹 Arquivo temporário removido.");
                                }
                            }

                            var telemetry = AutodeskApiService.GetTelemetry();
                            _logger.LogInformation($@"
                            📊 [PERFORMANCE - {projName}]
                            ⏱️ Tempo de Execução: {stopwatch.Elapsed:mm\:ss}
                            📞 Chamadas à API Autodesk: {telemetry.Calls}
                            📉 Rate Limit Atual: {telemetry.Limit}
                            __________________________________________________________");
                        }
                    }
                }
                
                stopwatch.Stop();
                var finalTelemetry = AutodeskApiService.GetTelemetry();
                Console.WriteLine("\n==========================================================");
                Console.WriteLine($"📊 RESUMO FINAL DO LOTE");
                Console.WriteLine($"⏱️ Tempo Total de Execução: {stopwatch.Elapsed:mm\\:ss}");
                Console.WriteLine($"📞 Total Chamadas API: {finalTelemetry.Calls}");
                Console.WriteLine($"📉 Rate Limit Final: {finalTelemetry.Limit}");
                Console.WriteLine("==========================================================");

                _logger.LogInformation("----------------------------------------------------------");
                _logger.LogInformation($"🏁 RELATÓRIO DE LOTE FINALIZADO em {stopwatch.Elapsed.Minutes}m {stopwatch.Elapsed.Seconds}s");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro fatal no processamento do Robô: {ex.Message}");
            }
            finally
            {
                // 🎯 RESTAURA O CONSOLE ORIGINAL PARA EVITAR VAZAMENTO DE MEMÓRIA
                Console.SetOut(originalConsole);
            }
        }

        // =========================================================================================
        // 🔍 DETECTOR DINÂMICO DE ESTRUTURA DE DATA
        // =========================================================================================
        private bool InsinuaSerData(object valor)
        {
            if (valor == null) return false;
            if (valor is DateTime) return true;

            string s = valor.ToString().Trim();
            
            if (double.TryParse(s, out double n) && n > 35000 && n < 60000) return true;
            if (s.Contains("/") && s.Length >= 8 && s.Length <= 10) return true;
            if (s.Contains("-") && s.Length == 10 && char.IsDigit(s[0])) return true;

            return false;
        }

        // =========================================================================================
        // 🛡️ MÉTODO BLINDADO PARA TRATAR DATAS DO EXCEL
        // =========================================================================================
        private object TratarData(object valorObj)
        {
            if (valorObj == null) return DBNull.Value;
            if (valorObj is DateTime dt) return dt;

            string valor = valorObj.ToString().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(valor) || valor == "-" || valor == "N/A" || valor == "NA" || valor == "TBD" || valor == "A DEFINIR" || valor == "S/D") 
                return DBNull.Value;

            if (DateTime.TryParse(valor, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out DateTime data))
            {
                if (data.Year < 1900 || data.Year > 2100) return DBNull.Value; 
                return data;
            }

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

            return DBNull.Value; 
        }
    }
}