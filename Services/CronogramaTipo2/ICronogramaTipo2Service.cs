using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Orquestração isolada do cronograma Tipo 2: localiza e baixa o .mpp mais recente do projeto
    /// (reaproveitando os métodos públicos já existentes em AccScheduleSyncService/AutodeskApiService,
    /// nunca reimplementados aqui), lê via ICronogramaTipo2Parser, persiste via
    /// ICronogramaTipo2Repository (uma única transação por arquivo) e executa um matching SOMENTE
    /// LEITURA contra a última versão dos gates da LDP. Nunca chamado pelo GrdAutomationWorker nesta
    /// fase — uso isolado/manual.
    /// </summary>
    public interface ICronogramaTipo2Service
    {
        Task<CronogramaTipo2Resultado> ProcessarAsync(
            string hubId,
            string projectId,
            string accessToken,
            CancellationToken cancellationToken = default);
    }
}
