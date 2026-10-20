using APS.WebApp.Services;
using OfficeOpenXml;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Correção 16/09/2026 (fase 3 — Etapa 05 única, sem variante por Tipo_LDP): a REGRA de
    /// Status_In_Flow é a mesma para qualquer Tipo_LDP; só o ACESSO FÍSICO à coluna "STATUS IN THE FLOW"
    /// pode variar, porque a LDP Tipo2 tem colunas extras de gate 30/60/90 que deslocam esse cabeçalho
    /// para uma posição diferente da coluna 17 fixa do layout Tipo1 original.
    /// LdpUpdateService.LocalizarColunaStatusInFlow nunca decide nada de negócio — só localiza a célula.
    /// Testado aqui como função pura, sem ACC/SQL (mesmo padrão de NormalizarNomePasta/ResolverPastaPrincipal).
    /// </summary>
    public class LocalizarColunaStatusInFlowTests
    {
        static LocalizarColunaStatusInFlowTests()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private static ExcelWorksheet NovaPlanilha(int linhaCabecalho, params (int Coluna, string Texto)[] cabecalhos)
        {
            var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("LDP");
            foreach (var (coluna, texto) in cabecalhos)
            {
                ws.Cells[linhaCabecalho, coluna].Value = texto;
            }
            return ws;
        }

        [Fact]
        public void CabecalhoNaPosicaoTipo1_ColunaDezessete_EhEncontrado()
        {
            var ws = NovaPlanilha(7, (17, "STATUS IN THE FLOW"));
            Assert.Equal(17, LdpUpdateService.LocalizarColunaStatusInFlow(ws, 7, colunaPadrao: 17));
        }

        [Fact]
        public void CabecalhoDeslocadoPorColunasExtrasDeGate_EhEncontradoEmOutraPosicao()
        {
            // Simula o LDP Tipo2 real: blocos de gate 30/60/90 empurram "STATUS IN THE FLOW" para uma
            // coluna bem depois da 17 fixa do Tipo1.
            var ws = NovaPlanilha(7, (44, "STATUS IN THE FLOW"));
            Assert.Equal(44, LdpUpdateService.LocalizarColunaStatusInFlow(ws, 7, colunaPadrao: 17));
        }

        [Fact]
        public void CabecalhoComEspacamentoIrregular_AindaEhEncontrado()
        {
            var ws = NovaPlanilha(7, (30, "STATUS   IN THE   FLOW"));
            Assert.Equal(30, LdpUpdateService.LocalizarColunaStatusInFlow(ws, 7, colunaPadrao: 17));
        }

        [Fact]
        public void CabecalhoAusente_CaiNoValorPadrao()
        {
            var ws = NovaPlanilha(7, (4, "N&A DOCUMENT NUMBER"), (5, "CLIENT DOCUMENT NUMBER"));
            Assert.Equal(17, LdpUpdateService.LocalizarColunaStatusInFlow(ws, 7, colunaPadrao: 17));
        }

        [Fact]
        public void CabecalhoApareceDuasVezes_AmbiguoCaiNoValorPadrao()
        {
            var ws = NovaPlanilha(7, (17, "STATUS IN THE FLOW"), (44, "STATUS IN THE FLOW"));
            Assert.Equal(17, LdpUpdateService.LocalizarColunaStatusInFlow(ws, 7, colunaPadrao: 17));
        }

        [Fact]
        public void LinhaDeCabecalhoInvalida_NuncaLancaExcecao_CaiNoValorPadrao()
        {
            var ws = NovaPlanilha(7, (17, "STATUS IN THE FLOW"));
            Assert.Equal(17, LdpUpdateService.LocalizarColunaStatusInFlow(ws, linhaCabecalho: 0, colunaPadrao: 17));
            Assert.Equal(17, LdpUpdateService.LocalizarColunaStatusInFlow(ws, linhaCabecalho: 999, colunaPadrao: 17));
        }

        [Fact]
        public void PlanilhaNula_NuncaLancaExcecao_CaiNoValorPadrao()
        {
            Assert.Equal(17, LdpUpdateService.LocalizarColunaStatusInFlow(null, 7, colunaPadrao: 17));
        }
    }
}
