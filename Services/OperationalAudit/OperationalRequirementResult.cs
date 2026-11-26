using System;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Resultado da verificação de UM requisito esperado por etapa (seção 11 da correção da Fase 2) —
    /// existe para que o Power BI mostre percentual de conformidade, não só problemas. Uma regra pode (e
    /// deve) produzir vários destes por execução: um para cada requisito real que ela checou, aprovado ou
    /// não. Nunca gerar um <see cref="OperationalFinding"/> para um requisito
    /// <see cref="OperationalRequirementStatus.Approved"/> — a aprovação vive só aqui.
    /// </summary>
    public sealed class OperationalRequirementResult
    {
        public Guid ExecutionId { get; init; }
        public DateTime DataHoraUtc { get; init; }

        public string ProjectIdAcc { get; init; } = string.Empty;

        /// <summary>"0".."17", ou "5B".</summary>
        public string Etapa { get; init; } = string.Empty;

        /// <summary>Código estável do requisito no catálogo (<c>audit.AuditRequirementCatalog.RequirementCode</c>).</summary>
        public string RequirementCode { get; init; } = string.Empty;

        /// <summary>O objeto concreto avaliado (ex.: nome do arquivo, da pasta, do documento) — nunca um resumo genérico.</summary>
        public string? ObjetoAvaliado { get; init; }

        public OperationalRequirementStatus Status { get; init; }

        public string? ValorEsperado { get; init; }
        public string? ValorEncontrado { get; init; }
    }
}
