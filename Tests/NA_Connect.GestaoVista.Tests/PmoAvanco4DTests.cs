using APS.WebApp.Services.Reports;
namespace NA_Connect.GestaoVista.Tests;

public class PmoAvanco4DTests
{
    private static readonly DateTime Ref = new(2026, 9, 19);
    private static Avanco4DTarefaFonte T(string wbs, string? disc, decimal? prev, decimal? real, DateTime? fim = null, bool resumo = false)
        => new(wbs, "t" + wbs, null, disc, prev, real, new DateTime(2026, 1, 1), fim, null, resumo);
    private static Avanco4DReport Build(IReadOnlyList<Avanco4DTarefaFonte> t, IReadOnlyList<Avanco4DLeitura>? l = null)
        => PmoAvanco4DRules.Build("p", 2, Ref, new DateTime(2026, 9, 18, 10, 0, 0), t, l ?? [], "prev", "real");

    [Fact]
    public void RealizadoZeroEmTodasAsTarefasEhSemDadoNuncaZeroFalso()
    {
        var r = Build([T("1", "A", 100, 0), T("2", "A", 87, 0), T("3", "B", 50, 0)]);
        Assert.False(r.Disponivel);
        Assert.Contains("SEM DADO DE AVANÇO REAL", r.Motivo);
        Assert.Null(r.RealizadoGeral); Assert.Empty(r.Timeline); Assert.Empty(r.Tarefas); Assert.Empty(r.PorDisciplina);
        Assert.False(r.Diagnostico.RealizadoPreenchido); Assert.True(r.Diagnostico.PrevistoPreenchido);
    }

    [Fact]
    public void ZeroConfirmadoSoQuandoOProjetoTemRealizadoEmOutrasTarefas()
    {
        var r = Build([T("1", "A", 100, 100), T("2", "A", 60, 0), T("3", "B", 40, null), T("4", "B", 80, 50)]);
        Assert.True(r.Disponivel);
        Assert.Equal("ZERO_CONFIRMADO", r.Tarefas.Single(x => x.Wbs == "2").SituacaoRealizado);
        Assert.Equal("SEM_DADO", r.Tarefas.Single(x => x.Wbs == "3").SituacaoRealizado);
        Assert.Equal("SEM_DADO", r.Tarefas.Single(x => x.Wbs == "3").Situacao);
        Assert.Equal(1, r.Diagnostico.ZeroConfirmado); Assert.Equal(1, r.Diagnostico.SemRealizado);
    }

    [Fact]
    public void DesvioEmPontosPercentuaisRealizadoMenosPrevisto()
    {
        var r = Build([T("1", "A", 70, 55), T("2", "A", 40, 50)]);
        var a = r.Tarefas.Single(x => x.Wbs == "1"); var b = r.Tarefas.Single(x => x.Wbs == "2");
        Assert.Equal(-15m, a.DesvioPp); Assert.Equal("ATRASADA", a.Situacao);
        Assert.Equal(10m, b.DesvioPp); Assert.Equal("NO_PRAZO", b.Situacao);
        Assert.Equal(55m, r.PrevistoGeral); Assert.Equal(52.5m, r.RealizadoGeral); Assert.Equal(-2.5m, r.DesvioPp);
        Assert.Equal("Média simples das tarefas-folha consideradas", r.FonteGeral);
    }

    [Fact]
    public void ResumoNaoEntraEPorDisciplinaNormalizaEReconcilia()
    {
        var r = Build([T("0", null, 90, 90, resumo: true), T("1", "Piping", 80, 60), T("2", " PIPING ", 40, 20), T("3", null, 10, 10)]);
        Assert.Equal(3, r.Diagnostico.TarefasFolha);
        var piping = r.PorDisciplina.Single(d => d.Disciplina == "PIPING");
        Assert.Equal(60m, piping.Previsto); Assert.Equal(40m, piping.Realizado); Assert.Equal(-20m, piping.DesvioPp); Assert.Equal(2, piping.Tarefas);
        Assert.Contains(r.PorDisciplina, d => d.Disciplina == "SEM DISCIPLINA");
        Assert.Equal(r.Diagnostico.Consideradas, r.PorDisciplina.Sum(d => d.Tarefas));
    }

    [Fact]
    public void CoberturaAbaixoDoMinimoTornaIndisponivel()
    {
        var tarefas = new List<Avanco4DTarefaFonte> { T("1", "A", 100, 100) };
        for (var i = 2; i <= 10; i++) tarefas.Add(T(i.ToString(), "A", 0, 0));
        var r = Build(tarefas);
        Assert.False(r.Disponivel); Assert.Equal(10m, r.Diagnostico.CoberturaPct); Assert.Contains("mínimo 50%", r.Motivo);
    }

    [Fact]
    public void TimelineUsaUltimaLeituraDeCadaDiaOrdenadaEUsaRaizComoGeral()
    {
        var leituras = new[] {
            new Avanco4DLeitura(new(2026, 9, 16, 18, 0, 0), 58, 49), new Avanco4DLeitura(new(2026, 9, 15, 9, 0, 0), 40, null),
            new Avanco4DLeitura(new(2026, 9, 16, 9, 0, 0), 50, 45), new Avanco4DLeitura(new(2026, 9, 18, 19, 0, 0), 73.87m, 54.88m) };
        var r = Build([T("1", "A", 80, 60), T("2", "A", 50, 50)], leituras);
        Assert.Equal([new DateTime(2026, 9, 15), new DateTime(2026, 9, 16), new DateTime(2026, 9, 18)], r.Timeline.Select(p => p.Data));
        Assert.Equal(58m, r.Timeline[1].Previsto); Assert.Equal(49m, r.Timeline[1].Realizado); Assert.Null(r.Timeline[0].Realizado);
        Assert.Equal(73.87m, r.PrevistoGeral); Assert.Equal(54.88m, r.RealizadoGeral); Assert.Equal(-18.99m, r.DesvioPp);
        Assert.StartsWith("Resumo do projeto", r.FonteGeral);
    }

    [Fact]
    public void DiasDeAtrasoUsamADataDeCorteDeclaradaEValoresInvalidosNaoViramAvanco()
    {
        var r = Build([T("1", "A", 100, 40, fim: new DateTime(2026, 9, 8)), T("2", "A", 100, 100), T("3", "A", 150, 10)]);
        Assert.Equal(10, r.Tarefas.Single(x => x.Wbs == "1").DiasAtraso); // corte 18/09 − fim 08/09
        Assert.Null(r.Tarefas.Single(x => x.Wbs == "3").Previsto);
        Assert.Equal(1, r.Diagnostico.Invalidos);
        Assert.All(r.Tarefas.Where(x => x.DesvioPp.HasValue), x => Assert.False(decimal.MaxValue == x.DesvioPp));
    }
}
