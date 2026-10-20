using APS.WebApp.Services.Planning;
namespace NA_Connect.GestaoVista.Tests;

public class LdpPlanningRulesTests
{
    private static ProjectPlanningFacts Cronograma(decimal? total = null)
    {
        var f = ProjectPlanningRules.Read("p", 2, [], "test", t => t.Discipline);
        return f with { PlannedHhTotal = total };
    }
    private static ProjectDisciplineIndex Index(params (string Code, string Disc)[] docs)
        => ProjectDisciplineIndex.Build(docs.Select(d => ((string?)d.Code, (string?)d.Disc)), [], []);
    private static LdpPlanRow R(string? code, string? disc, decimal? hh, decimal? cost) => new(code, null, disc, hh, cost);

    [Fact]
    public void HhECustoPrevistosSaoSomaSqlDosConhecidosENuloNuncaViraZero()
    {
        var rows = new[] { R("A", "Piping", 10, 100), R("B", "Piping", null, 50), R("C", "Process", 0, null), R("D", "Process", null, null) };
        var f = LdpPlanningRules.Apply(Cronograma(999), rows, Index(("A", "Piping"), ("B", "Piping"), ("C", "Process"), ("D", "Process")));
        Assert.Equal(10m, f.PlannedHhTotal);
        Assert.Equal("LDP_PLANNED_HH_ULTIMA_CARGA", f.Source);
        var piping = f.PlannedHhByDiscipline.Single(d => d.Discipline == "PIPING");
        var process = f.PlannedHhByDiscipline.Single(d => d.Discipline == "PROCESS");
        Assert.Equal(10m, piping.PlannedHh);
        Assert.Equal(0m, process.PlannedHh);                 // 0 informado continua 0
        Assert.Equal(150m, f.DocumentPlannedCost);
        Assert.Equal(150m, f.PlannedCostByDiscipline.Single(d => d.Discipline == "PIPING").DocumentCost);
        Assert.Null(f.PlannedCostByDiscipline.Single(d => d.Discipline == "PROCESS").DocumentCost); // nenhum valor conhecido = ausente
    }

    [Fact]
    public void GrupoSemNenhumHhConhecidoEhNuloNaoZero()
    {
        var f = LdpPlanningRules.Apply(Cronograma(), [R("A", "Civil", null, null)], Index(("A", "Civil")));
        Assert.Null(f.PlannedHhTotal);
        Assert.Null(f.PlannedHhByDiscipline.Single().PlannedHh);
        Assert.Equal(1, f.HhDocuments); Assert.Equal(0, f.DocumentsWithHh);
    }

    [Fact]
    public void ReconciliaTotalComDisciplinasESemDisciplinaExplicitoSemExcluirNada()
    {
        var rows = new[] { R("A", "Management", 5, 1), R("B", null, 7, 2), R("C", "Electrical", 20, 3), R("", null, 3, null) };
        var f = LdpPlanningRules.Apply(Cronograma(), rows, Index(("A", "Management"), ("C", "Electrical")));
        Assert.Equal(35m, f.PlannedHhTotal);                  // Gestão NÃO é mais excluída
        Assert.Equal(10m, f.UndisciplinedHh);                 // B (sem disciplina e sem match) + linha sem código
        Assert.Equal(25m, f.DisciplineTotal);
        Assert.Equal(0m, f.ReconciliationDifference);
        Assert.Equal("RECONCILIADO", f.Coverage);
        Assert.Contains(f.PlannedHhByDiscipline, d => d.Discipline == null);
    }

    [Fact]
    public void CodigoRepetidoNoMesmoSnapshotMantemAsDuasLinhasComHhPropriosEHhDoCronogramaFicaSoInformativo()
    {
        var rows = new[] { R("X", "Mechanical", 11, 1146.7m), R("X", "Mechanical", 17.6m, 1447.64m) };
        var f = LdpPlanningRules.Apply(Cronograma(123.4m), rows, Index(("X", "Mechanical")));
        Assert.Equal(28.6m, f.PlannedHhTotal);
        Assert.Equal(2, f.CostDocuments); Assert.Equal(1, f.AmbiguousCostDocuments);
        Assert.Contains(f.Diagnostics, d => d.Contains("123,4") || d.Contains("123.4"));
        Assert.Contains(f.Diagnostics, d => d.Contains("NÃO oficial"));
    }

