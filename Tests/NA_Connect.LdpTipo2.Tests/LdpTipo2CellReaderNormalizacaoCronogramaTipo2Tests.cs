using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using APS.WebApp.Services.LdpTipo2;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Hotfix de normalização Cronograma Tipo 2 -&gt; LDP Tipo 2: cobre APENAS os dois problemas de dados
    /// reais reportados no boundary central de leitura de célula (LdpTipo2CellReader) — escala x100 do
    /// "WBS (%)" e prefixo de dia da semana nas datas. Casos mínimos, sem infraestrutura nova.
    /// </summary>
    public class LdpTipo2CellReaderNormalizacaoCronogramaTipo2Tests
    {
        private static ExcelRange CelulaComValor(object valor)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add("T");
            ws.Cells[1, 1].Value = valor;

            using var ms = new MemoryStream(pkg.GetAsByteArray());
            var pkgReaberto = new ExcelPackage(ms);
            return pkgReaberto.Workbook.Worksheets[0].Cells[1, 1];
        }

        [Theory]
        [InlineData(1000d, 10)]
        [InlineData(2000d, 20)]
        [InlineData(9902d, 99.02)]
        [InlineData(30d, 30)]
        [InlineData(100d, 100)]
        public void LerPercentual_ComNormalizacaoCronogramaTipo2_CorrigeEscalaX100(double bruto, decimal esperado)
        {
            var cell = CelulaComValor(bruto);

            decimal? resultado = LdpTipo2CellReader.LerPercentual(
                cell, NullLogger.Instance, linha: 8, cabecalho: "WBS (%)", normalizarEscalaCronogramaTipo2: true);

            Assert.Equal(esperado, resultado);
        }

        [Fact]
        public void LerPercentual_SemNormalizacaoCronogramaTipo2_ValorAcimaDe100ContinuaInvalido()
        {
            var cell = CelulaComValor(2000d);

            decimal? resultado = LdpTipo2CellReader.LerPercentual(
                cell, NullLogger.Instance, linha: 8, cabecalho: "%PLANNED PHYSICAL 30%");

            Assert.Null(resultado);
        }

        [Theory]
        [InlineData("Qua 12/08/26", 2026, 8, 12)]
        [InlineData("QUI 16/07/26", 2026, 7, 16)]
        [InlineData("qua. 12/08/26", 2026, 8, 12)]
        [InlineData("Seg 27/07/26", 2026, 7, 27)]
        public void LerData_ComPrefixoDiaDaSemana_RemovePrefixoEInterpretaData(string bruto, int ano, int mes, int dia)
        {
            var cell = CelulaComValor(bruto);

            DateTime? resultado = LdpTipo2CellReader.LerData(cell, NullLogger.Instance, linha: 8, cabecalho: "PLANNED DATA (BASE LINE) 30%");

            Assert.Equal(new DateTime(ano, mes, dia), resultado);
        }

        [Fact]
        public void LerData_SemPrefixo_ContinuaFuncionandoComoAntes()
        {
            var cell = CelulaComValor("16/07/2026");

            DateTime? resultado = LdpTipo2CellReader.LerData(cell, NullLogger.Instance, linha: 8, cabecalho: "REPLANNED DATE 30%");

            Assert.Equal(new DateTime(2026, 7, 16), resultado);
        }

        [Fact]
        public void LerData_TextoInvalidoComDataEmbutida_NaoEhAceito()
        {
            var cell = CelulaComValor("qualquer coisa 12/08/26");

            DateTime? resultado = LdpTipo2CellReader.LerData(cell, NullLogger.Instance, linha: 8, cabecalho: "REPLANNED DATE 30%");

            Assert.Null(resultado);
        }
    }
}
