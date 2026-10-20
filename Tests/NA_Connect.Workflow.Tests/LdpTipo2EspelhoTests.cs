using APS.WebApp.Models;
using APS.WebApp.Services;
using APS.WebApp.Services.LdpTipo2;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace NA_Connect.Workflow.Tests;

public class LdpTipo2EspelhoTests
{
    private static AccFolderItem Excel(string name, string id = "item-1") => new()
    {
        Name = name,
        Id = id,
        Type = "items"
    };

    [Fact]
    public void ResolveDestinoEspelho_MantemRegraPreferencialDoSufixoXX()
    {
        var result = LdpTipo2EspelhoService.ResolverDestinoEspelho(
            [Excel("CHS001-000-PL0-LD-01.xlsx")], "CHS001-000-PL0-LD-001.xlsx");

        Assert.Equal(Tipo2EspelhoDestinoStatus.Preferencial, result.Status);
        Assert.Equal("CHS001-000-PL0-LD-01.xlsx", result.Item!.Name);
    }

    [Fact]
    public void ResolveDestinoEspelho_UsaFallbackLdpExistenteSemSufixoXX()
    {
        var result = LdpTipo2EspelhoService.ResolverDestinoEspelho(
            [Excel("CHS001-000-PL0-LD-001.xlsx", "item-ldp")], "CHS001-000-PL0-LD-001.xlsx");

        Assert.Equal(Tipo2EspelhoDestinoStatus.FallbackLdpExistente, result.Status);
        Assert.Equal("item-ldp", result.Item!.Id);
        Assert.Equal("CHS001-000-PL0-LD-001.xlsx", result.Item.Name);
    }

    [Fact]
    public void ResolveDestinoEspelho_FallbackNaoEscolhePrimeiroExcel()
    {
        var result = LdpTipo2EspelhoService.ResolverDestinoEspelho(
            [Excel("arquivo-qualquer.xlsx", "item-qualquer"), Excel("CHS001-000-PL0-LD-001.xlsx", "item-ldp")],
            "CHS001-000-PL0-LD-001.xlsx");

        Assert.Equal(Tipo2EspelhoDestinoStatus.FallbackLdpExistente, result.Status);
        Assert.Equal("item-ldp", result.Item!.Id);
    }

    [Fact]
    public void ResolveDestinoEspelho_BloqueiaFallbackAmbiguoEListaCandidatos()
    {
        var result = LdpTipo2EspelhoService.ResolverDestinoEspelho(
            [Excel("CHS001-000-PL0-LD-001.xlsx"), Excel("CHS001-000-PL0-LD-002.xlsx", "item-2")],
            "CHS001-000-PL0-LD-001.xlsx");

        Assert.Equal(Tipo2EspelhoDestinoStatus.Ambiguo, result.Status);
        Assert.Contains("CHS001-000-PL0-LD-001.xlsx", result.Candidatos);
        Assert.Contains("CHS001-000-PL0-LD-002.xlsx", result.Candidatos);
    }

    [Fact]
    public void ResolveDestinoEspelho_BloqueiaQuandoNaoHaDestinoCompativel()
    {
        var result = LdpTipo2EspelhoService.ResolverDestinoEspelho(
            [Excel("arquivo-qualquer.xlsx")], "CHS001-000-PL0-LD-001.xlsx");

        Assert.Equal(Tipo2EspelhoDestinoStatus.NaoEncontrado, result.Status);
        Assert.Null(result.Item);
    }

    private static List<LdpTipo2Documento> Documents(int count, List<LdpTipo2Gate>? gates = null)
    {
        var documents = new List<LdpTipo2Documento>();
        for (int i = 1; i <= count; i++)
            documents.Add(new LdpTipo2Documento {
                LinhaExcel = i + 7, ScheduleId = "S", Disciplina = "D", NumeroDocumentoNa = $"DOC-{i}",
                NumeroDocumentoCliente = "CLIENT", TituloDocumento = "Título", Formato = "A1", NumeroPaginas = 2,
                EmissionDate = new DateTime(2026, 9, 25), TransmittalNumber = "GRD", IssuedPurpose = "AP",
                Revision = "B", DocumentStatus = "Status geral", Gates = i == 1 ? gates ?? [] : []
            });
        return documents;
    }

