using APS.WebApp.Services.Costs;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Regra única de custo/hora (cadastro de profissionais) usada pelo custo por documento, pela tela de HH sem documento (AT)
/// e pelo Resumo LAB. Caso real que motivou a correção: Antonio Clareti, CUSTO/HORA manual R$ 250 com remuneração/itens
/// zerados, 6 lançamentos AT (48 HH) mostrando R$ 0,00.
/// </summary>
public sealed class ProfessionalHourlyCostCatalogTests
{
    private static readonly DateOnly Inicio = new(2026, 3, 1);

    private static ProfessionalHourlyCostEntry Pro(int id, string alias, bool active = true, DateTime? inactiveAt = null) =>
        new(id, alias, active, inactiveAt, Array.Empty<(string, string)>());

    private static ProfessionalHourlyCostPeriod Periodo(int id, decimal? manual, decimal remuneracao = 0m, DateOnly? inicio = null, DateOnly? fim = null,
        params ProfessionalCostComponentRule[] itens) =>
        new(new ProfessionalCostPeriodRule(id, inicio ?? Inicio, fim, 160m, remuneracao, manual), itens);

    private static ProfessionalHourlyCostCatalog Catalogo(IEnumerable<ProfessionalHourlyCostEntry> pros, Dictionary<int, IReadOnlyList<ProfessionalHourlyCostPeriod>> periodos) =>
        new(pros.ToList(), periodos);

    private static ProfessionalHourlyCostCatalog Antonio() => Catalogo(
        new[] { Pro(32, "Antonio Clareti") },
        new() { [32] = new[] { Periodo(1, 250m) } });

    [Fact] // 1 + caso real: CUSTO/HORA manual vale mesmo com remuneração, encargos, softwares e computadores zerados.
    public void CustoHoraManual_ComDemaisCamposZerados_EhUsado()
    {
        var r = Antonio().Resolve("Antonio Clareti", new DateOnly(2026, 3, 3));
        Assert.Equal(ProfessionalHourlyCostStatus.Vigente, r.Status);
        Assert.Equal(250m, r.Rate);
        Assert.Equal(2000m, r.CostFor(8m));
    }

    [Fact] // Caso real: os 6 lançamentos AT de Antonio (03/03 a 18/03/2026, 8 HH cada) = 48 HH × 250 = R$ 12.000.
    public void AntonioClareti_SeisLancamentosAt_Totalizam12Mil()
    {
        var catalogo = Antonio();
        var datas = new[] { 3, 5, 6, 9, 16, 18 }.Select(d => new DateOnly(2026, 3, d));
        var custos = datas.Select(d => catalogo.Resolve("Antonio Clareti", d).CostFor(8m)).ToList();
        Assert.All(custos, c => Assert.Equal(2000m, c));
        Assert.Equal(12000m, custos.Sum());
    }

    [Fact] // 2 e 3: o custo não depende de o lançamento ter documento — a resolução só usa profissional + data.
    public void MesmoLancamento_ComOuSemDocumento_TemMesmoCusto()
    {
        var catalogo = Antonio();
        var comDocumento = catalogo.Resolve("Antonio Clareti", new DateOnly(2026, 3, 10)).CostFor(10m);
        var semDocumento = catalogo.Resolve("Antonio Clareti", new DateOnly(2026, 3, 10)).CostFor(10m);
        Assert.Equal(2500m, comDocumento);
        Assert.Equal(comDocumento, semDocumento);
    }

    [Fact] // 4 e 5: dois profissionais com custos diferentes no mesmo documento/AT — calcula por pessoa e soma.
    public void DoisProfissionais_CustosDiferentes_SomaIndividual()
    {
        var catalogo = Catalogo(
            new[] { Pro(1, "Profissional A"), Pro(2, "Profissional B") },
            new() { [1] = new[] { Periodo(1, 250m) }, [2] = new[] { Periodo(2, 150m) } });
        var dia = new DateOnly(2026, 3, 10);
        var total = catalogo.Resolve("Profissional A", dia).CostFor(10m) + catalogo.Resolve("Profissional B", dia).CostFor(5m);
        Assert.Equal(3250m, total);
    }

