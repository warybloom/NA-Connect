// Caminho: Services/GrdAutomationWorker.cs
#nullable disable
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
    public class GrdAutomationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationWorker> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly RoboStatusService _statusRobo;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        public bool EstaExecutando => _statusRobo?.Executando ?? false;

        public GrdAutomationWorker(
            IServiceProvider serviceProvider,
            ILogger<GrdAutomationWorker> logger,
            ILoggerFactory loggerFactory,
            IHostApplicationLifetime hostApplicationLifetime,
            RoboStatusService statusRobo)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _hostApplicationLifetime = hostApplicationLifetime;
            _statusRobo = statusRobo;
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
            catch (Exception ex)
            {
                // 🎯 Rede de segurança: garante que uma falha aqui não derrube o host
                _logger.LogError(ex, "❌ Erro fatal não tratado em ExecuteAsync.");
            }
        }

        // =========================================================================================
        // 🎯 NOVO: DISPARO MANUAL PARA UM ÚNICO PROJETO (usado pelo botão da tela Files.tsx)
        // =========================================================================================
        /// <summary>
        /// Executa o ciclo completo do robô restrito a um único projeto (Project_ID_ACC).
        /// Retorna false sem executar nada se o robô já estiver em execução (evita concorrência).
        /// </summary>
        public async Task<bool> ExecutarParaProjetoAsync(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                _logger.LogWarning("⛔ ExecutarParaProjetoAsync chamado sem projectId.");
                return false;
            }

            if (EstaExecutando)
            {
                _logger.LogWarning($"⛔ Robô já está em execução. Ignorando novo pedido para o projeto {projectId}.");
                return false;
            }

            await ProcessarCicloCompleto(CancellationToken.None, projectId);
            return true;
        }

        // =========================================================================================
        // 🚀 MÉTODO DE PROCESSAMENTO DO LOTE
        // =========================================================================================
        /// <param name="stoppingToken">Token de cancelamento do ciclo.</param>
        /// <param name="projectIdFiltro">
        /// 🎯 NOVO (opcional): quando informado, restringe o processamento a um único projeto
        /// (comparando com a coluna Project_ID_ACC). Quando nulo/vazio, processa todos os projetos ativos.
        /// </param>
        public async Task ProcessarCicloCompleto(CancellationToken stoppingToken, string projectIdFiltro = null)
        {
            _statusRobo.IniciarExecucao();
            var stopwatch = Stopwatch.StartNew();
            AutodeskApiService.ResetCounter();

            string folderPath = @"C:\Dev\N-A-Connect\Logs";
            string fileName = $"Execucao_Completa_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string fullLogPath = Path.Combine(folderPath, fileName);

            // 🎯 Provider é criado FORA do try para poder ser Dispose() no finally,
            // mas a criação em si acontece DENTRO do try logo abaixo — protegida.
            SimpleFileLoggerProvider fileLoggerProvider = null;

            try
            {
                // 🎯 Toda a inicialização do log de arquivo agora está protegida.
                // Se falhar (pasta sem permissão, disco cheio, etc.), cai no catch
                // abaixo em vez de derrubar o host inteiro.
                fileLoggerProvider = new SimpleFileLoggerProvider(fullLogPath);
                _loggerFactory.AddProvider(fileLoggerProvider);

                // 🎯 REMOVIDO o File.AppendAllText — ele conflitava com o StreamWriter
                // que o próprio SimpleFileLoggerProvider já mantém aberto no mesmo arquivo.

                _logger.LogInformation($"🚀 EXECUÇÃO LOTE INICIADA EM: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");

                if (!string.IsNullOrEmpty(projectIdFiltro))
                {
                    _logger.LogInformation($"🎯 MODO DE EXECUÇÃO: Projeto único ({projectIdFiltro}), disparado manualmente.");
                }

                _logger.LogInformation($"🤖 Robô acordou às {DateTime.Now:HH:mm:ss}. Executando lote de projetos...");

                int totalProjetosProcessados = 0;
                int totalProjetosGeral = 0;
                var etapasComproblema = new List<string>();
                var etapasComAvisos = new List<string>();

                // 🎯 LogAviso: para situações tolerantes (não bloqueiam o robô, mas merecem destaque)
                void LogAviso(string mensagem)
                {
                    _logger.LogWarning(mensagem);
                    etapasComAvisos.Add(mensagem);
                }

                // 🎯 LogProblema: para falhas reais (projeto pulado, etapa não concluída, exceção)
                void LogProblema(string mensagem)
                {
                    _logger.LogError(mensagem);
                    etapasComproblema.Add(mensagem);
                }

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
                    var HoldControlService = scope.ServiceProvider.GetRequiredService<HoldControlSyncService>();
                    var QualitySyncService = scope.ServiceProvider.GetRequiredService<QualitySyncService>();

                    _logger.LogInformation("🔍 Consultando SQL para identificar projetos ativos...");
                    DataTable dtAtivos = dbService.GetProjetosAtivos();

                    // =====================================================================================
                    // 🎯 NOVO: filtro de projeto único (usado quando o robô é disparado pelo botão da tela)
                    // =====================================================================================
                    if (!string.IsNullOrEmpty(projectIdFiltro))
                    {
                        string idSeguro = projectIdFiltro.Replace("'", "''"); // evita quebrar a expressão do Select
                        var linhasFiltradas = dtAtivos.Select($"Project_ID_ACC = '{idSeguro}'");

                        if (linhasFiltradas.Length > 0)
                        {
                            dtAtivos = linhasFiltradas.CopyToDataTable();
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ Projeto '{projectIdFiltro}' não encontrado entre os projetos ativos (Is_Ativo = 1). Nada será processado.");
                            dtAtivos = dtAtivos.Clone(); // mantém a estrutura, mas sem linhas
                        }
                    }

                    if (dtAtivos.Rows.Count == 0)
                    {
                        _logger.LogWarning("⚠️ Nenhum projeto marcado como 'Is_Ativo = 1' no banco de dados.");
                    }
                    else
                    {
                        _logger.LogInformation($"✅ {dtAtivos.Rows.Count} projetos encontrados para processamento.");

                        string robotToken = await autodeskApi.Get2LeggedTokenAsync();

                        int totalProjetos = dtAtivos.Rows.Count;
                        totalProjetosGeral = totalProjetos;
                        int indiceProjeto = 0;

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
                                        new[] { "09 - MODEL DEVELOPING", "09.14 - MANAGEMENT" },
                                        new[]{  "01 - PLANEJAMENTO E CONTROLE", "01.06 - LISTA DE DOCUMENTOS PROJETO"}
                                    }, robotToken);

                                    if (!string.IsNullOrEmpty(folderLdpId))
                                    {
                                        _logger.LogInformation($"🔎 Pasta encontrada. verificando arquivos EXCEL");
                                        var fileInfo = await autodeskApi.GetFirstExcelInFolderAsync(formattedProjectId, folderLdpId, robotToken, "LDP");

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
                                    // 🎯 Arquivo não encontrado = PROBLEMA (projeto foi pulado, não processado)
                                    LogProblema($"Nenhum arquivo Excel encontrado na pasta de Gestão para o projeto {projName}. Pulando...");
                                    continue;
                                }

                                using (var dl = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, ldpFileId, robotToken))
                                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

                                _logger.LogInformation($"🔍 [0/11] Validando DNA do Projeto (Padrão, Idioma e Banco de Dados)...");
                                var discovery = await scheduleService.GetOrDiscoverProjectContext(projectId, ldpPath, robotToken);

                                if (discovery == null || string.IsNullOrEmpty(discovery.NamingStandard))
                                {
                                    LogProblema($"{projName} - Etapa 0: DNA não identificado. Execução abortada para este projeto.");
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

                                // ETAPA 3/11: QUALITY CONTROL (QUALIDADE ACC)
                                _logger.LogInformation($"🛑 [3/11] Sincronizando dados da pasta de qualidade");
                                try
                                {
                                    var resultqualidade = await QualitySyncService.SyncQualitySyncToDbAsync(hubId, formattedProjectId, robotToken);

                                    if (resultqualidade == null)
                                    {
                                        LogAviso($"   ⚠️ {projName} - Etapa 3 (Qualidade) retornou um resultado nulo (vazio).");
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"   ✅ Etapa 3 (Qualidade Concluída): {resultqualidade}");
                                    }
                                }
                                catch (Exception exqualidade)
                                {
                                    _logger.LogError($"   ❌ ERRO CRÍTICO na Etapa 3 (Qualidade):\n{exqualidade}");
                                    LogProblema($"{projName} - Etapa 3 (Qualidade): {exqualidade.Message}");
                                }

                                // Hold Control
                                try
                                {
                                    var resultHold = await HoldControlService.SyncHoldControlToDbAsync(hubId, formattedProjectId, robotToken);
                                    _logger.LogInformation($"   ✅ Etapa 3: {resultHold}");
                                }
                                catch (Exception exHold)
                                {
                                    // 🎯 Aqui continua como AVISO — Hold Control é tolerante a "não encontrou arquivo"
                                    LogAviso($"   ⚠️ {projName} - Etapa 3 (Hold Control) falhou ou não encontrou arquivos na pasta: {exHold.Message}");
                                }

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

                                _logger.LogInformation("🔄 Renovando token de acesso da Autodesk após carga da Etapa 7...");
                                robotToken = await autodeskApi.Get2LeggedTokenAsync();

                                // ETAPA 8/11: VEREDITOS
                                _logger.LogInformation($"🏁 [8/11] Sincronizando Vereditos de Aprovação...");
                                var result7 = await ldpUpdateService.RunApprovalVerdictSyncAsync(hubId, formattedProjectId, robotToken, "WDV284PFQTJDQZEG", ldpFileId);
                                _logger.LogInformation($"   ✅ Etapa 8: {result7}");

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
                                _logger.LogError($"❌ Erro ao processar projeto {projName}:\n{ex}");
                                LogProblema($"{projName} - Falha geral: {ex.Message}");
                            }
                            finally
                            {
                                if (File.Exists(ldpPath))
                                {
                                    File.Delete(ldpPath);
                                    _logger.LogInformation("🧹 Arquivo temporário removido.");
                                }
                                indiceProjeto++;
                                totalProjetosProcessados = indiceProjeto;
                                int progressoFimProjeto = (int)((indiceProjeto / (double)totalProjetos) * 100);
                                _statusRobo.AtualizarProgresso(progressoFimProjeto, $"Concluído {indiceProjeto}/{totalProjetos} projetos");
                            }

                            var telemetry = AutodeskApiService.GetTelemetry();
                            _logger.LogInformation($@"
                            📊 [PERFORMANCE - {projName}]
                            ⏱️ Tempo de Execução: {stopwatch.Elapsed:mm\:ss}
                            📞 Chamadas à API Autodesk: {telemetry.Calls}
                            📉 Rate Limit Atual: {telemetry.Limit}
                            ");
                        }
                    }
                }

                stopwatch.Stop();
                var finalTelemetry = AutodeskApiService.GetTelemetry();


                _logger.LogInformation($"📊 RESUMO FINAL DO LOTE");
                _logger.LogInformation($"⏱️ Tempo Total de Execução: {stopwatch.Elapsed:mm\\:ss}");
                _logger.LogInformation($"📞 Total Chamadas API: {finalTelemetry.Calls}");
                _logger.LogInformation($"📉 Rate Limit Final: {finalTelemetry.Limit}");
                _logger.LogInformation($"📈 Total de projetos processados: {totalProjetosProcessados}/{totalProjetosGeral}");

                // 🎯 CORRIGIDO: usa _logger (capturado pelo SimpleFileLoggerProvider) em vez de
                // Console.WriteLine, e interpola o .Count em vez da lista inteira.
                if (etapasComproblema.Count > 0)
                {
                    _logger.LogError($"❌ {etapasComproblema.Count} problema(s) crítico(s) encontrado(s):");
                    foreach (var problema in etapasComproblema)
                        _logger.LogError($"   ❌ {problema}");
                }
                else
                {
                    _logger.LogInformation("✅ Nenhum problema encontrado.");
                }

                if (etapasComAvisos.Count > 0)
                {
                    _logger.LogWarning($"⚠️ {etapasComAvisos.Count} aviso(s) encontrado(s):");
                    foreach (var aviso in etapasComAvisos)
                        _logger.LogWarning($"   ⚠️ {aviso}");
                }
                else
                {
                    _logger.LogInformation("✅ Nenhum aviso encontrado.");
                }

                _logger.LogInformation($"🏁 RELATÓRIO DE LOTE FINALIZADO em {stopwatch.Elapsed.Minutes}m {stopwatch.Elapsed.Seconds}s");
            }
            catch (Exception ex)
            {
                // 🎯 Agora captura QUALQUER exceção fatal, inclusive falha ao criar o
                // SimpleFileLoggerProvider ou escrever o cabeçalho do log.
                _logger.LogError(ex, "❌ Erro fatal no processamento do Robô");
            }
            finally
            {
                fileLoggerProvider?.Dispose();
                _statusRobo.FinalizarExecucao("Robô finalizado. Verifique o log completo para detalhes.");
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