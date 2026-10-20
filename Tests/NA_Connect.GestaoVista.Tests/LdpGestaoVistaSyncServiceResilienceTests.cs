using System.IO;
using System.Text;
using System.Threading.Tasks;
using APS.WebApp.Services.GestaoVista;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.GestaoVista.Tests.TestHelpers;

namespace NA_Connect.GestaoVista.Tests
{
    /// <summary>
    /// Prova a regra crítica do pedido (seção 14/19): uma falha na Gestão à Vista NUNCA pode propagar
    /// como exceção para o chamador (o Worker) — sempre volta um GestaoVistaSyncResultado com
    /// Sucesso=false/Erros preenchidos. Sem banco real: connection string deliberadamente vazia/ausente
    /// (mesmo padrão já usado por PlanningRepositoryTests) garante que nenhum servidor é contatado — a
    /// falha vem de SqlConnection.OpenAsync() com string vazia, não de rede.
    /// </summary>
    public class LdpGestaoVistaSyncServiceResilienceTests
    {
        private static LdpGestaoVistaSyncService NovoServico()
        {
            var configuration = new ConfigurationBuilder().Build(); // sem ConnectionStrings:LocalConnection
            var extractor = new GestaoVistaExtractor(NullLogger<GestaoVistaExtractor>.Instance);
            var repository = new GestaoVistaRepository(NullLogger<GestaoVistaRepository>.Instance);
            return new LdpGestaoVistaSyncService(configuration, NullLogger<LdpGestaoVistaSyncService>.Instance, extractor, repository);
        }

        [Fact]
        public async Task FalhaAoAbrirConexao_NuncaLancaExcecao_RetornaResultadoComErro()
        {
            var servico = NovoServico();

            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            // Visible aqui só porque o EPPlus recusa salvar um workbook sem NENHUMA aba visível — a
            // visibilidade em si é irrelevante para este teste (que só cobre resiliência de conexão).
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, OfficeOpenXml.eWorkSheetHidden.Visible);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");
            using var ms = new MemoryStream(pkg.GetAsByteArray());

            var resultado = await servico.SincronizarAsync("PROJ-TESTE", ms, "teste.xlsx", System.DateTime.UtcNow);

            Assert.False(resultado.Sucesso);
            Assert.NotEmpty(resultado.Erros);
        }

        [Fact]
        public async Task WorkbookIlegivel_NuncaLancaExcecao_RetornaErroSemTentarConectarAoBanco()
        {
            var servico = NovoServico();

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes("isto não é um arquivo .xlsx válido"));

            var resultado = await servico.SincronizarAsync("PROJ-TESTE", ms, "corrompido.xlsx", System.DateTime.UtcNow);

            Assert.False(resultado.Sucesso);
            Assert.NotEmpty(resultado.Erros);
        }

        [Fact]
        public async Task ProjectIdAccVazio_NuncaLancaExcecao_RetornaErroControlado()
        {
            var servico = NovoServico();
            using var ms = new MemoryStream();

            var resultado = await servico.SincronizarAsync("", ms, "teste.xlsx", System.DateTime.UtcNow);

            Assert.False(resultado.Sucesso);
            Assert.NotEmpty(resultado.Erros);
        }
    }
}
