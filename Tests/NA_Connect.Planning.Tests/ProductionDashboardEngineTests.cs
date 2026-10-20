using System.Text.RegularExpressions;
using APS.WebApp.Services.Reports.Dashboards;
using N = APS.WebApp.Services.Reports.Dashboards.ReportStatusNormalizer;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Fase 3 — motor dos dashboards (cross-filter), normalização PT/EN de relatório e contrato SQL das 12 definições.
/// </summary>
public class ProductionDashboardEngineTests
{
    private static DashboardRow Linha(string? disciplina, string? situacao, string? status, decimal? hh = null) =>
        new(new Dictionary<string, object?> { ["Discipline"] = disciplina, ["Situation"] = situacao, ["Document_Status"] = status, ["HH"] = hh });

    // Mistura PT/EN real: mesmo documento-tipo gravado em projetos de idiomas diferentes.
    private static readonly IReadOnlyList<DashboardRow> Ldp = new[]
    {
        Linha("MECÂNICA", "ISSUED", "CLIENT REVIEW", 10),
        Linha("Mecânica", "Emitido", "Em análise do cliente", 5),
        Linha("MECÂNICA", "TO BE ISSUED", "ELABORATION"),
        Linha("ELÉTRICA", "EMITIDO", "APPROVED", 8),
        Linha("ELÉTRICA", "CANCELED", "CANCELADO"),
        Linha("CIVIL", "A Emitir", "NÃO INICIADO", 2),
        Linha(null, "PENDING ISSUED", null),
    };

