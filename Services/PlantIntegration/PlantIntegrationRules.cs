using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.PlantIntegration;

// Kept in a separate partial class so the DCF history reader remains unchanged.
public sealed partial class PlantIntegrationService
{
    public sealed record FilterRule(string Id, string Side, string Action, string Target,
        string Field, string Operator, string Value, bool Enabled);
    public sealed record FilterResponse(Dictionary<string, List<FilterRule>> Filters, DateTime? UpdatedAtUtc);
    public sealed record InventoryItem(string RowId, string Table, string? Tag, string? Drawing,
        IReadOnlyDictionary<string, string?> Attributes, bool Excluded, IReadOnlyList<string> Reasons);
    public sealed record InventoryResponse(string Status, string Category, string Side, int RawCount,
        int IncludedCount, int ExcludedCount, IReadOnlyList<InventoryItem> Items,
        int TotalItems, int Offset, int Limit, string? RevisionToken);
    public sealed record MeasurementPhase(string Id, string Name, int SortOrder,
        Dictionary<string, decimal> Expected, Dictionary<string, decimal> Weights);
    public sealed record PhaseResponse(IReadOnlyList<MeasurementPhase> Phases, DateTime? UpdatedAtUtc);
    public sealed record MeasurementCategory(string Id, string Label, decimal? Expected, int? Pid, int? Piping);
    public sealed record MeasurementResponse(string PhaseId, string? PhaseName, string Status,
        IReadOnlyList<MeasurementCategory> Categories);

    public async Task<FilterResponse> GetFiltersAsync(string projectId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT FilterJson,UpdatedAtUtc FROM dbo.PlantCategoryFilters WHERE ProjectIdAcc=@id", cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await q.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new(new(StringComparer.OrdinalIgnoreCase), null);
        Dictionary<string, List<FilterRule>> filters;
        try { filters = JsonSerializer.Deserialize<Dictionary<string, List<FilterRule>>>(reader.GetString(0), JsonOptions) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException ex) { throw new PlantIntegrationValidationException("Filtros persistidos inválidos: " + ex.Message); }
        var normalized = new Dictionary<string, List<FilterRule>>(filters, StringComparer.OrdinalIgnoreCase);
        return new(normalized, reader.GetDateTime(1));
    }

