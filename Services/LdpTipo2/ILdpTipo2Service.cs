using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Processamento da LDP Tipo 2 (gates 30%/60%/90%). Nesta fase, a implementação é apenas
    /// estrutural — ver LdpTipo2Service.
    /// </summary>
    public interface ILdpTipo2Service
    {
        Task<LdpTipo2Resultado> ProcessarAsync(
            string hubId,
            string projectId,
            string accessToken,
            string ldpFileId,
            CancellationToken cancellationToken = default);
    }
}
