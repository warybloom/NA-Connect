using System.Collections.Generic;
using APS.WebApp.Services.OperationalAudit;

namespace APS.WebApp.Services.OperationalAudit.Persistence
{
    /// <summary>Agrupa tudo que uma execução produziu para UM projeto — a unidade de persistência transacional (seção 10).</summary>
    public sealed class OperationalAuditProjectData
    {
        public string ProjectIdAcc { get; init; } = string.Empty;
        public string? AccountIdAcc { get; init; }
        public string? ProjectName { get; init; }

        public IReadOnlyList<OperationalFinding> Findings { get; init; } = System.Array.Empty<OperationalFinding>();
        public IReadOnlyList<OperationalRequirementResult> Requirements { get; init; } = System.Array.Empty<OperationalRequirementResult>();
        public IReadOnlyList<OperationalStageResult> StageResults { get; init; } = System.Array.Empty<OperationalStageResult>();
    }

    /// <summary>Resultado da tentativa de persistir UM projeto — nunca lança para fora; o chamador decide como seguir (seção 10).</summary>
    public sealed class AuditProjectPersistResult
    {
        public bool Sucesso { get; init; }
        public string? Erro { get; init; }
        public long? AuditProjectRunId { get; init; }

        public static AuditProjectPersistResult Ok(long auditProjectRunId) => new() { Sucesso = true, AuditProjectRunId = auditProjectRunId };
        public static AuditProjectPersistResult Falha(string erro) => new() { Sucesso = false, Erro = erro };
    }
}
