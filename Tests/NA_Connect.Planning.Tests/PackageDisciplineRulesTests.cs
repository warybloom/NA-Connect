using APS.WebApp.Services.Costs;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>Pacote Por Disciplina: rateio igualitário pela LDP atual, DE/PARA com a LDP e apropriação com teto sem alterar o Timesheet.</summary>
public sealed class PackageDisciplineRulesTests
{
    private const string SemDisciplina = "SEM DISCIPLINA";

    private static LdpPackageDocument Doc(int n, string discipline = "Processo", bool cancelled = false, decimal? hh = 95m) =>
        new($"P-000-PR0-DE-{n:000}", discipline, $"Documento {n}", cancelled ? "CANCELADO" : "EMITIDO", cancelled, hh, hh * 100m);

    private static List<LdpPackageDocument> Ldp(int count, string discipline = "Processo", int start = 1) =>
        Enumerable.Range(start, count).Select(i => Doc(i, discipline)).ToList();

    private static PackageDisciplineInput Processo(decimal hh, decimal cost = 0m, string scope = PackageModel.Todos, IReadOnlyList<string>? docs = null, string? why = null) =>
        new("Processo", hh, cost, scope, docs, why);

    [Fact]
    public void MilHoras_DezDocumentos_CemPorDocumento()
    {
        var plan = PackageDisciplineRules.Plan(Processo(1000m, 150000m), Ldp(10), SemDisciplina);
        Assert.True(plan.Valido);
        Assert.Equal(10, plan.DocumentosElegiveis);
        Assert.All(plan.Documentos, d => Assert.Equal(100m, d.HHPrevisto));
        Assert.All(plan.Documentos, d => Assert.Equal(15000m, d.CustoPrevisto));
    }

    [Fact]
    public void DezDocumentosNovos_RecalculaCinquenta_SemAlterarTotal()
    {
        var plan = PackageDisciplineRules.Plan(Processo(1000m), Ldp(10).Concat(Ldp(10, start: 11)).ToList(), SemDisciplina);
        Assert.Equal(20, plan.DocumentosElegiveis);
        Assert.All(plan.Documentos, d => Assert.Equal(50m, d.HHPrevisto));
        Assert.Equal(1000m, plan.Documentos.Sum(d => d.HHPrevisto));
    }

    [Fact]
    public void Cancelados_SaemDoRateio_TotalRedistribuido_EVinculoVisivel()
    {
        var ldp = Ldp(10); ldp[0] = Doc(1, cancelled: true); ldp[1] = Doc(2, cancelled: true);
        var plan = PackageDisciplineRules.Plan(Processo(1000m), ldp, SemDisciplina);
        Assert.Equal(8, plan.DocumentosElegiveis);
        Assert.All(plan.Documentos, d => Assert.Equal(125m, d.HHPrevisto));
        Assert.Equal(2, plan.Excluidos.Count);
        Assert.DoesNotContain(plan.Documentos, d => d.DocumentoCodigo == ldp[0].Codigo);
    }

