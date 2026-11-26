using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Implementação simples de ILdpProcessingResult devolvida por LdpProcessingRouterService.
    /// </summary>
    public sealed class LdpProcessingRouterResult : ILdpProcessingResult
    {
        public TipoLdp TipoLdp { get; set; }
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; }

        // Granularidade por sub-etapa Tipo2 (correção 16/09/2026) — só EXPÕE o que o router já computa
        // internamente (nunca recalcula, nunca duplica lógica de CronogramaTipo2/LdpTipo2). Usado por
        // GrdAutomationWorker para registrar as Etapas legadas 09 (Cronograma -> LDP) e 11 (Persistência
        // LDP) como "executadas pelo equivalente Tipo2" quando o sub-passo correspondente realmente rodou
        // com sucesso — null quando o sub-passo nem chegou a ser tentado (etapa anterior já falhou).
        public bool? LdpPersistidoComSucesso { get; set; }
        public int? LdpDocumentosEncontrados { get; set; }
        public bool? CronogramaSincronizadoComSucesso { get; set; }
        public int? CronogramaTarefasValidas { get; set; }
        public int? CronogramaTarefasAnalisadas { get; set; }
        public int? CronogramaMatchesEncontrados { get; set; }
    }
}
