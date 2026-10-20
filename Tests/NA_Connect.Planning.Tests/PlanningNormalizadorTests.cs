using APS.WebApp.Services.Planning;

namespace NA_Connect.Planning.Tests
{
    public class PlanningNormalizadorTests
    {
        [Theory]
        [InlineData("-")]
        [InlineData("--")]
        [InlineData("N/A")]
        [InlineData("n/a")]
        [InlineData("NA")]
        [InlineData("N.D.")]
        [InlineData("N/D")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EhValorAusente_TracosPlaceholdersEVazio_SaoConsideradosAusentes(string valor)
        {
            Assert.True(PlanningNormalizador.EhValorAusente(valor));
        }

        [Theory]
        [InlineData("Item 1")]
        [InlineData("123")]
        [InlineData("Aprovado")]
        public void EhValorAusente_ValorReal_NaoEhAusente(string valor)
        {
            Assert.False(PlanningNormalizador.EhValorAusente(valor));
        }

        [Fact]
        public void NormalizarCabecalho_PreservaAcentoESomenteNormalizaEspacos()
        {
            string a = PlanningNormalizador.NormalizarCabecalho("DESCRIÇÃO\n");
            string b = PlanningNormalizador.NormalizarCabecalho("descricao");

            Assert.NotEqual(a, b);
            Assert.Equal(a, PlanningNormalizador.NormalizarCabecalho("  descrição  "));
        }

        [Fact]
        public void NormalizarCabecalho_NumeroDoDesvioNaoColideComDesvioInterno()
        {
            string a = PlanningNormalizador.NormalizarCabecalho("Nº DO DESVIO");
            string b = PlanningNormalizador.NormalizarCabecalho("DESVIO INTERNO");

            Assert.NotEqual(a, b);
        }
    }
}