    [Fact]
    public void PivotPreservaLayoutLimpaExcedentesEUsaAplicabilidadeSemStatus()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("LDP");
        ws.Cells[7, 1, 7, 23].Value = "Cabeçalho";
        ws.Cells[1, 1, 1, 4].Merge = true;
        ws.Row(8).Height = 27;
        ws.Column(4).Width = 41;
        ws.Cells[8, 1, 8, 23].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        ws.Cells[8, 9, 8, 11].Style.Numberformat.Format = "dd/MM/yyyy";
        var baseline = new DateTime(2026, 10, 1);
        var replan = baseline.AddDays(3);
        var gates = new List<LdpTipo2Gate> {
            new() { GatePercentual = 30, GateAtivo = true, DocumentStatus = "APROVADO" },
            new() { GatePercentual = 60, GateAtivo = true, DocumentStatus = "", PlannedBaseline = baseline, ReplannedDate = replan },
            new() { GatePercentual = 90, GateAtivo = false, DocumentStatus = "Manual" }
        };
        LdpTipo2EspelhoService.Preencher(ws, Documents(4, gates));
        Assert.Equal("DOC-4", ws.Cells[11, 4].Value);
        Assert.Equal(27, ws.Row(11).Height);
        Assert.Equal(ExcelBorderStyle.Thin, ws.Cells[11, 1].Style.Border.Bottom.Style);
        ws.Cells[12, 4].Formula = "1+1";
        LdpTipo2EspelhoService.Preencher(ws, Documents(1, gates));
        Assert.Equal(1, ws.Cells[8, 1].Value);
        Assert.Equal(baseline, ws.Cells[8, 9].Value);
        Assert.Equal(replan, ws.Cells[8, 10].Value);
        Assert.Equal("APROVADO", ws.Cells[8, 18].Value);
        Assert.Equal("", ws.Cells[8, 19].Value);
        Assert.Equal("Manual", ws.Cells[8, 20].Value);
        Assert.Equal("X", ws.Cells[8, 21].Value);
        Assert.Equal("X", ws.Cells[8, 22].Value);
        Assert.Null(ws.Cells[8, 23].Value);
        Assert.Equal(ws.Cells[8, 13].Value, ws.Cells[8, 16].Value);
        Assert.Equal(ws.Cells[8, 14].Value, ws.Cells[8, 17].Value);
        for (int col = 1; col <= 23; col++) Assert.Equal(col is >= 10 and <= 17, ws.Column(col).Hidden);
        Assert.Null(ws.Cells[11, 4].Value);
        Assert.True(string.IsNullOrEmpty(ws.Cells[12, 4].Formula));
        using var reopened = new ExcelPackage(new MemoryStream(package.GetAsByteArray()));
        var saved = reopened.Workbook.Worksheets["LDP"];
        Assert.True(saved.Cells[1, 1, 1, 4].Merge);
        Assert.Equal(41, saved.Column(4).Width);
        Assert.Equal("Cabeçalho", saved.Cells[7, 1].Value);
        LdpTipo2EspelhoService.Preencher(saved, Documents(0));
        Assert.Null(saved.Cells[8, 4].Value);
    }

    [Fact]
    public void DocumentosDoParserMantemSeusPropriosGatesSemIdsOuDatasDeSincronizacaoSql()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("LDP");
        ws.Cells[8, 23].Value = "Modelo";
        var documents = Documents(2);
        documents[0].Gates.Add(new() { GatePercentual = 30, GateAtivo = true, DocumentStatus = "Primeiro" });
        documents[1].Gates.Add(new() { GatePercentual = 30, GateAtivo = false, DocumentStatus = "Segundo" });
        documents[1].Gates.Add(new() { GatePercentual = 90, GateAtivo = true });
        LdpTipo2EspelhoService.Preencher(ws, documents);
        Assert.Equal("Primeiro", ws.Cells[8, 18].Value);
        Assert.Equal("Segundo", ws.Cells[9, 18].Value);
        Assert.Equal("X", ws.Cells[8, 21].Value);
        Assert.Null(ws.Cells[9, 21].Value);
        Assert.Equal("X", ws.Cells[9, 23].Value);
        Assert.Equal(2, ws.Cells[9, 1].Value);
    }

    [Fact]
    public void DatasEscolhemPrimeiroAtivoPendenteOuUltimoFinalizadoSemInventarDatas()
    {
        var g30 = new LdpTipo2Gate { GatePercentual = 30, GateAtivo = false };
        var g60 = new LdpTipo2Gate { GatePercentual = 60, GateAtivo = true, DocumentStatus = " concluído " };
        var g90 = new LdpTipo2Gate { GatePercentual = 90, GateAtivo = true, DocumentStatus = "Approved" };
        Assert.Same(g90, LdpTipo2EspelhoService.EscolherGate([g90, g30, g60]));
        g60.DocumentStatus = "";
        Assert.Same(g60, LdpTipo2EspelhoService.EscolherGate([g90, g30, g60]));
        Assert.Null(LdpTipo2EspelhoService.EscolherGate([g30]));
        Assert.Null(LdpTipo2EspelhoService.EscolherGate([g60])!.PlannedBaseline);
    }

    [Fact]
    public void ReferenciaExternaDaCapaViraLocal()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("LDP");
        package.Workbook.Worksheets.Add("CAPA").Cells["J3"].Value = "Projeto";
        ws.Cells["A1"].Formula = "[1]CAPA!J3";
        LdpTipo2EspelhoService.RemoverReferenciasExternas(package);
        Assert.Equal("CAPA!J3", ws.Cells["A1"].Formula);
        Assert.Empty(package.Workbook.ExternalLinks);
    }
}
