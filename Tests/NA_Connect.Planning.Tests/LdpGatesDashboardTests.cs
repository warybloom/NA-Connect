using APS.WebApp.Services.Reports.Dashboards;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// LDP Tipo 1 × Tipo 2 (gates 30/60/90) no dashboard Controle de Produção → LDP (Gerencial e Operacional usam a
/// mesma definição). Valores de status/situação = combinações reais auditadas na VW_Ldp_Documento_Gates_Ultima_Versao.
/// </summary>
public class LdpGatesDashboardTests
{
    private static Dictionary<string, object?> Doc(long id, int tipo, string na, DateTime? emissao = null, string? status = null, string? situacao = null,
        string? flow = null) => new()
    {
        ["ID_Registro"] = id, ["Item"] = id.ToString(), ["Discipline"] = "PIPING", ["NA_Document_Number"] = na, ["Document_Title"] = "T" + id,
        ["Format"] = "A1", ["Document_Status"] = status, ["Status_In_Flow"] = flow, ["Situation"] = situacao, ["Date_Comments_Received"] = null,
        ["Replanned_Date"] = null, ["Emission_Date"] = emissao, ["Transmittal_Number"] = null, ["Issued_Purpose"] = null, ["Revision"] = null,
        ["Data_Sincronizacao"] = new DateTime(2026, 9, 29), ["Tipo_LDP"] = tipo,
    };

