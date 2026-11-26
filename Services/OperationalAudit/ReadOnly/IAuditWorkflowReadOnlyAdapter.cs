using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Uma Review do ACC (workflow de aprovação) — status bruto, nunca já interpretado (a interpretação é responsabilidade da regra, seção 11 do Checkpoint 2).</summary>
    public sealed class AuditReviewRef
    {
        public string ReviewId { get; init; } = string.Empty;

        /// <summary>Valor bruto do status do ACC (ex.: "OPEN", "INITIATED", "CLOSED", "COMPLETED", "VOID") — nunca normalizado aqui.</summary>
        public string Status { get; init; } = string.Empty;
        public DateTime? IniciadaEmUtc { get; init; }
        public DateTime? FinalizadaEmUtc { get; init; }
        public string? CurrentStepId { get; init; }

        /// <summary>
        /// Checkpoint 3 (WF-006/WF-007) — campos aditivos, nunca preenchidos por Checkpoint 1/2. Valor BRUTO
        /// (nunca já parseado aqui — a regra decide parseabilidade, mesmo padrão de <c>Status</c> "nunca
        /// normalizado aqui"). Podem ficar <c>null</c> quando o endpoint de listagem de Reviews não os
        /// devolve (a regra trata isso como evidência insuficiente, nunca como erro).
        /// </summary>
        public string? CurrentStepDueDateRaw { get; init; }
        public string? WorkflowId { get; init; }

        /// <summary>Veredito por versão dentro desta Review — null quando o ACC não devolveu approveStatus para aquela versão.</summary>
        public IReadOnlyList<AuditReviewVersionVerdictRef> Vereditos { get; init; } = Array.Empty<AuditReviewVersionVerdictRef>();
    }

    public sealed class AuditReviewVersionVerdictRef
    {
        public string VersionUrn { get; init; } = string.Empty;
        public string? ApproveStatus { get; init; }
    }

    /// <summary>
    /// Leitura somente-leitura do histórico de Reviews (workflow de aprovação) de um item do ACC — nunca
    /// abre/fecha/vota uma Review, nunca publica LDP (seção 11 do Checkpoint 2: Stage08 é só comparação).
    /// </summary>
    public interface IAuditWorkflowReadOnlyAdapter
    {
        Task<IReadOnlyList<AuditReviewRef>> ObterHistoricoDeReviewsAsync(string projectId, string itemId, CancellationToken ct = default);

        /// <summary>
        /// Correção pós-Checkpoint 3 (WF-007): devolve a definição BRUTA do workflow (mesmo GET usado por
        /// <c>AccReviewWorkflowService.ResolverNomeEtapaAtualAsync</c>:
        /// <c>construction/reviews/v1/projects/{projectId}/workflows/{workflowId}</c>), para que a resolução
        /// completa de nome de etapa (<see cref="Configuration.WorkflowStepNameResolver"/> — os mesmos
        /// quatro níveis do método real, reproduzidos em código próprio da auditoria) rode sobre o dado
        /// real, nunca sobre um subconjunto pré-resolvido aqui. <c>null</c> quando a definição não pôde ser
        /// obtida (rede, 404, JSON inválido) — a regra chamadora trata isso como evidência insuficiente,
        /// nunca como "não resolvido"/erro.
        /// </summary>
        Task<JObject?> ObterDefinicaoDoWorkflowAsync(string projectId, string workflowId, CancellationToken ct = default);
    }
}
