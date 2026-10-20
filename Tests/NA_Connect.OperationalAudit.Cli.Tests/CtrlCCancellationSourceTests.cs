using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seção 14 e teste 10: Ctrl+C cooperativo.</summary>
    public class CtrlCCancellationSourceTests
    {
        [Fact]
        public void AntesDeCancelar_TokenNaoEstaCancelado()
        {
            using var origem = new CtrlCCancellationSource();

            Assert.False(origem.CancellationRequested);
            Assert.False(origem.Token.IsCancellationRequested);
        }

        [Fact]
        public void SolicitarCancelamento_CancelaOToken()
        {
            using var origem = new CtrlCCancellationSource();

            origem.SolicitarCancelamento();

            Assert.True(origem.CancellationRequested);
            Assert.True(origem.Token.IsCancellationRequested);
        }

        [Fact]
        public void SolicitarCancelamentoDuasVezes_NuncaLanca()
        {
            using var origem = new CtrlCCancellationSource();

            origem.SolicitarCancelamento();
            var excecao = Record.Exception(() => origem.SolicitarCancelamento());

            Assert.Null(excecao);
        }

        [Fact]
        public void Dispose_NuncaLanca()
        {
            var origem = new CtrlCCancellationSource();
            origem.SolicitarCancelamento();

            var excecao = Record.Exception(origem.Dispose);

            Assert.Null(excecao);
        }
    }
}