    [Fact]
    public void SumKnownSemanticaSql()
    {
        Assert.Null(LdpPlanningRules.SumKnown([]));
        Assert.Null(LdpPlanningRules.SumKnown([null, null]));
        Assert.Equal(0m, LdpPlanningRules.SumKnown([0m, null]));
        Assert.Equal(3m, LdpPlanningRules.SumKnown([1m, null, 2m]));
    }

    [Fact]
    public void SnapshotTipo1MantemFronteiraDeCargaPorLacunaDe30Segundos()
    {
        var sql = PmoLdpSnapshot.Tabela;
        var tipo1 = sql[sql.IndexOf("OR (NOT EXISTS", StringComparison.Ordinal)..];
        Assert.Equal(30, PmoLdpSnapshot.GapSegundosNovaCarga);
        Assert.Contains("P.Project_ID_ACC = @projectId AND P.Tipo_LDP = 2", tipo1);
        Assert.Contains("L.Data_Sincronizacao >= ISNULL((SELECT MAX(G.Data_Sincronizacao)", tipo1);
        Assert.Contains("LAG(X.Data_Sincronizacao)", tipo1);
        Assert.Contains($"G.Gap > {PmoLdpSnapshot.GapSegundosNovaCarga}", tipo1);
        Assert.Contains("X.Project_ID_ACC = @projectId", tipo1);
    }

    [Fact]
    public void SnapshotTipo2SelecionaSomenteTimestampMaximoDoProjetoSemUnirLotesProximos()
    {
        var sql = PmoLdpSnapshot.Tabela;
        var tipo2 = sql[sql.IndexOf("AND ((EXISTS", StringComparison.Ordinal)..sql.IndexOf("OR (NOT EXISTS", StringComparison.Ordinal)];
        Assert.Contains("P.Project_ID_ACC = @projectId AND P.Tipo_LDP = 2", tipo2);
        Assert.Contains("L.Data_Sincronizacao = (SELECT MAX(T2.Data_Sincronizacao)", tipo2);
        Assert.Contains("FROM dbo.Ldp_Unificada T2 WHERE T2.Project_ID_ACC = @projectId", tipo2);
        Assert.DoesNotContain("LAG(", tipo2);
        Assert.DoesNotContain(">=", tipo2);
    }

    [Fact]
    public void SnapshotPreservaLinhasRepetidasDoLoteSemDeduplicarDocumentos()
    {
        var sql = PmoLdpSnapshot.Tabela;
        Assert.Contains("dbo.Ldp_Unificada", sql);
        Assert.DoesNotContain("VW_Ldp_Unificada", sql);
        Assert.DoesNotContain("ROW_NUMBER", sql);
        Assert.DoesNotContain("DISTINCT", sql);
        Assert.DoesNotContain("GROUP BY", sql);
        Assert.DoesNotContain("DELETE", sql);
        Assert.Contains("WHERE L.Project_ID_ACC = @projectId", sql);
    }

