using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.LdpTipo2.Tests.TestHelpers;
using net.sf.mpxj;
using OfficeOpenXml;
using Xunit.Abstractions;

namespace NA_Connect.LdpTipo2.Tests;

public class Hotfix2WorkRealizadoTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1000d, 10000d, 10d)]
    [InlineData(2500d, 10000d, 25d)]
    [InlineData(9902d, 10000d, 99.02d)]
    [InlineData(1000d, 0d, null)]
    [InlineData(null, 10000d, null)]
    public void Formula(double? item, double total, double? esperado)
        => Assert.Equal((decimal?)esperado, CronogramaTipo2Work.CalcularPercentual((decimal?)item, (decimal)total));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasDinamicoAlimentaRegistroSemPercentuaisNativos(bool moverCampo)
    {
        var project = new ProjectFile();
        var campo = moverCampo ? TaskField.TEXT19 : TaskField.TEXT7;
        project.getCustomFields().getOrCreate(campo).setAlias("%realizado");
        var task = project.addTask();
        task.set(campo, "37,25%");
        task.set(TaskField.PERCENT_COMPLETE, java.lang.Double.valueOf(99));
        task.set(TaskField.PHYSICAL_PERCENT_COMPLETE, java.lang.Double.valueOf(88));
        task.setWork(Duration.getInstance(1000, TimeUnit.HOURS));
        var parser = new CronogramaTipo2Parser(NullLogger<CronogramaTipo2Parser>.Instance);
        var result = parser.ProcessarProjeto(project, new CronogramaTipo2Resultado(), 2, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal(37.25m, Assert.Single(result.Registros).PercentRealizado);
        Assert.Equal(100m, CronogramaTipo2Work.LerPercentualSnapshot(result.Registros[0].CamposCustomizadosJson));
        project.getCustomFields().getOrCreate(campo).setAlias("outro");
        result = parser.ProcessarProjeto(project, new CronogramaTipo2Resultado(), 2, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Null(Assert.Single(result.Registros).PercentRealizado);
    }

    [Fact]
    public void TotalReconciliaResumoEConverteUnidadesSemDuplaContagem()
    {
        var project = new ProjectFile();
        var resumo = project.addTask();
        resumo.setID(java.lang.Integer.valueOf(0));
        resumo.setSummary(true);
        resumo.setWork(Duration.getInstance(100, TimeUnit.HOURS));
        var a = resumo.addTask();
        a.setWork(Duration.getInstance(600, TimeUnit.MINUTES));
        var b = resumo.addTask();
        b.setWork(Duration.getInstance(90, TimeUnit.HOURS));
        Assert.Equal(100m, CronogramaTipo2Work.ObterTotal(project, out var fonte));
        Assert.StartsWith("Project Summary", fonte);
        resumo.setWork(Duration.getInstance(999, TimeUnit.HOURS));
        Assert.Equal(100m, CronogramaTipo2Work.ObterTotal(project, out fonte));
        Assert.StartsWith("Fallback", fonte);
        Assert.Null(CronogramaTipo2Work.LerPercentualSnapshot("{\"WBS (%)\":\"9902\"}"));
    }

    [Fact]
    public void MppExistenteComprovaFonteRealSemAcessarServicos()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var project = new net.sf.mpxj.reader.UniversalProjectReader().read(
            Path.Combine(dir.FullName, "Services", "cronograma", "CHS001-000-PMO-SC-001-05.mpp"));
        var resolver = new CronogramaTipo2FieldResolver(project);
        bool encontrado = resolver.TentarResolverQualquer(new[] { "%realizado", "% Realizado" }, out var campo, out var alias);
        output.WriteLine($"%realizado: encontrado={encontrado}; alias={alias}; campo={campo}");
        var total = CronogramaTipo2Work.ObterTotal(project, out var fonte);
        output.WriteLine($"Total Work (horas)={total}; fonte={fonte}");
        var it = project.getTasks().iterator();
        int exemplos = 0;
        while (it.hasNext())
        {
            var task = (net.sf.mpxj.Task)it.next();
            if (task.getID()?.intValue() == 0 || (!task.getSummary() && exemplos++ < 3))
                output.WriteLine($"ID={task.getID()}; Summary={task.getSummary()}; Work={task.getWork()}; Horas={CronogramaTipo2Work.LerHoras(task)}; Realizado={(encontrado ? task.get(campo) : null)}");
        }
        Assert.True(total > 0);
    }

    [Fact]
    public void WbsReabreComoPercentualESomaGatesSemReutilizarValorAntigo()
    {
        using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
        var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, 7);
        LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Teste",
            new Dictionary<byte, string> { [30] = "X", [60] = "X" });
        layout.Worksheet.Cells[7, layout.Worksheet.Dimension.End.Column + 1].Value = "WBS (%)";
        var resolved = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "fixture", new List<string>());
        int col = resolved.ColunasComuns[LdpTipo2CabecalhosComuns.WbsPercent];
        layout.Worksheet.Cells[8, col].Value = 9902;
        layout.Worksheet.Cells[8, col].Style.Numberformat.Format = "0.00%";
        layout.Worksheet.Cells[8, layout.Gates[30].ColunaPlannedPhysical].Style.Numberformat.Format = "0.00%";
        string origem = Path.Combine(Path.GetTempPath(), $"hotfix2-{Guid.NewGuid()}.xlsx");
        string destino = Path.Combine(Path.GetTempPath(), $"hotfix2-{Guid.NewGuid()}.xlsx");
        try
        {
            File.WriteAllBytes(origem, pkg.GetAsByteArray());
            var itens = new[] {
                new CronogramaTipo2LdpPlanilhaAtualizacaoItem { NumeroDocumentoNaNormalizadoLdp = "DOC-001", GatePercentual = 30, PlannedPhysical = 10, PercentRealizado = 37.25m, WbsPercentPorWork = 10 },
                new CronogramaTipo2LdpPlanilhaAtualizacaoItem { NumeroDocumentoNaNormalizadoLdp = "DOC-001", GatePercentual = 60, WbsPercentPorWork = 25 }
            };
            var result = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, itens);
            Assert.True(result.Sucesso);
            Assert.Equal(1, result.CelulasWbsAtualizadas);
            using (var aberto = new ExcelPackage(new FileInfo(destino)))
            {
                var cell = aberto.Workbook.Worksheets[0].Cells[8, col];
                output.WriteLine($"WBS Value={cell.Value}; Text={cell.Text}; NumberFormat={cell.Style.Numberformat.Format}");
                Assert.Equal(0.35m, cell.GetValue<decimal>());
                Assert.Contains("35", cell.Text);
                Assert.Equal("0.00%", cell.Style.Numberformat.Format);
                var planned = aberto.Workbook.Worksheets[0].Cells[8, layout.Gates[30].ColunaPlannedPhysical];
                output.WriteLine($"Planned Value={planned.Value}; Text={planned.Text}; NumberFormat={planned.Style.Numberformat.Format}");
                Assert.Equal(0.10m, planned.GetValue<decimal>());
                Assert.Contains("10", planned.Text);
            }
            Assert.False(CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(destino, origem, itens).HouveAlteracao);
            itens[0].WbsPercentPorWork = 5;
            Assert.Equal(1, CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(destino, origem, itens).CelulasWbsAtualizadas);
        }
        finally { File.Delete(origem); File.Delete(destino); }
    }
}
