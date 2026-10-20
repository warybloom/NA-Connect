using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Correção 17/09/2026 (achado real: CHS001 tem a pasta raiz "09 - DEVELOPMENT", nome que não
    /// batia com nenhum dos 4 candidatos nominais fixos configurados). Cobre a extração pura do
    /// código numérico usada por AutodeskApiService.FindFolderIdByNumericPrefixPathAsync — nunca faz
    /// chamada ACC/SQL.
    /// </summary>
    public class ExtrairPrefixoNumericoDaPastaTests
    {
        [Theory]
        [InlineData("09 - DEVELOPMENT", "09")]
        [InlineData("09 - DESENVOLVIMENTO", "09")]
        [InlineData("09 - DEVELOPING", "09")]
        [InlineData("09 - MODEL DEVELOPING", "09")]
        [InlineData("09 - ENGINEERING DOCUMENTS", "09")]
        [InlineData("09.14 - GESTÃO", "09.14")]
        [InlineData("09.14 - MANAGEMENT", "09.14")]
        [InlineData("09.14 - QUALQUER NOME NOVO NUNCA VISTO ANTES", "09.14")]
        public void ExtraiCodigoNumericoIndependenteDoTextoDescritivo(string nomePasta, string prefixoEsperado)
        {
            Assert.Equal(prefixoEsperado, AutodeskApiService.ExtrairPrefixoNumericoDaPasta(nomePasta));
        }

        [Theory]
        [InlineData("09 -   DEVELOPMENT")] // espaçamento interno variável — ainda extrai "09"
        [InlineData("09-DEVELOPMENT")]
        [InlineData("  09 - DEVELOPMENT")]
        public void ToleraVariacaoDeEspacamento(string nomePasta)
        {
            Assert.Equal("09", AutodeskApiService.ExtrairPrefixoNumericoDaPasta(nomePasta));
        }

        [Theory]
        [InlineData("Project Files")]
        [InlineData("ENGINEERING")]
        [InlineData("")]
        [InlineData(null)]
        public void PastaSemCodigoNumerico_RetornaNulo(string nomePasta)
        {
            Assert.Null(AutodeskApiService.ExtrairPrefixoNumericoDaPasta(nomePasta));
        }

        [Fact]
        public void PrefixosDiferentesNuncaCorrespondemEntreSi()
        {
            // "09" (raiz) nunca deve casar com "09.14" (subpasta) nem vice-versa.
            Assert.NotEqual(
                AutodeskApiService.ExtrairPrefixoNumericoDaPasta("09 - DEVELOPMENT"),
                AutodeskApiService.ExtrairPrefixoNumericoDaPasta("09.14 - GESTÃO"));
        }
    }
}