    // Correção 2026-09-22 (BUG 1 — HH/Custo Previsto perdiam precisão antes da soma): a causa raiz é a
    // ESCALA FÍSICA de dbo.Ldp_Unificada.Planned_HH/Planned_Cost (DECIMAL(18,2) — corrigida por
    // Migrations/Reports/20260922_AumentarPrecisaoPlannedHhCost.sql, nunca executada automaticamente).
    // Este teste prova que a camada de AGREGAÇÃO EM C# (LdpPlanningRules/SumKnown) em si nunca arredonda
    // — soma com a precisão total dos decimals recebidos — para nunca reintroduzir um Math.Round aqui
    // depois que o banco for corrigido.
    [Fact]
    public void PrecisaoTotalNaoEhPerdidaNaAgregacao_SomaComTodasAsCasasDecimais()
    {
        var rows = new[] { R("A", "Piping", 6401.123456m, 100.111111m), R("B", "Piping", 489.876544m, 50.222222m) };
        var f = LdpPlanningRules.Apply(Cronograma(), rows, Index(("A", "Piping"), ("B", "Piping")));
        Assert.Equal(6891.000000m, f.PlannedHhTotal);
        Assert.Equal(150.333333m, f.DocumentPlannedCost);
        Assert.Equal(6891.000000m, f.PlannedHhByDiscipline.Single().PlannedHh);
    }

    // Correção 2026-09-22 (BUG 2 — Custo Previsto excluindo documento sem arquivo físico no ACC):
    // LdpPlanningRules.Apply/LdpPlanRow não recebem NENHUM sinal de "arquivo encontrado no ACC",
    // "seguro" ou "ambíguo" — a assinatura em si já prova que esse conceito não pode filtrar o total
    // aqui. Documento real da auditoria (BML011.013-000-3D0-AT-001, DIGITAL SYSTEM, Planned_Cost
    // 57.640,00, sem correspondência de arquivo no ACC) simulado: entra no total inteiro.
    [Fact]
    public void DocumentoSemArquivoFisicoNoAccAindaAssimEntraNoCustoPrevistoTotal()
    {
        var rows = new[] { R("BML011.013-000-3D0-AT-001", "DIGITAL SYSTEM", 352m, 57640.00m) };
        var f = LdpPlanningRules.Apply(Cronograma(), rows, Index(("BML011.013-000-3D0-AT-001", "DIGITAL SYSTEM")));
        Assert.Equal(57640.00m, f.DocumentPlannedCost);
        Assert.Equal(57640.00m, f.KnownDocumentCostSubtotal);
        Assert.Equal(57640.00m, f.PlannedCostByDiscipline.Single(d => d.Discipline == "DIGITAL SYSTEM").DocumentCost);
    }

    [Fact]
    public void DocumentoComCustoConhecidoMasSemDisciplinaResolvivel_EntraEmSemDisciplinaNuncaDescartado()
    {
        // Nenhum índice de disciplina resolve este código (nem LDP nem Cronograma) — cai em SEM
        // DISCIPLINA (chave null), mas o Custo Previsto continua íntegro no total.
        var rows = new[] { R("SEM-MATCH-001", null, 100m, 25000m) };
        var f = LdpPlanningRules.Apply(Cronograma(), rows, Index());
        Assert.Equal(25000m, f.DocumentPlannedCost);
        var semDisciplina = f.PlannedCostByDiscipline.Single(d => d.Discipline == null);
        Assert.Equal(25000m, semDisciplina.DocumentCost);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ContratoCanonicoSomaTodasAsLinhasSemMatchOuDisciplina(int tipo)
    {
        // Valores agregados da auditoria BML; a mesma regra canônica vale para ambos os tipos.
        var cronograma = ProjectPlanningRules.Read("p", tipo, [], "fixture", t => t.Discipline);
        var rows = new[] {
            R("DEMAIS-LINHAS", "Piping", 6539m, 809731.50m),
            R("BML011.013-000-3D0-AT-001", null, 352m, 57640m)
        };
        var f = LdpPlanningRules.Apply(cronograma, rows, Index());
        Assert.Equal(6891m, f.PlannedHhTotal);
        Assert.Equal(867371.50m, f.DocumentPlannedCost);
        Assert.Equal(f.DocumentPlannedCost, f.KnownDocumentCostSubtotal);
        Assert.Equal(57640m, f.PlannedCostByDiscipline.Single(d => d.Discipline == null).DocumentCost);
        Assert.Equal(f.DocumentPlannedCost, f.PlannedCostByDiscipline.Sum(d => d.DocumentCost));
    }
}
