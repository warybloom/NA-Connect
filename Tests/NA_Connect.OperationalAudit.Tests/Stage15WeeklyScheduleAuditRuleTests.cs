using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests;

public class Stage15WeeklyScheduleAuditRuleTests
{
    private static (Stage15WeeklyScheduleAuditRule Rule, FakeAuditAccReadOnlyAdapter Acc, FakeAuditPlanningReadOnlyAdapter Planning) Setup(int candidates = 1)
    {
        var acc = new FakeAuditAccReadOnlyAdapter();
        acc.ConfigurarPasta("01.13", TestFixtures.Pasta("folder", "01.13"));
        acc.ConfigurarListagem("folder", TestFixtures.Listagem(Enumerable.Range(0, candidates)
            .Select(i => TestFixtures.Item("I" + i, "weekly" + i + ".xlsx", "folder", "01.13")).ToArray()));
        var planning = new FakeAuditPlanningReadOnlyAdapter
        {
            Resultado = new AuditWorksheetSnapshot
            {
                Nome = "WEEKLY SCHEDULE", LinhaDeCabecalho = 7,
                Cabecalhos = WeeklyScheduleContract.Fields.Take(21).Select(f => f.Header).ToArray()
            },
            SchemaWeekly = WeeklyScheduleContract.Fields.ToDictionary(f => f.SqlColumn,
                f => f.Type == typeof(decimal) ? "decimal" : f.Type == typeof(DateTime) ? "date" : "nvarchar")
        };
        return (new Stage15WeeklyScheduleAuditRule(acc, planning), acc, planning);
    }

    [Fact]
    public async Task OfficialEmptyTemplateApprovesAllSixRequirements()
    {
        var (rule, _, _) = Setup();
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Equal("15", rule.Etapa);
        Assert.Empty(result.Findings);
        Assert.Equal(6, result.Requirements.Count);
        Assert.All(result.Requirements, r => Assert.Equal(OperationalRequirementStatus.Approved, r.Status));
    }

    [Fact]
    public async Task MissingFolderBlocks()
    {
        var (rule, acc, _) = Setup();
        acc.ConfigurarPasta("01.13", null);
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Contains(result.Findings, f => f.RegraId == "WKS-001" && f.BloqueiaEtapa);
    }

    [Theory]
    [InlineData(0, "ARQUIVO_WEEKLY_SCHEDULE_VALIDO_NAO_ENCONTRADO")]
    [InlineData(2, "ARQUIVO_WEEKLY_SCHEDULE_AMBIGUO")]
    public async Task CandidateSelectionRequiresExactlyOneSignature(int count, string reason)
    {
        var (rule, _, _) = Setup(count);
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Contains(result.Findings, f => f.RegraId == "WKS-002" && f.MotivoNormalizado == reason && f.BloqueiaEtapa);
    }

    [Theory]
    [InlineData("Sheet1", 6)]
    [InlineData("Arbitrary", 8)]
    [InlineData("WEEKLY SCHEDULE", 1)]
    public async Task HeaderWithinFirstTenApproves(string sheet, int row)
    {
        var (rule, _, planning) = Setup();
        planning.Resultado = new AuditWorksheetSnapshot { Nome = sheet, LinhaDeCabecalho = row, Cabecalhos = planning.Resultado.Cabecalhos };
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Empty(result.Findings);
    }

    public static IEnumerable<object[]> Fields() => WeeklyScheduleContract.RequiredHeaders.Select(h => new object[] { h });

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task EachMissingHeaderIsDiagnostic(string missing)
    {
        var (rule, _, planning) = Setup();
        planning.Resultado = new AuditWorksheetSnapshot
        {
            Nome = "WEEKLY SCHEDULE", LinhaDeCabecalho = 7,
            Cabecalhos = planning.Resultado.Cabecalhos.Where(h => h != missing).ToArray()
        };
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Contains(result.Findings, f => f.RegraId == "WKS-005" && !f.BloqueiaEtapa && f.MotivoNormalizado == "ITEM_OBRIGATORIO_AUSENTE");
    }

