using System.Data;
using APS.WebApp.Services.Planning;
using OfficeOpenXml;

namespace NA_Connect.Planning.Tests;

public class WeeklyScheduleContractTests
{
    private static ExcelPackage Template()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        return new ExcelPackage(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WEEKLY ENGINEERING SCHEDULE.xlsx")));
    }

    [Fact]
    public void OfficialTemplate_Has21HeadersAt7_AndZeroRecords()
    {
        using var package = Template();
        var sheet = WeeklyScheduleContract.FindWorksheet(package)!;
        Assert.Equal("WEEKLY SCHEDULE", sheet.Name);
        var header = WeeklyScheduleContract.Inspect(sheet);
        Assert.True(header.Valid);
        Assert.Equal(21, header.Map.Count);
        Assert.Equal(WeeklyScheduleContract.Fields.Take(21).Select(f => f.Header), header.Found);
        Assert.Empty(WeeklyScheduleContract.Read(package, "project").Rows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MandatorySheetCanBeSecondOrThird(int precedingSheets)
    {
        using var template = Template();
        using var package = new ExcelPackage();
        for (int i = 0; i < precedingSheets; i++) package.Workbook.Worksheets.Add("Other" + i).Cells[8, 1].Value = "wrong";
        package.Workbook.Worksheets.Add(" weekly   schedule ", template.Workbook.Worksheets[0]);
        Assert.Empty(WeeklyScheduleContract.Read(package, "project").Rows);
    }

    [Fact]
    public void HeaderAtOneSatisfiesCurrentContract()
    {
        using var template = Template();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Renamed");
        for (int c = 1; c <= 21; c++) sheet.Cells[1, c].Value = template.Workbook.Worksheets[0].Cells[7, c].Value;
        Assert.Empty(WeeklyScheduleContract.Read(package, "project").Rows);
    }

    public static IEnumerable<object[]> RequiredFields() => WeeklyScheduleContract.Fields.Select((f, i) => new { f.Header, Column = i + 1 }).Where(f => WeeklyScheduleContract.RequiredHeaders.Contains(f.Header)).Select(f => new object[] { f.Column, f.Header });

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void EveryMandatoryHeaderBlocksWhenMissing(int column, string name)
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[7, column].Value = null;
        Assert.Contains(name, WeeklyScheduleContract.Inspect(sheet).Missing);
        Assert.Empty(WeeklyScheduleContract.Read(package, "project").Rows);
    }

    [Fact]
    public void InsertedColumnAndReorderedFieldsDoNotShiftMappings()
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        sheet.InsertColumn(3, 1);
        sheet.Cells[7, 3].Value = "EXTRA";
        sheet.Cells[8, 3].Value = "ignore";
        sheet.Cells[7, 1].Value = "DOCUMENT TITLE";
        sheet.Cells[7, 6].Value = "ITEM";
        sheet.Cells[8, 1].Value = "Document A";
        sheet.Cells[8, 6].Value = "001";
        sheet.Cells[8, 5].Value = "NA-123";
        var row = Assert.Single(WeeklyScheduleContract.Read(package, "project").AsEnumerable());
        Assert.Equal("Document A", row["Nome_Tarefa"]);
        Assert.Equal("001", row["Weekly_Item"]);
        Assert.Equal("NA-123", row["Numero_NA_Atividade"]);
        Assert.Equal(new[] { "EXTRA" }, WeeklyScheduleContract.Inspect(sheet).Extras);
    }

    [Fact]
    public void Phase_ChegaAWeeklyPhase_PeloHeaderDinamico_MesmoForaDaPosicaoPadrao()
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        // Desloca PHASE (nova coluna antes dela) e usa caixa/espaços diferentes: a leitura é pelo header, nunca por índice.
        int phaseOriginal = WeeklyScheduleContract.Inspect(sheet).Map[WeeklyScheduleContract.Normalize("PHASE")];
        sheet.InsertColumn(phaseOriginal, 1);
        sheet.Cells[7, phaseOriginal].Value = "EXTRA";
        sheet.Cells[7, phaseOriginal + 1].Value = "  phase ";
        var map = WeeklyScheduleContract.Inspect(sheet).Map;
        int Col(string header) => map[WeeklyScheduleContract.Normalize(header)];
        Assert.Equal(phaseOriginal + 1, Col("PHASE"));

        var linhas = new[] { ("CHS001-PIPING-01", "Pipe Specs"), ("CHS001-PIPING-02", "S1") };
        for (int i = 0; i < linhas.Length; i++)
        {
            sheet.Cells[8 + i, Col("PHASE")].Value = "GERAL";
            sheet.Cells[8 + i, Col("DISCIPLINE")].Value = "DIGITAL SYSTEM";
            sheet.Cells[8 + i, Col("N&A DOCUMENT NUMBER")].Value = linhas[i].Item1;
            sheet.Cells[8 + i, Col("DOCUMENT TITLE")].Value = linhas[i].Item2;
        }

