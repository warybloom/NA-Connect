using System;
using System.Collections.Generic;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Resultado agregado de uma execução completa da auditoria (espelha <c>audit.AuditRun</c> +
    /// <c>audit.AuditProjectRun</c>). Única fonte usada tanto pelos writers (TXT/CSV/JSON) quanto pela
    /// persistência SQL (seção 16) — nenhuma saída tem uma cópia própria dos dados.
    /// </summary>
    public sealed class OperationalAuditResult
    {
        public Guid ExecutionId { get; init; }
        public DateTime IniciadoEmUtc { get; init; }
        public DateTime? FinalizadoEmUtc { get; set; }

        public OperationalAuditRunStatus Status { get; set; } = OperationalAuditRunStatus.Running;

        public List<string> ProjetosProcessados { get; } = new();
        public List<OperationalFinding> Findings { get; } = new();
        public List<OperationalRequirementResult> Requirements { get; } = new();
        public List<OperationalStageResult> StageResults { get; } = new();

        /// <summary>Erros de EXECUÇÃO da auditoria em si (nunca dados operacionais) — ex.: falha ao ler uma pasta do ACC.</summary>
        public List<string> ErrosDeExecucao { get; } = new();

        /// <summary>
        /// Seção 17: quando true, TXT/CSV/JSON ainda devem ser gerados quando possível — só a persistência
        /// em <c>audit</c> falhou; nunca mascarar isso, e nunca dizer que "foi salvo no banco".
        /// </summary>
        public bool PersistenciaSqlFalhou { get; set; }
        public string? PersistenciaSqlErro { get; set; }
    }
}
