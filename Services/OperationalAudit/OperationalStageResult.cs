using System;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Resultado consolidado de UMA etapa dentro de UM projeto (seção 16/23 da correção da Fase 2) —
    /// espelha <c>audit.AuditStageResult</c>. Sempre calculado por <c>RequirementStatusAggregator</c> (pura,
    /// testável) a partir das listas de <see cref="OperationalRequirementResult"/> e
    /// <see cref="OperationalFinding"/> daquela etapa/projeto — nunca montado à mão por uma regra.
    /// </summary>
    public sealed class OperationalStageResult
    {
        public Guid ExecutionId { get; init; }
        public string ProjectIdAcc { get; init; } = string.Empty;

        /// <summary>"0".."17", ou "5B".</summary>
        public string Etapa { get; init; } = string.Empty;
        public string NomeEtapa { get; init; } = string.Empty;

        public OperationalAuditStageStatus Resultado { get; init; }

        public int TotalRequisitos { get; init; }
        public int Aprovados { get; init; }
        public int Falhos { get; init; }
        public int Avisos { get; init; }
        public int ErrosOperacionais { get; init; }
        public int ErrosAutomacao { get; init; }
        public int Inconclusivos { get; init; }
        public int Bloqueadores { get; init; }

        /// <summary>Nulo quando <see cref="TotalRequisitos"/> é zero (etapa não aplicável/não executada).</summary>
        public decimal? PercentualConformidade { get; init; }

        public DateTime IniciadoEmUtc { get; init; }
        public DateTime? FinalizadoEmUtc { get; init; }
    }
}
