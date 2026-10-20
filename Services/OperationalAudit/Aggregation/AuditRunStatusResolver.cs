using APS.WebApp.Services.OperationalAudit;

namespace APS.WebApp.Services.OperationalAudit.Aggregation
{
    /// <summary>
    /// Decide o <see cref="OperationalAuditRunStatus"/> final de uma execução (seção 10). Trata só da SAÚDE
    /// MECÂNICA da execução (rodou, persistiu) — nunca da saúde operacional dos projetos auditados (isso é
    /// <see cref="OperationalAuditStageStatus"/>/<c>AuditIssue</c>). Puro, testável.
    /// </summary>
    public static class AuditRunStatusResolver
    {
        public static OperationalAuditRunStatus Resolver(
            int totalProjetos,
            int projetosComFalhaDePersistencia,
            int projetosComErroDeExecucao,
            bool persistenciaSqlFalhouGlobalmente)
        {
            if (totalProjetos == 0)
            {
                return OperationalAuditRunStatus.Failed;
            }
            if (projetosComErroDeExecucao == totalProjetos)
            {
                return OperationalAuditRunStatus.Failed;
            }
            if (projetosComFalhaDePersistencia > 0 || projetosComErroDeExecucao > 0)
            {
                return OperationalAuditRunStatus.PartiallyCompleted;
            }
            if (persistenciaSqlFalhouGlobalmente)
            {
                return OperationalAuditRunStatus.CompletedWithWarnings;
            }
            return OperationalAuditRunStatus.Completed;
        }
    }
}
