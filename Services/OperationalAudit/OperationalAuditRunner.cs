using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Aggregation;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Dedup;
using APS.WebApp.Services.OperationalAudit.Persistence;
using APS.WebApp.Services.OperationalAudit.Reporting;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Implementação real de <see cref="IOperationalAuditRunner"/>. Fluxo (seção 3/18 do Checkpoint 2):
    /// regra → resultado em memória → agregador → runner → repository/writers. A regra NUNCA persiste nem
    /// escreve relatório diretamente (seção 4) — só este runner faz isso, e só depois de agregar tudo.
    ///
    /// <see cref="IAuditRepository"/> é resolvido via <paramref name="_repositoryFactory"/> (nunca injetado
    /// direto no construtor) para que, quando <see cref="OperationalAuditOptions.PersistToDatabase"/> for
    /// false, a fábrica NUNCA seja chamada — e portanto <c>AuditConnectionStringResolver</c> nunca tenta
    /// resolver <c>AuditRepositoryConnection</c> (seção 18: "quando PersistToDatabase=false: não resolver
    /// AuditRepositoryConnection").
    /// </summary>
    public sealed class OperationalAuditRunner : IOperationalAuditRunner
    {
        private readonly OperationalAuditOptions _options;
        private readonly IReadOnlyList<IOperationalAuditRule> _regras;
        private readonly Func<IAuditRepository> _repositoryFactory;
        private readonly IReadOnlyList<IOperationalAuditReportWriter> _writers;
        private readonly ILogger<OperationalAuditRunner> _logger;

        public OperationalAuditRunner(
            OperationalAuditOptions options,
            IEnumerable<IOperationalAuditRule> regras,
            Func<IAuditRepository> repositoryFactory,
            IEnumerable<IOperationalAuditReportWriter> writers,
            ILogger<OperationalAuditRunner> logger)
        {
            _options = options;
            _regras = regras.ToList();
            _repositoryFactory = repositoryFactory;
            _writers = writers.ToList();
            _logger = logger;
        }

        public async Task<OperationalAuditResult> ExecutarAsync(
            IReadOnlyList<string> projectIdsAcc, string? triggeredBy = null, CancellationToken cancellationToken = default)
        {
            var executionId = Guid.NewGuid();
            var iniciadoEmUtc = DateTime.UtcNow;
            var resultado = new OperationalAuditResult { ExecutionId = executionId, IniciadoEmUtc = iniciadoEmUtc };

            var regrasSelecionadas = SelecionarRegras(_regras, _options.Stages);
            _logger.LogInformation(
                "[AUDITORIA][INICIO] ExecutionId={ExecutionId} | Projetos={QuantidadeProjetos} | RegrasSelecionadas={QuantidadeRegras}",
                executionId, projectIdsAcc.Count, regrasSelecionadas.Count);

            long? auditRunId = null;
            bool persistenciaSqlFalhouGlobalmente = false;

            if (_options.PersistToDatabase)
            {
                try
                {
                    var repository = _repositoryFactory();
                    auditRunId = await repository.IniciarAuditRunAsync(executionId, triggeredBy ?? "OperationalAuditRunner", _options.Stages, cancellationToken);
                }
                catch (Exception ex)
                {
                    persistenciaSqlFalhouGlobalmente = true;
                    resultado.PersistenciaSqlFalhou = true;
                    resultado.PersistenciaSqlErro = ex.Message;
                    _logger.LogError(ex, "[AUDITORIA][ERRO] Falha ao iniciar AuditRun — a execução continua, mas nada será persistido em audit.*.");
                }
            }

            int projetosComFalhaDePersistencia = 0;
            int projetosComErroDeExecucao = 0;

            foreach (var projectIdAcc in projectIdsAcc)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var (findings, requirements, stageResults) = await ExecutarRegrasParaProjetoAsync(
                        projectIdAcc, regrasSelecionadas, executionId, iniciadoEmUtc, cancellationToken);

                    resultado.ProjetosProcessados.Add(projectIdAcc);
                    foreach (var f in findings) resultado.Findings.Add(f);
                    foreach (var r in requirements) resultado.Requirements.Add(r);
                    foreach (var s in stageResults) resultado.StageResults.Add(s);

                    if (_options.PersistToDatabase && auditRunId.HasValue)
                    {
                        try
                        {
                            var repository = _repositoryFactory();
                            var dados = new OperationalAuditProjectData
                            {
                                ProjectIdAcc = projectIdAcc,
                                Findings = findings,
                                Requirements = requirements,
                                StageResults = stageResults
                            };
                            var persistResult = await repository.PersistirProjetoAsync(auditRunId.Value, dados, cancellationToken);
                            if (!persistResult.Sucesso)
                            {
                                projetosComFalhaDePersistencia++;
                                resultado.PersistenciaSqlFalhou = true;
                                resultado.PersistenciaSqlErro = persistResult.Erro;
                            }
                        }
                        catch (Exception exPersist)
                        {
                            projetosComFalhaDePersistencia++;
                            resultado.PersistenciaSqlFalhou = true;
                            resultado.PersistenciaSqlErro = exPersist.Message;
                            _logger.LogError(exPersist, "[AUDITORIA][ERRO] Falha ao persistir projeto {ProjectIdAcc} — achados preservados em memória para os relatórios.", projectIdAcc);
                        }
                    }
                }
                catch (Exception exProjeto)
                {
                    projetosComErroDeExecucao++;
                    resultado.ErrosDeExecucao.Add($"Projeto {projectIdAcc}: {exProjeto.Message}");
                    _logger.LogError(exProjeto, "[AUDITORIA][ERRO] Falha ao processar o projeto {ProjectIdAcc} — continuando para o próximo projeto.", projectIdAcc);
                    // Nunca relança — a falha de UM projeto não pode interromper os demais (seção 3/10).
                }
            }

            resultado.FinalizadoEmUtc = DateTime.UtcNow;
            resultado.Status = AuditRunStatusResolver.Resolver(
                projectIdsAcc.Count, projetosComFalhaDePersistencia, projetosComErroDeExecucao, persistenciaSqlFalhouGlobalmente);

            await GerarRelatoriosAsync(resultado, cancellationToken);

            if (_options.PersistToDatabase && auditRunId.HasValue)
            {
                try
                {
                    var repository = _repositoryFactory();
                    await repository.FinalizarAuditRunAsync(
                        auditRunId.Value, resultado.Status, resultado.ProjetosProcessados.Count,
                        resultado.Findings.Count, resultado.Requirements.Count, resultado.PersistenciaSqlErro, cancellationToken);
                }
                catch (Exception exFinalizar)
                {
                    _logger.LogError(exFinalizar, "[AUDITORIA][ERRO] Falha ao finalizar AuditRun {AuditRunId} — os relatórios já foram gerados normalmente.", auditRunId);
                }
            }

            _logger.LogInformation(
                "[AUDITORIA][FIM] ExecutionId={ExecutionId} | Status={Status} | Findings={TotalFindings} | Requisitos={TotalRequisitos}",
                executionId, resultado.Status, resultado.Findings.Count, resultado.Requirements.Count);

            return resultado;
        }

        /// <summary>Vazia em <see cref="OperationalAuditOptions.Stages"/> = todas as regras injetadas; preenchida = só as etapas listadas (seção 3, item 3).</summary>
        internal static List<IOperationalAuditRule> SelecionarRegras(IReadOnlyList<IOperationalAuditRule> todas, IReadOnlyList<string> etapasConfiguradas)
        {
            if (etapasConfiguradas == null || etapasConfiguradas.Count == 0) return todas.ToList();

            var etapasHashSet = new HashSet<string>(etapasConfiguradas, StringComparer.OrdinalIgnoreCase);
            return todas.Where(r => etapasHashSet.Contains(r.Etapa)).ToList();
        }

        private async Task<(List<OperationalFinding> Findings, List<OperationalRequirementResult> Requirements, List<OperationalStageResult> StageResults)>
            ExecutarRegrasParaProjetoAsync(
                string projectIdAcc, IReadOnlyList<IOperationalAuditRule> regras, Guid executionId, DateTime iniciadoEmUtc, CancellationToken ct)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var stageResults = new List<OperationalStageResult>();

            var contexto = new OperationalAuditContext
            {
                ExecutionId = executionId,
                IniciadoEmUtc = iniciadoEmUtc,
                ProjectIdAcc = projectIdAcc,
                Options = _options,
                CancellationToken = ct
            };

            foreach (var regra in regras)
            {
                var iniciadaEmUtc = DateTime.UtcNow;
                List<OperationalFinding> findingsDaRegra;
                List<OperationalRequirementResult> requirementsDaRegra;

                try
                {
                    var resultadoRegra = await regra.ExecutarAsync(contexto);
                    findingsDaRegra = resultadoRegra.Findings.ToList();
                    requirementsDaRegra = resultadoRegra.Requirements.ToList();
                }
                catch (Exception exRegra)
                {
                    // Falha de UMA regra nunca impede as demais (seção 3/10) — vira um finding de automação,
                    // nunca um erro silencioso, e a etapa correspondente fica REPROVADA_POR_AUTOMACAO.
                    _logger.LogError(exRegra, "[AUDITORIA][ERRO] Regra da Etapa {Etapa} falhou para o projeto {ProjectIdAcc} — continuando com as demais regras.", regra.Etapa, projectIdAcc);

                    findingsDaRegra = new List<OperationalFinding>
                    {
                        new OperationalFinding
                        {
                            ExecutionId = executionId,
                            DataHoraUtc = DateTime.UtcNow,
                            ProjectIdAcc = projectIdAcc,
                            Etapa = regra.Etapa,
                            RegraId = "RUNNER-ERRO-EXECUCAO",
                            CodigoProblema = "AUT-RULE-EXCEPTION",
                            Titulo = $"A regra da Etapa {regra.Etapa} ({regra.NomeEtapa}) lançou uma exceção durante a execução.",
                            Descricao = exRegra.Message,
                            ErrorDomain = OperationalAuditErrorDomain.Automacao,
                            Classificacao = OperationalAuditClassification.RiscoTecnico,
                            Severidade = OperationalAuditSeverity.Alto,
                            BloqueiaEtapa = true,
                            AutomacaoPodeCorrigir = false,
                            MotivoNormalizado = "REGRA_LANCOU_EXCECAO"
                        }
                    };
                    requirementsDaRegra = new List<OperationalRequirementResult>();
                }

                foreach (var f in findingsDaRegra)
                {
                    f.DedupHash = DedupHashCalculator.CalcularDedupHash(f);
                    f.OccurrenceHash = DedupHashCalculator.CalcularOccurrenceHash(f, executionId);
                }

                findings.AddRange(findingsDaRegra);
                requirements.AddRange(requirementsDaRegra);

                stageResults.Add(RequirementStatusAggregator.Agregar(
                    executionId, projectIdAcc, regra.Etapa, regra.NomeEtapa,
                    requirementsDaRegra, findingsDaRegra, iniciadaEmUtc, DateTime.UtcNow));
            }

            return (findings, requirements, stageResults);
        }

        private async Task GerarRelatoriosAsync(OperationalAuditResult resultado, CancellationToken ct)
        {
            foreach (var writer in _writers)
            {
                bool habilitado = writer switch
                {
                    OperationalAuditTxtReportWriter => _options.GenerateTxt,
                    OperationalAuditCsvReportWriter => _options.GenerateCsv,
                    OperationalAuditJsonReportWriter => _options.GenerateJson,
                    _ => true // writer não reconhecido (ex.: fake de teste) — sempre tentado.
                };
                if (!habilitado) continue;

                try
                {
                    await writer.EscreverAsync(resultado, _options.OutputDirectory, ct);
                }
                catch (Exception exWriter)
                {
                    // Nenhum finding pode desaparecer porque UM writer falhou — os outros dois ainda rodam
                    // (seção 18: "nenhum finding pode desaparecer porque a persistência falhou" aplica-se
                    // igualmente aqui: um writer com problema não pode silenciar os outros).
                    _logger.LogError(exWriter, "[AUDITORIA][ERRO] Falha ao gerar relatório via {Writer}.", writer.GetType().Name);
                    resultado.ErrosDeExecucao.Add($"Falha ao gerar relatório ({writer.GetType().Name}): {exWriter.Message}");
                }
            }
        }
    }
}
