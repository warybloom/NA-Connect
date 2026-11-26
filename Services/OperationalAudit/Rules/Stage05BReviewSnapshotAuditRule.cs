using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 5B — Snapshot ACC Reviews (Checkpoint 3). Fonte de referência (nunca chamada):
    /// <c>Services\AccReviewStageSnapshotService.cs</c>, método <c>SincronizarSnapshotAsync</c>, que
    /// persiste em <c>[dbo].[ACC_Review_Current_Stage]</c> (DELETE-por-projeto + bulk insert). Esta regra
    /// reconstrói em memória um snapshot equivalente (mesmo critério: Review vencedora = maior data de
    /// atualização por documento lógico normalizado — nunca um antigo disputa contra o mais novo) e compara
    /// contra a tabela real via SELECT (<see cref="IAuditDatabaseReadOnlyAdapter"/>) — nunca escreve nela.
    /// Chave lógica de comparação: (Project_ID_ACC, NumeroDocumentoNormalizado) — nunca contagem total.
    /// </summary>
    public sealed class Stage05BReviewSnapshotAuditRule : IOperationalAuditRule
    {
        public string Etapa => "5B";
        public string NomeEtapa => "Snapshot ACC Reviews";

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] AliasesEngenharia = { "04 - ENGINEERING", "04. ENGINEERING", "04 - ENGENHARIA", "04. ENGENHARIA" };
        private const int LinhaCabecalhoLdp = 7;
        private const string TabelaSnapshot = "ACC_Review_Current_Stage";

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;
        private readonly IAuditWorkflowReadOnlyAdapter _workflow;
        private readonly IAuditDatabaseReadOnlyAdapter _database;

        public Stage05BReviewSnapshotAuditRule(
            IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook, IAuditWorkflowReadOnlyAdapter workflow, IAuditDatabaseReadOnlyAdapter database)
        {
            _acc = acc;
            _workbook = workbook;
            _workflow = workflow;
            _database = database;
        }

        private sealed record DocumentoReconstruido(string NumeroDocumentoNormalizado, string ReviewId, string ReviewStatus, string? CurrentStepId, string? NomeEtapaResolvida, int QuantidadeExtensoes);

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaEngenharia = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesEngenharia, false, ct);
            if (pastaEngenharia == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            var conteudoEngenharia = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaEngenharia.FolderId, false, ct);
            var itensVivos = conteudoEngenharia.Itens.Where(i => !string.IsNullOrEmpty(i.LatestVersionUrn) && !i.IsHiddenFolder).ToList();

            var reconstruidos = new List<DocumentoReconstruido>();

            foreach (var grupo in itensVivos.GroupBy(i => NormalizarNomeBase(i.Name), StringComparer.OrdinalIgnoreCase))
            {
                var vereditosPorItem = new List<(AccItemRef Item, AuditReviewRef Review)>();
                foreach (var item in grupo)
                {
                    IReadOnlyList<AuditReviewRef> reviews;
                    try { reviews = await _workflow.ObterHistoricoDeReviewsAsync(contexto.ProjectIdAcc, item.ItemId, ct); }
                    catch { continue; }
                    if (reviews.Count == 0) continue;

                    var maisRecente = reviews.OrderByDescending(r => r.FinalizadaEmUtc ?? r.IniciadaEmUtc ?? DateTime.MinValue).First();
                    vereditosPorItem.Add((item, maisRecente));
                }
                if (vereditosPorItem.Count == 0) continue;

                var porReview = vereditosPorItem.GroupBy(x => x.Review.ReviewId);
                DateTime Recencia((AccItemRef Item, AuditReviewRef Review) x) => x.Review.FinalizadaEmUtc ?? x.Review.IniciadaEmUtc ?? DateTime.MinValue;
                var dataMaisRecente = vereditosPorItem.Max(Recencia);
                var reviewIdsMaisRecentes = porReview.Where(g => g.Max(Recencia) == dataMaisRecente).Select(g => g.Key).Distinct().ToList();

                if (reviewIdsMaisRecentes.Count > 1)
                {
                    findings.Add(NovoFinding(contexto, "SNP-004", $"Documento '{grupo.Key}' tem Reviews de IDs diferentes empatados na mesma data mais recente — snapshot ambíguo, não reconstruído",
                        OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "SNAPSHOT_CONFLITO_REVIEW_EMPATADO", documentoNa: grupo.Key));
                    continue;
                }

                var vencedora = vereditosPorItem.First(x => x.Review.ReviewId == reviewIdsMaisRecentes[0]);
                var extensoesDistintas = grupo.Select(i => i.Extension ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count();

                // SNP-003 — conflito entre extensões do mesmo documento lógico: extensões diferentes cujo
                // arquivo correspondente teve Review MAIS RECENTE com ReviewId/Status diferentes do vencedor.
                var statusPorExtensao = vereditosPorItem
                    .GroupBy(x => x.Item.Extension ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(Recencia).First())
                    .ToList();
                bool extensoesConflitam = statusPorExtensao.Select(x => x.Review.ReviewId).Distinct().Count() > 1;
                if (extensoesConflitam)
                {
                    findings.Add(NovoFinding(contexto, "SNP-003", $"Documento '{grupo.Key}' tem extensões com Reviews de status divergentes entre si",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Alto,
                        bloqueiaEtapa: false, "EXTENSOES_COM_REVIEW_DIVERGENTE", documentoNa: grupo.Key));
                }

                string? nomeEtapaResolvida = null;
                bool aberta = string.Equals(vencedora.Review.Status, "OPEN", StringComparison.OrdinalIgnoreCase) || string.Equals(vencedora.Review.Status, "INITIATED", StringComparison.OrdinalIgnoreCase);
                if (aberta && !string.IsNullOrWhiteSpace(vencedora.Review.CurrentStepId) && !string.IsNullOrWhiteSpace(vencedora.Review.WorkflowId))
                {
                    // Mesma resolução completa de 4 níveis usada pela Stage05 (WF-007) — nunca um
                    // subconjunto direto aqui.
                    var definicaoWorkflow = await _workflow.ObterDefinicaoDoWorkflowAsync(contexto.ProjectIdAcc, vencedora.Review.WorkflowId!, ct);
                    var (nome, _) = WorkflowStepNameResolver.Resolver(definicaoWorkflow, vencedora.Review.CurrentStepId!);
                    nomeEtapaResolvida = nome;
                }

                reconstruidos.Add(new DocumentoReconstruido(grupo.Key, vencedora.Review.ReviewId, vencedora.Review.Status, vencedora.Review.CurrentStepId, nomeEtapaResolvida, extensoesDistintas));
            }

            // Leitura SOMENTE LEITURA da tabela real persistida — nunca escreve nela.
            var parametros = new Dictionary<string, object?> { ["@ProjectIdAcc"] = contexto.ProjectIdAcc };
            DataTable tabela = await _database.ConsultarAsync(
                $"SELECT NumeroDocumentoNormalizado, ReviewId, ReviewStatus, CurrentStepId, EtapaAtual, OrigemResponsavel FROM [{TabelaSnapshot}] WHERE Project_ID_ACC = @ProjectIdAcc",
                parametros, ct);

            var persistidoPorDocumento = tabela.Rows.Cast<DataRow>()
                .Where(r => r["NumeroDocumentoNormalizado"] != DBNull.Value)
                .GroupBy(r => r["NumeroDocumentoNormalizado"]!.ToString()!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int divergencias = 0;
            foreach (var doc in reconstruidos)
            {
                if (!persistidoPorDocumento.TryGetValue(doc.NumeroDocumentoNormalizado, out var linha))
                {
                    divergencias++;
                    findings.Add(NovoFinding(contexto, "SNP-004", $"Documento '{doc.NumeroDocumentoNormalizado}' reconstruído em memória (Review {doc.ReviewId}), mas ausente em {TabelaSnapshot}",
                        OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "SNAPSHOT_DOCUMENTO_AUSENTE_NA_TABELA", documentoNa: doc.NumeroDocumentoNormalizado));
                    continue;
                }

                string reviewIdPersistido = linha["ReviewId"] == DBNull.Value ? string.Empty : linha["ReviewId"]!.ToString()!;
                string statusPersistido = linha["ReviewStatus"] == DBNull.Value ? string.Empty : linha["ReviewStatus"]!.ToString()!;
                string etapaPersistida = linha["EtapaAtual"] == DBNull.Value ? string.Empty : linha["EtapaAtual"]!.ToString()!;
                string origemResponsavel = linha["OrigemResponsavel"] == DBNull.Value ? string.Empty : linha["OrigemResponsavel"]!.ToString()!;

                if (!string.Equals(reviewIdPersistido, doc.ReviewId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(statusPersistido, doc.ReviewStatus, StringComparison.OrdinalIgnoreCase))
                {
                    divergencias++;
                    findings.Add(NovoFinding(contexto, "SNP-004", $"Documento '{doc.NumeroDocumentoNormalizado}': snapshot reconstruído (Review={doc.ReviewId}, Status={doc.ReviewStatus}) diverge do persistido (Review={reviewIdPersistido}, Status={statusPersistido})",
                        OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.ReviewInconsistente, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "SNAPSHOT_DIVERGENTE_DA_TABELA", documentoNa: doc.NumeroDocumentoNormalizado));
                }

                // SNP-001 — dados estavam disponíveis (resolvemos o nome da etapa a partir de dados reais)
                // mas a tabela persistida não tem EtapaAtual — reconstrução real falhou apesar do dado existir.
                if (doc.NomeEtapaResolvida != null && string.IsNullOrWhiteSpace(etapaPersistida))
                {
                    findings.Add(NovoFinding(contexto, "SNP-001", $"Documento '{doc.NumeroDocumentoNormalizado}': etapa '{doc.NomeEtapaResolvida}' era resolvível a partir de dados disponíveis, mas EtapaAtual está vazia em {TabelaSnapshot}",
                        OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.DadoAusente, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "METADADOS_ETAPA_PERDIDOS_NA_PERSISTENCIA", documentoNa: doc.NumeroDocumentoNormalizado));
                }

                // SNP-002 — responsável não resolvido por ausência de claimedBy/candidates: comportamento
                // esperado e comum (não é erro) — só registrado como informativo.
                if (string.Equals(origemResponsavel, "NAO_ENCONTRADO", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(NovoFinding(contexto, "SNP-002", $"Responsável não resolvido para '{doc.NumeroDocumentoNormalizado}' (sem claimedBy nem candidates) — comportamento esperado",
                        OperationalAuditErrorDomain.ComportamentoEsperado, OperationalAuditClassification.ComportamentoEsperado, OperationalAuditSeverity.Informativo,
                        bloqueiaEtapa: false, "RESPONSAVEL_NAO_ENCONTRADO", documentoNa: doc.NumeroDocumentoNormalizado));
                }
            }

            requirements.Add(NovoRequisito(contexto, "SNP-004", "Snapshot reconstruído consistente com a tabela persistida",
                divergencias == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 divergências", divergencias.ToString()));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string NormalizarNomeBase(string nomeArquivo) =>
            System.IO.Path.GetFileNameWithoutExtension(nomeArquivo).Trim().ToUpperInvariant();

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "5B", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? documentoNa = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "5B", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                DocumentoNa = documentoNa
            };
    }
}
