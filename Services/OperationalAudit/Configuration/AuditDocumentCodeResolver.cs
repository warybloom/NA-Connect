namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>
    /// Prioridade de código documental de UMA linha da LDP (correção pós-Checkpoint 2, item 6): 1) N&amp;A
    /// DOCUMENT NUMBER; 2) fallback CLIENT DOCUMENT NUMBER quando N&amp;A estiver vazio. Nunca escolhe
    /// nenhum candidato quando os dois estão vazios — a linha simplesmente não participa da reconciliação
    /// (nunca "escolha arbitrária").
    /// </summary>
    public static class AuditDocumentCodeResolver
    {
        public static string? ResolverCodigoDaLinha(string? codigoNa, string? codigoCliente)
        {
            if (!string.IsNullOrWhiteSpace(codigoNa)) return codigoNa!.Trim();
            if (!string.IsNullOrWhiteSpace(codigoCliente)) return codigoCliente!.Trim();
            return null;
        }
    }
}
