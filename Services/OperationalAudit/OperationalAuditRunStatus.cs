namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>Resultado final de uma execução completa da auditoria (<see cref="AuditRun"/>) — seção 10.</summary>
    public enum OperationalAuditRunStatus
    {
        Running,
        Completed,
        CompletedWithWarnings,
        PartiallyCompleted,
        Failed
    }

    /// <summary>Estado de persistência de UM projeto dentro da execução (<see cref="AuditProjectRun"/>) — seção 10.</summary>
    public enum OperationalAuditProjectPersistenceStatus
    {
        Ok,
        Failed,
        RolledBack
    }
}
