namespace APS.WebApp.Services.Planning;

public sealed record PlanningTask(int Id, string Uid, string? Wbs, bool Summary, string? Discipline,
    decimal? Work, string? Document, Tipo1AnalyticsMetadata? Metadata = null);
public sealed record PlannedDiscipline(string? Discipline, decimal? PlannedHh, IReadOnlyList<int> SummaryIds);
public sealed record PlannedCostDiscipline(string? Discipline, decimal? DocumentCost, int Documents, int WithCost);
public sealed record ProjectPlanningFacts(string ProjectId, int TipoLdp, decimal? PlannedHhTotal,
    IReadOnlyList<PlannedDiscipline> PlannedHhByDiscipline, decimal? UndisciplinedHh,
    decimal? DisciplineTotal, decimal? ReconciliationDifference, string Source, string DataSource,
    string Coverage, IReadOnlyList<string> Diagnostics, int? RootId, int SummaryCount,
    int DisciplineFilledCount, int TaskCount, int? ManagementTaskCount, decimal? ManagementHh)
{
    // No proven complete project cost budget yet. Documentary coverage must never masquerade as one.
    public decimal? PlannedCostTotal { get; init; }
    public IReadOnlyList<PlannedCostDiscipline> PlannedCostByDiscipline { get; init; } = [];
    public decimal? DocumentPlannedCost { get; init; }
    public decimal? KnownDocumentCostSubtotal { get; init; }
    public string CostSource { get; init; } = "LDP.Planned_Cost";
    public string CostCoverage { get; init; } = "DOCUMENTAL_PARCIAL";
    public int CostDocuments { get; init; }
    public int DocumentsWithCost { get; init; }
    public int AmbiguousCostDocuments { get; init; }
    // Cobertura do HH previsto oficial (LDP.Planned_HH da última carga): documentos considerados e quantos têm o valor informado.
    public int HhDocuments { get; init; }
    public int DocumentsWithHh { get; init; }
}

