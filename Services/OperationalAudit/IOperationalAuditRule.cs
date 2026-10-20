using System.Collections.Generic;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Contrato de uma regra de auditoria de UMA etapa (<c>Rules/Stage00ProjectDnaAuditRule.cs</c> etc.,
    /// Checkpoint 2+). Cada regra é desacoplada e testável isoladamente (seção 4 da matriz da Fase 1) — só
    /// lê dados via adaptadores somente-leitura, nunca chama um método operacional de sincronização.
    /// Nenhuma implementação existe ainda neste checkpoint.
    /// </summary>
    public interface IOperationalAuditRule
    {
        /// <summary>"0".."17", ou "5B" — nunca "3" nem "8" como escritor (Etapa 8 é só comparativa).</summary>
        string Etapa { get; }
        string NomeEtapa { get; }

        Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto);
    }

    /// <summary>Resultado de uma única regra: achados + resultados de requisitos (aprovados inclusive).</summary>
    public sealed class OperationalRuleExecutionResult
    {
        public IReadOnlyList<OperationalFinding> Findings { get; init; } = System.Array.Empty<OperationalFinding>();
        public IReadOnlyList<OperationalRequirementResult> Requirements { get; init; } = System.Array.Empty<OperationalRequirementResult>();
    }
}
