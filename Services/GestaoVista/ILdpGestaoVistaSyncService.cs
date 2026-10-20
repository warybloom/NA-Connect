using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.GestaoVista
{
    public interface ILdpGestaoVistaSyncService
    {
        /// <summary>
        /// Sincroniza a camada Gestão à Vista (Custo Real -> Custo Previsto -> Timesheet, nesta ordem) a
        /// partir do MESMO workbook da LDP já baixado do ACC pelo Worker. NUNCA lança exceção — qualquer
        /// falha (extração, schema ausente, banco) é capturada e devolvida no resultado. Aditiva e
        /// independente da sincronização operacional da LDP (Tipo 1/Tipo 2): nunca escreve na LDP,
        /// nunca faz upload, nunca lê/altera Services/CronogramaTipo2 ou Services/LdpTipo2.
        /// </summary>
        Task<GestaoVistaSyncResultado> SincronizarAsync(
            string projectIdAcc, Stream workbook, string nomeArquivo, DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);
    }
}
