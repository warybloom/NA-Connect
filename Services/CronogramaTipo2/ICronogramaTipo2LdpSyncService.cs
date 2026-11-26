using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Orquestra a Fase 4B: carrega o snapshot mais recente do Cronograma Tipo 2 e a última versão dos
    /// gates da LDP Tipo 2, aplica a matriz de decisão (matching por Project_ID_ACC + N&amp;A normalizado
    /// + Gate) e persiste, dentro de uma única transação, os UPDATEs de gate (só para MATCH_ENCONTRADO com
    /// Gate_Ativo = 1) e o status de matching em cada linha do snapshot do cronograma. Nunca ativa/desativa
    /// gate, nunca atualiza Planned_Physical, nunca envia arquivo ao ACC, nunca é chamado pelo Worker.
    /// </summary>
    public interface ICronogramaTipo2LdpSyncService
    {
        Task<CronogramaTipo2LdpSyncResult> SincronizarAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default);
    }
}