    public async Task<FilterResponse> SaveFiltersAsync(string projectId, Guid dashboardId,
        Dictionary<string, List<FilterRule>> filters, CancellationToken ct)
    {
        ValidateFiltersStructure(filters);
        var mappings = await GetConfigurationAsync(projectId, ct);
        if (mappings.Count == 0)
            throw new PlantIntegrationValidationException("Salve primeiro o DE/PARA e TAGs do projeto.");
        var mapped = mappings.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var history = await HistoryAsync(dashboardId, ct);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            throw new PlantIntegrationHistoryException("Falta versão-base completa do projeto.");

        var schemaP = Schema(history.Pnid.Rows.Values);
        var schema3 = Schema(history.Piping.Rows.Values);
        foreach (var (category, rules) in filters)
        {
            if (!mapped.TryGetValue(category, out var m))
                throw new PlantIntegrationValidationException("Categoria sem DE/PARA cadastrado: " + category);
            foreach (var rule in rules.Where(x => x.Enabled && x.Target.Equals("attribute", StringComparison.OrdinalIgnoreCase)))
            {
                if ((rule.Side == "pid" || rule.Side == "both") &&
                    (!schemaP.TryGetValue(m.PidTable, out var pcols) || !pcols.Contains(rule.Field, StringComparer.OrdinalIgnoreCase)))
                    throw new PlantIntegrationValidationException($"Atributo P&ID '{rule.Field}' não existe em {m.PidTable}.");
                if ((rule.Side == "piping" || rule.Side == "both") &&
                    (!schema3.TryGetValue(m.PipingTable, out var cols) || !cols.Contains(rule.Field, StringComparer.OrdinalIgnoreCase)))
                    throw new PlantIntegrationValidationException($"Atributo 3D '{rule.Field}' não existe em {m.PipingTable}.");
            }
        }

        var json = JsonSerializer.Serialize(filters, JsonOptions);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("""
            UPDATE dbo.PlantCategoryFilters WITH (UPDLOCK, SERIALIZABLE)
               SET FilterJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@id;
            IF @@ROWCOUNT=0
               INSERT dbo.PlantCategoryFilters(ProjectIdAcc,FilterJson,CreatedAtUtc,UpdatedAtUtc)
               VALUES(@id,@json,SYSUTCDATETIME(),SYSUTCDATETIME());
            """, cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        q.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
        await q.ExecuteNonQueryAsync(ct);
        return await GetFiltersAsync(projectId, ct);
    }

    private static void ValidateFiltersStructure(IReadOnlyDictionary<string, List<FilterRule>> filters)
    {
        if (filters.Count > 100) throw new PlantIntegrationValidationException("Máximo de 100 categorias de filtro.");
        var allowedSides = new HashSet<string>(["pid", "piping", "both"], StringComparer.Ordinal);
        var allowedActions = new HashSet<string>(["include", "exclude"], StringComparer.Ordinal);
        var allowedTargets = new HashSet<string>(["attribute", "drawing", "table"], StringComparer.Ordinal);
        var allowedOps = new HashSet<string>(
            ["eq","neq","contains","notContains","startsWith","endsWith","in","notIn","isEmpty","isNotEmpty"], StringComparer.Ordinal);
        foreach (var (cat, rules) in filters)
        {
            if (string.IsNullOrWhiteSpace(cat) || cat.Length > 80 || rules is null || rules.Count > 250)
                throw new PlantIntegrationValidationException("Categoria inválida ou excesso de regras.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rules)
            {
                if (r is null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 90 || !ids.Add(r.Id) ||
                    !allowedSides.Contains(r.Side ?? "") || !allowedActions.Contains(r.Action ?? "") ||
                    !allowedTargets.Contains(r.Target ?? "") || !allowedOps.Contains(r.Operator ?? ""))
                    throw new PlantIntegrationValidationException($"Regra inválida ou duplicada em {cat}.");
                if (r.Target == "attribute" && string.IsNullOrWhiteSpace(r.Field))
                    throw new PlantIntegrationValidationException($"Informe o atributo da regra {r.Id}.");
                if (r.Field?.Length > 256 || r.Value?.Length > 2048 ||
                    (r.Operator is not ("isEmpty" or "isNotEmpty") && string.IsNullOrWhiteSpace(r.Value)))
                    throw new PlantIntegrationValidationException($"Valor/campo inválido na regra {r.Id}.");
            }
        }
    }

    private static string? DrawingName(PlantRow row, IReadOnlyDictionary<string, PlantRow> drawings)
    {
        // P3dLineGroup é um agrupamento lógico sem DrawingRowId: usar os vínculos
        // P3dDrawingLineGroupRelationship (Drawing, LineGroup) do próprio DCF.
        if (row.Table.Equals("P3dLineGroup", StringComparison.OrdinalIgnoreCase) &&
            drawings.TryGetValue("group:" + row.RowId, out var association) &&
            association.Values.TryGetValue("Dwg Name", out var groupDrawing) &&
            !string.IsNullOrWhiteSpace(groupDrawing))
            return groupDrawing;
        // Coluna real no PnPDrawings é "Dwg Name" (COM espaço).
        if (row.DrawingRowId is { Length: > 0 } id && drawings.TryGetValue(id, out var drawing))
        {
            foreach (var name in new[] { "Dwg Name", "DwgName", "FileName", "DrawingName", "Name" })
                if (drawing.Values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
        }
        foreach (var name in new[] { "DwgName", "Dwg Name", "DRAWINGNO", "FileName", "DrawingName" })
            if (row.Values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static Dictionary<string, PlantRow> DrawingIndex(PartState state)
    {
        var index = state.Rows.Values.Where(x => x.Table.Equals("PnPDrawings", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.RowId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var relation in state.Rows.Values.Where(x =>
            x.Table.Equals("P3dDrawingLineGroupRelationship", StringComparison.OrdinalIgnoreCase)))
        {
            if (!relation.Values.TryGetValue("Drawing", out var drawingId) || string.IsNullOrWhiteSpace(drawingId) ||
                !relation.Values.TryGetValue("LineGroup", out var groupId) || string.IsNullOrWhiteSpace(groupId) ||
                !index.TryGetValue(drawingId, out var draw) ||
                !draw.Values.TryGetValue("Dwg Name", out var name) || string.IsNullOrWhiteSpace(name))
                continue;
            if (!groups.TryGetValue(groupId, out var names)) groups[groupId] = names = new(StringComparer.OrdinalIgnoreCase);
            names.Add(name);
        }
        // Sentinel prefix avoids collision with actual PnPDrawings PnPID.
        foreach (var (groupId, names) in groups)
            index["group:" + groupId] = new("PnPDrawings", groupId, null,
                new(StringComparer.OrdinalIgnoreCase) {
                    ["Dwg Name"] = string.Join(" | ", names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                });
        return index;
    }

    private static bool Matches(FilterRule r, PlantRow row, string? drawing)
    {
        string? fieldValue = null;
        bool exists;
        switch (r.Target)
        {
            case "attribute": exists = row.Values.TryGetValue(r.Field, out fieldValue); break;
            case "drawing": exists = drawing != null; fieldValue = drawing; break;
            case "table": exists = true; fieldValue = row.Table; break;
            default: return false;
        }
        if (r.Operator == "isEmpty") return !exists || string.IsNullOrWhiteSpace(fieldValue);
        if (r.Operator == "isNotEmpty") return exists && !string.IsNullOrWhiteSpace(fieldValue);
        if (!exists || fieldValue is null) return false;
        var v = fieldValue.Trim();
        var s = (r.Value ?? "").Trim();
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        return r.Operator switch
        {
            "eq" => string.Equals(v, s, cmp),
            "neq" => !string.Equals(v, s, cmp),
            "contains" => v.Contains(s, cmp),
            "notContains" => !v.Contains(s, cmp),
            "startsWith" => v.StartsWith(s, cmp),
            "endsWith" => v.EndsWith(s, cmp),
            "in" => s.Split(',', StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Any(x => string.Equals(v,x,cmp)),
            "notIn" => !s.Split(',', StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Any(x => string.Equals(v,x,cmp)),
            _ => false
        };
    }

    private static bool RuleApplies(FilterRule r, string side) =>
        r.Enabled && (r.Side == "both" || r.Side == side);

    private static IReadOnlyList<string> Reasons(
        PlantRow row, string? drawing, string side, IReadOnlyList<FilterRule> rules)
    {
        var applicable = rules.Where(r => RuleApplies(r, side)).ToArray();
        var linkedDrawings = string.IsNullOrWhiteSpace(drawing) ? Array.Empty<string>() :
            drawing.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exclusions = applicable.Where(r => r.Action == "exclude" && r.Target != "drawing" &&
            Matches(r, row, drawing)).Select(r => "Excluído por regra: " + r.Id).ToList();
        var drawingExclusions = applicable.Where(r => r.Action == "exclude" && r.Target == "drawing").ToArray();
        // Uma LineGroup pode constar em vários DWGs. Excluir um DWG de estudo
        // NÃO pode remover a linha enquanto há outro DWG válido ligado a ela.
        if (drawingExclusions.Length > 0 && linkedDrawings.Length > 0 &&
            linkedDrawings.All(name => drawingExclusions.Any(r => Matches(r, row, name))))
            exclusions.Add("Todos os desenhos vinculados estão excluídos pelas regras de DWG");
        if (exclusions.Count > 0) return exclusions;
        var includes = applicable.Where(r => r.Action == "include").ToArray();
        if (includes.Length > 0 && !includes.Any(r =>
            r.Target == "drawing" && linkedDrawings.Length > 0
                ? linkedDrawings.Any(name => Matches(r,row,name))
                : Matches(r,row,drawing)))
            return ["Não atende às regras de inclusão"];
        return Array.Empty<string>();
    }

    private static List<PlantRow> EffectiveRows(
        IEnumerable<PlantRow> original, string side, IReadOnlyList<FilterRule> rules,
        IReadOnlyDictionary<string, PlantRow> drawings)
    {
        if (!rules.Any(r => RuleApplies(r,side))) return original.ToList();
        return original.Where(r => Reasons(r, DrawingName(r,drawings),side,rules).Count == 0).ToList();
    }

    public async Task<InventoryResponse> GetInventoryAsync(string projectId, Guid dashboardId,
        string category, string side, int offset, int limit, CancellationToken ct)
    {
        var history = await HistoryAsync(dashboardId, ct);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            return new("no-baseline", category, side,0,0,0,[],0,offset,limit,null);
        var mappings = await GetConfigurationAsync(projectId, ct);
        if (mappings.Count == 0) return new("no-mapping",category,side,0,0,0,[],0,offset,limit,Revision(history));
        var map = mappings.FirstOrDefault(x=>x.Id.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (map is null) throw new PlantIntegrationValidationException("Categoria não está no DE/PARA: " + category);
        var source = side == "pid" ? history.Pnid : history.Piping;
        var table = side == "pid" ? map.PidTable : map.PipingTable;
        var rules = (await GetFiltersAsync(projectId, ct)).Filters.GetValueOrDefault(category, []);
        var drawings = DrawingIndex(source);
        var rows = source.Rows.Values.Where(x=>x.Table.Equals(table,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>x.RowId,StringComparer.Ordinal).ToArray();
        var page = new List<InventoryItem>(Math.Min(rows.Length,limit));
        var included = 0;
        for (var i=0; i<rows.Length; i++)
        {
            var row = rows[i];
            var drawing = DrawingName(row,drawings);
            var reasons = Reasons(row,drawing,side,rules);
            if (reasons.Count == 0) included++;
            if (i>=offset && i<offset+limit)
                page.Add(new(row.RowId,row.Table,Tag(map,row,side=="pid"),drawing,
                    row.Values,reasons.Count>0,reasons));
        }
        return new("ready",category,side,rows.Length,included,rows.Length-included,page,
            rows.Length,offset,limit,Revision(history));
    }
}