    private static Dictionary<string, object?> Gate(long id, int p, bool ativo, string? status, string? situacao) => new()
    {
        ["Ldp_Registro_Id"] = id, ["Gate_Percentual"] = (byte)p, ["Gate_Ativo"] = ativo, ["Document_Status"] = status, ["Situation"] = situacao,
    };

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> F(params (string D, string V)[] f) =>
        f.GroupBy(x => x.D).ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(x => x.V).ToHashSet());

    private static DashboardResult Avaliar(List<Dictionary<string, object?>> docs, List<Dictionary<string, object?>> gates,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? filtros = null)
    {
        var def = ProductionDashboards.Todos["ldp"];
        def.Merge!(docs, gates);
        var rows = docs.Select(d => new DashboardRow(d)).ToList();
        var validos = DashboardEngine.ValidarFiltros(def, (filtros ?? F()).ToDictionary(f => f.Key, f => (IReadOnlyList<string>)f.Value.ToList()));
        return DashboardEngine.Avaliar(def, rows, validos, 1, 50);
    }

    private static decimal Celula(DashboardResult r, string linha, string gate) =>
        r.Matrices!.Single().Cells.Single(c => c.Row == linha && c.Column == gate).Value;

    // Cenário Tipo 2 com combinações reais dos gates:
    //  A: 30 APPROVED/ISSUED · 60 CLIENT REVIEW/ISSUED · 90 NOT STARTED/TO BE ISSUED   (emitido em 30 e 60)
    //  B: 30 APPROVED/ISSUED · 60 CANCELLED/CANCELLED  · 90 inativo                     (cancelado só no 60)
    //  C: 30 inativo (com valores!) · 60 ELABORATION/TO BE ISSUED · 90 CANCELED/CANCELED (um L)
    private static (List<Dictionary<string, object?>> Docs, List<Dictionary<string, object?>> Gates) CenarioTipo2() => (
        new() { Doc(1, 2, "A", emissao: new DateTime(2026, 5, 1)), Doc(2, 2, "B", emissao: new DateTime(2026, 5, 2)), Doc(3, 2, "C") },
        new()
        {
            Gate(1, 30, true, "APPROVED", "ISSUED"), Gate(1, 60, true, "CLIENT REVIEW", "ISSUED"), Gate(1, 90, true, "NOT STARTED", "TO BE ISSUED"),
            Gate(2, 30, true, "APPROVED", "ISSUED"), Gate(2, 60, true, "CANCELLED", "CANCELLED"), Gate(2, 90, false, null, null),
            Gate(3, 30, false, "ELABORATION", "TO BE ISSUED"), Gate(3, 60, true, "ELABORATION", "TO BE ISSUED"), Gate(3, 90, true, "CANCELED", "CANCELED"),
        });

    // =================================================================================== TIPO 1 ====
    [Fact]
    public void Tipo1_SemMatriz_SemColunasDeGate_KpisPadrao()
    {
        var docs = new List<Dictionary<string, object?>>
        {
            Doc(1, 1, "A", emissao: new DateTime(2026, 5, 1), status: "APPROVED", situacao: "ISSUED"),
            Doc(2, 1, "B", status: "CANCELED", situacao: "CANCELED"),
            Doc(3, 1, "C", status: "CLIENT REVIEW", situacao: "TO BE ISSUED"),
        };
        var r = Avaliar(docs, new());
        Assert.Null(r.Matrices);
        Assert.Equal(1, Convert.ToInt32(r.Meta!["tipoLdp"]));
        Assert.Equal(new[] { "total", "emitidos", "cancelados", "revisaoCliente", "aEmitir" }, r.Kpis.Select(k => k.Key));
        Assert.Equal(new decimal?[] { 3, 1, 1, 1, 1 }, r.Kpis.Select(k => k.Value));
        Assert.DoesNotContain(r.Table.Columns, c => c.Key.StartsWith("gate3") || c.Key.StartsWith("gate6") || c.Key.StartsWith("gate9") || c.Label.Contains('%'));
        Assert.DoesNotContain(r.Dimensions, d => d.Key.StartsWith("gate"));           // nada de gate na resposta
        Assert.DoesNotContain(r.Table.Columns, c => c.Label.Contains("Gate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(r.Kpis, k => k.Detail.Contains("gate", StringComparison.OrdinalIgnoreCase) || k.Label.Contains("gate", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "porGestao", "porStatus", "porSituacao", "porDisciplina" }, r.Charts.Select(c => c.Key));
        Assert.Equal("CANCELADO", r.Charts.Single(c => c.Key == "porStatus").Points.Select(p => p.Key).Single(k => k == "CANCELADO"));
    }

    [Fact]
    public void Tipo1_FiltroDeGateEnviadoPorEngano_EhIgnorado()
    {
        var r = Avaliar(new() { Doc(1, 1, "A"), Doc(2, 1, "B") }, new(), F(("gate60", "EMITIDO")));
        Assert.Equal(2, r.FilteredRows);
        Assert.Empty(r.ActiveFilters);
    }

    // =================================================================================== TIPO 2 ====
    [Fact]
    public void Tipo2_MatrizPorFase_SemDuplicarDocumentos()
    {
        var (docs, gates) = CenarioTipo2();
        var r = Avaliar(docs, gates);
        Assert.Equal(2, Convert.ToInt32(r.Meta!["tipoLdp"]));
        var m = Assert.Single(r.Matrices!);
        Assert.Equal(new[] { "30", "60", "90" }, m.Columns.Select(c => c.Key));
        Assert.Equal(new[] { "emitidos", "cancelados", "analiseCliente", "aEmitir" }, m.Rows.Select(x => x.Key));

        Assert.Equal(3, r.Kpis.Single(k => k.Key == "total").Value);             // 1 por documento
        Assert.Equal(3, r.Table.Total);                                          // tabela: 1 linha por documento
        // A emitido em 30 e 60: conta nos dois gates (não é duplicidade).
        Assert.Equal(2, Celula(r, "emitidos", "30"));
        Assert.Equal(1, Celula(r, "emitidos", "60"));
        Assert.Equal(0, Celula(r, "emitidos", "90"));
        // B cancelado só no 60 (CANCELLED, dois L) — 30 e 90 não são afetados.
        Assert.Equal(0, Celula(r, "cancelados", "30"));
        Assert.Equal(1, Celula(r, "cancelados", "60"));
        Assert.Equal(1, Celula(r, "cancelados", "90"));                         // C: CANCELED (um L)
        Assert.Equal(1, Celula(r, "analiseCliente", "60"));                     // A: CLIENT REVIEW + ISSUED
        Assert.Equal(1, Celula(r, "aEmitir", "60"));                            // C
        Assert.Equal(1, Celula(r, "aEmitir", "90"));                            // A
        // C no 30 está INATIVO (mesmo com ELABORATION/TO BE ISSUED): fora de todas as contagens do 30.
        Assert.Equal(0, Celula(r, "aEmitir", "30"));
    }

    [Fact]
    public void Tipo2_KpisEmitidosGeralPreservado_SemCanceladoGeral()
    {
        var (docs, gates) = CenarioTipo2();
        var r = Avaliar(docs, gates);
        Assert.Equal(new[] { "total", "emitidosGeral", "revisaoAtual" }, r.Kpis.Select(k => k.Key));
        // Emitidos Geral = regra documental (Emission_Date e não cancelado na linha principal), NÃO soma dos gates (2+1+0).
        Assert.Equal(2, r.Kpis.Single(k => k.Key == "emitidosGeral").Value);
        Assert.DoesNotContain(r.Charts, c => c.Key == "porGestao");
    }

    [Fact]
    public void Tipo2_TabelaMostraOsTresGates_NAParaInativo_EGateAtualInalterado()
    {
        var (docs, gates) = CenarioTipo2();
        var r = Avaliar(docs, gates);
        Assert.Equal(new[] { "30%", "60%", "90%" }, r.Table.Columns.Where(c => c.Type == DashboardFormats.Gate).Select(c => c.Label));
        var linhaB = r.Table.Rows.Single(x => (string?)x["na"] == "B");
        var b90 = Assert.IsType<ProductionDashboards.GateCelula>(linhaB["gate90"]);
        Assert.Equal("N/A", b90.Rotulo);
        Assert.False(b90.Ativo);
        var b60 = Assert.IsType<ProductionDashboards.GateCelula>(linhaB["gate60"]);
        Assert.Equal("Cancelado", b60.Rotulo);
        Assert.Equal(("CANCELLED", "CANCELLED"), (b60.DocumentStatus, b60.Situation));   // detalhe para o tooltip
        var a60 = Assert.IsType<ProductionDashboards.GateCelula>(r.Table.Rows.Single(x => (string?)x["na"] == "A")["gate60"]);
        Assert.Equal("Emitido · Em análise do cliente", a60.Rotulo);                       // as duas facetas, sem precedência inventada
        var c30 = Assert.IsType<ProductionDashboards.GateCelula>(r.Table.Rows.Single(x => (string?)x["na"] == "C")["gate30"]);
        Assert.Equal("N/A", c30.Rotulo);

        // Gate atual = MESMA regra anterior (EscolherGate: primeiro gate ativo não finalizado).
        Assert.Equal("60%", docs[0]["Gate_Atual"]);    // A: 30 APPROVED (finalizado) → 60
        Assert.Equal("60%", docs[1]["Gate_Atual"]);    // B: 30 APPROVED → 60 (CANCELLED não é "finalizado")
        Assert.Equal("60%", docs[2]["Gate_Atual"]);    // C: 30 inativo → 60
    }

    [Fact]
    public void Tipo2_CrossFilter_Gate60Emitido_E_AndEntreGates()
    {
        var (docs, gates) = CenarioTipo2();
        var r = Avaliar(docs, gates, F(("gate60", "EMITIDO")));
        Assert.Equal(new[] { "A" }, r.Table.Rows.Select(x => (string?)x["na"]));
        Assert.Equal(1, r.Kpis.Single(k => k.Key == "total").Value);                  // KPIs respondem ao filtro
        Assert.True(r.Matrices!.Single().Cells.Single(c => c.Row == "emitidos" && c.Column == "60").Selected);
        // A célula da própria dimensão ignora o próprio filtro (como os gráficos): 60/Cancelados continua visível.
        Assert.Equal(1, Celula(r, "cancelados", "60"));

        // OR dentro da dimensão.
        var ou = Avaliar(CenarioTipo2().Docs, CenarioTipo2().Gates, F(("gate60", "EMITIDO"), ("gate60", "CANCELADO")));
        Assert.Equal(new[] { "A", "B" }, ou.Table.Rows.Select(x => (string?)x["na"]).OrderBy(x => x));

        // AND entre dimensões: emitido no 30 E cancelado no 60 → só B.
        var e = Avaliar(CenarioTipo2().Docs, CenarioTipo2().Gates, F(("gate30", "EMITIDO"), ("gate60", "CANCELADO")));
        Assert.Equal(new[] { "B" }, e.Table.Rows.Select(x => (string?)x["na"]));

        // Em análise do cliente (faceta de status) combina com emissão do mesmo gate.
        var cli = Avaliar(CenarioTipo2().Docs, CenarioTipo2().Gates, F(("gate60Status", "EM_ANALISE_CLIENTE"), ("gate60", "EMITIDO")));
        Assert.Equal(new[] { "A" }, cli.Table.Rows.Select(x => (string?)x["na"]));
    }

    [Fact]
    public void Tipo2_ContagemDaCelulaEhIgualAoResultadoDoFiltro()
    {
        // Garantia central: clicar numa célula filtra exatamente os documentos contados nela.
        var (docs, gates) = CenarioTipo2();
        var r = Avaliar(docs, gates);
        foreach (var c in r.Matrices!.Single().Cells)
        {
            var (d2, g2) = CenarioTipo2();
            var filtrado = Avaliar(d2, g2, F((c.FilterDimension, c.FilterValue)));
            Assert.True(c.Value == filtrado.FilteredRows, $"{c.Row}/{c.Column}: célula {c.Value} ≠ filtro {filtrado.FilteredRows}");
        }
    }

    [Theory]
    [InlineData("APPROVED", "ISSUED", "EMITIDO")]
    [InlineData("APROVADO", "EMITIDO", "EMITIDO")]
    [InlineData("CLIENT REVIEW", "ISSUED", "EMITIDO")]
    [InlineData("CANCELLED", "CANCELLED", "CANCELADO")]
    [InlineData("CANCELED", "CANCELED", "CANCELADO")]
    [InlineData("CANCELADO", "EMITIDO", "CANCELADO")]          // cancelamento prevalece (mesma precedência da regra geral)
    [InlineData("ELABORATION", "TO BE ISSUED", "A_EMITIR")]
    [InlineData("EM ELABORAÇÃO", "A EMITIR", "A_EMITIR")]
    [InlineData("INTERNAL", "NOT TO BE ISSUED", "NOT TO BE ISSUED")] // sem par comprovado: texto próprio, fora das 4 linhas da matriz
    [InlineData(null, null, "__NULL__")]
    public void ClasseDoGate_NormalizaPtEn(string? status, string? situacao, string esperado) =>
        Assert.Equal(esperado, ProductionDashboards.ClasseGate(true, status, situacao).Key);

    [Fact]
    public void GateInativo_NuncaEntraNasClasses() =>
        Assert.Equal("NA", ProductionDashboards.ClasseGate(false, "APPROVED", "ISSUED").Key);

    [Fact]
    public void OutrosDashboardsNaoTemMatrizNemMeta()
    {
        foreach (var (id, def) in ProductionDashboards.Todos.Where(kv => kv.Key != "ldp"))
        {
            Assert.Empty(def.Matrices);
            Assert.Null(def.Especializar);
        }
    }
}
