using System.Data;
using System.Security.Cryptography;
using APS.WebApp.Models;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace NA_Connect.Planning.Tests;

public class OfficialPlanningTemplateTests
{
    public static IEnumerable<object[]> Templates()
    {
        yield return new object[] { "CAP EXTERNO", 6, 16, "c11b653740a69388b3b9721ff8904ec87e3c35cb85f41e020c88fa29d7beda6d" };
        yield return new object[] { "CAP INTERNO", 6, 16, "c11b653740a69388b3b9721ff8904ec87e3c35cb85f41e020c88fa29d7beda6d" };
        yield return new object[] { "DESVIO DE ESCOPO", 7, 9, "890588e270bb0a0b07478bb6979c360bb51c55936bfa28d3504df8cb5878f125" };
        yield return new object[] { "PROGRAMAÇÃO", 7, 21, "929c4ae7aed78c09c46b23cc9d29661dbf54a1487db8ddbd881ce38d2846ab46" };
        yield return new object[] { "CONTROLE DE HOLD", 7, 9, "c6d39b49fcf71b70238207e7115fa70f8fa5fabb07c02361300759faf8a5f72c" };
    }
    private static PlanningSheetDefinition Schema(string name) => name switch
    {
        "CAP EXTERNO" => PlanningDefinicoes.External(), "CAP INTERNO" => PlanningDefinicoes.Internal(),
        "DESVIO DE ESCOPO" => PlanningDefinicoes.ScopeDeviation(), "CONTROLE DE HOLD" => PlanningDefinicoes.HoldControl(),
        _ => WeeklyScheduleContract.Schema()
    };
    private static ExcelPackage Open(string name)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        var file = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OfficialPlanning")).Single(f => Path.GetFileName(f).Contains(" - " + name));
        return new ExcelPackage(new MemoryStream(File.ReadAllBytes(file)));
    }
    private sealed class CaptureLog : ILogger<PlanningPreflightService>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(level + " " + formatter(state, exception));
    }
    [Theory, MemberData(nameof(Templates))]
    public void OfficialBytesAndLogicalHeadersArePreserved(string name, int row, int count, string hash)
    {
        var file = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OfficialPlanning")).Single(f => Path.GetFileName(f).Contains(" - " + name));
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
        using var package = Open(name);
        var selected = PlanningHeaderScan.Select(package.Workbook.Worksheets, Schema(name));
        Assert.Equal(row, selected.Header.HeaderRow);
        Assert.Equal(count, selected.Header.Mapa.Count);
        Assert.Equal(name == "CONTROLE DE HOLD" ? 8 : row, selected.Header.HeaderEndRow);
        if (name == "PROGRAMAÇÃO") Assert.Empty(WeeklyScheduleContract.Read(package, "test").Rows);
        else
        {
            var result = new PlanningPreflightService(new CaptureLog()).ExecutarPreflight(package, Schema(name), "test", "fixture", file);
            Assert.True(result.Sucesso, result.Mensagem);
            Assert.Equal(0, result.LinhasValidas);
            Assert.Equal(0, result.RowsWithErrors);
        }
    }
    [Theory, MemberData(nameof(Templates))]
    public void RenamedOfficialWorksheetInThirdPositionStillWorks(string name, int row, int count, string hash)
    {
        using var source = Open(name);
        var selected = PlanningHeaderScan.Select(source.Workbook.Worksheets, Schema(name));
        using var copy = new ExcelPackage();
        copy.Workbook.Worksheets.Add("A"); copy.Workbook.Worksheets.Add("B");
        copy.Workbook.Worksheets.Add("Nome alterado", selected.Sheet);
        var result = PlanningHeaderScan.Select(copy.Workbook.Worksheets, Schema(name));
        Assert.Equal("Nome alterado", result.Sheet.Name);
        Assert.Equal(count, result.Header.Mapa.Count);
    }
    [Theory, MemberData(nameof(Templates))]
    public void SemanticFieldsAtEveryAllowedRowWithReorderingAndWhitespace(string name, int originalRow, int count, string hash)
    {
        using var source = Open(name);
        var original = PlanningHeaderScan.Select(source.Workbook.Worksheets, Schema(name));
        for (int row = 1; row <= 10; row++)
        {
            using var copy = new ExcelPackage(); var sheet = copy.Workbook.Worksheets.Add("Planilha qualquer");
            int column = 1;
            foreach (var header in original.Header.OriginalHeaders.Values.Reverse())
            {
                sheet.Cells[row, column].Value = "  " + header.ToLowerInvariant().Replace(" ", "\n\u00a0 ") + "  ";
                column += 2;
            }
            sheet.Cells[row, 2].Value = "COLUNA EXTRA";
            var log = new CaptureLog();
            var selected = PlanningHeaderScan.Select(copy.Workbook.Worksheets, Schema(name), log, "copy.xlsx");
            Assert.Equal(row, selected.Header.HeaderRow); Assert.Equal(count, selected.Header.Mapa.Count);
            Assert.Contains(log.Messages, m => m.Contains("ITEM_FORA_DO_PADRAO") && m.Contains("COLUNA EXTRA"));
        }
    }
    [Theory, MemberData(nameof(Templates))]
    public void EveryRequiredHeaderMissingHasSpecificError(string name, int row, int count, string hash)
    {
        foreach (var required in Schema(name).CamposMinimosCabecalho)
        {
            using var package = Open(name);
            var selected = PlanningHeaderScan.Select(package.Workbook.Worksheets, Schema(name));
            selected.Sheet.Cells[row, selected.Header.Mapa[required]].Value = "UNKNOWN";
            var log = new CaptureLog();
            var partial = PlanningHeaderScan.Select(package.Workbook.Worksheets, Schema(name), log, "missing.xlsx");
            Assert.Equal(count - 1, partial.Header.Mapa.Count);
            Assert.Contains(log.Messages, m => m.Contains("ITEM_OBRIGATORIO_AUSENTE") && m.Contains("missing.xlsx"));
        }
    }
    [Theory, MemberData(nameof(Templates))]
    public void OptionalCellsRemainDatabaseNullAndMergedDataAreNotDuplicated(string name, int row, int count, string hash)
    {
        using var package = Open(name); var schema = Schema(name);
        var selected = PlanningHeaderScan.Select(package.Workbook.Worksheets, schema);
        int dataRow = selected.Header.HeaderEndRow + 1;
        foreach (var field in schema.Columns.Where(f => f.Required))
            selected.Sheet.Cells[dataRow, selected.Header.Mapa[field.LogicalName]].Value = field.DataType == PlanningColumnDataType.Data || field.LogicalName == "REPLANNED DATE" ? new DateTime(2026, 9, 10) : "registro";
        DataTable data;
        if (name == "PROGRAMAÇÃO") data = WeeklyScheduleContract.Read(package, "test");
        else
        {
            var result = new PlanningPreflightService(new CaptureLog()).ExecutarPreflight(package, schema, "test", "fixture", "copy.xlsx");
            Assert.Equal(1, result.RowsWithErrors); data = result.Tabela;
        }
        var record = Assert.Single(data.AsEnumerable());
        foreach (var field in schema.Columns.Where(f => !f.Required)) Assert.Equal(DBNull.Value, record[field.SqlColumnName]);
    }
    [Fact]
    public void CapRoutingUsesProcessContextBecauseOriginalBytesAreIdentical()
    {
        using var package = Open("CAP EXTERNO");
        Assert.Equal(16, PlanningHeaderScan.Select(package.Workbook.Worksheets, PlanningDefinicoes.External()).Header.Mapa.Count);
        Assert.Equal(16, PlanningHeaderScan.Select(package.Workbook.Worksheets, PlanningDefinicoes.Internal()).Header.Mapa.Count);
        Assert.NotEqual(PlanningDefinicoes.External().FolderName, PlanningDefinicoes.Internal().FolderName);
        Assert.NotEqual(PlanningDefinicoes.External().DestinationTable, PlanningDefinicoes.Internal().DestinationTable);
    }
    [Theory, MemberData(nameof(Templates))]
    public void OfficialFieldsHaveNullableDestinationsInActualReadOnlySqlEvidence(string name, int row, int count, string hash)
    {
        using var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OfficialPlanning", "sql-schema-evidence.json")));
        var schema = Schema(name);
        string table = schema.DestinationTable.Replace("[", "").Replace("]", "").Replace("dbo.", "");
        var columns = evidence.RootElement.EnumerateArray().Where(c => c.GetProperty("TABLE_NAME").GetString() == table)
            .ToDictionary(c => c.GetProperty("COLUMN_NAME").GetString()!);
        foreach (var field in schema.Columns)
        {
            Assert.True(columns.ContainsKey(field.SqlColumnName), field.SqlColumnName);
            Assert.Equal("YES", columns[field.SqlColumnName].GetProperty("IS_NULLABLE").GetString());
            string expected = field.StoreAsText || field.DataType == PlanningColumnDataType.Texto ? "nvarchar" : field.DataType == PlanningColumnDataType.Data ? "date" : "decimal";
            Assert.Equal(expected, columns[field.SqlColumnName].GetProperty("DATA_TYPE").GetString());
        }
    }
    [Theory, MemberData(nameof(Templates))]
    public void HeaderAfterTenIsRejectedAndOptionalHeaderMayBeAbsent(string name, int row, int count, string hash)
    {
        using var source = Open(name); var schema = Schema(name);
        var original = PlanningHeaderScan.Select(source.Workbook.Worksheets, schema);
        using var copy = new ExcelPackage(); var sheet = copy.Workbook.Worksheets.Add("Sem nome previsto");
        foreach (var header in original.Header.OriginalHeaders) sheet.Cells[11, header.Key].Value = header.Value;
        Assert.Equal("PADRAO_INCORRETO", Assert.Throws<PlanningLayoutException>(() => PlanningHeaderScan.Select(copy.Workbook.Worksheets, schema)).CodigoResultado);
        foreach (var optional in schema.Columns.Where(f => !f.Required && original.Header.Mapa.ContainsKey(f.LogicalName)))
            original.Sheet.Cells[row, original.Header.Mapa[optional.LogicalName]].Value = null;
        // Isolate the changed main sheet: CAP includes a separate populated BI tab in the original.
        using var minimal = new ExcelPackage(); var minimalSheet = minimal.Workbook.Worksheets.Add("Outro nome");
        foreach (var required in schema.Columns.Take(count > 10 ? 10 : 5)) minimalSheet.Cells[4, original.Header.Mapa[required.LogicalName]].Value = required.AcceptedHeaders[0];
        Assert.True(PlanningHeaderScan.Select(minimal.Workbook.Worksheets, schema).Valid);
    }
    [Fact]
    public void KnownOptionalHeaderDuplicatedCannotSilentlyDiscardData()
    {
        using var package = Open("DESVIO DE ESCOPO");
        var sheet = package.Workbook.Worksheets[0]; sheet.Cells[7, 50].Value = "DISCIPLINE";
        Assert.Equal("PADRAO_AMBIGUO", Assert.Throws<PlanningLayoutException>(() => PlanningHeaderScan.Select(package.Workbook.Worksheets, PlanningDefinicoes.ScopeDeviation())).CodigoResultado);
    }
    [Fact]
    public void LegacyProfileIsRetainedButCannotBeSelectedOrExecuted()
    {
        Assert.Contains(WeeklyProcessorRegistry.All, p => p.Id == WeeklyProcessorRegistry.Legacy);
        Assert.Null(WeeklyProcessorRegistry.Select(WeeklyProcessorRegistry.Legacy));
        Assert.DoesNotContain(WeeklyProcessorRegistry.Active, p => p.Id == WeeklyProcessorRegistry.Legacy);
        using var package = Open("PROGRAMAÇÃO");
        Assert.Throws<InvalidOperationException>(() => WeeklyScheduleContract.Read(package, "test", profile: WeeklyProcessorRegistry.Legacy));
        Assert.Equal(WeeklyProcessorRegistry.Official, Assert.Single(WeeklyProcessorRegistry.Active).Id);
    }
    [Fact]
    public void InvalidRowsAreIsolatedAndSixFunctionalEventsContainSourceEvidence()
    {
        var log = new CaptureLog(); var schema = PlanningDefinicoes.ScopeDeviation();
        using var source = Open("DESVIO DE ESCOPO");
        var selected = PlanningHeaderScan.Select(source.Workbook.Worksheets, schema);
        using var package = new ExcelPackage(); var sheet = package.Workbook.Worksheets.Add("Controle renomeado");
        foreach (var header in selected.Header.OriginalHeaders) sheet.Cells[7, header.Key].Value = header.Value;
        sheet.Cells[7, 50].Value = "ITEM NOVO";
        for (int row = 8; row <= 11; row++)
            foreach (var field in schema.Columns.Where(f => f.Required)) sheet.Cells[row, selected.Header.Mapa[field.LogicalName]].Value = field.DataType == PlanningColumnDataType.Data ? new DateTime(2026, 9, 10) : "R" + row;
        sheet.Cells[8, selected.Header.Mapa["Data"]].Value = "31/XP/2026";
        sheet.Cells[9, selected.Header.Mapa["Item"]].Value = " ";
        sheet.Cells[10, selected.Header.Mapa["QuantidadeHH"]].Value = "horas inválidas";
        var result = new PlanningPreflightService(log).ExecutarPreflight(package, schema, "test", "folder", "invalid.xlsx");
        Assert.True(result.Sucesso); Assert.Equal(4, result.RowsWithErrors); Assert.Equal(4, result.LinhasValidas);
        Assert.Contains(log.Messages, m => m.Contains("PREENCHIMENTO_INCORRETO") && m.Contains("Linha=8") && m.Contains("Coluna=") && m.Contains("31/XP/2026") && m.Contains("invalid.xlsx") && m.Contains("ValorEsperado=data válida"));
        Assert.Contains(log.Messages, m => m.Contains("VALOR_OBRIGATORIO_VAZIO") && m.Contains("Linha=9"));
        PlanningValidationEvent.EmptyFolder(log, schema.ProcessName, "folder", 0);
        using var empty = new ExcelPackage(); empty.Workbook.Worksheets.Add("Sem assinatura");
        Assert.False(new PlanningPreflightService(log).ExecutarPreflight(empty, schema, "test", "folder", "empty.xlsx").Sucesso);
        sheet.Cells[7, selected.Header.Mapa["Data"]].Value = null;
        Assert.True(new PlanningPreflightService(log).ExecutarPreflight(package, schema, "test", "folder", "missing.xlsx").Sucesso);
        foreach (var code in new[] { "PASTA_SEM_ARQUIVO", "PADRAO_INCORRETO", "ITEM_OBRIGATORIO_AUSENTE", "VALOR_OBRIGATORIO_VAZIO", "ITEM_FORA_DO_PADRAO", "PREENCHIMENTO_INCORRETO" }) Assert.Contains(log.Messages, m => m.Contains(code));
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "planning-validation-examples.log"), log.Messages);
    }
}
