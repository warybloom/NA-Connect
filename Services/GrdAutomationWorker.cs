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
using APS.WebApp.Services.Logging;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.RobotRun;
using APS.WebApp.Services.LdpRouting;
using APS.WebApp.Services.LdpTipo2;
using APS.WebApp.Models;
using Microsoft.Data.SqlClient;
using NA_Connect.Services;
using NA_Connect.Models;

namespace APS.WebApp.Services
{
    // =========================================================================================
    // 🛠️ CLONE DE CONSOLE: continua espelhando Console.WriteLine (e mensagens de runtime que
    // escrevem direto em Console.Out, como o aviso de TLS do driver SQL) para o arquivo da
    // execução — mas agora enfileira na MESMA sessão/arquivo usado pelo ExecutionFileLoggerProvider
    // (em vez de abrir seu próprio StreamWriter), evitando dois escritores concorrentes no mesmo
    // arquivo e mantendo tudo em ordem cronológica única.
    // =========================================================================================
    public class ConsoleOutputInterceptor : TextWriter
    {
        private readonly TextWriter _originalConsoleOutput;
        private readonly ExecutionLogSession _session;

        public ConsoleOutputInterceptor(ExecutionLogSession session, TextWriter originalConsoleOutput)
        {
            _session = session;
            _originalConsoleOutput = originalConsoleOutput;
        }

        public override Encoding Encoding => _originalConsoleOutput.Encoding;

        public override void Write(char value)
        {
            _originalConsoleOutput.Write(value);
        }

        public override void WriteLine(string value)
        {
            _originalConsoleOutput.WriteLine(value);

            // Limpa códigos de cor ANSI que deixam o TXT sujo
            string cleanLine = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\x1B\[[^@-~]*[@-~]", "");
            _session.WriteRaw(cleanLine);
        }
    }

    public class GrdAutomationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GrdAutomationWorker> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly ExecutionFileLoggerProvider _executionFileLoggerProvider;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        // Estado do WorkerTest:ExecutarUmaVez — true assim que ProcessarCicloCompleto começa a rodar
        // neste processo; usado por WorkerTestSelector.DeveBloquearRepeticao para nunca deixar um
        // segundo ciclo (ex.: um futuro Timer periódico) reprocessar o mesmo teste controlado.
        private bool _cicloJaExecutadoNesteProcesso;

        // Portão único de execução (ciclo automático e disparo manual do botão "Iniciar Robô") + erro fatal do último ciclo.
        // Acréscimo puro: nenhuma etapa lê estes membros; ver Services/RobotRun/RobotRun.cs.
        public RobotRunGate Gate { get; } = new();
        private volatile string? _erroFatalCiclo;
        private HashSet<string>? _operationalStages;
        private bool OperationalStage(string id, CancellationToken ct)
        {
            if (_operationalStages == null) return true;
            ct.ThrowIfCancellationRequested();
            return _operationalStages.Contains(RobotStageCatalog.Normalize(id));
        }
        private bool SelectedStage(WorkerTestConfig test, string id, CancellationToken ct) =>
            _operationalStages == null
                ? !test.Enabled || test.Etapas.Any(e => RobotStageCatalog.Normalize(e) == RobotStageCatalog.Normalize(id))
                : OperationalStage(id, ct);

        public async Task<RobotCycleOutcome> ExecutarCicloConfiguradoAsync(string projectId, IReadOnlyList<string> stages, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(projectId) || Gate.Snapshot.State != RobotRunStatus.Running)
                throw new InvalidOperationException("Execução configurada exige projeto e gate adquirido.");
            var selection = RobotStageCatalog.Validate(stages);
            int firstEntry = Gate.Entries().Count;
            _operationalStages = selection.ToHashSet(StringComparer.Ordinal);
            _erroFatalCiclo = null;
            try
            {
                await ProcessarCicloCompleto(ct, projectId, manual: true);
                var entries = Gate.Entries().Skip(firstEntry).ToArray();
                if (entries.Length == 0 || !entries.Any(e => e.Etapa == "PROJETO"))
                    return new(true, _erroFatalCiclo ?? "Projeto sem conclusão consolidada; consulte o histórico.");
                return RobotCycleOutcomeRules.Evaluate(entries, _erroFatalCiclo);
            }
            finally { _operationalStages = null; }
        }

        /// <summary>Disparo manual do MESMO pipeline (ProcessarCicloCompleto). projectId null = todos os projetos ativos; senão só o projeto informado, todas as etapas.</summary>
        public async Task<RobotCycleOutcome> ExecutarCicloManualAsync(string? projectId, CancellationToken ct)
        {
            _erroFatalCiclo = null;
            await ProcessarCicloCompleto(ct, projectId, manual: true);
            return RobotCycleOutcomeRules.Evaluate(Gate.Entries(), _erroFatalCiclo);
        }

        private async Task ExecutarCicloAutomaticoAsync(CancellationToken ct)
        {
            while (!Gate.TryBegin(false, null, null, DateTime.Now, out _)) await Task.Delay(TimeSpan.FromSeconds(5), ct);
            _erroFatalCiclo = null;
            try { await ProcessarCicloCompleto(ct); }
            finally { Gate.Finish(RobotCycleOutcomeRules.Evaluate(Gate.Entries(), _erroFatalCiclo), DateTime.Now); }
        }

        public GrdAutomationWorker(
            IServiceProvider serviceProvider,
            ILogger<GrdAutomationWorker> logger,
            IHostApplicationLifetime hostApplicationLifetime,
            ExecutionFileLoggerProvider executionFileLoggerProvider,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _executionFileLoggerProvider = executionFileLoggerProvider;
            _configuration = configuration;
        }

