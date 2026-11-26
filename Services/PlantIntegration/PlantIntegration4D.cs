using System.Globalization;

namespace APS.WebApp.Services.PlantIntegration;

// Planejamento 4D sob demanda: Excel selecionado pelo usuário + estado REAL da última
// versão DCF COMPLETED. O DCF não contém data histórica da conclusão de cada objeto,
// portanto só publicamos realizado ATUAL, nunca uma falsa curva S histórica.
public sealed partial class PlantIntegrationService
{
    public sealed record Plant4DLine(string Tag, string Date, decimal Weight);
    public sealed record Plant4DPoint(string Date, decimal Planned);
    public sealed record Plant4DItem(string Tag, string DueDate, decimal Weight, string State);
    public sealed record Plant4DResponse(string Status, string? Reason, string? RevisionToken,
        string CategoryId, int Total, int Matched, int Missing3d, int Divergent, int MissingPid,
        decimal PlannedToday, decimal ActualToday, decimal TotalWeight, DateTime AsOfDate,
        IReadOnlyList<Plant4DPoint> PlannedCurve, IReadOnlyList<Plant4DItem> Items);

    public async Task<Plant4DResponse> Calculate4DAsync(string projectId, Guid dashboardId,
        string categoryId, string grouping, IReadOnlyList<Plant4DLine> rows, CancellationToken ct)
    {
        if (rows is null || rows.Count is < 1 or > 25000)
            throw new PlantIntegrationValidationException("Forneça de 1 a 25.000 TAGs do Excel.");
        if (grouping is not ("dia" or "semana" or "mes"))
            throw new PlantIntegrationValidationException("Agrupamento inválido.");
        var configuration = await GetConfigurationAsync(projectId, ct);
        var map = configuration.FirstOrDefault(x => string.Equals(x.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (map is null) throw new PlantIntegrationValidationException("Selecione uma categoria cadastrada no DE/PARA.");
        var parsed = new List<(string Tag, DateOnly Due, decimal Weight)>(rows.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var tag = CleanIdentifier(row.Tag);
            if (tag is null || tag.Length > 256)
                throw new PlantIntegrationValidationException("O Excel contém uma TAG vazia, inválida ou muito longa.");
            if (!seen.Add(tag))
                throw new PlantIntegrationValidationException("TAG duplicada no cronograma: " + tag);
            if (!DateOnly.TryParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)
                || due.Year is < 1900 or > 2100)
                throw new PlantIntegrationValidationException("Data inválida no Excel para TAG: " + tag);
            if (row.Weight <= 0 || row.Weight > 100_000_000m)
                throw new PlantIntegrationValidationException("Peso/HH precisa ser maior que zero para TAG: " + tag);
            parsed.Add((tag, due, row.Weight));
        }
        var history = await HistoryAsync(dashboardId, ct);
        var revision = Revision(history);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            return new("no-baseline", "Necessárias versões COMPLETED P&ID e Piping.",
                revision, map.Id, parsed.Count, 0, 0, 0, parsed.Count, 0, 0, 0,
                DateTime.Today, [], []);
        var filters = (await GetFiltersAsync(projectId, ct)).Filters.GetValueOrDefault(map.Id, []);
        var pid = EffectiveRows(history.Pnid.Rows.Values.Where(x =>
            x.Table.Equals(map.PidTable, StringComparison.OrdinalIgnoreCase)), "pid",
            filters, DrawingIndex(history.Pnid));
        var model = EffectiveRows(history.Piping.Rows.Values.Where(x =>
            x.Table.Equals(map.PipingTable, StringComparison.OrdinalIgnoreCase)), "piping",
            filters, DrawingIndex(history.Piping));
        var comparison = Compare(map, pid, model);
        var byTag = comparison.Items.Where(x => CleanIdentifier(x.Tag) is not null)
            .GroupBy(x => CleanIdentifier(x.Tag)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var result = new List<Plant4DItem>(parsed.Count);
        int matched = 0, missing = 0, divergent = 0, unlocated = 0;
        decimal totalWeight = 0m, plannedWeight = 0m, modelledWeight = 0m;
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var line in parsed)
        {
            totalWeight += line.Weight;
            if (line.Due <= today) plannedWeight += line.Weight;
            string state;
            if (!byTag.TryGetValue(line.Tag, out var records) ||
                !records.Any(x => x.PidRowId is not null))
            {
                state = "missing-pid"; unlocated++;
            }
            else if (records.All(x => x.Classification == "matched"))
            {
                state = "matched"; matched++; modelledWeight += line.Weight;
            }
            else if (records.Any(x => x.Classification == "divergent"))
            {
                state = "divergent"; divergent++;
            }
            else
            {
                state = "missing-3d"; missing++;
            }
            result.Add(new(line.Tag, line.Due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), line.Weight, state));
        }
        static DateOnly GroupDate(DateOnly d, string grouping)
        {
            if (grouping == "mes") return new(d.Year, d.Month, 1);
            if (grouping == "semana") return d.AddDays(-((int)d.DayOfWeek + 6) % 7);
            return d;
        }
        decimal cum = 0;
        var points = new List<Plant4DPoint>();
        foreach (var group in parsed.GroupBy(x => GroupDate(x.Due, grouping)).OrderBy(x => x.Key))
        {
            cum += group.Sum(x => x.Weight);
            points.Add(new(group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Math.Round(cum * 100m / totalWeight, 2)));
        }
        return new("ready", null, revision, map.Id, parsed.Count,
            matched, missing, divergent, unlocated,
            Math.Round(plannedWeight * 100m / totalWeight, 2),
            Math.Round(modelledWeight * 100m / totalWeight, 2), totalWeight,
            DateTime.Today, points, result);
    }
}
