using System;
using APS.WebApp.Services.OperationalAudit;

namespace APS.WebApp.Services.OperationalAudit.Dedup
{
    /// <summary>Estado mínimo de um <c>audit.AuditIssue</c> já persistido, necessário para decidir o próximo estado.</summary>
    public sealed class AuditIssueSnapshot
    {
        public OperationalIssueStatus Status { get; init; }
        public DateTime FirstSeenAtUtc { get; init; }
        public DateTime LastSeenAtUtc { get; init; }
        public int OccurrenceCount { get; init; }
    }

    /// <summary>Decisão pura sobre o que gravar em <c>audit.AuditIssue</c> — nunca decide por si só se deve INSERT ou UPDATE no SQL, só o ESTADO resultante; a tradução para SQL é responsabilidade do repository.</summary>
    public sealed class AuditIssueDecision
    {
        public bool EhNovo { get; init; }
        public OperationalIssueStatus NovoStatus { get; init; }
        public DateTime FirstSeenAtUtc { get; init; }
        public DateTime LastSeenAtUtc { get; init; }
        public int OccurrenceCount { get; init; }
    }

    /// <summary>
    /// Motor de decisão da deduplicação (seção 9 da correção da Fase 2) — puro, sem SQL, sem I/O, 100%
    /// testável. O repository só chama estes dois métodos e traduz o resultado em INSERT/UPDATE.
    /// </summary>
    public static class AuditIssueDedupEngine
    {
        /// <summary>
        /// Passos 1-3 e 6 do pedido: chamado quando uma regra encontrou o problema NESTA execução (existe
        /// uma <c>OperationalFinding</c>/ocorrência real). Reaparecer depois de
        /// <see cref="OperationalIssueStatus.PossivelmenteResolvido"/> ou
        /// <see cref="OperationalIssueStatus.ResolvidoComEvidencia"/> vira
        /// <see cref="OperationalIssueStatus.Regressao"/> — nunca volta a <see cref="OperationalIssueStatus.Aberto"/>
        /// silenciosamente, porque "regressão" é uma informação distinta de "problema novo".
        /// </summary>
        public static AuditIssueDecision DecidirParaOcorrenciaEncontrada(AuditIssueSnapshot? existente, DateTime agoraUtc)
        {
            if (existente == null)
            {
                return new AuditIssueDecision
                {
                    EhNovo = true,
                    NovoStatus = OperationalIssueStatus.Aberto,
                    FirstSeenAtUtc = agoraUtc,
                    LastSeenAtUtc = agoraUtc,
                    OccurrenceCount = 1
                };
            }

            bool eraConsideradoResolvido =
                existente.Status == OperationalIssueStatus.PossivelmenteResolvido ||
                existente.Status == OperationalIssueStatus.ResolvidoComEvidencia;

            return new AuditIssueDecision
            {
                EhNovo = false,
                NovoStatus = eraConsideradoResolvido ? OperationalIssueStatus.Regressao : OperationalIssueStatus.Aberto,
                FirstSeenAtUtc = existente.FirstSeenAtUtc, // nunca reescrita
                LastSeenAtUtc = agoraUtc,
                OccurrenceCount = existente.OccurrenceCount + 1
            };
        }

        /// <summary>
        /// Passos 7-8 do pedido: chamado para um <c>AuditIssue</c> aberto que existia antes desta execução,
        /// mas nenhuma regra encontrou de novo. Nunca marca <see cref="OperationalIssueStatus.ResolvidoComEvidencia"/>
        /// sem uma prova positiva explícita (<paramref name="requisitoPositivoComprovadoNestaExecucao"/>) —
        /// a ausência sozinha só sustenta <see cref="OperationalIssueStatus.PossivelmenteResolvido"/>.
        /// </summary>
        public static OperationalIssueStatus DecidirParaAusenciaNestaExecucao(
            OperationalIssueStatus statusAtual,
            bool requisitoPositivoComprovadoNestaExecucao)
        {
            if (statusAtual == OperationalIssueStatus.ResolvidoComEvidencia)
            {
                return statusAtual; // já resolvido com evidência — nada a fazer.
            }

            return requisitoPositivoComprovadoNestaExecucao
                ? OperationalIssueStatus.ResolvidoComEvidencia
                : OperationalIssueStatus.PossivelmenteResolvido;
        }
    }
}
