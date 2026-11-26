using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Escolhe, com base em Projeto.TipoLdp, qual fluxo processa a LDP — nunca duplica a lógica do
    /// Tipo 1 ou do Tipo 2 aqui dentro, apenas encaminha.
    /// </summary>
    public interface ILdpProcessingRouterService
    {
        Task<ILdpProcessingResult> ProcessarAsync(
            Projeto projeto,
            string hubId,
            string accessToken,
            string ldpFileId,
            CancellationToken cancellationToken = default);
    }
}
