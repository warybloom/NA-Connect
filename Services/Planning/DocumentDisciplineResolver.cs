namespace APS.WebApp.Services.Planning;

/// <summary>
/// Single algorithm for resolving a document's canonical discipline, shared by
/// <see cref="PmoPlanningFactsProvider"/> (LDP cost-by-discipline) and <see cref="PmoActualFactsProvider"/>
/// (Timesheet HH/cost-by-discipline) so the two never diverge. Precedence, always scoped to the same
/// Project_ID_ACC, never global/fuzzy: 1) LDP by N&amp;A Document Number exact; 2) LDP by Client Document
/// Number exact; 3) Cronograma (Tipo1/Tipo2 current snapshot) by NA_DOCUMENT_NUMBER; 4) unresolved
/// (caller must treat as GERAL / SEM DISCIPLINA, never invent).
/// </summary>
public sealed record DisciplineMatch(string? DisciplineKey, string Source);
public sealed record DisciplineConflict(string DocumentCode, string? DisciplinaLdp, string? DisciplinaCronograma);

public static class DocumentDisciplineSources
{
    public const string LdpNa = "LDP_NA";
    public const string LdpClient = "LDP_CLIENT";
    public const string Cronograma = "CRONOGRAMA";
    public const string Timesheet = "TIMESHEET";
    public const string SemMatch = "SEM_MATCH";
}

public sealed class ProjectDisciplineIndex
{
    private readonly IReadOnlyDictionary<string, string?> _ldpByNa;
    private readonly IReadOnlyDictionary<string, string?> _ldpByClient;
    private readonly IReadOnlyDictionary<string, string?> _cronogramaByNa;
    public IReadOnlyList<DisciplineConflict> Conflicts { get; }

    private ProjectDisciplineIndex(IReadOnlyDictionary<string, string?> ldpByNa, IReadOnlyDictionary<string, string?> ldpByClient,
        IReadOnlyDictionary<string, string?> cronogramaByNa, IReadOnlyList<DisciplineConflict> conflicts)
    {
        _ldpByNa = ldpByNa; _ldpByClient = ldpByClient; _cronogramaByNa = cronogramaByNa; Conflicts = conflicts;
    }

    /// <summary>Code -> discipline key. A code with more than one distinct discipline among the rows given
    /// is ambiguous and is dropped entirely (never guessed, never picks the first one arbitrarily).</summary>
    private static Dictionary<string, string?> BuildMap(IEnumerable<(string? Code, string? Discipline)> rows)
    {
        var map = new Dictionary<string, string?>();
        var ambiguous = new HashSet<string>();
        foreach (var (code, discipline) in rows)
        {
            var key = ProjectPlanningRules.Key(code);
            if (key == "") continue;
            var value = ProjectPlanningRules.Key(discipline);
            var normalized = value == "" ? null : value;
            if (map.TryGetValue(key, out var existing))
            {
                if (existing != normalized) ambiguous.Add(key);
            }
            else map[key] = normalized;
        }
        foreach (var key in ambiguous) map.Remove(key);
        return map;
    }

    public static ProjectDisciplineIndex Build(
        IEnumerable<(string? Na, string? Discipline)> ldpByNaRows,
        IEnumerable<(string? Client, string? Discipline)> ldpByClientRows,
        IEnumerable<(string? Na, string? Discipline)> cronogramaRows)
    {
        var ldpByNa = BuildMap(ldpByNaRows);
        var ldpByClient = BuildMap(ldpByClientRows);
        var cronogramaByNa = BuildMap(cronogramaRows);
        var conflicts = new List<DisciplineConflict>();
        foreach (var key in ldpByNa.Keys.Concat(ldpByClient.Keys).Distinct())
        {
            var ldpDiscipline = ldpByNa.TryGetValue(key, out var na) && na != null ? na
                : ldpByClient.TryGetValue(key, out var client) ? client : null;
            if (ldpDiscipline != null && cronogramaByNa.TryGetValue(key, out var cron) && cron != null && cron != ldpDiscipline)
                conflicts.Add(new(key, ldpDiscipline, cron));
        }
        return new(ldpByNa, ldpByClient, cronogramaByNa, conflicts);
    }

    /// <summary>Resolves one document code. Falls through to the next source only when the current one
    /// matched the document but carried no discipline value — a match with a blank discipline is not
    /// useful and must not stop the search before Cronograma gets a chance.</summary>
    public DisciplineMatch Resolve(string? documentCode)
    {
        var key = ProjectPlanningRules.Key(documentCode);
        if (key == "") return new(null, DocumentDisciplineSources.SemMatch);
        if (_ldpByNa.TryGetValue(key, out var na) && na != null) return new(na, DocumentDisciplineSources.LdpNa);
        if (_ldpByClient.TryGetValue(key, out var client) && client != null) return new(client, DocumentDisciplineSources.LdpClient);
        if (_cronogramaByNa.TryGetValue(key, out var cron) && cron != null) return new(cron, DocumentDisciplineSources.Cronograma);
        return new(null, DocumentDisciplineSources.SemMatch);
    }
}
