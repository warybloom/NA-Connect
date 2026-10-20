using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a correção mínima do canário Tipo 2 (projeto b.9e5d47da-e37f-452f-9ef4-7df992f27033):
    /// AccFolderNameComparer deve tolerar whitespace interno variável ("09 - ENGINEERING DOCUMENTS"
    /// vs "09 -   ENGINEERING DOCUMENTS") sem virar fuzzy matching — prefixo, hífen e palavra
    /// continuam tendo que bater exatamente.
    /// </summary>
    public class AccFolderNameComparerTests
    {
        [Theory]
        [InlineData("09 - ENGINEERING DOCUMENTS", "09 -   ENGINEERING DOCUMENTS")]
        [InlineData("09.14 - MANAGEMENT", "09.14 - MANAGEMENT")]
        [InlineData("09 - ENGINEERING DOCUMENTS", " 09 - ENGINEERING DOCUMENTS ")]
        [InlineData("09 - ENGINEERING DOCUMENTS", "09 -\tENGINEERING DOCUMENTS")]
        public void AreEquivalent_ToleraEspacamentoVariavel(string a, string b)
        {
            Assert.True(AccFolderNameComparer.AreEquivalent(a, b));
        }

        [Theory]
        [InlineData("09 - ENGINEERING DOCUMENTS", "08 - ENGINEERING DOCUMENTS")]
        [InlineData("09 - ENGINEERING DOCUMENTS", "09 - ENGINEERING DOCUMENT")]
        [InlineData("09 - ENGINEERING DOCUMENTS", "09 - OTHER DOCUMENTS")]
        public void AreEquivalent_NaoFazFuzzyMatching(string a, string b)
        {
            Assert.False(AccFolderNameComparer.AreEquivalent(a, b));
        }
    }
}
