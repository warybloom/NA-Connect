namespace APS.WebApp.Services.Planning;

public sealed record LdpPlanRow(string? Code, string? Client, string? Discipline, decimal? PlannedHh, decimal? PlannedCost);

/// <summary>
/// REGRA OFICIAL DOS RELATÓRIOS (aprovada 21/09/2026): HH PREVISTO = SUM(Planned_HH) e CUSTO PREVISTO = SUM(Planned_Cost) da ÚLTIMA carga da LDP
/// (<see cref="PmoLdpSnapshot"/>), agrupados pela disciplina canônica. Substitui "HH previsto = Work do Cronograma" (o Cronograma segue sendo fonte de datas,
/// hierarquia, programação e Curva S; seu HH fica só como informação de diagnóstico). Semântica SQL SUM: somam-se os valores CONHECIDOS; NULL nunca vira 0
/// (grupo sem nenhum valor conhecido = null = "—"; 0 informado continua 0). Nenhuma disciplina é excluída do total.
/// </summary>
public static class LdpPlanningRules
{
    public static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    /// <summary>Disciplina canônica da linha. 15/10/2026: PRIMEIRO os 2 primeiros caracteres do DOC3 via Norma do projeto
    /// (<paramref name="doc3Canonical"/>, mesma rota do realizado); sem resolução, a da própria LDP ("LDP N&amp;A"); vazia → mesmo
    /// resolver homologado por documento; senão null (SEM DISCIPLINA).</summary>
    public static string? Discipline(LdpPlanRow row, ProjectDisciplineIndex index, Func<string?, string?>? doc3Canonical = null)
    {
        var porDoc3 = ProjectPlanningRules.Key(doc3Canonical?.Invoke(row.Code));
        if (porDoc3 != "") return porDoc3;
        var own = ProjectPlanningRules.Key(row.Discipline);
        if (own != "") return own;
        return ProjectPlanningRules.Key(row.Code) == "" ? null : index.Resolve(row.Code).DisciplineKey;
    }

    public static ProjectPlanningFacts Apply(ProjectPlanningFacts cronogramaFacts, IReadOnlyList<LdpPlanRow> rows, ProjectDisciplineIndex index,
        Func<string?, string?>? doc3Canonical = null)
    {
        var groups = rows.GroupBy(r => Discipline(r, index, doc3Canonical)).ToArray();
        var hhGroups = groups.Select(g => new PlannedDiscipline(g.Key, SumKnown(g.Select(r => r.PlannedHh)), Array.Empty<int>())).ToList();
        var total = SumKnown(rows.Select(r => r.PlannedHh));
        var undisciplined = hhGroups.FirstOrDefault(g => g.Discipline == null)?.PlannedHh ?? 0m;
        var disciplineTotal = SumKnown(hhGroups.Where(g => g.Discipline != null).Select(g => g.PlannedHh)) ?? 0m;
        var difference = (total ?? 0m) - disciplineTotal - undisciplined;
        int withHh = rows.Count(r => r.PlannedHh.HasValue), withCost = rows.Count(r => r.PlannedCost.HasValue);
        var duplicatedCodes = rows.Where(r => ProjectPlanningRules.Key(r.Code) != "").GroupBy(r => ProjectPlanningRules.Key(r.Code)).Count(g => g.Count() > 1);
        var diagnostics = new List<string>
        {
            $"HH previsto oficial = SUM(Planned_HH) da última carga da LDP: {rows.Count} documento(s), {withHh} com Planned_HH e {rows.Count - withHh} sem (ausente não é zero; total soma só os valores conhecidos).",
            $"Custo previsto oficial = SUM(Planned_Cost) da última carga da LDP: {withCost}/{rows.Count} documento(s) com Planned_Cost.",
            $"HH previsto do Cronograma (informativo, NÃO oficial): {cronogramaFacts.PlannedHhTotal?.ToString("0.##") ?? "indisponível"}."
        };
        if (doc3Canonical is not null)
        {
            var semDoc3 = rows.Count(r => ProjectPlanningRules.Key(doc3Canonical(r.Code)) == "");
            diagnostics.Add($"Disciplina pelos 2 primeiros caracteres do DOC3 (Norma do projeto): {rows.Count - semDoc3}/{rows.Count} linha(s); {semDoc3} sem DOC3 resolvido usam a Discipline da LDP.");
        }
        if (duplicatedCodes > 0) diagnostics.Add($"{duplicatedCodes} N&A Document Number repetido(s) na mesma carga: são linhas distintas da LDP, mantidas com HH/custo próprios.");
        if (index.Conflicts.Count > 0)
            diagnostics.Add($"CONFLITO_DISCIPLINA: {index.Conflicts.Count} documento(s) com disciplina diferente entre LDP e Cronograma. A LDP prevalece na classificação.");
        return cronogramaFacts with
        {
            PlannedHhTotal = total, PlannedHhByDiscipline = hhGroups, UndisciplinedHh = undisciplined, DisciplineTotal = disciplineTotal,
            ReconciliationDifference = difference,
            Source = "LDP_PLANNED_HH_ULTIMA_CARGA", DataSource = "dbo.Ldp_Unificada (última carga do projeto)",
            Coverage = difference == 0 ? "RECONCILIADO" : "INCOMPLETO",
            Diagnostics = diagnostics.ToArray(),
            HhDocuments = rows.Count, DocumentsWithHh = withHh,
            DocumentPlannedCost = SumKnown(rows.Select(r => r.PlannedCost)), KnownDocumentCostSubtotal = SumKnown(rows.Select(r => r.PlannedCost)),
            PlannedCostByDiscipline = groups.Select(g => new PlannedCostDiscipline(g.Key, SumKnown(g.Select(r => r.PlannedCost)), g.Count(), g.Count(r => r.PlannedCost.HasValue))).ToArray(),
            CostSource = "LDP.Planned_Cost", CostCoverage = withCost == rows.Count ? "COMPLETA" : "PARCIAL",
            CostDocuments = rows.Count, DocumentsWithCost = withCost, AmbiguousCostDocuments = duplicatedCodes,
        };
    }
}