    private static DashboardDefinition Def()
    {
        var disc = new DashboardDimension("disciplina", "Disciplina", r => N.Texto(r["Discipline"]));
        var sit = new DashboardDimension("situacao", "Situação", r => N.Situation(r["Situation"]));
        var st = new DashboardDimension("status", "Status", r => N.StatusDoc(r["Document_Status"]));
        return new DashboardDefinition
        {
            Id = "teste", Title = "Teste", Source = "fixture", Sql = "SELECT 1 WHERE @projectId IS NOT NULL", LastSyncColumn = null,
            Dimensions = new[] { disc, sit, st },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total", "", DashboardEngine.Contagem()),
                new("emitidos", "Emitidos", "", DashboardEngine.ContagemOnde(sit, "EMITIDO"), FilterDimension: "situacao", FilterValue: "EMITIDO"),
                new("hh", "HH", "", DashboardEngine.SomaConhecida("HH"), DashboardFormats.Horas),
                new("pct", "%", "", DashboardEngine.PercentualOnde(sit, "EMITIDO"), DashboardFormats.Percentual),
            },
            Charts = new DashboardChart[]
            {
                new("porDisciplina", "Por disciplina", DashboardKinds.Barra, "disciplina"),
                new("porSituacao", "Por situação", DashboardKinds.Coluna, "situacao"),
                new("hhPorDisciplina", "HH por disciplina", DashboardKinds.Barra, "disciplina", "HH", DashboardEngine.SomaMedida("HH")),
            },
            Table = new[] { new DashboardColumn("disciplina", "Disciplina", DashboardFormats.Texto, r => r["Discipline"]) },
        };
    }

    private static Dictionary<string, IReadOnlySet<string>> F(params (string Dim, string Valor)[] f) =>
        f.GroupBy(x => x.Dim).ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(x => x.Valor).ToHashSet());

    private static decimal? Kpi(DashboardResult r, string key) => r.Kpis.Single(k => k.Key == key).Value;
    private static DashboardChartResult Grafico(DashboardResult r, string key) => r.Charts.Single(c => c.Key == key);

    // ------------------------------------------------------------------ normalização PT/EN ----
    [Theory]
    [InlineData("ISSUED", "EMITIDO")]
    [InlineData("Emitido", "EMITIDO")]
    [InlineData(" to be issued ", "A_EMITIR")]
    [InlineData("A EMITIR", "A_EMITIR")]
    [InlineData("CANCELED", "CANCELADO")]
    public void Situacao_EquivalenciasPtEn(string bruto, string chave) => Assert.Equal(chave, N.Situation(bruto).Key);

    [Theory]
    [InlineData("CLIENT REVIEW", "EM_ANALISE_CLIENTE")]
    [InlineData("Em análise do cliente", "EM_ANALISE_CLIENTE")]
    [InlineData("EM ANALISE DO CLIENTE", "EM_ANALISE_CLIENTE")]
    [InlineData("ADRESSING COMMENTS", "ATENDIMENTO_COMENTARIOS")]
    [InlineData("Atendimento de comentários", "ATENDIMENTO_COMENTARIOS")]
    [InlineData("ELABORANDO", "EM_ELABORACAO")]
    [InlineData("ISSUANCE FLOW", "EM_FLUXO")]
    [InlineData("NÃO INICIADO", "NAO_INICIADO")]
    public void StatusDocumental_EquivalenciasPtEn(string bruto, string chave) => Assert.Equal(chave, N.StatusDoc(bruto).Key);

    [Theory]
    [InlineData("PENDING ISSUED")]
    [InlineData("LIBERADO PARA CONSTRUÇÃO")]
    [InlineData("INFORMATIVO")]
    public void ValorSemParComprovado_NaoEhMapeado(string bruto)
    {
        Assert.Equal(bruto, N.StatusDoc(bruto).Label);
        Assert.Equal(bruto, N.Situation(bruto).Label);
    }

    [Fact]
    public void Vazio_ViraNaoInformado() => Assert.Equal((N.NullKey, N.NullLabel), N.StatusDoc("  "));

    // ---------------------------------------------------------------------- cross-filter ----
    [Fact]
    public void SemFiltro_TudoSobreOConjuntoInteiro()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(), 1, 50);
        Assert.Equal(7, Kpi(r, "total"));
        Assert.Equal(3, Kpi(r, "emitidos"));                     // ISSUED + Emitido + EMITIDO agrupados
        Assert.Equal(25, Kpi(r, "hh"));                          // só valores conhecidos
        Assert.Equal(3, Grafico(r, "porDisciplina").Points.Single(p => p.Key == "MECANICA").Value); // MECÂNICA/Mecânica juntos
        Assert.Empty(r.ActiveFilters);
    }

    [Fact]
    public void FiltroDeDisciplina_RecalculaKpisTabelaEOutrosGraficos()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(("disciplina", "MECANICA")), 1, 50);
        Assert.Equal(3, Kpi(r, "total"));
        Assert.Equal(2, Kpi(r, "emitidos"));
        Assert.Equal(3, r.Table.Total);
        Assert.Equal(new[] { "EMITIDO", "A_EMITIR" }, Grafico(r, "porSituacao").Points.Select(p => p.Key));
        Assert.Equal("MECÂNICA", r.ActiveFilters.Single().Values.Single().Label);   // rótulo = primeira grafia real
    }

    [Fact]
    public void GraficoDeOrigem_MantemContextoParaTrocarASelecao()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(("disciplina", "MECANICA")), 1, 50);
        var origem = Grafico(r, "porDisciplina");
        Assert.Equal(4, origem.Points.Count);                    // todas as disciplinas continuam visíveis
        Assert.True(origem.Points.Single(p => p.Key == "MECANICA").Selected);
        Assert.All(origem.Points.Where(p => p.Key != "MECANICA"), p => Assert.False(p.Selected));
    }

    [Fact]
    public void CombinacaoDisciplinaMaisSituacao_AndEntreDimensoes()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(("disciplina", "MECANICA"), ("situacao", "EMITIDO")), 1, 50);
        Assert.Equal(2, Kpi(r, "total"));
        Assert.Equal(15, Kpi(r, "hh"));
        Assert.True(r.Kpis.Single(k => k.Key == "emitidos").Selected);
        // gráfico de situação ignora o próprio filtro, mas respeita o de disciplina
        Assert.Equal(new[] { "EMITIDO", "A_EMITIR" }, Grafico(r, "porSituacao").Points.Select(p => p.Key));
        // gráfico de disciplina ignora o próprio filtro, mas respeita o de situação
        Assert.Equal(new[] { "MECANICA", "ELETRICA" }, Grafico(r, "porDisciplina").Points.Select(p => p.Key));
        Assert.Equal(2, r.ActiveFilters.Count);
    }

    [Fact]
    public void DoisValoresNaMesmaDimensao_OrEntreValores()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(("disciplina", "MECANICA"), ("disciplina", "CIVIL")), 1, 50);
        Assert.Equal(4, Kpi(r, "total"));
    }

    [Fact]
    public void LimparFiltros_VoltaAoConjuntoInteiro()
    {
        var comFiltro = DashboardEngine.Avaliar(Def(), Ldp, F(("situacao", "CANCELADO")), 1, 50);
        var limpo = DashboardEngine.Avaliar(Def(), Ldp, F(), 1, 50);
        Assert.Equal(1, Kpi(comFiltro, "total"));
        Assert.Equal(7, Kpi(limpo, "total"));
        Assert.All(limpo.Charts.SelectMany(c => c.Points), p => Assert.False(p.Selected));
    }

    [Fact]
    public void GraficoDeSoma_OmiteGruposSemValorConhecido()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(), 1, 50);
        var hh = Grafico(r, "hhPorDisciplina");
        Assert.DoesNotContain(hh.Points, p => p.Key == N.NullKey);   // linha sem disciplina tem HH vazio
        Assert.Equal(15, hh.Points.Single(p => p.Key == "MECANICA").Value);
    }

    [Fact]
    public void Percentual_SobreOConjuntoFiltrado()
    {
        Assert.Equal(42.9m, Kpi(DashboardEngine.Avaliar(Def(), Ldp, F(), 1, 50), "pct"));
        Assert.Equal(50m, Kpi(DashboardEngine.Avaliar(Def(), Ldp, F(("disciplina", "ELETRICA")), 1, 50), "pct"));
        Assert.Null(Kpi(DashboardEngine.Avaliar(Def(), Array.Empty<DashboardRow>(), F(), 1, 50), "pct"));
    }

    [Fact]
    public void Tabela_PaginadaSobreOConjuntoFiltrado()
    {
        var r = DashboardEngine.Avaliar(Def(), Ldp, F(), 2, 3);
        Assert.Equal(7, r.Table.Total);
        Assert.Equal(3, r.Table.Rows.Count);
    }

    [Fact]
    public void FiltroDeDimensaoDesconhecida_Rejeitado() =>
        Assert.Throws<ArgumentException>(() => DashboardEngine.ValidarFiltros(Def(),
            new Dictionary<string, IReadOnlyList<string>> { ["custo"] = new[] { "1" } }));

    // ------------------------------------------------------------ contrato das 12 definições ----
    public static TheoryData<string> Ids => new(ProductionDashboards.Todos.Keys);

    [Fact]
    public void AsDozeTelasTemDashboard() => Assert.Equal(
        new[] { "cap-externo", "cap-interno", "change-order", "controle-documental", "controle-programacao", "desvio-escopo",
                "desvio-qualidade", "hold", "ldp", "nivelamento-programacao", "programacao", "rnc" },
        ProductionDashboards.Todos.Keys.OrderBy(k => k));

    [Theory, MemberData(nameof(Ids))]
    public void Sql_UmSelectSomenteLeituraComEscopoDeProjeto(string id)
    {
        foreach (var sql in new[] { ProductionDashboards.Todos[id].Sql, ProductionDashboards.Todos[id].ComplementSql }.OfType<string>())
            AssertSelectSeguro(sql);
    }

    private static void AssertSelectSeguro(string sql)
    {
        Assert.False(Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|INTO)\b", RegexOptions.IgnoreCase));
        Assert.Contains("Project_ID_ACC = @projectId", sql);
        Assert.Single(sql.Split(';', StringSplitOptions.RemoveEmptyEntries), s => !string.IsNullOrWhiteSpace(s));
        Assert.DoesNotContain("{", sql);
    }

    [Theory, MemberData(nameof(Ids))]
    public void KpisClicaveis_ApontamParaDimensoesReais(string id)
    {
        var def = ProductionDashboards.Todos[id];
        foreach (var k in def.Kpis.Where(k => k.FilterDimension != null))
            Assert.Contains(def.Dimensions, d => d.Key == k.FilterDimension);
        foreach (var c in def.Charts) Assert.Contains(def.Dimensions, d => d.Key == c.Dimension);
    }

    [Theory, MemberData(nameof(Ids))]
    public void NenhumaDashboardExibeCusto(string id)
    {
        var def = ProductionDashboards.Todos[id];
        var textos = def.Kpis.Select(k => k.Label).Concat(def.Charts.Select(c => c.Title)).Concat(def.Table.Select(c => c.Label));
        Assert.DoesNotContain(textos, t => Regex.IsMatch(t, "cost|custo", RegexOptions.IgnoreCase));
        // Colunas PROJETADAS pelo SELECT externo (o PmoLdpSnapshot embutido lê custo internamente, mas não o expõe).
        var projetadas = Regex.Match(def.Sql, @"SELECT\s+(.*?)\s+FROM", RegexOptions.Singleline).Groups[1].Value;
        Assert.DoesNotMatch("(?i)cost", projetadas);
    }
}
