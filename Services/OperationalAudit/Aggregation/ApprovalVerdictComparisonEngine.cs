using System;
using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Aggregation
{
    public enum VeredictoTipoDecisao
    {
        /// <summary>A versão tem um <c>approveStatus</c> explícito — usado como está.</summary>
        StatusExplicito,

        /// <summary>Review aberta com etapa (currentStepId) resolvida — nome da etapa usado como status provisório.</summary>
        NomeDaEtapa,

        /// <summary>Nenhum resultado comprovado (review aberta sem etapa resolvida, OU review não-aberta — inclusive VOID — sem approveStatus) — a célula é preservada, nunca sobrescrita.</summary>
        PreservarCelula,

        /// <summary>Nenhuma review encontrada para avaliar.</summary>
        SemReviews
    }

    public sealed class VeredictoDecisao
    {
        public VeredictoTipoDecisao Tipo { get; init; }
        public string? Valor { get; init; }
        public string? ReviewIdUsada { get; init; }
    }

    /// <summary>
    /// Compara, em memória, a decisão que a Etapa 5 (atual, autoridade única de Status_In_Flow) tomaria
    /// contra a decisão que o motor histórico da antiga Etapa 8 (<c>RunApprovalVerdictSyncAsync</c>, hoje um
    /// stub inerte — nunca chamado por esta engine) teria tomado — seção 11 do Checkpoint 2. Puro, sem I/O,
    /// 100% testável; nunca escreve nada, nunca decide o veredito "real" de nada.
    /// </summary>
    public static class ApprovalVerdictComparisonEngine
    {
        /// <summary>
        /// Espelha <c>AccReviewWorkflowService.DecidirStatusDaVersao</c>: explícito > etapa aberta resolvida >
        /// preservar (review aberta sem etapa OU não-aberta/VOID sem resultado) — nunca cai para uma review
        /// mais antiga.
        /// </summary>
        public static VeredictoDecisao DecidirComoEtapa5(IReadOnlyList<AuditReviewRef> reviews, string versionUrn)
        {
            var maisRecente = reviews.OrderByDescending(r => r.IniciadaEmUtc ?? DateTime.MinValue).FirstOrDefault();
            if (maisRecente == null) return new VeredictoDecisao { Tipo = VeredictoTipoDecisao.SemReviews };

            var veredito = maisRecente.Vereditos.FirstOrDefault(v => v.VersionUrn == versionUrn);
            if (!string.IsNullOrWhiteSpace(veredito?.ApproveStatus))
            {
                return new VeredictoDecisao { Tipo = VeredictoTipoDecisao.StatusExplicito, Valor = veredito!.ApproveStatus, ReviewIdUsada = maisRecente.ReviewId };
            }

            bool aberta = EhStatusAberto(maisRecente.Status);
            if (aberta && !string.IsNullOrWhiteSpace(maisRecente.CurrentStepId))
            {
                return new VeredictoDecisao { Tipo = VeredictoTipoDecisao.NomeDaEtapa, Valor = maisRecente.CurrentStepId, ReviewIdUsada = maisRecente.ReviewId };
            }

            // Aberta sem etapa resolvida, OU não-aberta (inclusive VOID) sem approveStatus — preserva,
            // NUNCA cai para uma review mais antiga.
            return new VeredictoDecisao { Tipo = VeredictoTipoDecisao.PreservarCelula, ReviewIdUsada = maisRecente.ReviewId };
        }

        /// <summary>
        /// Espelha o algoritmo histórico removido (commit anterior à desativação da Etapa 8): considera só
        /// reviews CLOSED/COMPLETED, da mais recente para a mais antiga, e usa o PRIMEIRO approveStatus
        /// encontrado — uma review VOID nunca entra neste filtro (não é OPEN/INITIATED nem CLOSED/COMPLETED),
        /// então o algoritmo histórico simplesmente pula para a review fechada mais antiga seguinte.
        /// </summary>
        public static VeredictoDecisao DecidirComoMotorHistorico(IReadOnlyList<AuditReviewRef> reviews, string versionUrn)
        {
            var fechadas = reviews
                .Where(r => string.Equals(r.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.FinalizadaEmUtc ?? r.IniciadaEmUtc ?? DateTime.MinValue)
                .ToList();

            foreach (var review in fechadas)
            {
                var veredito = review.Vereditos.FirstOrDefault(v => v.VersionUrn == versionUrn);
                if (!string.IsNullOrWhiteSpace(veredito?.ApproveStatus))
                {
                    return new VeredictoDecisao { Tipo = VeredictoTipoDecisao.StatusExplicito, Valor = veredito!.ApproveStatus, ReviewIdUsada = review.ReviewId };
                }
            }

            return new VeredictoDecisao { Tipo = VeredictoTipoDecisao.SemReviews };
        }

        private static bool EhStatusAberto(string status) =>
            string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "INITIATED", StringComparison.OrdinalIgnoreCase);
    }
}
