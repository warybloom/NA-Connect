using System;
using System.Threading;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Checkpoint 4, seção 14: Ctrl+C cooperativo — nunca mata o processo abruptamente
    /// (<c>ConsoleCancelEventArgs.Cancel = true</c>), só sinaliza o <see cref="CancellationToken"/> que
    /// <c>OperationalAuditRunner</c> já observa entre projetos/etapas. Como este CLI é um processo
    /// SEPARADO, cancelar aqui nunca afeta <c>GrdAutomationWorker</c> nem qualquer outro processo
    /// operacional.
    /// </summary>
    public sealed class CtrlCCancellationSource : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ConsoleCancelEventHandler _handler;

        public CancellationToken Token => _cts.Token;
        public bool CancellationRequested => _cts.IsCancellationRequested;

        public CtrlCCancellationSource()
        {
            _handler = (_, e) =>
            {
                e.Cancel = true;
                SolicitarCancelamento();
            };
            Console.CancelKeyPress += _handler;
        }

        /// <summary>Exposto separadamente do handler do Console para ser testável sem precisar simular um Ctrl+C real.</summary>
        public void SolicitarCancelamento()
        {
            if (!_cts.IsCancellationRequested) _cts.Cancel();
        }

        public void Dispose()
        {
            Console.CancelKeyPress -= _handler;
            _cts.Dispose();
        }
    }
}
