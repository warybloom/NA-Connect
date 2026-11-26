using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Abstração mínima e comum ao resultado do roteamento, independentemente de qual serviço
    /// (Tipo 1 ou Tipo 2) foi selecionado. Evita retornar "object" do roteador.
    /// </summary>
    public interface ILdpProcessingResult
    {
        TipoLdp TipoLdp { get; }
        bool Sucesso { get; }
        string Mensagem { get; }

        // Granularidade por sub-etapa Tipo2 (correção 16/09/2026) — null quando não aplicável (caminho
        // Tipo1) ou quando o sub-passo nem chegou a ser tentado. Ver LdpProcessingRouterResult.
        bool? LdpPersistidoComSucesso { get; }
        int? LdpDocumentosEncontrados { get; }
        bool? CronogramaSincronizadoComSucesso { get; }
        int? CronogramaTarefasValidas { get; }
        int? CronogramaTarefasAnalisadas { get; }
        int? CronogramaMatchesEncontrados { get; }
    }
}