public static class ProjectPlanningRules
{
    public static string Key(string? value) => (value ?? "").Trim().ToUpperInvariant();
    public static decimal? Sum(IEnumerable<decimal?> values)
    {
        var all = values.ToArray();
        return all.Length == 0 || all.Any(x => !x.HasValue) ? null : all.Sum(x => x!.Value);
    }
    // Correção 2026-09-19: exigir documento real na LDP como condição de entrada no HH Previsto provou-se
    // arriscado em dados reais (BML) — cronograma pode ter HH legítimo de documento ainda não chegado/
    // cadastrado/renomeado na LDP. HH Previsto volta a contar toda tarefa-folha válida do cronograma; a
    // única exclusão passa a ser por DISCIPLINA (Gestão/Gerenciamento), nunca por ausência de match na LDP.
    // Valores canônicos confirmados em auditoria real (dbo.Ldp_Unificada.Discipline e Disciplina_Cronograma,
    // sistema inteiro): "MANAGEMENT" e "GERENCIAMENTO" — únicas grafias que aparecem sozinhas (não como
    // parte de um rótulo composto como "PLANNING AND COORDINATION" ou "CONTRACT MANAGEMENT", que não são
    // tratadas como Gestão sem confirmação — evitar heurística de texto/substring).
    private static readonly HashSet<string> ManagementDisciplineKeys = ["MANAGEMENT", "GERENCIAMENTO"];
    public static bool IsManagementDiscipline(string? disciplineKey) => ManagementDisciplineKeys.Contains(Key(disciplineKey));
    public static ProjectPlanningFacts Read(string projectId, int tipo, IReadOnlyList<PlanningTask> tasks, string source,
        Func<PlanningTask, string?> resolveDiscipline)
    {
        var diagnostics = new List<string>();
        var byUid = tasks.GroupBy(t => t.Uid).ToDictionary(g => g.Key, g => g.ToArray());
        bool Descends(PlanningTask child, PlanningTask parent)
        {
            if (tipo == 1)
            {
                var visited = new HashSet<string>();
                for (var uid = child.Metadata?.ParentUid; uid != null && visited.Add(uid);)
                {
                    if (uid == parent.Uid) return true;
                    uid = byUid.TryGetValue(uid, out var p) && p.Length == 1 ? p[0].Metadata?.ParentUid : null;
                }
                return false;
            }
            return !string.IsNullOrWhiteSpace(parent.Wbs) && child.Wbs?.StartsWith(parent.Wbs + ".", StringComparison.Ordinal) == true;
        }
        // Sem metadado (Tipo1 sem resumo/parentesco — ramo FALLBACK_TAREFAS_SEM_METADADOS logo abaixo) não
        // há IsLeaf para checar; toda tarefa não-resumo já É a folha, mesma convenção usada nesse ramo.
        bool Leaf(PlanningTask t) => !t.Summary && (tipo != 1 || t.Metadata == null || t.Metadata.IsLeaf == true);
        decimal? Work(PlanningTask t) => t.Work is >= 0 ? t.Work : null;
        var summaries = tasks.Where(t => t.Summary).ToArray();
        // Gestão exclui a tarefa tanto pela disciplina resolvida na própria linha (Cronograma; senão LDP
        // pelo documento — mesma precedência de PmoPlanningFactsProvider.ComDisciplinaPorTarefa) quanto por
        // herdar de um resumo ancestral já rotulado Gestão (ex.: resumo "GESTÃO" com filhos "Coordenação"/
        // "Administração" sem disciplina própria). Sem hierarquia (Tipo1 sem metadado), Descends nunca casa
        // ninguém, então isso se reduz automaticamente à checagem direta da própria linha.
        bool IsManagementTask(PlanningTask t) => IsManagementDiscipline(resolveDiscipline(t))
            || summaries.Any(s => s.Id != t.Id && IsManagementDiscipline(Key(s.Discipline)) && Descends(t, s));
        ProjectPlanningFacts Result(decimal? total, IReadOnlyList<PlannedDiscipline> disciplines,
            decimal? general, decimal? disciplineTotal, string provenance, int? rootId) => new(projectId, tipo,
                total, disciplines, general, disciplineTotal, total - general - disciplineTotal, provenance, source,
                total.HasValue && general.HasValue && disciplineTotal.HasValue && total == general + disciplineTotal ? "RECONCILIADO" : "INCOMPLETO",
                diagnostics, rootId, tasks.Count(t => t.Summary), tasks.Count(t => Key(t.Discipline) != ""), tasks.Count,
                tipo == 1 && tasks.Any(t => t.Metadata?.Version != 1) ? null : tasks.Count(t => !t.Summary && IsManagementTask(t)),
                Sum(tasks.Where(t => Leaf(t) && IsManagementTask(t)).Select(Work)));
        if (tasks.Count == 0 || byUid.Any(g => g.Value.Length != 1)
            || (tipo == 1 && tasks.Any(t => t.Metadata != null && t.Metadata.Version != 1)))
        {
            diagnostics.Add("Snapshot ausente, legado ou ambíguo. Ressincronizar o Cronograma; não há fallback seguro sobre snapshot incompleto.");
            return Result(null, [], null, null, "SEM_DADO", null);
        }
        // Tipo1 sem nenhum metadado de hierarquia (import atual não persiste resumo/parentesco — ver
        // Services/AccScheduleSyncService.cs) não é um snapshot incompleto/legado: é o formato normal do
        // Tipo1 hoje. Eh_Tarefa_Resumo e Trabalho já vêm corretos direto do Cronograma_Projetos, então o
        // total não depende de árvore/resumo — soma-se toda tarefa não-resumo com Trabalho válido, sem
        // exigir NA_DOCUMENT_NUMBER. A quebra por disciplina desse caso é resolvida por tarefa, fora daqui
        // (ver PmoPlanningFactsProvider), porque aqui não há como montar rollup de resumo.
        if (tipo == 1 && tasks.All(t => t.Metadata == null))
        {
            var validasNaoResumo = tasks.Where(t => !t.Summary && Work(t).HasValue && !IsManagementTask(t)).ToArray();
            var totalBruto = validasNaoResumo.Length == 0 ? null : Sum(validasNaoResumo.Select(Work));
            diagnostics.Add("Sem metadados de hierarquia (resumo/parentesco) no Tipo1. Total = soma das tarefas não-resumo com Trabalho válido, exceto disciplina Gestão/Gerenciamento (ver ManagementTaskCount/ManagementHh); vínculo com documento na LDP não é mais exigido como condição de entrada.");
            return Result(totalBruto, [], null, null, totalBruto.HasValue ? "FALLBACK_TAREFAS_SEM_METADADOS" : "SEM_DADO", null);
        }
        var roots = tipo == 1
            ? summaries.Where(t => t.Metadata?.IsProjectSummary == true && t.Metadata.ParentUid == null
                && ((t.Metadata.NativeProjectSummary && t.Metadata.TaskId == 0 && t.Wbs == "0")
                    || tasks.All(o => o.Id == t.Id || Descends(o, t)))).ToArray()
            : summaries.Where(t => t.Uid == "0" && t.Wbs == "0").ToArray();
        if (roots.Length == 0)
            roots = summaries.Where(t => tasks.All(o => o.Id == t.Id || Descends(o, t))).ToArray();
        var root = roots.Length == 1 && Work(roots[0]).HasValue ? roots[0] : null;
        var rawTotal = root == null ? Sum(tasks.Where(Leaf).Select(Work)) : Work(root);
        // HH Previsto = HH válido do cronograma reconciliado MENOS HH de folhas cuja disciplina resolvida é
        // Gestão/Gerenciamento (própria ou herdada de resumo ancestral) — nunca soma de resumo Gestão +
        // filhos Gestão ao mesmo tempo, porque managementLeaves só soma folhas, nunca o Work do resumo.
        var managementLeaves = tasks.Where(t => Leaf(t) && IsManagementTask(t)).ToArray();
        decimal? managementHh = managementLeaves.Length == 0 ? 0m : Sum(managementLeaves.Select(Work));
        var total = rawTotal - managementHh;
        var provenance = root != null ? "RESUMO_PROJETO" : total.HasValue ? "FALLBACK_FOLHAS" : "SEM_DADO";
        if (root == null) diagnostics.Add("Resumo raiz sem Work confiável: fallback somente sobre folhas com Work válido, com cobertura integral dessas folhas. Vínculo com documento na LDP não é mais exigido.");
        if (managementLeaves.Length > 0) diagnostics.Add($"HH_GESTAO: {managementLeaves.Length} tarefa(s) com disciplina Gestão/Gerenciamento excluída(s) do HH Previsto (ManagementHh).");
        var resolutionIssues = tasks.Select(t => t.Metadata?.DisciplineResolution)
            .Where(x => x != null && x.StartsWith("DISCIPLINA_")).Distinct().ToArray();
        if (resolutionIssues.Length > 0)
        {
            diagnostics.AddRange(resolutionIssues!);
            diagnostics.Add("Campo de disciplina não comprovado: não converter ausência de mapeamento em GERAL.");
            return Result(total, [], null, null, provenance, root?.Id);
        }
        var candidates = summaries.Where(t => !roots.Any(r => r.Id == t.Id) && Key(t.Discipline) != "").ToArray();
        var selected = candidates.Where(t => !candidates.Any(p => Descends(t, p))).ToArray();
        if (selected.Any(p => candidates.Any(c => Descends(c,p) && Key(c.Discipline) != Key(p.Discipline))))
        {
            diagnostics.Add("Resumos ancestrais e descendentes têm disciplinas diferentes. Classificação ambígua; nenhum rollup redistribuído.");
            return Result(total, [], null, null, provenance, root?.Id);
        }
        var generalCandidates = summaries.Where(t => !roots.Any(r => r.Id == t.Id) && Key(t.Discipline) == ""
            && !selected.Any(p => Descends(t, p) || Descends(p, t))).ToArray();
        var general = generalCandidates.Where(t => !generalCandidates.Any(p => Descends(t, p))).ToArray();
        // Folha solta sem disciplina própria, não coberta por nenhum resumo (rotulado ou não) — cobertura
        // continua usando o `selected` completo (com Gestão incluído), então um resumo "GESTÃO" ainda cobre
        // seus filhos aqui mesmo depois de Gestão ser removido da lista exibida logo abaixo.
        var generalLeaves = tasks.Where(t => Leaf(t) && Key(t.Discipline) == ""
            && !selected.Concat(general).Any(p => Descends(t, p))).ToArray();
        // Gestão nunca aparece na lista de disciplinas exibida nem no HH Previsto: removida aqui, depois do
        // `selected` completo já ter servido para cobertura acima (evita reabrir folhas de um resumo Gestão
        // como "sem disciplina"/GERAL).
        var selectedDisplayed = selected.Where(t => !IsManagementDiscipline(Key(t.Discipline))).ToArray();
        var groups = selectedDisplayed.GroupBy(t => Key(t.Discipline)).Select(g => new PlannedDiscipline(g.Key,
            Sum(g.Select(Work)), g.Select(t => t.Id).ToArray())).ToList();
        // Labeled standalone leaves are not a discipline rollup. Keep their budget unavailable and expose the gap.
        foreach (var key in tasks.Where(t => Key(t.Discipline) != "" && !IsManagementDiscipline(Key(t.Discipline)))
                     .Select(t => Key(t.Discipline)).Distinct())
            if (!groups.Any(g => g.Discipline == key)) groups.Add(new(key, null, []));
        var generalWork = general.Concat(generalLeaves).ToArray();
        decimal? undisciplined = generalWork.Length == 0 ? 0m : Sum(generalWork.Select(Work));
        if (generalWork.Length > 0) groups.Add(new(null, undisciplined, generalWork.Select(t => t.Id).ToArray()));
        decimal? disciplineTotal = selectedDisplayed.Length == 0 ? 0m : Sum(selectedDisplayed.Select(Work));
        if (total - disciplineTotal - undisciplined != 0) diagnostics.Add("Diferença de fechamento preservada, sem redistribuição.");
        if (groups.Any(g => g.PlannedHh == null)) diagnostics.Add("Há disciplinas sem resumo/Work disponível; valores ausentes não são zero.");
        if (generalWork.Length > 0) diagnostics.Add("GERAL preserva os ramos sem disciplina no próprio resumo e folhas sem disciplina não cobertas por resumo.");
        return Result(total, groups, undisciplined, disciplineTotal, provenance, root?.Id);
    }
}
