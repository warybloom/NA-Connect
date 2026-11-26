namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Resultado consolidado de UMA etapa dentro de UM projeto (<see cref="OperationalStageResult"/> /
    /// <c>AuditStageResult</c>) — seção 23 da matriz da Fase 1. Nunca reduzir a só sucesso/erro.
    /// </summary>
    public enum OperationalAuditStageStatus
    {
        Aprovada,
        AprovadaComAlertas,
        ReprovadaPorDados,
        ReprovadaPorEstrutura,
        ReprovadaPorConfiguracao,
        ReprovadaPorAutomacao,
        NaoExecutada,
        NaoAplicavel,
        Inconclusiva
    }
}
