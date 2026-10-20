using System;
using APS.WebApp.Services.Qualidade;
using NA_Connect.Qualidade.Tests.TestHelpers;

namespace NA_Connect.Qualidade.Tests
{
    public class QualidadeParsersTests
    {
        [Fact]
        public void TentarParseData_DateTimeRealDoExcel_RetornaValorExato()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "Aba");
            var data = new DateTime(2026, 3, 15);
            ws.Cells[1, 1].Value = data;

            bool ok = QualidadeParsers.TentarParseData(ws.Cells[1, 1], out DateTime? valor, out string erro);

            Assert.True(ok);
            Assert.Null(erro);
            Assert.Equal(data, valor);
        }

        [Fact]
        public void TentarParseData_TextoPtBrValido_RetornaValorReconhecido()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "Aba");
            ws.Cells[1, 1].Value = "15/03/2026";

            bool ok = QualidadeParsers.TentarParseData(ws.Cells[1, 1], out DateTime? valor, out string erro);

            Assert.True(ok);
            Assert.Null(erro);
            Assert.Equal(new DateTime(2026, 3, 15), valor);
        }

        [Fact]
        public void TentarParseData_TextoInvalido_RetornaErroSemDateTimeMinValue()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "Aba");
            ws.Cells[1, 1].Value = "não é uma data";

            bool ok = QualidadeParsers.TentarParseData(ws.Cells[1, 1], out DateTime? valor, out string erro);

            Assert.False(ok);
            Assert.Null(valor);
            Assert.NotEqual(DateTime.MinValue, valor.GetValueOrDefault(DateTime.MaxValue));
            Assert.False(string.IsNullOrWhiteSpace(erro));
        }

        [Fact]
        public void TentarParseData_CelulaVazia_RetornaSucessoComValorNulo()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "Aba");

            bool ok = QualidadeParsers.TentarParseData(ws.Cells[1, 1], out DateTime? valor, out string erro);

            Assert.True(ok);
            Assert.Null(valor);
            Assert.Null(erro);
        }

        [Fact]
        public void TentarParseInteiro_Vazio_RetornaSucessoComValorNulo()
        {
            bool ok = QualidadeParsers.TentarParseInteiro("   ", out int? valor, out string erro);

            Assert.True(ok);
            Assert.Null(valor);
            Assert.Null(erro);
        }

        [Fact]
        public void TentarParseInteiro_Invalido_RetornaErroSemArredondar()
        {
            bool ok = QualidadeParsers.TentarParseInteiro("3,5", out int? valor, out string erro);

            Assert.False(ok);
            Assert.Null(valor);
            Assert.False(string.IsNullOrWhiteSpace(erro));
        }

        [Fact]
        public void TentarParseInteiro_ValorValido_RetornaInteiroExato()
        {
            bool ok = QualidadeParsers.TentarParseInteiro("42", out int? valor, out string erro);

            Assert.True(ok);
            Assert.Equal(42, valor);
            Assert.Null(erro);
        }
    }
}