    [Fact]
    public async Task SqlRequiresAllMappedColumnsWithCorrectTypes()
    {
        var (rule, _, planning) = Setup();
        planning.SchemaWeekly = new Dictionary<string, string> { ["Disciplina"] = "nvarchar" };
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Contains(result.Findings, f => f.RegraId == "WKS-006" && f.BloqueiaEtapa);
    }

    [Fact]
    public async Task IncompleteListingCannotEstablishUniqueCandidate()
    {
        var (rule, acc, _) = Setup();
        acc.ConfigurarListagem("folder", new AccFolderListingResult { PaginacaoCompleta = false });
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Contains(result.Findings, f => f.MotivoNormalizado == "LISTAGEM_WEEKLY_SCHEDULE_INCOMPLETA");
    }

    [Fact]
    public void RealAdapterReadsNamedSheetAtSevenInsteadOfFirst()
    {
        OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
        using var package = new OfficeOpenXml.ExcelPackage();
        package.Workbook.Worksheets.Add("Wrong").Cells[1, 1].Value = "wrong";
        var sheet = package.Workbook.Worksheets.Add(" weekly   schedule ");
        for (int i = 0; i < WeeklyScheduleContract.Fields.Count; i++) sheet.Cells[7, i + 1].Value = WeeklyScheduleContract.Fields[i].Header;
        using var stream = new System.IO.MemoryStream(package.GetAsByteArray());
        var adapter = new AuditPlanningReadOnlyAdapter(new AuditWorkbookReadOnlyAdapter(), new FakeAuditDatabaseReadOnlyAdapter());
        var result = adapter.LerAba(stream, "WEEKLY SCHEDULE", 7);
        Assert.Equal(sheet.Name, result.Nome);
        Assert.Equal(7, result.LinhaDeCabecalho);
        Assert.True(WeeklyScheduleContract.ValidateHeaders(result.Cabecalhos).Valid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RealExcelContentDeterminesSelectionRegardlessOfNameAndOrder(int validCount)
    {
        OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
        var acc = new FakeAuditAccReadOnlyAdapter();
        acc.ConfigurarPasta("01.13", TestFixtures.Pasta("folder", "01.13"));
        var items = new List<AccItemRef>();
        for (int i = 0; i <= validCount; i++)
        {
            using var package = new OfficeOpenXml.ExcelPackage();
            package.Workbook.Worksheets.Add("First sheet");
            var sheet = package.Workbook.Worksheets.Add("WEEKLY SCHEDULE");
            // First candidate has the right filename and worksheet but obsolete header row.
            for (int c = 0; c < WeeklyScheduleContract.Fields.Count; c++)
                sheet.Cells[i == 0 ? 11 : 7, c + 1].Value = WeeklyScheduleContract.Fields[c].Header;
            string urn = "urn:" + i;
            acc.ConfigurarConteudo(urn, package.GetAsByteArray());
            items.Add(TestFixtures.Item("id" + i, i == 0 ? "WEEKLY ENGINEERING SCHEDULE.xlsx" : "unrelated-name.xlsm", "folder", "01.13", urn));
        }
        acc.ConfigurarListagem("folder", TestFixtures.Listagem(items.ToArray()));
        var db = new FakeAuditDatabaseReadOnlyAdapter();
        db.ConfigurarResposta("INFORMATION_SCHEMA.COLUMNS", TestFixtures.Tabela(new[] { "COLUMN_NAME", "DATA_TYPE" },
            WeeklyScheduleContract.Fields.Select(f => new object?[] { f.SqlColumn,
                f.Type == typeof(decimal) ? "decimal" : f.Type == typeof(DateTime) ? "date" : "nvarchar" }).ToArray()));
        var rule = new Stage15WeeklyScheduleAuditRule(acc, new AuditPlanningReadOnlyAdapter(new AuditWorkbookReadOnlyAdapter(), db));
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        var selection = Assert.Single(result.Requirements, r => r.RequirementCode == "WKS-002");
        Assert.Equal(validCount == 1 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed, selection.Status);
        Assert.Equal(validCount.ToString(), selection.ValorEncontrado);
    }

    [Fact]
    public async Task PartialHeadersAreDiagnosedWithoutBlocking()
    {
        var (rule, _, planning) = Setup();
        planning.Resultado = new AuditWorksheetSnapshot
        { Nome = "Any name", LinhaDeCabecalho = 7, Cabecalhos = WeeklyScheduleContract.Fields.Take(19).Select(f=>f.Header).ToArray() };
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Equal(2,result.Findings.Count);
        Assert.All(result.Findings,f=>Assert.False(f.BloqueiaEtapa));
    }

    [Fact]
    public async Task TwoSheetsInOneWorkbookGiveWorksheetAmbiguity()
    {
        var (rule, _, planning) = Setup();
        planning.WorkbookResultado = new AuditWorkbookSnapshot
        {
            NomesDasAbas = new[] { "one", "two" },
            Abas = new Dictionary<string, AuditWorksheetSnapshot>
            {
                ["one"] = new() { Nome = "one", LinhaDeCabecalho = 7, Cabecalhos = WeeklyScheduleContract.Fields.Select(f=>f.Header).ToArray() },
                ["two"] = new() { Nome = "two", LinhaDeCabecalho = 7, Cabecalhos = WeeklyScheduleContract.Fields.Select(f=>f.Header).ToArray() }
            }
        };
        var result = await rule.ExecutarAsync(TestFixtures.NovoContexto());
        Assert.Contains(result.Findings, f => f.RegraId == "WKS-003" && f.MotivoNormalizado == "ABA_WEEKLY_SCHEDULE_AMBIGUA");
        Assert.Contains(result.Requirements, r => r.RequirementCode == "WKS-002" && r.ValorEncontrado == "0");
    }

    [Theory]
    [InlineData("WeeklyLayoutOfficial.xlsx", 21)]
    [InlineData("WeeklyLayoutLegacy26.xlsm", 26)]
    public async Task RealFixturePassesAuditWithCompatibleSqlForPresentFields(string file, int count)
    {
        var acc = new FakeAuditAccReadOnlyAdapter();
        acc.ConfigurarPasta("01.13", TestFixtures.Pasta("folder", "01.13"));
        acc.ConfigurarListagem("folder", TestFixtures.Listagem(TestFixtures.Item("id", file, "folder", "01.13")));
        acc.ConfigurarConteudo("urn:1", System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
        var db = new FakeAuditDatabaseReadOnlyAdapter();
        db.ConfigurarResposta("INFORMATION_SCHEMA.COLUMNS", TestFixtures.Tabela(new[] { "COLUMN_NAME", "DATA_TYPE" },
            WeeklyScheduleContract.Fields.Select(f => new object?[] { f.SqlColumn,
                f.Type == typeof(decimal) ? "decimal" : f.Type == typeof(DateTime) ? "date" : "nvarchar" }).ToArray()));
        var adapter = new AuditPlanningReadOnlyAdapter(new AuditWorkbookReadOnlyAdapter(), db);
        var result = await new Stage15WeeklyScheduleAuditRule(acc, adapter).ExecutarAsync(TestFixtures.NovoContexto());
        if (file.EndsWith("xlsm"))
        {
            Assert.Contains(result.Findings, f => f.MotivoNormalizado == "ITEM_OBRIGATORIO_AUSENTE");
            Assert.Contains(result.Findings, f => f.MotivoNormalizado == "WEEKLY_COLUNAS_EXTRAS");
            return; // Historical workbook is diagnosed against the current 21-field contract.
        }
        Assert.Empty(result.Findings);
        Assert.Contains(result.Requirements, r => r.RequirementCode == "WKS-006" && r.ValorEsperado == count + " campos compativeis");
        Assert.All(result.Requirements, r => Assert.Equal(OperationalRequirementStatus.Approved, r.Status));
    }
}
