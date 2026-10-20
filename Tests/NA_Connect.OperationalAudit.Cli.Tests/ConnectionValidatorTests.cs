using System.Data;
using System.Threading.Tasks;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    public class ConnectionValidatorTests
    {
        [Fact]
        public async Task ConnectionStringAusente_FalhaSemTentarConectar()
        {
            var executor = new FakeCliSqlQueryExecutor();

            var resultado = await ConnectionValidator.ValidarConexaoAsync("Origem", null, executor);

            Assert.False(resultado.Sucesso);
            Assert.Empty(executor.SqlExecutados);
        }

        [Fact]
        public async Task ConnectionStringPresente_TestaComSelect1_NuncaEscreve()
        {
            var executor = new FakeCliSqlQueryExecutor();

            var resultado = await ConnectionValidator.ValidarConexaoAsync("Origem", "Server=fake;Database=fake;", executor);

            Assert.True(resultado.Sucesso);
            Assert.Single(executor.SqlExecutados);
            Assert.Contains("SELECT", executor.SqlExecutados[0]);
            Assert.DoesNotContain("SenhaSecreta", resultado.ResumoSeguro ?? string.Empty);
        }

        [Fact]
        public async Task FalhaDeConexao_NuncaLanca()
        {
            var executor = new FakeCliSqlQueryExecutor { LancarExcecao = true };

            var resultado = await ConnectionValidator.ValidarConexaoAsync("Origem", "Server=fake;Database=fake;", executor);

            Assert.False(resultado.Sucesso);
            Assert.NotNull(resultado.Erro);
        }

        [Fact]
        public async Task AccProbe_SucessoSimulado_NuncaFazGetReal()
        {
            var probe = new FakeAccConnectivityProbe { DeveTerSucesso = true };

            var resultado = await ConnectionValidator.ValidarAccAsync(probe);

            Assert.True(resultado.Sucesso);
        }

        [Fact]
        public async Task AccProbe_FalhaSimulada_NuncaLanca()
        {
            var probe = new FakeAccConnectivityProbe { DeveTerSucesso = false };

            var resultado = await ConnectionValidator.ValidarAccAsync(probe);

            Assert.False(resultado.Sucesso);
        }
    }
}
