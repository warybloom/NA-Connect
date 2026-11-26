namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Resultado da verificação de UM requisito (<see cref="OperationalRequirementResult"/>) — seção 11 da
    /// correção da Fase 2. Nunca gerar <see cref="OperationalFinding"/> para um requisito
    /// <see cref="Approved"/>; requisitos aprovados só existem aqui, para permitir calcular percentual de
    /// conformidade no Power BI.
    /// </summary>
    public enum OperationalRequirementStatus
    {
        Approved,
        Failed,
        Warning,
        NotApplicable,
        NotExecuted,
        Inconclusive
    }
}
