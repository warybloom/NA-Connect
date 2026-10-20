using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
namespace NA_Connect.GestaoVista.Tests;

public class PmoCurvaSTests
{
    private static readonly DateTime Ref = new(2026, 9, 19);
    private static PmoDocumento Doc(string codigo, DateTime? baseline, DateTime? emissao = null, string? situacao = null, decimal? hh = null, decimal? custo = null)
        => PmoExecutiveRules.Completar(new("d" + codigo, "p", codigo, null, null, hh, custo, null, baseline, null, emissao, null, null, situacao, null, null), [], 1, Ref);

    [Fact]
    public void AcumuladoEOrdenadoMonotonicoENuncaNaN()
    {
        var docs = new[] { Doc("B", new(2026, 5, 10), new(2026, 5, 12), hh: 20, custo: 200), Doc("A", new(2026, 3, 1), new(2026, 3, 5), hh: 10, custo: 100), Doc("C", new(2026, 7, 1), hh: 30, custo: 300) };
        var r = PmoCurvaSRules.Build("p", Ref, docs, 60);
        Assert.Equal(r.Pontos.OrderBy(x => x.Data).Select(x => x.Data), r.Pontos.Select(x => x.Data));
        for (var i = 1; i < r.Pontos.Count; i++)
        {
            Assert.True(r.Pontos[i].PrevistoHh >= r.Pontos[i - 1].PrevistoHh);
            Assert.True(r.Pontos[i].RealizadoHh >= r.Pontos[i - 1].RealizadoHh);
            Assert.True(r.Pontos[i].PrevistoCusto >= r.Pontos[i - 1].PrevistoCusto);
            Assert.True(r.Pontos[i].EmitidosDocumentos >= r.Pontos[i - 1].EmitidosDocumentos);
        }
    }

    [Fact]
    public void PontoFinalReconciliaComOsTotais()
    {
        var docs = new[] { Doc("A", new(2026, 3, 1), new(2026, 3, 5), hh: 10, custo: 100), Doc("B", new(2026, 5, 10), new(2026, 5, 12), hh: 20, custo: 200), Doc("C", new(2026, 7, 1), hh: 30, custo: 300),
            Doc("X", new(2026, 4, 1), situacao: "CANCELED", hh: 5, custo: 50), Doc("S", null, hh: 7, custo: 70) };
        var r = PmoCurvaSRules.Build("p", Ref, docs, 72);
        var fim = r.Pontos[^1];
        Assert.Equal(60m, fim.PrevistoHh);          // A+B+C (cancelado e sem baseline fora)
        Assert.Equal(30m, fim.RealizadoHh);         // só emitidos A+B
        Assert.Equal(600m, fim.PrevistoCusto); Assert.Equal(300m, fim.RealizadoCusto);
        Assert.Equal(3, fim.PrevistoDocumentos); Assert.Equal(2, fim.EmitidosDocumentos);
        var c = r.Cobertura;
        Assert.Equal(1, c.Cancelados); Assert.Equal(1, c.SemBaseline);
        Assert.Equal(5m, c.HhCancelados); Assert.Equal(7m, c.HhSemBaseline);
        // total do projeto = curva + cancelados + sem baseline + sem documento
        Assert.Equal(72m, c.HhCurvaPrevisto + c.HhCancelados + c.HhSemBaseline + c.HhSemDocumento!.Value);
        Assert.Equal(0m, c.HhSemDocumento!.Value); // total da LDP = soma das linhas: nada fora dos documentos
    }

    [Fact]
    public void CanceladoNuncaEntraNemComoEmitido()
    {
        var docs = new[] { Doc("X", new(2026, 4, 1), new(2026, 4, 2), "CANCELADO", 8, 80) };
        var r = PmoCurvaSRules.Build("p", Ref, docs, 8);
        Assert.Empty(r.Pontos);
    }

    [Fact]
    public void DataInvalidaNaoViraPontoEDocumentoDuplicadoNaoDobra()
    {
        var docs = new[] { Doc("D", DateTime.MinValue, hh: 5, custo: 1), Doc("E", new(2026, 4, 1), hh: 9, custo: 9), Doc("E", new(2026, 4, 1), hh: 4, custo: 4) };
        var r = PmoCurvaSRules.Build("p", Ref, docs, 18);
        Assert.Equal(1, r.Cobertura.SemBaseline);
        Assert.Equal(2, r.Cobertura.Ambiguos);
        Assert.Equal(2, r.Pontos[^1].PrevistoDocumentos);
        Assert.Equal(13m, r.Pontos[^1].PrevistoHh); // linhas repetidas do mesmo código são linhas distintas da LDP, cada uma com seu HH
    }

    [Fact]
    public void PrevistoEEmitidoUsamMesmoEixoTemporal()
    {
        var docs = new[] { Doc("A", new(2026, 3, 1), new(2026, 3, 5), hh: 10, custo: 100) };
        var r = PmoCurvaSRules.Build("p", Ref, docs, 10);
        Assert.Equal(new DateTime(2026, 3, 1), r.Pontos[0].Data);
        Assert.Equal(new DateTime(2026, 3, 5), r.Pontos[1].Data);
        Assert.Equal(10m, r.Pontos[1].PrevistoHh); Assert.Equal(10m, r.Pontos[1].RealizadoHh);
    }
}
