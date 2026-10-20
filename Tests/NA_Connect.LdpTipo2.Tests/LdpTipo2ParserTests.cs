using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Models;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.LdpTipo2.Tests.TestHelpers;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Cobre a regra do "X" (aplicabilidade), as combinações de gates aplicáveis, identidade documental
    /// (N&amp;A obrigatório / Client opcional) e a detecção de duplicidade de N&amp;A/Client dentro da
    /// própria LDP — itens 2/3/4/6/7 do plano de finalização de LDP Tipo 2 / Cronograma Tipo 2.
    /// </summary>
    public class LdpTipo2ParserTests
    {
        private static async Task<LdpTipo2Resultado> ProcessarAsync(ExcelPackage pkg)
        {
            using var ms = new MemoryStream(pkg.GetAsByteArray());

            var parser = new LdpTipo2Parser(NullLogger<LdpTipo2Parser>.Instance);
            return await parser.ProcessarAsync(ms, "PROJ-TESTE", "teste.xlsx", System.DateTime.UtcNow);
        }

        [Theory]
        [InlineData("X", true)]
        [InlineData("x", true)]
        [InlineData(" X ", true)]
        [InlineData("", false)]
        [InlineData("SIM", false)]
        public async Task Gate30_ValorDeAplicabilidade_InterpretadoConformeRegra(string valor, bool esperadoAtivo)
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            var aplicabilidade = new Dictionary<byte, string> { [30] = valor };
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Título", aplicabilidade);

            var resultado = await ProcessarAsync(pkg);

            Assert.Empty(resultado.Erros);
            var doc = Assert.Single(resultado.Documentos);
            var gate30 = doc.Gates.Single(g => g.GatePercentual == 30);
            Assert.Equal(esperadoAtivo, gate30.GateAtivo);

            if (!esperadoAtivo && valor.Length > 0)
            {
                Assert.Contains(resultado.Avisos, a => a.Contains("valor inesperado no indicador de aplicabilidade"));
            }
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [InlineData(false, false, false)]
        public async Task Documento_CombinacoesDeGatesAplicaveis_RefletemExatamenteOsXsMarcados(bool g30, bool g60, bool g90)
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            var aplicabilidade = new Dictionary<byte, string>();
            if (g30) aplicabilidade[30] = "X";
            if (g60) aplicabilidade[60] = "X";
            if (g90) aplicabilidade[90] = "X";

            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", null, "Título", aplicabilidade);

            var resultado = await ProcessarAsync(pkg);

            Assert.Empty(resultado.Erros);
            var doc = Assert.Single(resultado.Documentos);

            // Sempre 3 objetos de gate (30/60/90) por documento — só GateAtivo muda. Nunca criar
            // artificialmente um gate a mais nem a menos.
            Assert.Equal(3, doc.Gates.Count);
            Assert.Equal(g30, doc.Gates.Single(g => g.GatePercentual == 30).GateAtivo);
            Assert.Equal(g60, doc.Gates.Single(g => g.GatePercentual == 60).GateAtivo);
            Assert.Equal(g90, doc.Gates.Single(g => g.GatePercentual == 90).GateAtivo);
        }

        [Fact]
        public async Task Documento_SemNumeroNA_LinhaIgnoradaComAviso()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, numeroNa: "", numeroCliente: "CLI-001", titulo: "Título", new Dictionary<byte, string> { [30] = "X" });

            var resultado = await ProcessarAsync(pkg);

            Assert.Empty(resultado.Documentos);
            Assert.Contains(resultado.Avisos, a => a.Contains("número N&A vazio"));
        }

        [Fact]
        public async Task Documento_SemClientDocumentNumber_ProcessaNormalmente()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", numeroCliente: null, titulo: "Título", new Dictionary<byte, string> { [60] = "X" });

            var resultado = await ProcessarAsync(pkg);

            Assert.Empty(resultado.Erros);
            var doc = Assert.Single(resultado.Documentos);
            Assert.True(string.IsNullOrEmpty(doc.NumeroDocumentoCliente));
        }

        [Fact]
        public async Task NumeroNADuplicado_EntreLinhas_GeraAvisoEstruturado()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Título A", new Dictionary<byte, string> { [30] = "X" });
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 9, "DOC-001", "CLI-002", "Título B", new Dictionary<byte, string> { [60] = "X" });

            var resultado = await ProcessarAsync(pkg);

            Assert.Equal(2, resultado.Documentos.Count);
            Assert.Contains(resultado.Avisos, a => a.Contains("N&A_DUPLICADO") && a.Contains("DOC-001"));
        }

        [Fact]
        public async Task NumeroClienteDuplicado_EntreLinhas_GeraAvisoEstruturado()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-999", "Título A", new Dictionary<byte, string> { [30] = "X" });
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 9, "DOC-002", "CLI-999", "Título B", new Dictionary<byte, string> { [60] = "X" });

            var resultado = await ProcessarAsync(pkg);

            Assert.Equal(2, resultado.Documentos.Count);
            Assert.Contains(resultado.Avisos, a => a.Contains("CLIENT_DUPLICADO") && a.Contains("CLI-999"));
        }

        [Fact]
        public async Task SemDuplicidade_NenhumAvisoDeDuplicidadeGerado()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Título A", new Dictionary<byte, string> { [30] = "X" });
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 9, "DOC-002", "CLI-002", "Título B", new Dictionary<byte, string> { [60] = "X" });

            var resultado = await ProcessarAsync(pkg);

            Assert.DoesNotContain(resultado.Avisos, a => a.Contains("_DUPLICADO"));
        }
    }
}
