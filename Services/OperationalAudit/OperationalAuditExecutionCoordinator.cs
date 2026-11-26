using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Implementação real de <see cref="IOperationalAuditExecutionCoordinator"/> — integração de produção
    /// (checkpoint "PRODUCTION_INTEGRATION"). Chamada UMA VEZ por projeto, pelo <c>GrdAutomationWorker</c>,
    /// depois que TODAS as etapas operacionais (0 a 17) daquele projeto já terminaram (sucesso, alerta ou
    /// erro — a auditoria observa o ESTADO FINAL, nunca participa da decisão operacional).
    ///
    /// Monta, por chamada, um <see cref="ServiceCollection"/> isolado — nunca reaproveita nem estende o
    /// container de DI do site (que resolve <c>DatabaseService</c>/<c>LdpUpdateService</c>/etc.) — só
    /// <see cref="OperationalAuditServiceCollectionExtensions.AddOperationalAudit"/>, exatamente como
    /// <c>Tools\NA_Connect.OperationalAudit.Cli\Program.cs</c> já faz. Isso evita: (a) qualquer dependência
    /// nova do runner/regras sobre os serviços operacionais; (b) registrar
    /// <see cref="IOperationalAuditReportWriter"/> duas vezes (o site já registra os 3 writers de forma
    /// passiva em <c>Program.cs</c> desde o Checkpoint 1 — combinar os dois registros geraria TXT/CSV/JSON
    /// duplicados).
    ///
    /// O <see cref="ILoggerFactory"/> do site é reaproveitado (nunca um novo <c>AddLogging()</c> "do zero")
    /// para que as linhas "===== AUDITORIA OPERACIONAL =====" caiam no MESMO Execucao_Completa_*.txt do
    /// lote em andamento (via <c>ExecutionFileLoggerProvider</c>, já registrado como singleton no host).
    /// </summary>
    public sealed class OperationalAuditExecutionCoordinator : IOperationalAuditExecutionCoordinator
    {
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<OperationalAuditExecutionCoordinator> _logger;

        public OperationalAuditExecutionCoordinator(
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            ILogger<OperationalAuditExecutionCoordinator> logger)
        {
            _configuration = configuration;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        public async Task<OperationalAuditCoordinatorResult> ExecutarAuditoriaDoProjetoAsync(
            string projectIdAcc, string nomeProjeto, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            // Lida sozinha, fora de qualquer container de DI — só configuração, nunca I/O de rede/SQL —
            // então uma leitura extra (a mesma que AddOperationalAudit fará de novo lá dentro) é inofensiva
            // e evita montar o container inteiro só para descobrir que Enabled=false.
            var optionsPreliminares = OperationalAuditOptions.LerConfiguracao(_configuration);
            if (!optionsPreliminares.Enabled)
            {
                return OperationalAuditCoordinatorResult.NaoExecutada(stopwatch.Elapsed);
            }

            _logger.LogInformation("===== AUDITORIA OPERACIONAL =====");
            _logger.LogInformation(
                "[AUDITORIA] Projeto={Projeto} | ProjectIdAcc={ProjectIdAcc} | Início={InicioUtc:O}",
                nomeProjeto, projectIdAcc, DateTime.UtcNow);

            try
            {
                var services = new ServiceCollection();

                // TryAdd por trás de AddLogging(): como o ILoggerFactory do site já está registrado aqui
                // ANTES, AddLogging() nunca cria uma fábrica nova — reaproveita esta (com
                // ExecutionFileLoggerProvider já anexado), só completa o registro de ILogger<T>.
                services.AddSingleton(_loggerFactory);
                services.AddLogging();

                services.AddOperationalAudit(_configuration);
                await using var provider = services.BuildServiceProvider();

                var options = provider.GetRequiredService<OperationalAuditOptions>();
                options.OutputDirectory = ResolverSubpastaDaExecucao(options.OutputDirectory);

                var runner = provider.GetRequiredService<IOperationalAuditRunner>();
                var resultado = await runner.ExecutarAsync(new[] { projectIdAcc }, triggeredBy: "GrdAutomationWorker", cancellationToken);

                stopwatch.Stop();
                RegistrarResumo(nomeProjeto, projectIdAcc, resultado, stopwatch.Elapsed, options.OutputDirectory);

                return OperationalAuditCoordinatorResult.ExecutadaComSucesso(resultado, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "===== FALHA DA AUDITORIA =====\n[AUDITORIA][FALHA_COORDINATOR] Projeto={Projeto} | ProjectIdAcc={ProjectIdAcc} — falha inesperada ao preparar/executar a Auditoria Operacional. A automação do N&A Connect NÃO foi afetada por esta falha.",
                    nomeProjeto, projectIdAcc);

                return OperationalAuditCoordinatorResult.Falhou(ex.Message, stopwatch.Elapsed);
            }
        }

        /// <summary>Uma subpasta nova por execução (timestamp + id curto) dentro do <c>OutputDirectory</c> configurado — nunca sobrescreve uma execução anterior. Equivalente ao <c>AuditReportPathResolver</c> do CLI (não reaproveitável diretamente: assembly separada, sentido de dependência CLI → Site, nunca o inverso).</summary>
        private static string ResolverSubpastaDaExecucao(string diretorioBaseConfigurado)
        {
            string idCurto = Guid.NewGuid().ToString("N").Substring(0, 8);
            string nomeSubpasta = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{idCurto}";
            return string.IsNullOrWhiteSpace(diretorioBaseConfigurado)
                ? nomeSubpasta
                : Path.Combine(diretorioBaseConfigurado, nomeSubpasta);
        }

        private void RegistrarResumo(string nomeProjeto, string projectIdAcc, OperationalAuditResult resultado, TimeSpan duracao, string outputDirectory)
        {
            int operacionais = resultado.Findings.Count(f => f.ErrorDomain == OperationalAuditErrorDomain.Operacional);
            int automacao = resultado.Findings.Count(f => f.ErrorDomain == OperationalAuditErrorDomain.Automacao);
            int inconclusivos = resultado.Findings.Count(f => f.ErrorDomain == OperationalAuditErrorDomain.Inconclusivo);
            int blockers = resultado.Findings.Count(f => f.BloqueiaEtapa);

            _logger.LogInformation(
                "[AUDITORIA] Projeto={Projeto} | ProjectIdAcc={ProjectIdAcc} | AuditRunId={ExecutionId} | Fim={FimUtc:O} | Duração={Duracao} | Status={Status} | EtapasAuditadas={Etapas} | Requirements={Requirements} | Findings={Findings} | Operacionais={Operacionais} | Automacao={Automacao} | Inconclusivos={Inconclusivos} | Blockers={Blockers} | PersistenciaAudit={PersistenciaSqlFalhou} | Relatorios={OutputDirectory}",
                nomeProjeto, projectIdAcc, resultado.ExecutionId, resultado.FinalizadoEmUtc ?? DateTime.UtcNow, duracao, resultado.Status,
                resultado.StageResults.Count, resultado.Requirements.Count, resultado.Findings.Count,
                operacionais, automacao, inconclusivos, blockers,
                resultado.PersistenciaSqlFalhou ? $"FALHOU ({resultado.PersistenciaSqlErro})" : "OK",
                outputDirectory);

            if (resultado.ErrosDeExecucao.Count > 0)
            {
                _logger.LogWarning(
                    "[AUDITORIA] Projeto={Projeto} | {QuantidadeErros} erro(s) de execução da auditoria (nunca afetam o status operacional): {Erros}",
                    nomeProjeto, resultado.ErrosDeExecucao.Count, string.Join(" || ", resultado.ErrosDeExecucao));
            }
        }
    }
}
