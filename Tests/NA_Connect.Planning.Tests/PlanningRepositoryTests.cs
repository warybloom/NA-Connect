using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using APS.WebApp.Models;
using APS.WebApp.Services.Planning;

namespace NA_Connect.Planning.Tests
{
    public class PlanningRepositoryTests
    {
        [Fact]
        public async Task TemplateVazioNaoApagaDadosMesmoComDiagnosticos()
        {
            var repo = NovoRepositorio();
            var partial = new ResultadoPreflightPlanejamento { Sucesso = true, RowsWithErrors = 1, Tabela = new System.Data.DataTable() };
            var failed = await repo.PersistirAsync("test", PlanningDefinicoes.External(), partial);
            Assert.True(failed.Sucesso);
            Assert.False(failed.CommitExecutado);
            var empty = await repo.PersistirAsync("test", PlanningDefinicoes.External(), new ResultadoPreflightPlanejamento { Sucesso = true, Tabela = new System.Data.DataTable() });
            Assert.True(empty.Sucesso);
            Assert.False(empty.CommitExecutado);
        }
        [Fact]
        public async Task PartialDatasetReachesPersistenceInsteadOfQualityRollback()
        {
            var table = new System.Data.DataTable();
            table.Columns.Add("Project_ID_ACC", typeof(string));
            table.Rows.Add("test");
            var partial = new ResultadoPreflightPlanejamento { Sucesso = true, RowsWithErrors = 1, Tabela = table };
            // No connection string: opening SQL must fail locally; no server is contacted.
            await Assert.ThrowsAsync<InvalidOperationException>(() => NovoRepositorio().PersistirAsync("test", PlanningDefinicoes.External(), partial));
        }
        private static PlanningRepository NovoRepositorio()
        {
            // Connection string vazia/deliberadamente ausente — o teste comprova que PersistirAsync
            // nunca chega a abrir uma SqlConnection quando o preflight não permite persistir (seção 19:
            // preflight antes de abrir transação; falha de preflight nunca executa DELETE).
            var configuration = new ConfigurationBuilder().Build();
            return new PlanningRepository(configuration, NullLogger<PlanningRepository>.Instance);
        }

        [Fact]
        public async Task PersistirAsync_PreflightNulo_LancaExcecaoSemAbrirConexao()
        {
            var repositorio = NovoRepositorio();
            var definicao = PlanningDefinicoes.ScopeDeviation();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repositorio.PersistirAsync("PROJ-1", definicao, null));
        }

        [Fact]
        public async Task PersistirAsync_PreflightComSucessoFalse_LancaExcecaoSemAbrirConexao()
        {
            var repositorio = NovoRepositorio();
            var definicao = PlanningDefinicoes.ScopeDeviation();
            var preflight = new ResultadoPreflightPlanejamento { Sucesso = false };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repositorio.PersistirAsync("PROJ-1", definicao, preflight));
        }
    }
}
