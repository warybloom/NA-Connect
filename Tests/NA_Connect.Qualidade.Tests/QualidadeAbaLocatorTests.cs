using OfficeOpenXml;
using APS.WebApp.Services.Qualidade;
using NA_Connect.Qualidade.Tests.TestHelpers;

namespace NA_Connect.Qualidade.Tests
{
    public class QualidadeAbaLocatorTests
    {
        private static ExcelPackage CriarPacoteComAbas(params string[] nomesDeAba)
        {
            var package = WorkbookHelper.NovoPacote();
            foreach (var nome in nomesDeAba)
            {
                WorkbookHelper.AdicionarAba(package, nome);
            }
            return package;
        }

        [Fact]
        public void LocalizarTresAbas_ComAsTresAbasPresentes_NaoLancaExcecao()
        {
            using var package = CriarPacoteComAbas("BASE LANÇAMENTO", "RNC", "DESVIO DE QUALIDADE");

            var abas = QualidadeAbaLocator.LocalizarTresAbas(package);

            Assert.NotNull(abas.BaseLancamento);
            Assert.NotNull(abas.Rnc);
            Assert.NotNull(abas.DesvioQualidade);
        }

        [Fact]
        public void LocalizarTresAbas_ToleraCaixaEspacosEAcentos()
        {
            using var package = CriarPacoteComAbas("  base lançamento ", "rnc", "Desvio de Qualidade");

            var abas = QualidadeAbaLocator.LocalizarTresAbas(package);

            Assert.NotNull(abas.BaseLancamento);
            Assert.NotNull(abas.Rnc);
            Assert.NotNull(abas.DesvioQualidade);
        }

        [Fact]
        public void LocalizarTresAbas_SemAbaBaseLancamento_LancaAbaAusenteComCodigoCorreto()
        {
            using var package = CriarPacoteComAbas("RNC", "DESVIO DE QUALIDADE");

            var ex = Assert.Throws<QualidadeLayoutException>(() => QualidadeAbaLocator.LocalizarTresAbas(package));

            Assert.Equal(QualidadeAbaLocator.CodigoAbaAusente, ex.CodigoResultado);
            Assert.Contains("BASE LANÇAMENTO", ex.Message);
        }

        [Fact]
        public void LocalizarTresAbas_SemAbaRnc_LancaAbaAusenteComCodigoCorreto()
        {
            using var package = CriarPacoteComAbas("BASE LANÇAMENTO", "DESVIO DE QUALIDADE");

            var ex = Assert.Throws<QualidadeLayoutException>(() => QualidadeAbaLocator.LocalizarTresAbas(package));

            Assert.Equal(QualidadeAbaLocator.CodigoAbaAusente, ex.CodigoResultado);
            Assert.Contains("RNC", ex.Message);
        }

        [Fact]
        public void LocalizarTresAbas_SemAbaDesvioQualidade_LancaAbaAusenteComCodigoCorreto()
        {
            using var package = CriarPacoteComAbas("BASE LANÇAMENTO", "RNC");

            var ex = Assert.Throws<QualidadeLayoutException>(() => QualidadeAbaLocator.LocalizarTresAbas(package));

            Assert.Equal(QualidadeAbaLocator.CodigoAbaAusente, ex.CodigoResultado);
            Assert.Contains("DESVIO DE QUALIDADE", ex.Message);
        }

        [Fact]
        public void LocalizarTresAbas_NuncaAceitaAbaParecidaComoFallback()
        {
            // "RELATORIO RNC" não é igual a "RNC" após normalização — nunca deve casar por Contains.
            using var package = CriarPacoteComAbas("BASE LANÇAMENTO", "RELATORIO RNC", "DESVIO DE QUALIDADE");

            var ex = Assert.Throws<QualidadeLayoutException>(() => QualidadeAbaLocator.LocalizarTresAbas(package));

            Assert.Equal(QualidadeAbaLocator.CodigoAbaAusente, ex.CodigoResultado);
        }
    }
}
