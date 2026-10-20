using APS.WebApp.Services;
using OfficeOpenXml;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Correção 17/09/2026 (Etapa 01 universal — auditoria de X8/Y8): LdpHeaderLookup é o utilitário
    /// compartilhado de acesso físico por cabeçalho (usado pela Etapa 05 para "STATUS IN THE FLOW" e
    /// pela Etapa 01 para "CR"/"CR-OS"). Estes testes cobrem especificamente o cenário CR/CR-OS: LDP
    /// Tipo1 com CR/CR-OS nas colunas fixas originais (24/25 — X8/Y8) e LDP Tipo2 com essas mesmas
    /// colunas deslocadas por blocos de gate 30/60/90 (cenário real confirmado no CHS001).
    /// </summary>
    public class LdpHeaderLookupTests
    {
        static LdpHeaderLookupTests()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private static ExcelWorksheet NovaPlanilhaLdp(int linhaCabecalho, params (int Coluna, string Texto)[] cabecalhos)
        {
            var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("LDP");
            foreach (var (coluna, texto) in cabecalhos) ws.Cells[linhaCabecalho, coluna].Value = texto;
            return ws;
        }

        [Fact]
        public void CrECrOs_NaPosicaoTipo1_ColunasVinteEQuatroEVinteECinco_SaoEncontradas()
        {
            var ws = NovaPlanilhaLdp(7, (24, "CR"), (25, "CR-OS"));
            Assert.Equal(24, LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR" }, colunaPadrao: 24));
            Assert.Equal(25, LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR-OS", "CR OS", "CROS" }, colunaPadrao: 25));
        }

        [Fact]
        public void CrECrOs_DeslocadosPorBlocosDeGateNoLdpTipo2_SaoEncontradosEmOutraPosicao()
        {
            // Cenário real do CHS001: blocos de gate 30/60/90 empurram colunas subsequentes bem além
            // da posição fixa 24/25 do layout Tipo1.
            var ws = NovaPlanilhaLdp(7, (71, "CR"), (72, "CR-OS"));
            Assert.Equal(71, LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR" }, colunaPadrao: 24));
            Assert.Equal(72, LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR-OS", "CR OS", "CROS" }, colunaPadrao: 25));
        }

        [Fact]
        public void CrOsComGrafiaAlternativa_CrComEspaco_AindaEhEncontrado()
        {
            var ws = NovaPlanilhaLdp(7, (30, "CR OS"));
            Assert.Equal(30, LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR-OS", "CR OS", "CROS" }, colunaPadrao: 25));
        }

        [Fact]
        public void CabecalhoAusente_CaiNaColunaFixaOriginal_PreservaComportamentoTipo1()
        {
            var ws = NovaPlanilhaLdp(7, (4, "N&A DOCUMENT NUMBER"));
            Assert.Equal(24, LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR" }, colunaPadrao: 24));
        }

        [Fact]
        public void CrNuncaConfundidoComCrOs_AliasesSaoDistintos()
        {
            var ws = NovaPlanilhaLdp(7, (24, "CR"), (25, "CR-OS"));
            int colCr = LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR" }, colunaPadrao: 24);
            int colCrOs = LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, 7, new[] { "CR-OS", "CR OS", "CROS" }, colunaPadrao: 25);
            Assert.NotEqual(colCr, colCrOs);
        }
    }
}
