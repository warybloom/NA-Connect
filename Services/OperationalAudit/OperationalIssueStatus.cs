namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Estado do ciclo de vida de um <see cref="AuditIssue"/> (problema lógico deduplicado) — seção 9 da
    /// correção da Fase 2. Nunca pulamos direto para <see cref="ResolvidoComEvidencia"/> só porque o
    /// problema não apareceu de novo — a ausência numa execução vira <see cref="PossivelmenteResolvido"/>
    /// primeiro; só uma regra positiva comprovando que o mesmo requisito passou promove para
    /// <see cref="ResolvidoComEvidencia"/>.
    /// </summary>
    public enum OperationalIssueStatus
    {
        /// <summary>Encontrado na execução mais recente (ou nunca visto como resolvido).</summary>
        Aberto,

        /// <summary>Não apareceu na execução mais recente, mas ainda não há prova positiva de que foi corrigido.</summary>
        PossivelmenteResolvido,

        /// <summary>Uma regra positiva comprovou que o mesmo requisito agora passa — só aqui o problema é dado como resolvido.</summary>
        ResolvidoComEvidencia,

        /// <summary>Reapareceu depois de ter sido dado como resolvido (com ou sem evidência).</summary>
        Regressao
    }
}
