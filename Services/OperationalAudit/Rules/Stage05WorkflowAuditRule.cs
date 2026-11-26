using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using NA_Connect.Services;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 5 — Workflow / Status_In_Flow (Checkpoint 3). CRÍTICA: a lógica real
    /// (<c>AccReviewWorkflowService</c> + <c>LdpUpdateService.UpdateWorkflowStatusAsync</c>, coluna Q /
    /// índice 17) está CONGELADA e nunca é chamada aqui — esta regra só LÊ Reviews via
    /// <see cref="IAuditWorkflowReadOnlyAdapter"/> e reconstrói, em memória, o mesmo raciocínio de decisão
    /// já pesquisado no código real (nunca uma heurística nova):
    ///
    /// - Review mais recente vence sempre (nunca um Review antigo disputa contra um mais novo — mesmo
    ///   princípio de "REVIEW_ATUAL_BLOQUEIA_ANTERIOR"); quando a mais recente não produz um veredito
    ///   utilizável (VOID, CLOSED sem resultado, OPEN sem currentStepId), a auditoria trata isso como
    ///   PRESERVAÇÃO DE CÉLULA — comportamento esperado, nunca erro por si só (seção 8 do pedido).
    /// - N&amp;A tem prioridade sobre Cliente; os dois são sempre avaliados (nunca curto-circuito) para
    ///   detectar conflito real entre eles.
    /// - Correspondência documental via <see cref="DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens"/>
    ///   — nunca Contains cru.
    ///
    /// Limitação honesta e documentada: <see cref="AuditReviewRef.CurrentStepDueDateRaw"/> e
    /// <see cref="AuditReviewRef.WorkflowId"/> só vêm preenchidos se o endpoint de LISTAGEM de Reviews do
    /// ACC os devolver junto do resumo (o método real usa um "reviewDetail" separado por Review, cuja forma
    /// exata não foi confirmada por esta pesquisa) — quando ausentes, WF-006/WF-007 tratam a lacuna como
    /// evidência insuficiente, nunca como erro (nunca inventa um resultado sem dado real).
    /// </summary>
    public sealed class Stage05WorkflowAuditRule : IOperationalAuditRule
    {
        public string Etapa => "5";
        public string NomeEtapa => "Workflow (Status_In_Flow)";

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] AliasesEngenharia = { "04 - ENGINEERING", "04. ENGINEERING", "04 - ENGENHARIA", "04. ENGENHARIA" };
        private static readonly string[] OrdemPrioridadeExtensao = { "DWG", "DOCX", "PDF" };
        private const int LinhaCabecalhoLdp = 7;

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;
        private readonly IAuditWorkflowReadOnlyAdapter _workflow;

        public Stage05WorkflowAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook, IAuditWorkflowReadOnlyAdapter workflow)
        {
            _acc = acc;
            _workbook = workbook;
            _workflow = workflow;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            if (pastaGestao == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
            var candidatoLdp = conteudoGestao.Itens.FirstOrDefault(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn));
            if (candidatoLdp == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            await using var streamLdp = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoLdp.LatestVersionUrn!, ct);
            var workbook = _workbook.AbrirSomenteLeitura(streamLdp, linhaDeCabecalhoPadrao: LinhaCabecalhoLdp);
            var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase)) ?? workbook.NomesDasAbas.FirstOrDefault();
            if (nomeAbaLdp == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            var abaLdp = workbook.Abas[nomeAbaLdp];

            var pastaEngenharia = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesEngenharia, false, ct);
            if (pastaEngenharia == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            var conteudoEngenharia = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaEngenharia.FolderId, false, ct);
            var itensVivos = conteudoEngenharia.Itens.Where(i => !string.IsNullOrEmpty(i.LatestVersionUrn) && !i.IsHiddenFolder).ToList();

            int documentosProcessados = 0, documentosComResultado = 0, reviewsAtivosComCurrentStep = 0;

            foreach (var (_, valores) in abaLdp.Linhas)
            {
                string? codigoNa = ObterValorDeColuna(valores, "N&A DOCUMENT NUMBER", "N&A");
                string? codigoCliente = ObterValorDeColuna(valores, "CLIENT DOCUMENT NUMBER");
                string? statusAtualNaLdp = ObterValorDeColuna(valores, "STATUS IN THE FLOW", "STATUS_IN_FLOW");

                if (string.IsNullOrWhiteSpace(codigoNa) && string.IsNullOrWhiteSpace(codigoCliente)) continue;

                var itensPorNa = string.IsNullOrWhiteSpace(codigoNa) ? new List<AccItemRef>() :
                    itensVivos.Where(i => DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(i.Name, codigoNa)).ToList();
                var itensPorCliente = string.IsNullOrWhiteSpace(codigoCliente) ? new List<AccItemRef>() :
                    itensVivos.Where(i => DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(i.Name, codigoCliente)).ToList();

                if (itensPorNa.Count > 0 && itensPorCliente.Count > 0 && !itensPorNa.Any(a => itensPorCliente.Any(b => b.ItemId == a.ItemId)))
                {
                    findings.Add(NovoFinding(contexto, "WF-005", $"Conflito N&A x Cliente: código N&A '{codigoNa}' e código Cliente '{codigoCliente}' resolvem para arquivos diferentes",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Critico,
                        bloqueiaEtapa: true, "CONFLITO_NA_CLIENTE", documentoNa: codigoNa));
                    continue;
                }

                var itensCandidatos = itensPorNa.Count > 0 ? itensPorNa : itensPorCliente;
                string? codigoResolvido = AuditDocumentCodeResolver.ResolverCodigoDaLinha(codigoNa, codigoCliente);

                if (itensCandidatos.Count == 0)
                {
                    findings.Add(NovoFinding(contexto, "WF-002", $"Documento '{codigoResolvido}' da LDP sem correspondência por N&A nem Cliente em arquivo ACC",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "DOCUMENTO_SEM_CORRESPONDENCIA_ACC", documentoNa: codigoResolvido));
                    continue;
                }

                var itemPrincipal = itensCandidatos
                    .OrderBy(i => { int idx = Array.IndexOf(OrdemPrioridadeExtensao, i.Extension?.ToUpperInvariant()); return idx < 0 ? int.MaxValue : idx; })
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .First();

                IReadOnlyList<AuditReviewRef> reviews;
                try
                {
                    reviews = await _workflow.ObterHistoricoDeReviewsAsync(contexto.ProjectIdAcc, itemPrincipal.ItemId, ct);
                }
                catch (Exception ex)
                {
                    findings.Add(NovoFinding(contexto, "WF-001", $"Falha ao coletar histórico de Reviews de '{itemPrincipal.Name}': {ex.Message}",
                        OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.IntegracaoExterna, OperationalAuditSeverity.Critico,
                        bloqueiaEtapa: true, "FALHA_COLETA_REVIEWS", nomeArquivo: itemPrincipal.Name, documentoNa: codigoResolvido));
                    continue;
                }

                if (reviews.Count == 0)
                {
                    // WF-003 só quando a LDP já mostra um status derivado de Review, mas nenhuma Review real
                    // foi encontrada — nunca assume que todo documento precisa de Review.
                    if (!string.IsNullOrWhiteSpace(statusAtualNaLdp))
                    {
                        findings.Add(NovoFinding(contexto, "WF-003", $"LDP mostra status de workflow '{statusAtualNaLdp}' para '{codigoResolvido}', mas nenhuma Review foi encontrada no ACC para o arquivo correspondente",
                            OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "STATUS_LDP_SEM_REVIEW_CORRESPONDENTE", nomeArquivo: itemPrincipal.Name, documentoNa: codigoResolvido, valorEncontrado: statusAtualNaLdp));
                    }
                    continue;
                }

                DateTime Recencia(AuditReviewRef r) => r.FinalizadaEmUtc ?? r.IniciadaEmUtc ?? DateTime.MinValue;
                var dataMaisRecente = reviews.Max(Recencia);
                var maisRecentes = reviews.Where(r => Recencia(r) == dataMaisRecente).ToList();

                if (maisRecentes.Count > 1)
                {
                    bool statusDivergem = maisRecentes.Select(r => r.Status?.Trim().ToUpperInvariant()).Distinct().Count() > 1;
                    bool vereditosDivergem = maisRecentes.SelectMany(r => r.Vereditos.Select(v => v.ApproveStatus?.Trim().ToUpperInvariant() ?? string.Empty))
                        .Distinct().Count() > 1;
                    if (statusDivergem || vereditosDivergem)
                    {
                        findings.Add(NovoFinding(contexto, "WF-005", $"Reviews concorrentes empatadas na mesma data mais recente com decisões divergentes para '{codigoResolvido}'",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Critico,
                            bloqueiaEtapa: true, "REVIEWS_CONCORRENTES_DIVERGENTES", nomeArquivo: itemPrincipal.Name, documentoNa: codigoResolvido));
                        continue;
                    }
                }
                var vencedora = maisRecentes[0];
                documentosProcessados++;

                bool aberta = string.Equals(vencedora.Status, "OPEN", StringComparison.OrdinalIgnoreCase) || string.Equals(vencedora.Status, "INITIATED", StringComparison.OrdinalIgnoreCase);
                bool concluida = string.Equals(vencedora.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) || string.Equals(vencedora.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

                if (aberta)
                {
                    if (string.IsNullOrWhiteSpace(vencedora.CurrentStepId))
                    {
                        requirements.Add(NovoRequisito(contexto, "WF-PRESERVACAO_CELULA", $"'{codigoResolvido}': Review aberta sem currentStepId",
                            OperationalRequirementStatus.Approved, "célula preservada (comportamento esperado)", "preservada"));
                        continue;
                    }

                    reviewsAtivosComCurrentStep++;

                    if (!string.IsNullOrWhiteSpace(vencedora.CurrentStepDueDateRaw) && !DateTimeOffset.TryParse(vencedora.CurrentStepDueDateRaw, out _))
                    {
                        findings.Add(NovoFinding(contexto, "WF-006", $"currentStepDueDate presente mas não parseável ('{vencedora.CurrentStepDueDateRaw}') para '{codigoResolvido}'",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "DUE_DATE_INVALIDA", nomeArquivo: itemPrincipal.Name, documentoNa: codigoResolvido, valorEncontrado: vencedora.CurrentStepDueDateRaw));
                    }

                    if (!string.IsNullOrWhiteSpace(vencedora.WorkflowId))
                    {
                        var definicaoWorkflow = await _workflow.ObterDefinicaoDoWorkflowAsync(contexto.ProjectIdAcc, vencedora.WorkflowId!, ct);
                        var (_, resultadoResolucao) = WorkflowStepNameResolver.Resolver(definicaoWorkflow, vencedora.CurrentStepId!);

                        // Só quando os QUATRO níveis reais rodaram por completo (definição disponível) e
                        // mesmo assim não resolveram — nunca quando a definição simplesmente não pôde ser
                        // obtida (isso é DefinicaoIndisponivel: evidência insuficiente, nunca WF-007).
                        if (resultadoResolucao == WorkflowStepResolutionOutcome.EtapaAusenteNaDefinicao ||
                            resultadoResolucao == WorkflowStepResolutionOutcome.EtapaPresenteSemNome)
                        {
                            findings.Add(NovoFinding(contexto, "WF-007", $"currentStepId '{vencedora.CurrentStepId}' não resolvido para nome de etapa (workflow '{vencedora.WorkflowId}', motivo={resultadoResolucao}) em '{codigoResolvido}'",
                                OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ConfiguracaoProjeto, OperationalAuditSeverity.Medio,
                                bloqueiaEtapa: false, "CURRENT_STEP_ID_NAO_RESOLVIDO", nomeArquivo: itemPrincipal.Name, documentoNa: codigoResolvido));
                        }
                    }
                }
                else if (concluida)
                {
                    var veredito = vencedora.Vereditos.FirstOrDefault(v => v.VersionUrn == itemPrincipal.LatestVersionUrn) ?? vencedora.Vereditos.FirstOrDefault();
                    bool temResultado = veredito != null && !string.IsNullOrWhiteSpace(veredito.ApproveStatus);
                    if (temResultado)
                    {
                        documentosComResultado++;
                    }
                    else
                    {
                        findings.Add(NovoFinding(contexto, "WF-004", $"Review CLOSED sem approveStatus comprovado para '{codigoResolvido}'",
                            OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "REVIEW_CLOSED_SEM_RESULTADO", nomeArquivo: itemPrincipal.Name, documentoNa: codigoResolvido));
                    }
                }
                else
                {
                    // VOID ou status bruto não reconhecido — sempre preservação de célula, nunca erro
                    // automático (seção 8: "não transformar automaticamente todo VOID em erro").
                    requirements.Add(NovoRequisito(contexto, "WF-PRESERVACAO_CELULA", $"'{codigoResolvido}': Review VOID/status não mapeado ('{vencedora.Status}')",
                        OperationalRequirementStatus.Approved, "célula preservada (comportamento esperado)", "preservada"));
                }
            }

            // WF-008 — cenário que dispararia a proteção anti-aprovação-em-massa real: reviews ativos com
            // etapa resolvida existem, mas TODOS os documentos processados terminaram com resultado (nenhum
            // ainda em aberto) — mesma condição do LdpUpdateService real.
            bool cenarioDeProtecaoAntiAprovacaoEmMassa = reviewsAtivosComCurrentStep > 0 && documentosProcessados > 0 && documentosComResultado == documentosProcessados;
            requirements.Add(NovoRequisito(contexto, "WF-008", "Proteção anti-aprovação-em-massa não seria disparada",
                !cenarioDeProtecaoAntiAprovacaoEmMassa ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "cenário não presente", cenarioDeProtecaoAntiAprovacaoEmMassa ? "cenário presente" : "não presente"));
            if (cenarioDeProtecaoAntiAprovacaoEmMassa)
            {
                findings.Add(NovoFinding(contexto, "WF-008", $"Cenário que dispararia a proteção anti-aprovação-em-massa: {reviewsAtivosComCurrentStep} Review(s) ativo(s) com etapa resolvida, mas todos os {documentosProcessados} documento(s) processados têm resultado — a sincronização real abortaria o lote inteiro aqui",
                    OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.RiscoTecnico, OperationalAuditSeverity.Critico,
                    bloqueiaEtapa: true, "CENARIO_PROTECAO_ANTI_APROVACAO_EM_MASSA"));
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string? ObterValorDeColuna(IReadOnlyDictionary<string, string?> valores, params string[] candidatos)
        {
            foreach (var candidato in candidatos)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), candidato, StringComparison.OrdinalIgnoreCase));
                if (chave != null) return valores[chave];
            }
            return null;
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "5", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? nomeArquivo = null, string? documentoNa = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "5", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                NomeArquivo = nomeArquivo, DocumentoNa = documentoNa, ValorEncontrado = valorEncontrado
            };
    }
}
