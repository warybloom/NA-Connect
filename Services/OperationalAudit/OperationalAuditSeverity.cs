namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>Severidade de um <see cref="OperationalFinding"/> — seção 7 da matriz da Fase 1.</summary>
    public enum OperationalAuditSeverity
    {
        Informativo,
        Baixo,
        Medio,
        Alto,
        Critico
    }
}
