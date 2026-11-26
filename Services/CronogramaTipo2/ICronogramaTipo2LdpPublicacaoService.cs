using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Publica no ACC as atualizações de gate decididas por ICronogramaTipo2LdpSyncService (Fase 4B) —
    /// somente se houver alguma alteração REAL de valor (nunca publica uma versão idêntica à anterior).
    /// Reaproveita os mesmos métodos de download/upload já usados pelo Tipo 1 em AutodeskApiService.
    /// </summary>
    public interface ICronogramaTipo2LdpPublicacaoService
    {
        Task<CronogramaTipo2LdpPublicacaoResultado> PublicarSeHouverAlteracaoAsync(
            string hubId,
            string projectId,
            string accessToken,
            string ldpFileId,
            IReadOnlyList<CronogramaTipo2LdpPlanilhaAtualizacaoItem> atualizacoes,
            CancellationToken cancellationToken = default);
    }
}
