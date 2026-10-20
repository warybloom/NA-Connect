using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Leitura isolada de um projeto ativo (dbo.Projetos), incluindo Tipo_LDP, para uso pelo
    /// roteamento Tipo 1/Tipo 2. Nunca usado hoje pelo GrdAutomationWorker — que continua usando
    /// DatabaseService.GetProjetosAtivos() (DataTable) exatamente como antes.
    /// </summary>
    public interface ILdpProjetoRepository
    {
        Task<Projeto> ObterProjetoAtivoAsync(string projectIdAcc, CancellationToken cancellationToken = default);
    }
}