        // =========================================================================================
        // 🕵️ HOOK DA AUDITORIA OPERACIONAL (checkpoint de integração de produção)
        // =========================================================================================
        // Único ponto de contato com a Auditoria Operacional: o Worker conhece SOMENTE o
        // coordinator (nunca IOperationalAuditRunner nem as 18 regras diretamente — seção 6). Chamado
        // depois que TODAS as escritas operacionais de UM projeto já terminaram (ver
        // ProcessarCicloCompleto, logo após o bloco try/catch/finally + telemetria de performance de
        // cada projeto), nunca antes/durante as etapas. O coordinator NUNCA lança por contrato — o
        // try/catch aqui é defesa em profundidade (seção 2: a auditoria nunca pode interromper o
        // robô), nunca a única barreira.
        private async Task ExecutarAuditoriaDoProjetoComProtecaoAsync(
            IOperationalAuditExecutionCoordinator auditCoordinator, string projectId, string projName, CancellationToken stoppingToken)
        {
            try
            {
                var auditoriaResultado = await auditCoordinator.ExecutarAuditoriaDoProjetoAsync(projectId, projName, stoppingToken);

                if (!auditoriaResultado.Executada)
                {
                    _logger.LogInformation("🕵️ [AUDITORIA] Desabilitada (OperationalAudit:Enabled=false) — nenhuma execução para {ProjName}.", projName);
                }
                else if (!auditoriaResultado.Sucesso)
                {
                    _logger.LogWarning(
                        "🕵️ [AUDITORIA] Falha inesperada da Auditoria Operacional para {ProjName}: {ErroFatal} — status operacional do projeto NÃO foi afetado.",
                        projName, auditoriaResultado.ErroFatal);
                }
            }
            catch (Exception exAuditoria)
            {
                // Rede de segurança adicional: mesmo que o coordinator quebre seu próprio contrato de
                // nunca lançar, esta chamada nunca pode derrubar o ciclo do robô nem impedir os
                // próximos projetos do lote (seção 2/21, testes 3/4/5).
                _logger.LogError(
                    exAuditoria,
                    "🕵️ [AUDITORIA] Exceção inesperada ao chamar o coordinator para {ProjName} — status operacional NÃO afetado, seguindo para o próximo projeto.",
                    projName);
            }
        }

