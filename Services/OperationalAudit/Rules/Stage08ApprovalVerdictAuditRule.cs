using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Aggregation;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 8 — SOMENTE comparação (seção 11 do Checkpoint 2). NUNCA chama
    /// <c>RunApprovalVerdictSyncAsync</c> (hoje um stub inerte — nem precisaria, mas esta regra nem o
    /// referencia), NUNCA escreve na coluna Q, NUNCA publica LDP. Existe para demonstrar, com evidência
    /// concreta por projeto, por que a Etapa 8 não pode voltar a escrever.
    /// </summary>
    public sealed class Stage08ApprovalVerdictAuditRule : IOperationalAuditRule
    {
        public string Etapa => "8";
        public string NomeEtapa => "Comparação de Veredito de Aprovação (somente leitura)";

        private static readonly string[] AliasesEngenharia04 = { "04 - ENGINEERING", "04. ENGINEERING", "04 - ENGENHARIA", "04. ENGENHARIA" };
        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] CabecalhosCodigoComposto = { "CÓDIGO COMPOSTO", "CODIGO COMPOSTO", "COMPOSITE CODE", "CÓDIGO COMPOSTO ANTIGO" };
        private static readonly string[] CabecalhosCodigoNa = { "N&A DOCUMENT NUMBER", "N&A", "NA DOCUMENT NUMBER", "CÓDIGO N&A", "CODIGO N&A" };
        private static readonly string[] CabecalhosCodigoCliente = { "CLIENT DOCUMENT NUMBER", "NÚMERO DOCUMENTO CLIENTE", "NUMERO DOCUMENTO CLIENTE", "CÓDIGO CLIENTE", "CODIGO CLIENTE" };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkflowReadOnlyAdapter _workflow;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage08ApprovalVerdictAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkflowReadOnlyAdapter workflow, IAuditWorkbookReadOnlyAdapter workbook)
        {
            _acc = acc;
            _workflow = workflow;
            _workbook = workbook;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaEngenharia = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesEngenharia04, navegarRecursivamente: false, ct);
            if (pastaEngenharia != null)
            {
                var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaEngenharia.FolderId, recursivo: true, ct);
                int documentosComparados = 0;
                int divergencias = 0;
                int riscosVoid = 0;

                foreach (var item in conteudo.Itens.Where(i => !string.IsNullOrEmpty(i.LatestVersionUrn)))
                {
                    var reviews = await _workflow.ObterHistoricoDeReviewsAsync(contexto.ProjectIdAcc, item.ItemId, ct);
                    if (reviews.Count == 0) continue;

                    var decisaoEtapa5 = ApprovalVerdictComparisonEngine.DecidirComoEtapa5(reviews, item.LatestVersionUrn!);
                    var decisaoHistorico = ApprovalVerdictComparisonEngine.DecidirComoMotorHistorico(reviews, item.LatestVersionUrn!);
                    documentosComparados++;

                    // VRD-002 — o caso perigoso: Etapa 5 preservou a célula (review mais recente VOID ou sem
                    // approveStatus) e o motor histórico teria usado uma aprovação antiga.
                    if (decisaoEtapa5.Tipo == VeredictoTipoDecisao.PreservarCelula && decisaoHistorico.Tipo == VeredictoTipoDecisao.StatusExplicito)
                    {
                        riscosVoid++;
                        findings.Add(NovoFinding(contexto, "VRD-002",
                            $"Motor histórico utilizaria a aprovação antiga '{decisaoHistorico.Valor}' (Review {decisaoHistorico.ReviewIdUsada}) quando a Etapa 5 preservou a célula por Review mais recente VOID ou sem approveStatus",
                            OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.RiscoTecnico, OperationalAuditSeverity.Alto,
                            bloqueiaEtapa: true, motivoNormalizado: "VOID_CAIRIA_PARA_APROVACAO_ANTIGA",
                            nomeArquivo: item.Name, valorEsperado: "preservar célula (Etapa 5)", valorEncontrado: decisaoHistorico.Valor));
                    }
                    // VRD-001 — divergência geral (ambos decidiram, mas diferente) — nunca conta o mesmo caso do VRD-002 de novo.
                    else if (decisaoEtapa5.Tipo != VeredictoTipoDecisao.PreservarCelula && decisaoEtapa5.Tipo != VeredictoTipoDecisao.SemReviews
                             && decisaoHistorico.Tipo != VeredictoTipoDecisao.SemReviews
                             && !string.Equals(decisaoEtapa5.Valor, decisaoHistorico.Valor, StringComparison.OrdinalIgnoreCase))
                    {
                        divergencias++;
                        findings.Add(NovoFinding(contexto, "VRD-001",
                            $"Motor histórico ('{decisaoHistorico.Valor}') diverge da decisão da Etapa 5 ('{decisaoEtapa5.Valor}')",
                            OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.RiscoTecnico, OperationalAuditSeverity.Alto,
                            bloqueiaEtapa: false, motivoNormalizado: "MOTOR_HISTORICO_DIVERGE_ETAPA5",
                            nomeArquivo: item.Name, valorEsperado: decisaoEtapa5.Valor, valorEncontrado: decisaoHistorico.Valor));
                    }
                }

                requirements.Add(NovoRequisito(contexto, "VRD-001", "Motor histórico não diverge da Etapa 5",
                    divergencias == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    "0 divergências", $"{divergencias} de {documentosComparados}"));
                requirements.Add(NovoRequisito(contexto, "VRD-002", "Motor histórico nunca usaria aprovação antiga em caso VOID",
                    riscosVoid == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    "0 casos", $"{riscosVoid} de {documentosComparados}"));
            }

            // VRD-003 — código composto existente sem N&A e sem Cliente.
            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, navegarRecursivamente: false, ct);
            if (pastaGestao != null)
            {
                var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, recursivo: false, ct);
                var candidato = conteudoGestao.Itens.FirstOrDefault(i =>
                    (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrEmpty(i.LatestVersionUrn));

                if (candidato != null)
                {
                    await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidato.LatestVersionUrn!, ct);
                    var workbook = _workbook.AbrirSomenteLeitura(stream);
                    var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));

                    int casosComCodigoCompostoSemNaSemCliente = 0;
                    if (nomeAbaLdp != null)
                    {
                        var aba = workbook.Abas[nomeAbaLdp];
                        foreach (var (numeroLinha, valores) in aba.Linhas)
                        {
                            string? codigoComposto = ObterValorDeColunaCandidata(valores, CabecalhosCodigoComposto);
                            string? codigoNa = ObterValorDeColunaCandidata(valores, CabecalhosCodigoNa);
                            string? codigoCliente = ObterValorDeColunaCandidata(valores, CabecalhosCodigoCliente);

                            if (!string.IsNullOrWhiteSpace(codigoComposto) && string.IsNullOrWhiteSpace(codigoNa) && string.IsNullOrWhiteSpace(codigoCliente))
                            {
                                casosComCodigoCompostoSemNaSemCliente++;
                                findings.Add(NovoFinding(contexto, "VRD-003",
                                    $"Código composto '{codigoComposto}' existente sem N&A e sem Cliente na linha {numeroLinha}",
                                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                                    bloqueiaEtapa: false, motivoNormalizado: "CODIGO_COMPOSTO_SEM_NA_SEM_CLIENTE",
                                    nomeArquivo: candidato.Name, abaEncontrada: nomeAbaLdp, linha: numeroLinha, valorEncontrado: codigoComposto));
                            }
                        }
                    }

                    requirements.Add(NovoRequisito(contexto, "VRD-003", "Nenhum código composto sem N&A e sem Cliente",
                        casosComCodigoCompostoSemNaSemCliente == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                        "0", casosComCodigoCompostoSemNaSemCliente.ToString()));
                }
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string? ObterValorDeColunaCandidata(IReadOnlyDictionary<string, string?> valores, params string[] candidatos)
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
                Etapa = "8", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? nomeArquivo = null, string? abaEncontrada = null, int? linha = null,
            string? valorEsperado = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "8", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada, Linha = linha, ValorEsperado = valorEsperado, ValorEncontrado = valorEncontrado
            };
    }
}
