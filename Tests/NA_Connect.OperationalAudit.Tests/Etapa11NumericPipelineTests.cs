using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services;
using APS.WebApp.Services;
using OfficeOpenXml;

namespace NA_Connect.OperationalAudit.Tests;

public class Etapa11NumericPipelineTests
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void XlsxNumbers_SurviveParserAndSqlConversion(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var source = new ExcelPackage();
            var sheet = source.Workbook.Worksheets.Add("LDP");
            double[][] values = [
                [17.6, 201.5, -183.9, 2008.1818181818182, 45686.477272727279, -43678.295454545463],
                [352, 393, -41, 57640, 42899.644886363596, 14740.355113636404]];
            for (var r = 0; r < values.Length; r++)
                for (var c = 0; c < 6; c++) sheet.Cells[r + 8, c + 30].Value = values[r][c];
            using var stream = new MemoryStream(source.GetAsByteArray());
            using var loaded = new ExcelPackage(stream);
            var db = new DatabaseService(new ConfigurationBuilder().Build(), NullLogger<DatabaseService>.Instance);
            var convert = typeof(DatabaseService).GetMethod("GetDecimal", BindingFlags.NonPublic | BindingFlags.Instance)!;
            for (var r = 0; r < values.Length; r++)
            {
                var row = GrdAutomationWorker.ParseLdpUnificadaRow(c => loaded.Workbook.Worksheets[0].Cells[r + 8, c].Value);
                for (var c = 0; c < 6; c++)
                {
                    Assert.IsType<double>(row[c + 30]);
                    var actual = Assert.IsType<decimal>(convert.Invoke(db, [row, c + 30]));
                    Assert.InRange(Math.Abs(actual - (decimal)values[r][c]), 0m, 0.000000001m);
                }
            }
            Assert.Equal(867371.500910m, 809731.500910m + 57640m);
            Assert.Equal(763601.812159m, Math.Round(675015.69m + 45686.477272727279m + 42899.644886363596m, 6));
            // Reproduz o ponto de NULL anterior à proteção: data não é decimal.
            foreach (var value in new[] { 57640d, 45686.477272727279d, 42899.644886363596d })
                Assert.Equal(DBNull.Value, convert.Invoke(db, [new Dictionary<int, object> { [33] = DateTime.FromOADate(value) }, 33]));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void FinancialNumbersInDateSerialRange_RemainNumeric(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var db = new DatabaseService(new ConfigurationBuilder().Build(), NullLogger<DatabaseService>.Instance);
            var convert = typeof(DatabaseService).GetMethod("GetDecimal", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var dateHeuristic = typeof(GrdAutomationWorker).GetMethod("InsinuaSerData", BindingFlags.NonPublic | BindingFlags.Static)!;
            var dateConversion = typeof(GrdAutomationWorker).GetMethod("TratarData", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var value in new[] { 45686.477272727279d, 57640d, 42899.644886363596d })
            {
                // Demonstra a causa sem desativar a proteção de produção.
                Assert.True((bool)dateHeuristic.Invoke(null, [value])!);
                var wronglyConverted = Assert.IsType<DateTime>(dateConversion.Invoke(null, [value]));
                Assert.Equal(DBNull.Value, convert.Invoke(db, [new Dictionary<int, object> { [33] = wronglyConverted }, 33]));
                var row = GrdAutomationWorker.ParseLdpUnificadaRow(c => c >= 30 && c <= 35 ? value : null!);
                for (var column = 30; column <= 35; column++)
                {
                    Assert.Equal(value, Assert.IsType<double>(row[column]));
                    using var command = new Microsoft.Data.SqlClient.SqlCommand();
                    var parameter = command.Parameters.AddWithValue("@Value", convert.Invoke(db, [row, column])!);
                    Assert.Equal(System.Data.SqlDbType.Decimal, parameter.SqlDbType);
                    Assert.InRange(Math.Abs(Assert.IsType<decimal>(parameter.Value) - (decimal)value), 0m, 0.000000001m);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void ActualDateColumns_StillRecognizeDates(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var expected = new DateTime(2026, 9, 22);
            foreach (var input in new object[] { expected, expected.ToOADate(), "22/09/2026" })
            {
                var row = GrdAutomationWorker.ParseLdpUnificadaRow(c => c == 9 || c == 10 || c == 11 ? input : null!);
                foreach (var column in new[] { 9, 10, 11 })
                    Assert.Equal(expected, Assert.IsType<DateTime>(row[column]));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
