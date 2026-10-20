using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using APS.WebApp.Models;
using APS.WebApp.Services.Qualidade;

namespace NA_Connect.Qualidade.Tests
{
    public class QualidadeRepositoryTests
    {
        private static QualidadeRepository NovoRepositorio()
        {
            // Connection string ausente deliberadamente — comprova que PersistirTabelaAsync nunca chega
            // a abrir uma SqlConnection quando o preparo não permite persistir (seção 13: falha de
            // preflight nunca executa DELETE).
            var configuration = new ConfigurationBuilder().Build();
            return new QualidadeRepository(configuration, NullLogger<QualidadeRepository>.Instance);
        }

        [Fact]
        public async Task PersistirTabelaAsync_PreparadoNulo_LancaExcecaoSemAbrirConexao()
        {
            var repositorio = NovoRepositorio();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repositorio.PersistirTabelaAsync("PROJ-1", null));
        }

        [Fact]
        public async Task PersistirTabelaAsync_PreparadoComSucessoFalse_LancaExcecaoSemAbrirConexao()
        {
            var repositorio = NovoRepositorio();
            var preparado = new ResultadoTabelaQualidade { Sucesso = false, Aba = "RNC", TabelaDestino = QualidadeTabelas.NcrNonconformityReport };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repositorio.PersistirTabelaAsync("PROJ-1", preparado));
        }
    }
}
