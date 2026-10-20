using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
namespace NA_Connect.GestaoVista.Tests;

public class GestaoVistaDocumentStatusTests
{
    private static readonly DateTime Hoje = new(2026, 9, 19);
    private static PmoDocumento Doc(DateTime? baseline = null, DateTime? replan = null, DateTime? emissao = null, string? situacao = null)
        => new("d", "p", "DOC", null, null, null, null, null, baseline, replan, emissao, null, null, situacao, null, null);
    private static string Status(PmoDocumento d) => PmoExecutiveRules.Completar(d, [], 1, Hoje).StatusDocumento;

    [Fact]
    public void ParticaoEExclusivaEmitidoNuncaEhNoPrazoNemAtrasado()
    {
        Assert.Equal("EMITIDO", Status(Doc(baseline: Hoje.AddDays(-30), emissao: Hoje.AddDays(-1))));
        Assert.Equal("EMITIDO", Status(Doc(baseline: Hoje.AddDays(10), emissao: Hoje.AddDays(-1))));
        Assert.Equal("EMITIDO", Status(Doc(emissao: Hoje)));
    }

    [Fact]
    public void NaoEmitidoComparaDataAlvoComHoje()
    {
        Assert.Equal("NO_PRAZO", Status(Doc(replan: Hoje)));
        Assert.Equal("NO_PRAZO", Status(Doc(baseline: Hoje.AddDays(1))));
        Assert.Equal("ATRASADO", Status(Doc(baseline: Hoje.AddDays(-1))));
        Assert.Equal("SEM_PRAZO", Status(Doc()));
    }

    [Fact]
    public void DataSentinelaNuncaEhPrazoReal()
    {
        Assert.Equal("SEM_PRAZO", Status(Doc(baseline: DateTime.MinValue, replan: new DateTime(1900, 1, 1))));
        Assert.Equal("EMITIDO", Status(Doc(baseline: DateTime.MinValue, emissao: Hoje)));
    }

    [Fact]
    public void CanceladoTemPrioridadeSobreEmissaoEPrazo()
    {
        Assert.Equal("CANCELADO", Status(Doc(baseline: Hoje.AddDays(-5), emissao: Hoje.AddDays(-1), situacao: "CANCELED")));
    }

    [Fact]
    public void CancelamentoTambemPeloStatusInternoEHoldNaoEhCancelamento()
    {
        Assert.Equal("CANCELADO", Status(Doc(baseline: Hoje.AddDays(-5)) with { StatusInterno = "Cancelado" }));
        Assert.Equal("CANCELADO", Status(Doc() with { StatusCliente = "CANCELADO" }));
        Assert.Equal("ATRASADO", Status(Doc(baseline: Hoje.AddDays(-5)) with { StatusInterno = "HOLD" }));
    }

    [Fact]
    public void TodoDocumentoCaiEmExatamenteUmaCategoria()
    {
        var docs = new[] { Doc(emissao: Hoje), Doc(baseline: Hoje), Doc(baseline: Hoje.AddDays(-1)), Doc(), Doc(situacao: "CANCELED"),
            Doc(baseline: Hoje.AddDays(-9), emissao: Hoje.AddDays(-2), situacao: "CANCELADO"), Doc(replan: DateTime.MinValue) };
        var validas = new[] { "CANCELADO", "EMITIDO", "NO_PRAZO", "ATRASADO", "SEM_PRAZO" };
        var por = docs.Select(Status).ToArray();
        Assert.All(por, x => Assert.Contains(x, validas));
        Assert.Equal(docs.Length, validas.Sum(v => por.Count(x => x == v)));
    }

    [Fact]
    public void StatusPrazoHomologadoNaoMudou()
    {
        var emitidoTarde = PmoExecutiveRules.Completar(Doc(baseline: Hoje.AddDays(-30), emissao: Hoje.AddDays(-1)), [], 1, Hoje);
        Assert.Equal("ATRASADO", emitidoTarde.StatusPrazo);
    }

    [Fact]
    public void AvancoFisicoUsaPercentualDaLdpNoTipo1ECronogramaComoFallback()
    {
        var t = new PmoTarefa("t", "p", "DOC", null, 40m, null, null, null, null, true, false, 1m);
        var comLdp = PmoExecutiveRules.Completar(Doc() with { PercentFisicoLdp = 75m }, [t], 1, Hoje);
        Assert.Equal(75m, comLdp.AvancoFisico); Assert.Equal("LDP_PERCENT_PHYSICAL_COMPLETED", comLdp.FonteAvancoFisico);
        var soCronograma = PmoExecutiveRules.Completar(Doc(), [t], 1, Hoje);
        Assert.Equal(40m, soCronograma.AvancoFisico); Assert.Equal("CRONOGRAMA_PERCENT_PHYSICAL_COMPLETED", soCronograma.FonteAvancoFisico);
        var semDado = PmoExecutiveRules.Completar(Doc(), [], 1, Hoje);
        Assert.Null(semDado.AvancoFisico); Assert.Equal("SEM_DADO", semDado.FonteAvancoFisico);
    }

    [Fact]
    public void AvancoFisicoDoTipo2NaoUsaLdp()
    {
        var r = PmoExecutiveRules.Completar(Doc() with { PercentFisicoLdp = 75m }, [], 2, Hoje);
        Assert.Null(r.AvancoFisico);
    }

    [Fact]
    public void PercentualLdpInvalidoNaoSubstituiCronograma()
    {
        var t = new PmoTarefa("t", "p", "DOC", null, 40m, null, null, null, null, true, false, 1m);
        Assert.Equal(40m, PmoExecutiveRules.Completar(Doc() with { PercentFisicoLdp = 250m }, [t], 1, Hoje).AvancoFisico);
    }
}