    [Fact]
    public void Arredondamento_FechaExatamenteOTotal()
    {
        var hh = PackageDisciplineRules.EqualSplit(1000m, 3, PackageDisciplineRules.HoursDecimals);
        var cost = PackageDisciplineRules.EqualSplit(100m, 3, PackageDisciplineRules.MoneyDecimals);
        Assert.Equal(1000m, hh.Sum()); Assert.Equal(100m, cost.Sum());
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, cost);
        Assert.Equal(10.123457m, PackageDisciplineRules.EqualSplit(10.123457m, 1, 2).Single());
    }

    [Fact]
    public void DivergenciaDe50Horas_PermiteSalvarComAlerta_ELdpPreservada()
    {
        var ldp = Ldp(10); // 10 × 95 = 950 HH na LDP
        var plan = PackageDisciplineRules.Plan(Processo(1000m, why: "Proposta revisada"), ldp, SemDisciplina);
        Assert.True(plan.Valido);
        Assert.Equal(950m, plan.HHLdp); Assert.Equal(50m, plan.DivergenciaHH);
        Assert.Contains(plan.Avisos, a => a.Contains("Divergência"));
        Assert.Equal("Proposta revisada", plan.Justificativa);
        Assert.All(ldp, d => Assert.Equal(95m, d.PlannedHH)); // nada é escrito na LDP
    }

    [Fact]
    public void Selecionados_NaoAceitaDocumentoDeOutraDisciplina()
    {
        var ldp = Ldp(3).Concat(Ldp(2, "Mecânica", 50)).ToList();
        var ok = PackageDisciplineRules.Plan(Processo(100m, scope: PackageModel.Selecionados, docs: new[] { ldp[0].Codigo, ldp[1].Codigo }), ldp, SemDisciplina);
        Assert.True(ok.Valido); Assert.Equal(2, ok.DocumentosElegiveis); Assert.All(ok.Documentos, d => Assert.Equal(50m, d.HHPrevisto));
        var wrong = PackageDisciplineRules.Plan(Processo(100m, scope: PackageModel.Selecionados, docs: new[] { ldp[0].Codigo, ldp[3].Codigo }), ldp, SemDisciplina);
        Assert.False(wrong.Valido);
        Assert.Contains(wrong.Erros, e => e.Contains("Mecânica"));
    }

    [Fact]
    public void SemDocumentosElegiveis_NaoPermiteSalvar()
    {
        var ldp = new List<LdpPackageDocument> { Doc(1, cancelled: true) };
        var preview = PackageDisciplineRules.Preview(new(null, "Everton", "Everton", null, new[] { Processo(100m) }), ldp, SemDisciplina);
        Assert.False(preview.Valido);
        var repeated = PackageDisciplineRules.Preview(new(null, "Everton", "Everton", null, new[] { Processo(100m), Processo(50m) }), Ldp(2), SemDisciplina);
        Assert.False(repeated.Valido);
    }

    [Fact]
    public void Timesheet600HorasSemDocumento_RateiaSessentaPorDocumento()
    {
        var docs = Ldp(10).Select(d => (d.Codigo, 100m)).ToList();
        var result = PackageDisciplineRules.Appropriate(docs, new Dictionary<string, decimal>(), 600m);
        Assert.All(result, d => { Assert.Equal(60m, d.HHBruto); Assert.Equal(60m, d.HHApropriado); Assert.Equal(0m, d.HHExcedente); Assert.Equal(40m, d.HHSaldo); });
    }

    [Fact]
    public void Timesheet1200Para1000_Apropria1000_Excedente200_BrutoPreservado()
    {
        var docs = Ldp(10).Select(d => (d.Codigo, 100m)).ToList();
        var result = PackageDisciplineRules.Appropriate(docs, new Dictionary<string, decimal>(), 1200m);
        Assert.Equal(1200m, result.Sum(x => x.HHBruto));
        Assert.Equal(1000m, result.Sum(x => x.HHApropriado));
        Assert.Equal(200m, result.Sum(x => x.HHExcedente));
        Assert.Equal(0m, result.Sum(x => x.HHSaldo));
        var money = CostAllocationRules.Recognize(120m, 100m, 1000m, null); // regra financeira existente
        Assert.Equal(1000m, money.RecognizedCost); Assert.Equal(200m, money.ExcessCost);
    }

    [Fact]
    public void HoraComDocumento_NaoEntraNoRateioSemDocumento()
    {
        var docs = Ldp(2).Select(d => (d.Codigo, 100m)).ToList();
        var direct = new Dictionary<string, decimal> { [docs[0].Codigo] = 30m };
        var result = PackageDisciplineRules.Appropriate(docs, direct, 20m);
        Assert.Equal(40m, result[0].HHBruto); Assert.Equal(30m, result[0].HHDireto); Assert.Equal(10m, result[0].HHRateado);
        Assert.Equal(10m, result[1].HHBruto);
        Assert.Equal(50m, result.Sum(x => x.HHBruto)); // 30 + 20, sem dupla contagem
    }

    [Fact]
    public void ClassificaLinhasDoTimesheet_SemDuplaContagemEComAmbiguidade()
    {
        var ldp = Ldp(2).Concat(Ldp(1, "Mecânica", 50)).ToList();
        var eligible = new Dictionary<string, IReadOnlySet<string>> { ["Processo"] = ldp.Take(2).Select(d => d.Codigo).ToHashSet() };
        var everton = new DisciplinePackageMatcher(1, "Everton", Array.Empty<(string, string)>(), eligible);
        var rows = new List<TimesheetWork>
        {
            new("Everton Silva", ldp[0].Codigo, 8m),   // direto
            new("Everton Silva", "", 6m),               // sem documento → rateio
            new("Everton Silva", ldp[2].Codigo, 4m),    // documento de outra disciplina
            new("Ana Souza", "", 5m),                   // não é do pacote
        };
        var result = PackageDisciplineRules.AssignRows(rows, Enumerable.Range(0, rows.Count), new[] { everton },
            ldp.Select(d => d.Codigo).ToHashSet(), _ => null);
        Assert.Equal(3, result.Count);
        Assert.Equal(DisciplineRowKind.Direto, result.Single(x => x.RowIndex == 0).Kind);
        Assert.Equal(DisciplineRowKind.SemDocumento, result.Single(x => x.RowIndex == 1).Kind);
        Assert.Equal(DisciplineRowKind.ForaDaAbrangencia, result.Single(x => x.RowIndex == 2).Kind);
        var twin = everton with { PackageId = 2 };
        var ambiguous = PackageDisciplineRules.AssignRows(rows, new[] { 1 }, new[] { everton, twin }, new HashSet<string>(), _ => null);
        Assert.Equal(DisciplineRowKind.Ambiguo, ambiguous.Single().Kind);
        Assert.Null(ambiguous.Single().PackageId);
    }

    [Fact]
    public void VariasDisciplinas_LinhaSemDocumentoUsaDisciplinaDoTimesheetOuFicaPendente()
    {
        var eligible = new Dictionary<string, IReadOnlySet<string>> { ["Processo"] = new HashSet<string> { "A" }, ["Mecânica"] = new HashSet<string> { "B" } };
        var package = new DisciplinePackageMatcher(1, "Everton", Array.Empty<(string, string)>(), eligible);
        var rows = new List<TimesheetWork> { new("Everton", "", 2m, Disciplina: "MECANICA"), new("Everton", "", 3m) };
        var result = PackageDisciplineRules.AssignRows(rows, new[] { 0, 1 }, new[] { package }, new HashSet<string>(), r => r.Disciplina);
        Assert.Equal("Mecânica", result[0].Disciplina); Assert.Equal(DisciplineRowKind.SemDocumento, result[0].Kind);
        Assert.Equal(DisciplineRowKind.DisciplinaIndefinida, result[1].Kind);
    }

    [Fact]
    public void SemDisciplina_NaoFormaPacote()
    {
        var plan = PackageDisciplineRules.Plan(new PackageDisciplineInput(SemDisciplina, 10m, 0m, null, null, null), Ldp(2, SemDisciplina), SemDisciplina);
        Assert.False(plan.Valido);
    }
}