        // Cada chamada termina antes da próxima. Falhas operacionais não saem para o catch do projeto.
        internal static async Task<(int Gravidade, string Detalhe)> ExecutarEtapaIsoladaAsync(
            string id, Func<Task<(int Gravidade, string Detalhe)>> executar, ILogger logger,
            CancellationToken ct, bool registrarLimites = true, string tipo = "")
        {
            ct.ThrowIfCancellationRequested();
            if (registrarLimites) logger.LogInformation("[ETAPA {Etapa}][INICIO] {Tipo}", id, tipo);
            try
            {
                var resultado = await executar();
                ct.ThrowIfCancellationRequested();
                if (resultado.Gravidade >= 3)
                    logger.LogError("[ETAPA {Etapa}][ERRO] {Motivo}", id, resultado.Detalhe);
                return resultado;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ETAPA {Etapa}][ERRO] {Motivo}", id, ex.Message);
                return (3, ex.Message);
            }
            finally
            {
                if (registrarLimites) logger.LogInformation("[ETAPA {Etapa}][FIM] {Tipo}", id, tipo);
            }
        }

        // =========================================================================================
        // ⏰ INICIALIZAÇÃO PARA TESTES (RODA 5 SEGUNDOS APÓS O INÍCIO)
        // =========================================================================================
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Explicit startup suppression for safe maintenance/UI validation.
            if (!_configuration.GetValue("Worker:RunOnStartup", true))
            {
                _logger.LogInformation("Disparo automático no startup desabilitado. Execução manual permanece disponível.");
                return;
            }
            _logger.LogInformation("🤖 Robô de Processos N&A INICIADO EM MODO DE TESTE.");

            try
            {
                _logger.LogInformation("⏳ MODO TESTE ATIVO: Aguardando 5 segundos para disparar o motor principal...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    var configurationStore = _serviceProvider.GetRequiredService<RobotConfigurationStore>();
                    if (!_configuration.GetValue("RobotConfiguration:LegacyStartup", false))
                    {
                        // Saved configuration owns automatic selection too. Invalid/disabled never falls back to all projects.
                        try
                        {
                            var saved = await configurationStore.ReadAsync(stoppingToken);
                            var robot = saved.Robots.Single(r => r.Id == "main");
                            if (!robot.Enabled) return;
                            var registered = await _serviceProvider.GetRequiredService<IRobotProjectDirectory>().ReadAsync(stoppingToken);
                            var queue = RobotConfigurationRules.BuildQueue(robot, registered);
                            var runner = _serviceProvider.GetRequiredService<RobotQueueRunner>();
                            if (runner.TryStart(robot.Id, queue).Started) await runner.Completion;
                        }
                        catch (Exception ex) { _logger.LogError("Fila configurada automática bloqueada: {Type}", ex.GetType().Name); }
                        return;
                    }
                    await ExecutarCicloAutomaticoAsync(stoppingToken);
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
        private async Task ProcessarCicloCompleto(CancellationToken stoppingToken, string? projetoManual = null, bool manual = false)
        {
            var stopwatch = Stopwatch.StartNew();
            AutodeskApiService.ResetCounter();

            // 🎯 INICIA O ARQUIVO DA EXECUÇÃO (Execucao_Completa_*.txt)
            // A partir daqui, TODO ILogger.LogInformation/Warning/Error/Critical emitido por
            // qualquer serviço resolvido neste lote (mesmo em escopos de DI diferentes, porque
            // todos compartilham o mesmo ILoggerFactory/ExecutionFileLoggerProvider) é gravado
            // neste arquivo. O ConsoleOutputInterceptor continua existindo apenas para capturar
            // Console.WriteLine "cru" e avisos que o runtime escreve direto em Console.Out (ex.:
            // o aviso de TLS do driver SQL) — ele agora enfileira na MESMA sessão, em vez de abrir
            // seu próprio arquivo, então não há dois escritores concorrentes.
            string folderPath = @"C:\Dev\N-A-Connect\Logs";
            string fileName = $"Execucao_Completa_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string fullLogPath = Path.Combine(folderPath, fileName);

            await using var executionLog = _executionFileLoggerProvider.BeginExecution(fullLogPath);

            var originalConsole = Console.Out;
            using var interceptor = new ConsoleOutputInterceptor(executionLog, originalConsole);
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
                    var docMatchingService = scope.ServiceProvider.GetRequiredService<DocumentMatchingService>();
                    var autodeskApi = scope.ServiceProvider.GetRequiredService<AutodeskApiService>();
                    var ldpUpdateService = scope.ServiceProvider.GetRequiredService<LdpUpdateService>();
                    var tsDumpService = scope.ServiceProvider.GetRequiredService<TimesheetDumpService>();
                    var pdfMetadataService = scope.ServiceProvider.GetRequiredService<PdfMetadataService>();
                    var grdSyncService = scope.ServiceProvider.GetRequiredService<GrdToLdpSyncService>();
                    var ldpToCustoService = scope.ServiceProvider.GetRequiredService<LdpToCustoSyncService>();
                    var scheduleService = scope.ServiceProvider.GetRequiredService<AccScheduleSyncService>();
                    var planningSyncService = scope.ServiceProvider.GetRequiredService<PlanningControlSyncService>();
                    var qualitySyncService = scope.ServiceProvider.GetRequiredService<QualityControlSyncService>();
                    var workflowService = scope.ServiceProvider.GetRequiredService<AccReviewWorkflowService>();
                    var stageSnapshotService = scope.ServiceProvider.GetRequiredService<AccReviewStageSnapshotService>();
                    var auditCoordinator = scope.ServiceProvider.GetRequiredService<IOperationalAuditExecutionCoordinator>();
                    var ldpProjetoRepository = scope.ServiceProvider.GetRequiredService<ILdpProjetoRepository>();
                    var ldpProcessingRouter = scope.ServiceProvider.GetRequiredService<ILdpProcessingRouterService>();
                    var gestaoVistaSyncService = scope.ServiceProvider.GetRequiredService<APS.WebApp.Services.GestaoVista.ILdpGestaoVistaSyncService>();
                    var etapa03SnapshotService = scope.ServiceProvider.GetRequiredService<APS.WebApp.Services.GestaoVista.IEtapa03TimesheetSnapshotService>();

                    // 🧪 SELETOR GENÉRICO DE ETAPAS (WorkerTest no appsettings.json — seção 16 do pedido
                    // de correção da Etapa 7): Enabled=false roda o fluxo normal (todos os projetos
                    // ativos, todas as etapas — WorkerTest:Etapas é ignorada). Enabled=true exige
                    // ProjectId e ao menos uma Etapa; só as etapas listadas rodam. Nunca cria uma flag
                    // "SomenteEtapaN" nova — WorkerTestSelector.DeveExecutarEtapa é a única decisão
                    // central, chamada antes de cada etapa numerada abaixo.
                    var workerTestConfig = WorkerTestSelector.LerConfiguracao(_configuration);
                    // Disparo MANUAL (botão Iniciar Robô): fluxo normal completo (todas as etapas), independente do filtro WorkerTest do appsettings
                    // e sem consumir/bloquear o ExecutarUmaVez do ciclo automático.
                    if (manual) workerTestConfig = new WorkerTestConfig { Enabled = false, ExecutarUmaVez = false };
                    var (workerTestValido, workerTestErro) = WorkerTestSelector.Validar(workerTestConfig);

                    if (WorkerTestSelector.DeveBloquearRepeticao(workerTestConfig, _cicloJaExecutadoNesteProcesso))
                    {
                        _logger.LogWarning("🧪 [WORKER_TEST] ExecutarUmaVez=true e este processo já executou um ciclo — nova execução BLOQUEADA. Reinicie a aplicação para rodar de novo.");
                        return;
                    }

                    if (!workerTestValido)
                    {
                        _logger.LogCritical("❌ [WORKER_TEST] Configuração inválida — {Erro} — nenhum projeto será processado.", workerTestErro);
                        return;
                    }

                    _logger.LogInformation(
                        "[WORKER_TEST][PLANO] ProjectId={ProjectId} | Etapas=[{Etapas}] | ExecutarUmaVez={ExecutarUmaVez}",
                        !workerTestConfig.Enabled ? "(todos os projetos ativos)"
                            : string.IsNullOrWhiteSpace(workerTestConfig.ProjectId) ? "(todos os projetos ativos — Is_Ativo=1)"
                            : workerTestConfig.ProjectId,
                        workerTestConfig.Enabled ? string.Join(",", workerTestConfig.Etapas) : "(todas)",
                        workerTestConfig.ExecutarUmaVez);

                    DataTable dtAtivos;
                    if (manual && !string.IsNullOrWhiteSpace(projetoManual))
                    {
                        _logger.LogWarning("▶️ [MANUAL] Execução manual solicitada para Project_ID_ACC={ProjectId} (todas as etapas).", projetoManual);
                        dtAtivos = dbService.GetProjetoPorId(projetoManual);
                        if (dtAtivos.Rows.Count == 0)
                            _logger.LogCritical("❌ [MANUAL] Project_ID_ACC={ProjectId} não encontrado na tabela Projetos — nenhum projeto será processado.", projetoManual);
                    }
                    else if (workerTestConfig.Enabled && string.IsNullOrWhiteSpace(workerTestConfig.ProjectId))
                    {
                        // ProjectId vazio + Enabled=true (correção 17/09/2026): mantém o universo normal de
                        // projetos (Is_Ativo=1, mesma consulta do fluxo de produção) mas restringe as etapas
                        // executadas conforme WorkerTest:Etapas — usado para lotes como "Etapa 01+03 em
                        // todos os projetos ativos" sem precisar isolar um único Project_ID_ACC.
                        _logger.LogWarning(
                            "🧪 [WORKER_TEST] Filtro de teste HABILITADO — ProjectId vazio: processando TODOS os projetos Is_Ativo=1 | Etapas={Etapas}.",
                            string.Join(",", workerTestConfig.Etapas));
                        dtAtivos = dbService.GetProjetosAtivos();
                    }
                    else if (workerTestConfig.Enabled)
                    {
                        _logger.LogWarning(
                            "🧪 [WORKER_TEST] Filtro de teste HABILITADO — processando exclusivamente Project_ID_ACC={ProjectId} | Etapas={Etapas}.",
                            workerTestConfig.ProjectId, string.Join(",", workerTestConfig.Etapas));

                        dtAtivos = dbService.GetProjetoPorId(workerTestConfig.ProjectId);

                        if (dtAtivos.Rows.Count == 0)
                        {
                            _logger.LogCritical("❌ [WORKER_TEST] Project_ID_ACC={ProjectId} não encontrado na tabela Projetos — nenhum projeto será processado.", workerTestConfig.ProjectId);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("🔍 Consultando SQL para identificar projetos ativos...");
                        dtAtivos = dbService.GetProjetosAtivos();
                    }

                    if (!manual) _cicloJaExecutadoNesteProcesso = true;

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
                            string etapaAtual = "INICIALIZAÇÃO DO PROJETO";

                            _logger.LogInformation($"--- 🏗️ Processando Projeto: {projName} ---");
                            RegistrarLogHistorico(projName, etapaAtual, "INICIADO", $"Project_ID_ACC: {projectId}");

                            // Todo ILogger.* chamado dentro deste escopo — inclusive por serviços
                            // como ldpUpdateService, tsDumpService etc., que têm seu próprio
                            // ILogger<T> — grava "[Projeto: {projName}]" na linha correspondente do
                            // Execucao_Completa_*.txt (ver ExecutionFileLogger.FindProjectInScope).
                            using var _projectLogScope = _logger.BeginScope(new Dictionary<string, object>
                            {
                                ["Project"] = projName
                            });

                            try
                            {
                                etapaAtual = "DESCOBERTA E DOWNLOAD DA LDP";
                                RegistrarLogHistorico(projName, etapaAtual, "INICIADO");

                                string ldpFileId = dbService.GetLdpFileIdFromCache(projectId);
                                string ldpRealName = "";

                                // [CANÁRIO][DIAGNÓSTICO] Instrumentação somente-leitura (seção "DESCOBERTA DA LDP") —
                                // lê o roteamento Tipo_LDP AQUI apenas para logar, sem antecipar nem alterar a decisão
                                // de roteamento em si (o mesmo objeto é reaproveitado, sem query duplicada, no bloco
                                // de roteamento Tipo 1/Tipo 2 mais abaixo).
                                var projetoRoteamentoDiscovery = await ldpProjetoRepository.ObterProjetoAtivoAsync(projectId, stoppingToken);
                                if (_operationalStages != null)
                                {
                                    if (projetoRoteamentoDiscovery == null) throw new InvalidOperationException("Projeto inativo/ausente; execução configurada bloqueada.");
                                    RobotStageCatalog.Validate(_operationalStages, (int)projetoRoteamentoDiscovery.TipoLdp);
                                }
                                bool fileIdLdpPreenchidoNoBanco = !string.IsNullOrEmpty(ldpFileId);
                                _logger.LogInformation(
                                    "[DISCOVERY][PROJECT] Project_ID_ACC={ProjectIdAcc} | Tipo_LDP={TipoLdp} | File_ID_LDP_ACC={FileIdLdpAcc}",
                                    projectId,
                                    projetoRoteamentoDiscovery?.TipoLdp.ToString() ?? "(não resolvido — projeto ausente/inativo em dbo.Projetos)",
                                    fileIdLdpPreenchidoNoBanco ? ldpFileId : "(vazio)");
                                _logger.LogInformation(
                                    "[DISCOVERY][FILE_ID_LDP_ACC] DatabaseValue={DatabaseValue} | UsedByDiscovery={UsedByDiscovery}",
                                    fileIdLdpPreenchidoNoBanco ? ldpFileId : "(vazio)",
                                    fileIdLdpPreenchidoNoBanco ? "YES (cache usado, descoberta de pasta pulada)" : "NO (cache vazio, descoberta de pasta será executada)");

                                // folderLdpId fica visível fora do bloco abaixo só para distinguir, no diagnóstico
                                // de erro logo adiante, "pasta não localizada" de "pasta localizada, sem Excel"
                                // (ver seção 5 da correção do canário Tipo 2 — a mensagem antiga acusava
                                // "nenhum Excel encontrado" mesmo quando a busca de Excel nunca chegou a rodar).
                                string folderLdpId = null;
                                if (string.IsNullOrEmpty(ldpFileId))
                                {
                                    _logger.LogInformation($"📂 [DESCOBERTA] Localizando pasta de Gestão...");

                                    // Correção 17/09/2026: tenta primeiro por CÓDIGO NUMÉRICO (09/09.14) —
                                    // o texto descritivo da pasta (DESENVOLVIMENTO/DEVELOPING/DEVELOPMENT/
                                    // MODEL DEVELOPING/ENGINEERING DOCUMENTS) tem variado entre projetos e
                                    // quebrava a lista fixa de candidatos nominais sempre que um nome novo
                                    // aparecia (achado real: CHS001 tem "09 - DEVELOPMENT", que não batia
                                    // com nenhum dos 4 candidatos abaixo). Os candidatos nominais continuam
                                    // como rede de segurança, nunca removidos.
                                    folderLdpId = await scheduleService.FindGestaoFolderByNumericPrefixAsync(hubId, formattedProjectId, robotToken);

                                    if (string.IsNullOrEmpty(folderLdpId))
                                    {
                                        folderLdpId = await scheduleService.FindFolderByPaths(hubId, formattedProjectId, new[] {
                                            new[] { "09 - DESENVOLVIMENTO", "09.14 - GESTÃO" },
                                            new[] { "09 - DEVELOPING", "09.14 - MANAGEMENT" },
                                            new[] { "09 - MODEL DEVELOPING", "09.14 - MANAGEMENT" },
                                            new[] { "09 - ENGINEERING DOCUMENTS", "09.14 - MANAGEMENT" }
                                        }, robotToken);
                                    }

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
                                    string mensagemDescobertaErro = string.IsNullOrEmpty(folderLdpId)
                                        ? "Nenhum caminho válido para a pasta de Gestão foi localizado no ACC. Excel ainda não foi pesquisado."
                                        : "Pasta de Gestão localizada, mas nenhum arquivo Excel válido foi encontrado.";

                                    _logger.LogCritical($"❌ [DESCOBERTA][ERRO] {mensagemDescobertaErro} Projeto: {projName}. Pulando...");
                                    RegistrarLogHistorico(projName, etapaAtual, "ERRO", mensagemDescobertaErro);
                                    continue;
                                }

                                _logger.LogInformation(
                                    "[DISCOVERY][FILE_ID_LDP_ACC][RESULTADO] AccItemResolved=YES | ResolvedFileName={ResolvedFileName} | Origem={Origem}",
                                    string.IsNullOrEmpty(ldpRealName) ? "(nome não resolvido nesta etapa)" : ldpRealName,
                                    fileIdLdpPreenchidoNoBanco ? "CACHE(File_ID_LDP_ACC)" : "DESCOBERTA(pasta+excel)");

                                using (var dl = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, ldpFileId, robotToken))
                                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

                                RegistrarLogHistorico(projName, etapaAtual, "CONCLUIDO", $"LDP: {ldpRealName} | File_ID: {ldpFileId}");

                                var projetoRoteamento = projetoRoteamentoDiscovery;
                                bool ehTipo2 = projetoRoteamento?.TipoLdp == TipoLdp.Tipo2;
                                AccReviewWorkflowService.WorkflowBuildResult workflowResult = null;
                                var resultadosEtapas = new List<(string Etapa, int Gravidade, string Detalhe)>();

                                async Task<bool> ExecutarEtapaAsync(string id, string nome,
                                    Func<Task<(int Gravidade, string Detalhe)>> executar, bool registrarLimites = true)
                                {
                                    stoppingToken.ThrowIfCancellationRequested();
                                    string logId = id == "5B" ? "05B" : id;
                                    if (!SelectedStage(workerTestConfig, id, stoppingToken))
                                    {
                                        _logger.LogInformation("[ETAPA {Etapa}][PULADA] Motivo=FILTRO_DE_ETAPAS", logId);
                                        return false;
                                    }
                                    string nomeEtapa = $"ETAPA {logId} - {nome}";
                                    etapaAtual = nomeEtapa;
                                    var resultado = await ExecutarEtapaIsoladaAsync(logId, async () =>
                                    {
                                        RegistrarLogHistorico(projName, nomeEtapa, "INICIADO");
                                        var retorno = await executar();
                                        if (retorno.Gravidade < 3)
                                            RegistrarLogHistorico(projName, nomeEtapa, "CONCLUIDO", retorno.Detalhe);
                                        return retorno;
                                    }, _logger, stoppingToken, registrarLimites, ehTipo2 ? "Tipo2" : "Tipo1");
                                    resultadosEtapas.Add((nomeEtapa, resultado.Gravidade, resultado.Detalhe));
                                    if (resultado.Gravidade >= 3)
                                    {
                                        try { RegistrarLogHistorico(projName, nomeEtapa, "ERRO", resultado.Detalhe); }
                                        catch (Exception exLog)
                                        {
                                            _logger.LogError(exLog, "[ETAPA {Etapa}][ERRO] Falha ao gravar histórico.", logId);
                                        }
                                    }
                                    return resultado.Gravidade < 3;
                                }

                                async Task AtualizarCopiaLdpAsync()
                                {
                                    var details = await autodeskApi.GetItemDetailsAsync(formattedProjectId, ldpFileId, robotToken)
                                        ?? throw new InvalidOperationException("Não foi possível obter a versão mais recente da LDP.");
                                    ldpRealName = details.DisplayName;
                                    using var download = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, details.TipVersionUrn, robotToken);
                                    using var file = new FileStream(ldpPath, FileMode.Create, FileAccess.Write);
                                    await download.CopyToAsync(file, stoppingToken);
                                }

                                // 00 — DNA: leitura/validação específica, sem executar o router ou persistir a LDP.
                                await ExecutarEtapaAsync("00", "DNA DO PROJETO", async () =>
                                {
                                    if (ehTipo2)
                                    {
                                        var parser = scope.ServiceProvider.GetRequiredService<ILdpTipo2Parser>();
                                        using var stream = new FileStream(ldpPath, FileMode.Open, FileAccess.Read);
                                        var leitura = await parser.ProcessarAsync(stream, projectId, ldpRealName, DateTime.UtcNow, stoppingToken);
                                        if (leitura.Erros.Count > 0 || leitura.Documentos.Count == 0)
                                            throw new InvalidOperationException("DNA Tipo2 não confirmado: " + string.Join(" | ", leitura.Erros));
                                        string idioma = dbService.ObterContextoProjeto(projectId)?.Language ?? "PT";
                                        return (0, $"Tipo2: Padrao=NA | Idioma={idioma} | Documentos={leitura.Documentos.Count}");
                                    }
                                    var discovery = await scheduleService.GetOrDiscoverProjectContext(projectId, ldpPath, robotToken);
                                    if (discovery == null || string.IsNullOrEmpty(discovery.NamingStandard))
                                        throw new InvalidOperationException("DNA não identificado.");
                                    if (string.IsNullOrEmpty(ldpRealName)) ldpRealName = discovery.LdpName ?? "LDP_Desconhecida.xlsx";
                                    dbService.RegistrarDnaProjeto(projectId, ldpFileId, hubId, ldpRealName, discovery.Language, discovery.NamingStandard);
                                    return (0, $"Padrão: {discovery.NamingStandard} | Idioma: {discovery.Language} | LDP: {ldpRealName}");
                                });

                                // 01 — Timesheet comum aos dois layouts.
                                bool etapa01Sucesso = await ExecutarEtapaAsync("01", "TIMESHEET", async () =>
                                {
                                    var result = await tsDumpService.ResetAndDumpTimesheetAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                    await AtualizarCopiaLdpAsync();
                                    return (0, result?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                // 02 permanece desligada; nenhuma implementação é chamada.
                                _logger.LogInformation("[ETAPA 02][PULADA] Motivo=DESABILITADA_TEMPORARIAMENTE");

                                // 03 — o serviço existente é responsável pelos logs INICIO/FIM desta etapa.
                                await ExecutarEtapaAsync("03", "SNAPSHOT TIMESHEET", async () =>
                                {
                                    if (!etapa01Sucesso)
                                    {
                                        _logger.LogInformation("[ETAPA 03][INICIO]");
                                        _logger.LogInformation("[ETAPA 03][FIM] Resultado=NAO_EXECUTADA Motivo=DEPENDENCIA_ETAPA_01");
                                        return (3, "DEPENDENCIA_ETAPA_01");
                                    }
                                    var result = await etapa03SnapshotService.ExecutarAsync(
                                        projectId, ldpPath, ldpRealName, DateTime.UtcNow, stoppingToken);
                                    return result.Sucesso
                                        ? (0, $"Linhas={result.Inseridos} | HH={result.HHBancoDepois}")
                                        : (3, result.Motivo);
                                }, registrarLimites: false);

                                await ExecutarEtapaAsync("04", "GRDs", async () =>
                                {
                                    var result = await grdSyncService.SyncGrdsToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                    return (0, result?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                // 4B - classificação gerencial de emissão. Somente leitura da LDP atual no banco;
                                // não altera GRD, Emission_Date, Document_Status, Excel, ACC nem dbo.Ldp_Unificada.
                                await ExecutarEtapaAsync("4B", "CLASSIFICAÇÃO DE EMISSÃO", async () =>
                                {
                                    var auditoria = await scope.ServiceProvider
                                        .GetRequiredService<APS.WebApp.Services.Documents.IDocumentEmissionAuditService>()
                                        .AuditAsync(projectId, stoppingToken);
                                    return (0, auditoria.Resumo());
                                });

                                await ExecutarEtapaAsync("05", "WORKFLOW", async () =>
                                {
                                    workflowResult = await workflowService.GetProjectWorkflowResultAsync(hubId, formattedProjectId, robotToken);
                                    var result = await ldpUpdateService.UpdateWorkflowStatusAsync(
                                        hubId, formattedProjectId, robotToken, "WDV284PFQTJDQZEG", ldpFileId, workflowResult);
                                    return (0, result?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                await ExecutarEtapaAsync("5B", "SNAPSHOT DAS ETAPAS ATUAIS DOS ACC REVIEWS", async () =>
                                {
                                    var workflowParaSnapshot = workflowResult
                                        ?? await workflowService.GetProjectWorkflowResultAsync(hubId, formattedProjectId, robotToken);
                                    var result = await stageSnapshotService.SincronizarSnapshotAsync(projectId, workflowParaSnapshot);
                                    return (0, result?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                async Task<(int Gravidade, string Detalhe)> SincronizarGestaoVistaAsync()
                                {
                                    await AtualizarCopiaLdpAsync();
                                    using var stream = new FileStream(ldpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                    var result = await gestaoVistaSyncService.SincronizarAsync(
                                        projectId, stream, ldpRealName, DateTime.UtcNow, stoppingToken);
                                    if (!result.SchemaDisponivel) return (3, "Schema Gestão à Vista indisponível.");
                                    if (!result.Sucesso) return (3, string.Join(" | ", result.Erros));
                                    return (0, $"CustoReal(ins={result.CustoReal.Inseridos},novas={result.CustoReal.NovasVersoes}) " +
                                        $"CustoPrevisto(ins={result.CustoPrevisto.Inseridos}) Timesheet(ins={result.Timesheet.Inseridos}).");
                                }

                                bool gestaoVistaExecutada = false;
                                await ExecutarEtapaAsync("06", "CUSTO", async () =>
                                {
                                    if (ehTipo2)
                                    {
                                        gestaoVistaExecutada = true;
                                        return await SincronizarGestaoVistaAsync();
                                    }
                                    var result = await ldpToCustoService.ExecuteFullSyncProcessAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                    await Task.Delay(2000, stoppingToken);
                                    return (0, result?.ToString() ?? "Sem mensagem de retorno.");
                                });
                                // Extração opcional existente: para Tipo2 já é a implementação de 06, nunca duplica.
                                if (!gestaoVistaExecutada)
                                    await ExecutarEtapaAsync("GESTAO_VISTA", "GESTÃO À VISTA", SincronizarGestaoVistaAsync);

                                await ExecutarEtapaAsync("07", "ATRIBUTOS ACC", async () =>
                                {
                                    // Continua lendo o SQL disponível; não antecipa a persistência final de 11.
                                    var result = await ldpUpdateService.SyncAccAttributesFromSqlAsync(hubId, formattedProjectId, robotToken);
                                    return (0, result?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                // 08 continua REMOVIDA: RunApprovalVerdictSyncAsync não é chamado.
                                await ExecutarEtapaAsync("09", "CRONOGRAMA", async () =>
                                {
                                    robotToken = await autodeskApi.Get2LeggedTokenAsync();
                                    if (ehTipo2)
                                    {
                                        // A carga inicial SQL é pré-requisito interno do matching do router, não a Etapa 11.
                                        var result = await ldpProcessingRouter.ProcessarAsync(
                                            projetoRoteamento, hubId, robotToken, ldpFileId, stoppingToken);
                                        return (result.Sucesso ? 0 : 3, result.Mensagem);
                                    }
                                    var resultTipo1 = await scheduleService.SyncScheduleToLdpAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                    return (0, resultTipo1?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                await ExecutarEtapaAsync("10", "ESPELHAMENTO", async () =>
                                {
                                    if (ehTipo2)
                                    {
                                        var espelho = ActivatorUtilities.CreateInstance<LdpTipo2EspelhoService>(scope.ServiceProvider);
                                        var result = await espelho.GerarAsync(hubId, projetoRoteamento.ProjectIdAcc, robotToken, ldpFileId, stoppingToken);
                                        return (0, result);
                                    }
                                    var resultTipo1 = await ldpUpdateService.MirrorLdpControlToProjectAsync(hubId, formattedProjectId, robotToken, ldpFileId);
                                    return (0, resultTipo1?.ToString() ?? "Sem mensagem de retorno.");
                                });

                                await ExecutarEtapaAsync("11", "PERSISTÊNCIA FINAL NA LDP UNIFICADA", async () =>
                                {
                                    if (ehTipo2)
                                    {
                                        var details = await autodeskApi.GetItemDetailsAsync(formattedProjectId, ldpFileId, robotToken)
                                            ?? throw new InvalidOperationException("LDP original não encontrada para persistência final.");
                                        using var stream = await autodeskApi.DownloadFileStreamAsync(formattedProjectId, details.TipVersionUrn, robotToken);
                                        var parser = scope.ServiceProvider.GetRequiredService<ILdpTipo2Parser>();
                                        var leitura = await parser.ProcessarAsync(stream, projetoRoteamento.ProjectIdAcc,
                                            details.DisplayName, DateTime.UtcNow, stoppingToken);
                                        var persistence = scope.ServiceProvider.GetRequiredService<ILdpTipo2PersistenceService>();
                                        var saved = await persistence.PersistirAsync(leitura, stoppingToken);
                                        if (!saved.Sucesso) throw new InvalidOperationException(string.Join(" | ", saved.Erros));
                                        _logger.LogInformation("[ETAPA 11][TIPO2] Documentos={Documentos} | Gates={Gates}",
                                            saved.DocumentosInseridos, saved.GatesInseridos);
                                        return (0, $"Documentos={saved.DocumentosInseridos} | Gates={saved.GatesInseridos}");
                                    }

                                    await AtualizarCopiaLdpAsync();
                                    using var package = new OfficeOpenXml.ExcelPackage(new FileInfo(ldpPath));
                                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
                                    int totalRowsSaved = 0;
                                    for (int excelRow = 8; excelRow <= ws.Dimension.End.Row; excelRow++)
                                    {
                                        var raw = Enumerable.Range(1, 35).ToDictionary(col => col, col => ws.Cells[excelRow, col].Value);
                                        LdpEtapa11Diagnostico.Log(_logger, _configuration, "EXCEL_VALUE", projectId, raw);
                                        var rowData = ParseLdpUnificadaRow(col => raw[col]);
                                        LdpEtapa11Diagnostico.Log(_logger, _configuration, "APOS_PARSER", projectId, rowData);
                                        if (rowData[4] != null && rowData[4] != DBNull.Value || rowData[2] != null && rowData[2] != DBNull.Value)
                                        {
                                            dbService.SalvarDadosLdpUnificada(projectId, rowData);
                                            totalRowsSaved++;
                                        }
                                    }
                                    return (0, $"{totalRowsSaved} linhas persistidas no SQL.");
                                });

                                await ExecutarEtapaAsync("12", "External Action", async () =>
                                {
                                    var result = await planningSyncService.SincronizarTabelaDinamicaAsync(hubId, formattedProjectId, robotToken, PlanningDefinicoes.External());
                                    return (result.Sucesso ? 0 : 3, result.Mensagem);
                                });

                                await ExecutarEtapaAsync("13", "Internal Action", async () =>
                                {
                                    var result = await planningSyncService.SincronizarTabelaDinamicaAsync(hubId, formattedProjectId, robotToken, PlanningDefinicoes.Internal());
                                    return (result.Sucesso ? 0 : 3, result.Mensagem);
                                });

                                await ExecutarEtapaAsync("14", "Scope Deviation", async () =>
                                {
                                    var result = await planningSyncService.SincronizarTabelaDinamicaAsync(hubId, formattedProjectId, robotToken, PlanningDefinicoes.ScopeDeviation());
                                    return (result.Sucesso ? 0 : 3, result.Mensagem);
                                });

                                await ExecutarEtapaAsync("15", "Weekly Schedule", async () =>
                                {
                                    var result = await planningSyncService.SincronizarWeeklyScheduleAsync(hubId, formattedProjectId, robotToken, dbService.ObterContextoProjeto(projectId)?.Language);
                                    return (result.Sucesso ? 0 : 3, result.Mensagem);
                                });

                                await ExecutarEtapaAsync("16", "Hold Control", async () =>
                                {
                                    var result = await planningSyncService.SincronizarTabelaDinamicaAsync(hubId, formattedProjectId, robotToken, PlanningDefinicoes.HoldControl());
                                    return (result.Sucesso ? 0 : 3, result.Mensagem);
                                });

                                await ExecutarEtapaAsync("17", "Qualidade", async () =>
                                {
                                    var (status, mensagem) = await qualitySyncService.SincronizarArquivoQualidadeAsync(hubId, formattedProjectId, robotToken);
                                    int gravidade = status switch
                                    {
                                        QualityControlSyncService.StatusConcluidoComSucesso => 0,
                                        QualityControlSyncService.StatusConcluidoComAlertas => 1,
                                        QualityControlSyncService.StatusConcluidoParcialmente => 2,
                                        _ => 3
                                    };
                                    return (gravidade, $"{status}\n{mensagem}");
                                });

                                // Resultado geral do PROJETO reflete a PIOR gravidade entre as etapas executadas
                                // — nunca mais "CONCLUIDO COM SUCESSO" incondicional só porque o try chegou ao fim.
                                // Ex.: enquanto a Etapa 17 tiver alguma tabela com preenchimento automático, o
                                // projeto termina como CONCLUIDO COM ALERTAS mesmo que 12/13/14/16 tenham dado certo.
                                int gravidadeMaximaProjeto = resultadosEtapas.Count == 0 ? 0 : resultadosEtapas.Max(r => r.Gravidade);
                                string statusProjeto = gravidadeMaximaProjeto switch
                                {
                                    0 => "CONCLUIDO COM SUCESSO",
                                    1 => "CONCLUIDO COM ALERTAS",
                                    2 => "CONCLUIDO PARCIALMENTE",
                                    _ => "CONCLUIDO COM ERROS"
                                };

                                if (gravidadeMaximaProjeto == 0)
                                {
                                    _logger.LogInformation($"🏁 Ciclo concluído com sucesso para {projName}.");
                                    RegistrarLogHistorico(projName, "PROJETO", statusProjeto);
                                }
                                else
                                {
                                    string detalheEtapas = string.Join(" || ", resultadosEtapas.Where(r => r.Gravidade > 0).Select(r => $"{r.Etapa}: {r.Detalhe}"));
                                    _logger.LogWarning("⚠️ Ciclo concluído para {ProjName} — {Status} — {Detalhe}", projName, statusProjeto, detalheEtapas);
                                    RegistrarLogHistorico(projName, "PROJETO", statusProjeto, detalheEtapas);
                                }
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                            catch (Exception ex)
                            {
                                _logger.LogError($"❌ Erro ao processar projeto {projName}:\n{ex.ToString()}");
                                RegistrarLogHistorico(projName, etapaAtual, "ERRO", ex.ToString());
                            }
                            finally
                            {
                                if (File.Exists(ldpPath))
                                {
                                    File.Delete(ldpPath);
                                    _logger.LogInformation("🧹 Arquivo temporário removido.");
                                }

                                // 🕵️ AUDITORIA OPERACIONAL: precisa rodar em TODO caminho de saída deste
                                // projeto — sucesso, erro (catch acima) OU saída antecipada via `continue`
                                // dentro do try (ex.: LDP não encontrada, DNA não identificado). Um
                                // `continue` pula qualquer código posicionado DEPOIS deste try/catch/finally
                                // dentro do mesmo corpo do foreach — só o finally é garantido em todos os
                                // casos, por isso o hook fica aqui, nunca depois. projectId/projName ainda em
                                // contexto, antes de seguir para o próximo projeto do lote.
                                await ExecutarAuditoriaDoProjetoComProtecaoAsync(auditCoordinator, projectId, projName, stoppingToken);
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro fatal no processamento do Robô: {ex.Message}");
                _erroFatalCiclo = ex.Message;
            }
            finally
            {
                // 🎯 RESTAURA O CONSOLE ORIGINAL PARA EVITAR VAZAMENTO DE MEMÓRIA
                Console.SetOut(originalConsole);
            }
        }

        // =========================================================================================
        // 📝 LOG HISTÓRICO POR PROJETO, ETAPA E HORÁRIO
        // =========================================================================================
        private void RegistrarLogHistorico(string projeto, string etapa, string status, string mensagem = "")
        {
            string path = @"C:\Robo_NA_Connect\Logs_Historico";
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            string dataHora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string arquivoHistorico = Path.Combine(path, $"Historico_{DateTime.Now:yyyyMMdd}.txt");
            string logLine = $"{dataHora} | Projeto: {projeto} | Etapa: {etapa} | Status: {status}";

            if (!string.IsNullOrWhiteSpace(mensagem))
            {
                logLine += $" | {mensagem}";
            }

            File.AppendAllText(arquivoHistorico, logLine + Environment.NewLine);
            Gate.Record(new RobotHistoricoEntry(projeto, etapa, status, mensagem));

            if (status.Equals("ERRO", StringComparison.OrdinalIgnoreCase))
            {
                string arquivoErro = Path.Combine(path, $"Erros_{DateTime.Now:yyyyMMdd}.txt");
                File.AppendAllText(arquivoErro, logLine + Environment.NewLine);
            }
        }

        // =========================================================================================
        // 🔍 DETECTOR DINÂMICO DE ESTRUTURA DE DATA
        // =========================================================================================
        /// <summary>
        /// Colunas da aba LDP (Etapa 11) que DatabaseService.SalvarDadosLdpUnificada grava como número
        /// (No_Pages, WBS_Percent, Percent_Planned/Physical, Planned/Actual/Remaining HH e Cost). Nunca passam
        /// pela heurística de data: InsinuaSerData trata qualquer número entre 35000 e 60000 como serial de
        /// data do Excel — ex.: BML011.013-000-3D0-AT-001, Planned_Cost=57640 virava 2057-10-22 e era gravado
        /// NULL (R$ 57.640 a menos no Custo Previsto; auditoria real 2026-09-22).
        /// </summary>
        private static readonly HashSet<int> ColunasNumericasLdpUnificada = new() { 8, 16, 21, 22, 30, 31, 32, 33, 34, 35 };

        internal static Dictionary<int, object> ParseLdpUnificadaRow(Func<int, object> readCell)
        {
            var rowData = new Dictionary<int, object>();
            for (int col = 1; col <= 35; col++)
            {
                var valorBruto = readCell(col);

                if (!ColunasNumericasLdpUnificada.Contains(col) && InsinuaSerData(valorBruto))
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

            return rowData;
        }

        private static bool InsinuaSerData(object valor)
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
        private static object TratarData(object valorObj)
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