        var rows = WeeklyScheduleContract.Read(package, "project").AsEnumerable()
            .ToDictionary(r => (string)r["Numero_NA_Atividade"]);
        Assert.Equal("GERAL", rows["CHS001-PIPING-01"]["Weekly_Phase"]);
        Assert.Equal("GERAL", rows["CHS001-PIPING-02"]["Weekly_Phase"]);
        Assert.Equal("DIGITAL SYSTEM", rows["CHS001-PIPING-01"]["Disciplina"]);
        Assert.Equal("Pipe Specs", rows["CHS001-PIPING-01"]["Nome_Tarefa"]);
    }

    [Fact]
    public void BlankRowsIgnored_AnyOfficialFieldKeepsRow_ExtrasDoNotCreateRow()
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[7, 22].Value = "EXTRA";
        sheet.Cells[8, 22].Value = "extra only";
        sheet.Cells[9, 1].Value = " ";
        sheet.Cells[10, 21].Value = "Daily observation";
        var row = Assert.Single(WeeklyScheduleContract.Read(package, "project").AsEnumerable());
        Assert.Equal("Daily observation", row["Weekly_Observacao_Dia"]);
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(.5d, "0.5")]
    [InlineData(1d, "1")]
    [InlineData(50d, "0.5")]
    [InlineData(100d, "1")]
    [InlineData("0%", "0")]
    [InlineData("50%", "0.5")]
    [InlineData("100%", "1")]
    [InlineData("0,75", "0.75")]
    public void PercentagesAreNormalizedOnceForBothSqlFields(object value, string expected)
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[8, 6].Value = value;
        sheet.Cells[8, 7].Value = value;
        sheet.Cells[8, 6, 8, 7].Style.Numberformat.Format = "0%";
        var row = Assert.Single(WeeklyScheduleContract.Read(package, "project").AsEnumerable());
        Assert.Equal(expected, row["Perc_Previsto_Semana"]);
        Assert.Equal(expected, row["Perc_Realizado"]);
    }

    [Fact]
    public void DatesAcceptRealSerialTextAndNull_HoursRemainNumbersEvenWithTimeFormat()
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        var date = new DateTime(2026, 9, 9);
        sheet.Cells[8, 9].Value = date;
        sheet.Cells[8, 10].Value = date.ToOADate();
        sheet.Cells[9, 9].Value = "09/09/2026";
        foreach (var column in new[] { 15, 16, 18 })
        {
            sheet.Cells[8, column].Value = 12.5;
            sheet.Cells[8, column].Style.Numberformat.Format = "hh:mm";
        }
        var table = WeeklyScheduleContract.Read(package, "project");
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(date, table.Rows[0]["Weekly_Baseline_Date"]);
        Assert.Equal(date, table.Rows[0]["Weekly_Replanned_Date"]);
        Assert.Equal(date, table.Rows[1]["Weekly_Baseline_Date"]);
        Assert.Equal(DBNull.Value, table.Rows[1]["Weekly_Replanned_Date"]);
        foreach (var name in new[] { "Weekly_HH_Previsto", "Weekly_HH_Verificacao", "Weekly_HH_Aprovacao" })
            Assert.Equal(12.5m, Assert.IsType<decimal>(table.Rows[0][name]));
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("xlsm")]
    public void ExistingParserReadsBothExtensions(string extension)
    {
        Assert.True(WeeklyScheduleContract.IsExcel(extension));
        using var template = Template();
        if (extension == "xlsm") template.Workbook.CreateVBAProject();
        using var stream = new MemoryStream(template.GetAsByteArray());
        using var reopened = new ExcelPackage(stream);
        Assert.Empty(WeeklyScheduleContract.Read(reopened, "project").Rows);
    }

    [Fact]
    public void DuplicateKnownHeaderBlocksMappingButSheetNameIsFree()
    {
        using var package = Template();
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[7, 22].Value = " item ";
        Assert.True(WeeklyScheduleContract.Inspect(sheet).Valid);
        Assert.Equal("PADRAO_AMBIGUO", Assert.Single(WeeklyScheduleContract.Read(package, "project").AsEnumerable())["Tipo_Registro"]);
        sheet.Cells[7, 22].Value = null;
        sheet.Name = "WEEKLY SCHEDULE OLD";
        Assert.Empty(WeeklyScheduleContract.Read(package, "project").Rows);
    }

    [Fact]
    public void MissingOrWrongSqlTypesBlockAndFolderPriorityIsExact()
    {
        var schema = WeeklyScheduleContract.Fields.ToDictionary(f => f.SqlColumn,
            f => f.Type == typeof(decimal) ? "decimal" : f.Type == typeof(DateTime) ? "date" : "nvarchar");
        Assert.Empty(WeeklyScheduleContract.SqlErrors(schema));
        schema["Weekly_HH_Previsto"] = "time";
        Assert.Single(WeeklyScheduleContract.SqlErrors(schema));
        schema.Remove("Weekly_Phase");
        Assert.Equal(2, WeeklyScheduleContract.SqlErrors(schema).Count);
        Assert.Equal("01.13 - PROGRAMAÇÃO SEMANAL ENGENHARIA", WeeklyScheduleContract.FolderAliases("PT")[0]);
        Assert.Equal("01.13 - WEEKLY ENGINEERING SCHEDULE", WeeklyScheduleContract.FolderAliases("EN")[0]);
        Assert.Equal("ARQUIVO_WEEKLY_SCHEDULE_VALIDO_NAO_ENCONTRADO", WeeklyScheduleContract.SelectionError(0));
        Assert.Null(WeeklyScheduleContract.SelectionError(1));
        Assert.Equal("ARQUIVO_WEEKLY_SCHEDULE_AMBIGUO", WeeklyScheduleContract.SelectionError(2));
    }
}
