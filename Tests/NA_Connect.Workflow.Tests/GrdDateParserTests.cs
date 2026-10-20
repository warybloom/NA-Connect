using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// GrdDateParser estende (nunca substitui) DateTime.TryParse para a célula G4 da planilha GRD, que às
    /// vezes traz o mês como abreviação PT/EN em vez de número (ex.: "25/Agu/2026").
    /// </summary>
    public class GrdDateParserTests
    {
        [Theory]
        [InlineData("25/Agu/2026", 2026, 8, 25)]
        [InlineData("25/ago/2026", 2026, 8, 25)]
        [InlineData("25/AGO/2026", 2026, 8, 25)]
        [InlineData("25/Aug/2026", 2026, 8, 25)]
        [InlineData("25/aug/2026", 2026, 8, 25)]
        [InlineData("01/Jan/2026", 2026, 1, 1)]
        [InlineData("28/Fev/2026", 2026, 2, 28)]
        [InlineData("28/Feb/2026", 2026, 2, 28)]
        [InlineData("15/Mar/2026", 2026, 3, 15)]
        [InlineData("15/Abr/2026", 2026, 4, 15)]
        [InlineData("15/Apr/2026", 2026, 4, 15)]
        [InlineData("10/Mai/2026", 2026, 5, 10)]
        [InlineData("10/May/2026", 2026, 5, 10)]
        [InlineData("05/Jun/2026", 2026, 6, 5)]
        [InlineData("05/Jul/2026", 2026, 7, 5)]
        [InlineData("12/Set/2026", 2026, 9, 12)]
        [InlineData("12/Sep/2026", 2026, 9, 12)]
        [InlineData("20/Out/2026", 2026, 10, 20)]
        [InlineData("20/Oct/2026", 2026, 10, 20)]
        [InlineData("30/Nov/2026", 2026, 11, 30)]
        [InlineData("31/Dez/2026", 2026, 12, 31)]
        [InlineData("31/Dec/2026", 2026, 12, 31)]
        public void MesAbreviadoPtOuEn_EhReconhecido(string bruto, int ano, int mes, int dia)
        {
            Assert.True(GrdDateParser.TentarParse(bruto, out var data));
            Assert.Equal(new DateTime(ano, mes, dia), data);
        }

        [Theory]
        [InlineData("25/08/2026", 2026, 8, 25)]
        [InlineData("2026-08-25", 2026, 8, 25)]
        public void FormatosNumericosExistentesContinuamFuncionando(string bruto, int ano, int mes, int dia)
        {
            Assert.True(GrdDateParser.TentarParse(bruto, out var data));
            Assert.Equal(new DateTime(ano, mes, dia), data.Date);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("não é uma data")]
        [InlineData("32/Agu/2026")]
        [InlineData("25/Xyz/2026")]
        public void ValoresInvalidosContinuamSendoRejeitados(string? bruto)
        {
            Assert.False(GrdDateParser.TentarParse(bruto, out _));
        }

        [Fact]
        public void CasoRealDoLog_25AguDe2026_ResultaEm20260825()
        {
            Assert.True(GrdDateParser.TentarParse("25/Agu/2026", out var data));
            Assert.Equal(new DateTime(2026, 8, 25), data);
        }
    }
}
