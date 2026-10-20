using System;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// ETAPA 03 — SNAPSHOT TIMESHEET PARA GESTÃO À VISTA. Depende da Etapa 01 ter terminado com sucesso
    /// (o chamador — GrdAutomationWorker — decide isso; este serviço não sabe nada sobre a Etapa 01, só
    /// recebe o caminho local de uma LDP já reconstruída). Lê a aba TIMESHEET (nunca
    /// tbTimeSheetRealizadoDocumento de novo — essa é a fonte da Etapa 01) e substitui o snapshot atual
    /// do projeto em gestao.TimesheetSnapshotAtual.
    /// </summary>
    public interface IEtapa03TimesheetSnapshotService
    {
        Task<GestaoVistaTimesheetSnapshotResultado> ExecutarAsync(
            string projectIdAcc, string ldpPathLocal, string nomeArquivo, DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);
    }
}
