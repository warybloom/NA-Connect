using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;
using N = APS.WebApp.Services.Reports.Dashboards.ReportStatusNormalizer;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Ajustes de 29/09: LDP com a regra OFICIAL da Gestão à Vista (PmoExecutiveRules) e status do gate atual no Tipo 2;
/// clique em linha da tabela filtra a tela; HH do Nivelamento por semana (regra do PlanningSourceReader).
/// </summary>
public class ProductionDashboardAjustesTests
{
    private static Dictionary<string, IReadOnlySet<string>> F(params (string Dim, string Valor)[] f) =>
        f.GroupBy(x => x.Dim).ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(x => x.Valor).ToHashSet());

    private static decimal? Kpi(DashboardResult r, string key) => r.Kpis.Single(k => k.Key == key).Value;

    private static Dictionary<string, object?> LdpLinha(int id, int tipo, string? status = null, string? situacao = null,
        string? flow = null, DateTime? emissao = null, string? na = null) => new()
    {
        ["ID_Registro"] = id, ["Tipo_LDP"] = tipo, ["Document_Status"] = status, ["Situation"] = situacao,
        ["Status_In_Flow"] = flow, ["Emission_Date"] = emissao, ["NA_Document_Number"] = na ?? $"DOC-{id}",
    };

    private static Dictionary<string, object?> Gate(long doc, byte pct, bool ativo, string? status, string? sit = null) => new()
    {
        ["Ldp_Registro_Id"] = doc, ["Gate_Percentual"] = pct, ["Gate_Ativo"] = ativo, ["Document_Status"] = status, ["Situation"] = sit,
    };

    // ------------------------------------------------------------------------------ LDP oficial ----
    [Fact]
    public void Ldp_ClasseGestao_EhARegraOficialDaGestaoAVista()
    {
        var linhas = new List<Dictionary<string, object?>>
        {
            LdpLinha(1, 1, status: "CANCELADO"),                                   // cancelado pelo status
            LdpLinha(2, 2, flow: "Cancelado", emissao: new DateTime(2026, 1, 1)),   // cancelado vence emissão (Status_In_Flow)
            LdpLinha(3, 1, emissao: new DateTime(2026, 1, 1)),                      // emitido
            LdpLinha(4, 1),                                                         // a emitir
        };
        ProductionDashboards.MesclarLdp(linhas, new List<Dictionary<string, object?>>());
        Assert.Equal(new object?[] { "CANCELADO", "CANCELADO", "EMITIDO", "A_EMITIR" }, linhas.Select(l => l["Classe_Gestao"]));
        Assert.True(PmoExecutiveRules.Cancelado(null, null, "Cancelado"));        // mesma função oficial dos relatórios
    }

    [Fact]
    public void Ldp_Tipo2_StatusAtualVemDoGateAtual_Tipo1DaPropriaLinha()
    {
        var linhas = new List<Dictionary<string, object?>> { LdpLinha(10, 2), LdpLinha(11, 1, status: "CLIENT REVIEW", situacao: "ISSUED") };
        var gates = new List<Dictionary<string, object?>>
        {
            Gate(10, 30, true, "APPROVED", "ISSUED"), Gate(10, 60, true, "CLIENT REVIEW", "ISSUED"), Gate(10, 90, true, "NOT STARTED", "TO BE ISSUED"),
            Gate(11, 30, true, "NOT STARTED"),                                      // Tipo 1 ignora gates
        };
        ProductionDashboards.MesclarLdp(linhas, gates);
        Assert.Equal("CLIENT REVIEW", linhas[0]["Status_Atual"]);                  // 1º gate ativo não finalizado (regra do Espelho)
        Assert.Equal("60%", linhas[0]["Gate_Atual"]);
        Assert.Equal("CLIENT REVIEW", linhas[1]["Status_Atual"]);
        Assert.Null(linhas[1]["Gate_Atual"]);
    }

    [Fact]
    public void Ldp_Tipo2_KpisComStatusVazioNaLinhaPrincipal()
    {
        // Reprodução do bug: no Tipo 2 Document_Status/Situation da linha principal são vazios (dado real CHS001).
        var def = ProductionDashboards.Todos["ldp"];
        var linhas = new List<Dictionary<string, object?>>
        {
            LdpLinha(1, 2, emissao: new DateTime(2026, 5, 1)), LdpLinha(2, 2), LdpLinha(3, 2, flow: "Cancelado"),
        };
        def.Merge!(linhas, new List<Dictionary<string, object?>> { Gate(1, 60, true, "CLIENT REVIEW"), Gate(2, 30, true, "ELABORATION") });
        var r = DashboardEngine.Avaliar(def, linhas.Select(l => new DashboardRow(l)).ToList(), F(), 1, 10);
        // Tipo 2 (spec 29/09): Total + Emitidos Geral (regra documental) + Revisão Atual (gate atual); cancelamento
        // é lido por fase na matriz — não há mais KPI "Cancelados" geral nem "A emitir" geral no Tipo 2.
        Assert.Equal(3, Kpi(r, "total"));
        Assert.Equal(1, Kpi(r, "emitidosGeral"));
        Assert.Equal(1, Kpi(r, "revisaoAtual"));
        Assert.DoesNotContain(r.Kpis, k => k.Key is "cancelados" or "aEmitir" or "emitidos");
    }

    [Fact]
    public void Cancelled_ComDoisL_EhCancelado() => Assert.Equal("CANCELADO", N.StatusDoc("CANCELLED").Key);

    // -------------------------------------------------------------------------- clique na linha ----
    [Fact]
    public void CliqueNaLinha_FiltraPeloDocumentoEMarcaSelecao()
    {
        var def = ProductionDashboards.Todos["ldp"];
        var linhas = new List<Dictionary<string, object?>> { LdpLinha(1, 1, na: "NA-001"), LdpLinha(2, 1, na: "NA-002") };
        def.Merge!(linhas, new List<Dictionary<string, object?>>());
        var rows = linhas.Select(l => new DashboardRow(l)).ToList();

        var semFiltro = DashboardEngine.Avaliar(def, rows, F(), 1, 10);
        Assert.Equal("documento", semFiltro.Table.RowFilterDimension);
        Assert.Equal("NA-001", semFiltro.Table.Rows[0]["__rowKey"]);
        Assert.Equal(false, semFiltro.Table.Rows[0]["__selected"]);

        var r = DashboardEngine.Avaliar(def, rows, F(("documento", "NA-001")), 1, 10);
        Assert.Equal(1, Kpi(r, "total"));
        Assert.Equal(true, Assert.Single(r.Table.Rows)["__selected"]);
        Assert.Equal(2, r.TotalRows);
    }

    [Fact]
    public void CliqueNaLinha_CombinaComOutrosFiltros()
    {
        var def = ProductionDashboards.Todos["ldp"];
        var linhas = new List<Dictionary<string, object?>> { LdpLinha(1, 1, na: "NA-001", emissao: new DateTime(2026, 1, 1)), LdpLinha(2, 1, na: "NA-002") };
        def.Merge!(linhas, new List<Dictionary<string, object?>>());
        var r = DashboardEngine.Avaliar(def, linhas.Select(l => new DashboardRow(l)).ToList(), F(("documento", "NA-002"), ("gestao", "EMITIDO")), 1, 10);
        Assert.Equal(0, Kpi(r, "total"));                                           // AND entre documento e situação
    }

    // ------------------------------------------------------------------- Nivelamento semanal ----
    private static DashboardRow Atividade(DateTime? baseline, DateTime? reprogramada, decimal? hh) =>
        new(new Dictionary<string, object?> { ["Weekly_Baseline_Date"] = baseline, ["Weekly_Replanned_Date"] = reprogramada, ["Weekly_HH_Previsto"] = hh });

    [Fact]
    public void Semana_DomingoASabado_DataReprogramadaTemPrioridade()
    {
        Assert.Equal(new DateTime(2026, 9, 27), ProductionDashboards.InicioSemana(new DateTime(2026, 10, 3)));   // sábado → domingo 27/09
        Assert.Equal(new DateTime(2026, 9, 27), ProductionDashboards.InicioSemana(new DateTime(2026, 9, 27)));   // domingo
        Assert.Equal(new DateTime(2026, 10, 5), ProductionDashboards.DataProgramada(Atividade(new DateTime(2026, 9, 1), new DateTime(2026, 10, 5), 1)));
    }

    [Fact]
    public void Nivelamento_HhDaSemanaPadrao_NaoSomaContinuasNemOutrasSemanas()
    {
        var def = ProductionDashboards.Todos["nivelamento-programacao"];
        var hoje = new DateTime(2026, 9, 29);
        var rows = new[]
        {
            Atividade(null, new DateTime(2026, 9, 29), 10),   // semana atual
            Atividade(new DateTime(2026, 10, 1), null, 5),    // semana atual (pela baseline)
            Atividade(null, new DateTime(2026, 10, 6), 40),   // outra semana
            Atividade(null, null, 700),                       // contínua (sem data)
        };
        var inicial = DashboardEngine.Avaliar(def, rows, F(), 1, 10, hoje);
        Assert.Equal(new[] { "2026-09-27" }, inicial.DefaultFilters!["semana"]);

        var semana = DashboardEngine.Avaliar(def, rows, F(("semana", "2026-09-27")), 1, 10, hoje);
        Assert.Equal(15, Kpi(semana, "hhPlanejado"));
        Assert.Equal(1, Kpi(inicial, "semData"));                                   // a contínua é contada à parte
        var porSemana = semana.Charts.Single(c => c.Key == "hhPorSemana").Points;
        Assert.Equal(new[] { "2026-09-27", "2026-10-04", ProductionDashboards.SemData }, porSemana.Select(p => p.Key)); // cronológico
        Assert.True(porSemana[0].Selected);
    }

    [Fact]
    public void Nivelamento_SemAtividadeNaSemanaAtual_UsaASemanaMaisProxima()
    {
        var def = ProductionDashboards.Todos["nivelamento-programacao"];
        var rows = new[] { Atividade(new DateTime(2026, 9, 1), null, 1), Atividade(new DateTime(2026, 9, 24), null, 1) };
        Assert.Equal(new[] { "2026-09-20" }, DashboardEngine.Avaliar(def, rows, F(), 1, 10, new DateTime(2026, 9, 29)).DefaultFilters!["semana"]);
        Assert.Empty(DashboardEngine.Avaliar(def, new[] { Atividade(null, null, 1) }, F(), 1, 10, new DateTime(2026, 9, 29)).DefaultFilters!);
    }
}