    [Fact] // Sem CustoHoraManual: composição mensal ÷ HH base (regra existente do cadastro).
    public void SemCustoHoraManual_UsaComposicaoMensal()
    {
        var catalogo = Catalogo(new[] { Pro(1, "Maria") },
            new() { [1] = new[] { Periodo(1, null, 16000m, itens: new ProfessionalCostComponentRule("PERCENTUAL", 0m, 50m, "REMUNERACAO")) } });
        Assert.Equal(150m, catalogo.Resolve("Maria", new DateOnly(2026, 3, 10)).Rate);
    }

    [Fact] // 6: profissional fora do cadastro → pendência explícita, nunca zero.
    public void ProfissionalSemCadastro_FicaPendente()
    {
        var r = Antonio().Resolve("Fulano Sem Cadastro", new DateOnly(2026, 3, 10));
        Assert.Equal(ProfessionalHourlyCostStatus.SemCadastro, r.Status);
        Assert.Null(r.Rate);
        Assert.Null(r.CostFor(8m));
    }

    [Fact] // 6: lançamento antes do início da vigência → pendente; não usa "custo atual" de outra data.
    public void LancamentoForaDaVigencia_FicaPendente()
    {
        var r = Antonio().Resolve("Antonio Clareti", new DateOnly(2026, 2, 27));
        Assert.Equal(ProfessionalHourlyCostStatus.SemVigencia, r.Status);
        Assert.Equal(32, r.ProfessionalId);
        Assert.Null(r.CostFor(8m));
    }

    [Fact] // 6: profissional inativo sem período (caso Leandro Meleiro) → pendente.
    public void ProfissionalInativoSemPeriodo_FicaPendente()
    {
        var catalogo = Catalogo(new[] { Pro(31, "Leandro Meleiro", active: false) }, new());
        Assert.Equal(ProfessionalHourlyCostStatus.SemCadastro, catalogo.Resolve("Leandro Meleiro", new DateOnly(2026, 3, 10)).Status);
    }

    [Fact] // Nome que casa com dois cadastros → ambíguo, sem escolher um silenciosamente.
    public void NomeAmbiguo_NaoEscolheProfissional()
    {
        var catalogo = Catalogo(new[] { Pro(1, "Silva"), Pro(2, "Joao Silva") },
            new() { [1] = new[] { Periodo(1, 100m) }, [2] = new[] { Periodo(2, 200m) } });
        var r = catalogo.Resolve("Joao Silva", new DateOnly(2026, 3, 10));
        Assert.Equal(ProfessionalHourlyCostStatus.Ambiguo, r.Status);
        Assert.Null(r.Rate);
    }

    [Fact] // Vigência histórica: o custo/hora é o do período que cobre a data.
    public void VigenciaHistorica_UsaPeriodoDaData()
    {
        var catalogo = Catalogo(new[] { Pro(1, "Ana") }, new()
        {
            [1] = new[] { Periodo(1, 100m, fim: new DateOnly(2026, 5, 31)), Periodo(2, 120m, inicio: new DateOnly(2026, 6, 1)) }
        });
        Assert.Equal(100m, catalogo.Resolve("Ana", new DateOnly(2026, 5, 31)).Rate);
        Assert.Equal(120m, catalogo.Resolve("Ana", new DateOnly(2026, 6, 1)).Rate);
    }

    [Fact] // 7 e 8: reprocessar/reclassificar o mesmo lançamento é determinístico — não duplica nem perde custo.
    public void ResolucaoEhDeterministica()
    {
        var catalogo = Antonio();
        var dia = new DateOnly(2026, 3, 5);
        Assert.Equal(catalogo.Resolve("Antonio Clareti", dia), catalogo.Resolve("ANTÔNIO CLARETI", dia));
        Assert.Equal(catalogo.Resolve("Antonio Clareti", dia).CostFor(8m), catalogo.Resolve("Antonio Clareti", dia).CostFor(8m));
    }

    [Fact]
    public void SemNomeOuSemData_FicaPendente()
    {
        Assert.Equal(ProfessionalHourlyCostStatus.SemNome, Antonio().Resolve(null, new DateOnly(2026, 3, 5)).Status);
        Assert.Equal(ProfessionalHourlyCostStatus.SemData, Antonio().Resolve("Antonio Clareti", null).Status);
    }
}
