using System.Data;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.PlantIntegration;

/// <summary>Read-only Dashboard history access plus N&A-side Plant configuration.</summary>
public sealed partial class PlantIntegrationService(IConfiguration configuration, IHostEnvironment environment)
{
    private const int MaxRows = 300_000, MaxCompressedBytes = 128 * 1024 * 1024, MaxChunkJsonBytes = 64 * 1024 * 1024, MaxCachedRows = 250_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim CacheGate = new(1, 1);
    private static CachedHistory? cache; // one revision only: bounded retained memory

    public sealed record NaProject(string ProjectIdAcc, string Name, Guid? DashboardProjectId);
    public sealed record DashboardProject(Guid DashboardProjectId, string ProjectCode, string? SourceProvider, string? ExternalProjectId);
    public sealed record VersionStatus(int CompletedVersions, DateTime? LatestCompletedAtUtc) { public bool HasBaseline => CompletedVersions > 0; }
    public sealed record ProjectStatus(Guid DashboardProjectId, VersionStatus PnIdBaseline, VersionStatus PipingBaseline, VersionStatus PnIdDelta, VersionStatus PipingDelta, string Message);
    public sealed record CategoryMapping(string Id, string Label, string PidTable, string PipingTable, bool IsComposedTag, string? Delimiter, IReadOnlyList<AttributeMapping> Attributes);
    public sealed record AttributeMapping(string Id, string Name, string PidCol, string PipingCol, bool IsTagPart, bool IsFixedTag);
    public sealed record VersionSummary(string VersionId, DateTime AtUtc, int Rows);
    public sealed record SchemaResult(IReadOnlyDictionary<string, string[]> Pid, IReadOnlyDictionary<string, string[]> Piping, object Latest);
    public sealed record ResultTotals(int Pid, int Piping, int Matched, int Missing3d, int Extra3d, int Divergent, int TaglessPid, int Tagless3d);
    public sealed record CategoryTotals(string Id, string Label, int PidCount, int PipingCount, int Matched, int Missing3d, int Extra3d, int Divergent, int TaglessPid, int Tagless3d);
    public sealed record AttributeDifference(string Id, string Name, string? Pid, string? Piping);
    public sealed record ResultItem(string Id, string Category, string? Tag, string Classification, string? PidTable, string? PipingTable, string? PidRowId, string? PipingRowId, string? PidDrawing, string? PipingDrawing, IReadOnlyList<AttributeDifference> Details);
    public sealed record ResultsResponse(string ProjectIdAcc, Guid? DashboardProjectId, DateTime? UpdatedAtUtc, string? RevisionToken, string Status, ResultTotals Totals, IReadOnlyList<CategoryTotals> Categories, IReadOnlyList<ResultItem> Items, int TotalItems, int Offset, int Limit);

    public async Task<IReadOnlyList<NaProject>> ListProjectsAsync(IReadOnlyCollection<string> allowed, CancellationToken ct)
    {
        if (allowed.Count == 0) return Array.Empty<NaProject>();
        await using var c = new SqlConnection(LocalConnection()); await c.OpenAsync(ct); await using var q = c.CreateCommand(); var ps = new List<string>(); var n = 0;
        foreach (var id in allowed.Distinct(StringComparer.OrdinalIgnoreCase)) { var p = "@p" + n++; ps.Add(p); q.Parameters.Add(p, SqlDbType.NVarChar, 100).Value = id; }
        q.CommandText = $"""SELECT p.Project_ID_ACC,LTRIM(RTRIM(ISNULL(p.Nome_Projeto,N''))),l.DashboardProjectId FROM dbo.Projetos p LEFT JOIN dbo.PlantProjectLinks l ON l.ProjectIdAcc=p.Project_ID_ACC WHERE p.Project_ID_ACC IN ({string.Join(",", ps)}) ORDER BY p.Nome_Projeto,p.Project_ID_ACC;""";
        var result = new List<NaProject>(); await using var r = await q.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetGuid(2))); return result;
    }

    public async Task<IReadOnlyList<DashboardProject>> ListDashboardCatalogAsync(CancellationToken ct)
    {
        await using var c = new SqlConnection(DashboardConnection()); await c.OpenAsync(ct); await using var q = new SqlCommand("""SELECT p.ProjectId,LTRIM(RTRIM(p.ProjectCode)),e.Provider,CONVERT(nvarchar(100),e.ExternalProjectId) FROM dbo.Projects p LEFT JOIN dbo.ProjectExternalIdentities e ON e.ProjectId=p.ProjectId ORDER BY p.ProjectCode,p.ProjectId;""", c);
        var result = new List<DashboardProject>(); await using var r = await q.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new(r.GetGuid(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3))); return result;
    }

    public async Task<bool> ProjectExistsAsync(string id, CancellationToken ct) => await ExistsAsync(LocalConnection(), "SELECT COUNT_BIG(1) FROM dbo.Projetos WHERE Project_ID_ACC=@id;", id, ct);
    public async Task<bool> DashboardProjectExistsAsync(Guid id, CancellationToken ct) => await ExistsAsync(DashboardConnection(), "SELECT COUNT_BIG(1) FROM dbo.Projects WHERE ProjectId=@id;", id, ct);
    private static async Task<bool> ExistsAsync(string cs, string sql, object value, CancellationToken ct) { await using var c = new SqlConnection(cs); await c.OpenAsync(ct); await using var q = new SqlCommand(sql, c); q.Parameters.AddWithValue("@id", value); return Convert.ToInt64(await q.ExecuteScalarAsync(ct) ?? 0) > 0; }

    public async Task LinkAsync(string projectId, Guid dashboardId, CancellationToken ct)
    {
        await using var c = new SqlConnection(LocalConnection()); await c.OpenAsync(ct); await using var q = new SqlCommand("""UPDATE dbo.PlantProjectLinks SET DashboardProjectId=@dashboardId,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@projectId; IF @@ROWCOUNT=0 INSERT INTO dbo.PlantProjectLinks(ProjectIdAcc,DashboardProjectId,CreatedAtUtc,UpdatedAtUtc) VALUES(@projectId,@dashboardId,SYSUTCDATETIME(),SYSUTCDATETIME());""", c);
        q.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId; q.Parameters.Add("@dashboardId", SqlDbType.UniqueIdentifier).Value = dashboardId; await q.ExecuteNonQueryAsync(ct);
    }
    public async Task<Guid?> GetDashboardProjectLinkAsync(string projectId, CancellationToken ct)
    {
        await using var c = new SqlConnection(LocalConnection()); await c.OpenAsync(ct); await using var q = new SqlCommand("SELECT DashboardProjectId FROM dbo.PlantProjectLinks WHERE ProjectIdAcc=@projectId;", c); q.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        var value = await q.ExecuteScalarAsync(ct); return value is null or DBNull ? null : (Guid)value;
    }

    public async Task<ProjectStatus> GetStatusAsync(Guid id, CancellationToken ct)
    {
        await using var c = new SqlConnection(DashboardConnection()); await c.OpenAsync(ct); var bases = await VersionCounts(c, "dbo.PlantDcfSnapshots", id, ct); var deltas = await VersionCounts(c, "dbo.PlantDcfDeltaVersions", id, ct);
        var pnid = bases.GetValueOrDefault("PNID", new(0, null)); var piping = bases.GetValueOrDefault("PIPING", new(0, null));
        return new(id, pnid, piping, deltas.GetValueOrDefault("PNID", new(0, null)), deltas.GetValueOrDefault("PIPING", new(0, null)), !pnid.HasBaseline && !piping.HasBaseline ? "Sem baseline completa: aguardando versão-base COMPLETED; PlantRows não é usado." : "Status calculado exclusivamente por versões COMPLETED.");
    }

    public async Task<SchemaResult> GetSchemaAsync(Guid dashboardId, CancellationToken ct)
    {
        var h = await HistoryAsync(dashboardId, ct); return new(Schema(h.Pnid.Rows.Values), Schema(h.Piping.Rows.Values), new { pnid = h.Pnid.Base is null ? null : Summary(h.Pnid), piping = h.Piping.Base is null ? null : Summary(h.Piping) });
    }
    public async Task<IReadOnlyList<CategoryMapping>> GetConfigurationAsync(string projectId, CancellationToken ct)
    {
        await using var c = new SqlConnection(LocalConnection()); await c.OpenAsync(ct); await using var q = new SqlCommand("SELECT MappingJson FROM dbo.PlantCategoryMappings WHERE ProjectIdAcc=@projectId;", c); q.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        return await q.ExecuteScalarAsync(ct) is string json ? Deserialize(json) : Array.Empty<CategoryMapping>();
    }
    public async Task SaveConfigurationAsync(string projectId, Guid dashboardId, IReadOnlyList<CategoryMapping> maps, CancellationToken ct)
    {
        var h = await HistoryAsync(dashboardId, ct); if (h.Pnid.Base is null || h.Piping.Base is null) throw new PlantIntegrationHistoryException("Aguardando versão-base COMPLETED para validar a configuração.");
        Validate(maps, Schema(h.Pnid.Rows.Values), Schema(h.Piping.Rows.Values)); var json = JsonSerializer.Serialize(maps, JsonOptions);
        await using var c = new SqlConnection(LocalConnection()); await c.OpenAsync(ct); await using var q = new SqlCommand("""UPDATE dbo.PlantCategoryMappings SET MappingJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@projectId; IF @@ROWCOUNT=0 INSERT INTO dbo.PlantCategoryMappings(ProjectIdAcc,MappingJson,CreatedAtUtc,UpdatedAtUtc) VALUES(@projectId,@json,SYSUTCDATETIME(),SYSUTCDATETIME());""", c);
        q.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId; q.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json; await q.ExecuteNonQueryAsync(ct);
    }

    public async Task<ResultsResponse> GetResultsAsync(string projectId, Guid dashboardId, int limit, int offset, string? requestedCategory, CancellationToken ct)
    {
        var h = await HistoryAsync(dashboardId, ct); if (h.Pnid.Base is null || h.Piping.Base is null) return EmptyResults(projectId, dashboardId, "no-baseline", limit, offset);
        var maps = await GetConfigurationAsync(projectId, ct); if (maps.Count == 0) return EmptyResults(projectId, dashboardId, "no-mapping", limit, offset, Revision(h));
        Validate(maps, Schema(h.Pnid.Rows.Values), Schema(h.Piping.Rows.Values)); var selected = string.IsNullOrWhiteSpace(requestedCategory) ? maps : maps.Where(x => string.Equals(x.Id, requestedCategory, StringComparison.Ordinal)).ToArray();
        if (selected.Count == 0) throw new PlantIntegrationValidationException("Categoria não encontrada na configuração.");
        // A MESMA seleção administrativa vale para inventário, auditoria, BI e medição.
        // Nunca altera o histórico: filtra apenas a visão corrente reconstruída.
        var filterSet = (await GetFiltersAsync(projectId, ct)).Filters;
        var drawingPid = DrawingIndex(h.Pnid);
        var drawing3d = DrawingIndex(h.Piping);
        var total = new MutableTotals(); var categories = new List<CategoryTotals>(); var items = new List<ResultItem>();
        foreach (var map in selected)
        {
            var rules = filterSet.GetValueOrDefault(map.Id, []);
            var p = EffectiveRows(h.Pnid.Rows.Values.Where(x => string.Equals(x.Table, map.PidTable, StringComparison.OrdinalIgnoreCase)), "pid", rules, drawingPid).OrderBy(x => x.RowId, StringComparer.Ordinal).ToArray();
            var t = EffectiveRows(h.Piping.Rows.Values.Where(x => string.Equals(x.Table, map.PipingTable, StringComparison.OrdinalIgnoreCase)), "piping", rules, drawing3d).OrderBy(x => x.RowId, StringComparer.Ordinal).ToArray();
            var compared = Compare(map, p, t); total.Add(compared.Total); categories.Add(compared.Category); items.AddRange(compared.Items);
        }
        var updated = new[] { h.Pnid.UpdatedAtUtc, h.Piping.UpdatedAtUtc }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Max();
        return new(projectId, dashboardId, updated == default ? null : updated, Revision(h), "ready", total.ToResult(), categories, items.Skip(offset).Take(limit).ToArray(), items.Count, offset, limit);
    }
    public static ResultsResponse EmptyResults(string projectId, Guid? dashboardId, string status, int limit, int offset, string? revision = null) => new(projectId, dashboardId, null, revision, status, new(0, 0, 0, 0, 0, 0, 0, 0), Array.Empty<CategoryTotals>(), Array.Empty<ResultItem>(), 0, offset, limit);

    private async Task<History> HistoryAsync(Guid projectId, CancellationToken ct)
    {
        await using var c = new SqlConnection(DashboardConnection()); await c.OpenAsync(ct); var pnid = await PlanAsync(c, projectId, "PNID", ct); var piping = await PlanAsync(c, projectId, "PIPING", ct); var rev = pnid.Revision + "|" + piping.Revision;
        await CacheGate.WaitAsync(ct); try
        {
            if (cache is { } hit && hit.ProjectId == projectId && hit.Revision == rev && hit.ExpiresUtc > DateTime.UtcNow) return hit.History;
            var h = new History(await RebuildAsync(c, pnid, ct), await RebuildAsync(c, piping, ct)); cache = h.Pnid.Rows.Count + h.Piping.Rows.Count <= MaxCachedRows ? new(projectId, rev, DateTime.UtcNow.AddMinutes(10), h) : null; return h;
        }
        finally { CacheGate.Release(); }
    }
    private static async Task<Plan> PlanAsync(SqlConnection c, Guid projectId, string part, CancellationToken ct)
    {
        await using var q = new SqlCommand("""SELECT TOP(1) SnapshotId,SourceFingerprint,CompletedAtUtc,[RowCount] FROM dbo.PlantDcfSnapshots WHERE ProjectId=@projectId AND UPPER(Part)=@part AND UPPER(Status)=N'COMPLETED' ORDER BY CompletedAtUtc DESC,SnapshotId DESC;""", c); q.Parameters.Add("@projectId", SqlDbType.UniqueIdentifier).Value = projectId; q.Parameters.Add("@part", SqlDbType.NVarChar, 20).Value = part;
        Baseline b;
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return new(part, null, []);
            b = new(r.GetGuid(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetDateTime(2), Number(r.GetValue(3)));
        }
        await using var d = new SqlCommand("""SELECT VersionId,ParentVersionId,SequenceId,CompletedAtUtc,[RowCount] FROM dbo.PlantDcfDeltaVersions WHERE ProjectId=@projectId AND BaseSnapshotId=@baseId AND UPPER(Part)=@part AND UPPER(Status)=N'COMPLETED' AND (SourceFingerprint=@fingerprint OR (SourceFingerprint IS NULL AND @fingerprint IS NULL)) ORDER BY SequenceId,CompletedAtUtc,VersionId;""", c);
        d.Parameters.Add("@projectId", SqlDbType.UniqueIdentifier).Value = projectId; d.Parameters.Add("@baseId", SqlDbType.UniqueIdentifier).Value = b.Id; d.Parameters.Add("@part", SqlDbType.NVarChar, 20).Value = part; d.Parameters.Add("@fingerprint", SqlDbType.NVarChar, 512).Value = (object?)b.Fingerprint ?? DBNull.Value;
        var versions = new List<Delta>(); await using var dr = await d.ExecuteReaderAsync(ct); while (await dr.ReadAsync(ct)) versions.Add(new(dr.GetGuid(0), dr.IsDBNull(1) ? null : dr.GetGuid(1), Convert.ToInt64(dr.GetValue(2)), dr.GetDateTime(3), Number(dr.GetValue(4)))); VerifyChain(b, versions, part); return new(part, b, versions);
    }
    private static void VerifyChain(Baseline b, IEnumerable<Delta> deltas, string part) { Guid? previous = b.Id; long? sequence = null; foreach (var d in deltas) { if (sequence.HasValue && d.SequenceId <= sequence || d.ParentVersionId != previous) throw new PlantIntegrationHistoryException($"Histórico {part} não possui cadeia ParentVersionId/SequenceId válida para baseline {b.Id}."); previous = d.Id; sequence = d.SequenceId; } }
    private static async Task<PartState> RebuildAsync(SqlConnection c, Plan plan, CancellationToken ct)
    {
        if (plan.Base is null) return new(plan.Part, null, null, new(), null); var rows = await SnapshotRowsAsync(c, plan.Base.Id, ct); var updated = plan.Base.CompletedAtUtc; Guid? version = null;
        foreach (var d in plan.Deltas) { await ApplyAsync(c, d.Id, rows, ct); if (rows.Count > MaxRows) throw new PlantIntegrationHistoryException($"Histórico {plan.Part} excede {MaxRows:N0} linhas."); version = d.Id; updated = d.CompletedAtUtc; }
        // No DCF Piping, válvulas e instrumentos herdam TAG do PipeRunComponent
        // usando o MESMO PnPID. Enriquecer apenas essas folhas (sem adicionar linhas).
        // Fazer APÓS os deltas evita usar uma TAG de revisão anterior.
        if (string.Equals(plan.Part, "PIPING", StringComparison.OrdinalIgnoreCase))
            MergeInheritedPipingAttributes(rows);
        return new(plan.Part, plan.Base, version, rows, updated);
    }

    private static void MergeInheritedPipingAttributes(Dictionary<RowKey, PlantRow> rows)
    {
        var parents = rows.Values
            .Where(row => string.Equals(row.Table, "PipeRunComponent", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(row => row.RowId, row => row, StringComparer.Ordinal);

        foreach (var child in rows.Values.Where(row =>
            string.Equals(row.Table, "Valve", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.Table, "Instrument", StringComparison.OrdinalIgnoreCase)))
        {
            if (!parents.TryGetValue(child.RowId, out var parent)) continue;
            foreach (var attribute in parent.Values)
            {
                if (!child.Values.TryGetValue(attribute.Key, out var current) ||
                    string.IsNullOrWhiteSpace(current))
                    child.Values[attribute.Key] = attribute.Value;
            }
        }
    }
    private static async Task<Dictionary<RowKey, PlantRow>> SnapshotRowsAsync(SqlConnection c, Guid id, CancellationToken ct)
    {
        await using var q = new SqlCommand("SELECT CompressedJson FROM dbo.PlantDcfSnapshotChunks WHERE SnapshotId=@id ORDER BY ChunkIndex;", c); q.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id; var rows = new Dictionary<RowKey, PlantRow>(); var size = 0;
        await using var r = await q.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct); while (await r.ReadAsync(ct)) { var bytes = (byte[])r.GetValue(0); size += bytes.Length; if (size > MaxCompressedBytes) throw new PlantIntegrationHistoryException("Snapshot excede o limite compactado de memória."); using var input = new MemoryStream(bytes, false); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var inflated = await ReadBoundedJsonAsync(gzip, ct); using var json = await JsonDocument.ParseAsync(inflated, cancellationToken: ct); if (json.RootElement.ValueKind != JsonValueKind.Array) throw new PlantIntegrationHistoryException("Chunk de snapshot inválido."); foreach (var e in json.RootElement.EnumerateArray()) { var row = SnapshotRow(e); rows[new(row.Table, row.RowId)] = row; if (rows.Count > MaxRows) throw new PlantIntegrationHistoryException($"Snapshot excede {MaxRows:N0} linhas."); } } return rows;
    }
    private static async Task<MemoryStream> ReadBoundedJsonAsync(Stream source, CancellationToken ct) { var destination = new MemoryStream(); var buffer = new byte[81920]; var total = 0; while (true) { var read = await source.ReadAsync(buffer, ct); if (read == 0) break; total += read; if (total > MaxChunkJsonBytes) { destination.Dispose(); throw new PlantIntegrationHistoryException("Chunk descompactado excede o limite de memória."); } await destination.WriteAsync(buffer.AsMemory(0, read), ct); } destination.Position = 0; return destination; }
    private static async Task ApplyAsync(SqlConnection c, Guid id, Dictionary<RowKey, PlantRow> rows, CancellationToken ct)
    {
        await using var q = new SqlCommand("SELECT TableName,RowId,IsDeleted,DrawingRowId,ValuesJson FROM dbo.PlantDcfDeltaRows WHERE VersionId=@id;", c); q.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id; await using var r = await q.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) { var table = r.GetString(0); var rowId = r.GetInt32(1).ToString(System.Globalization.CultureInfo.InvariantCulture); var key = new RowKey(table, rowId); if (r.GetBoolean(2)) { rows.Remove(key); continue; } if (r.IsDBNull(4)) throw new PlantIntegrationHistoryException($"Delta {id} contém alteração sem ValuesJson."); using var json = JsonDocument.Parse(r.GetString(4)); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new PlantIntegrationHistoryException($"Delta {id} contém ValuesJson inválido."); rows[key] = new(table, rowId, r.IsDBNull(3) ? null : r.GetInt32(3).ToString(System.Globalization.CultureInfo.InvariantCulture), Values(json.RootElement)); }
    }
    // System.Text.Json serializes PlantMirrorRow with PascalCase properties and numeric
    // PnPID/DrawingRowId. Read both old/camelCase and new/PascalCase safely.
    private static bool JsonField(JsonElement e, string camel, out JsonElement value) =>
        e.TryGetProperty(camel, out value) ||
        e.TryGetProperty(char.ToUpperInvariant(camel[0]) + camel[1..], out value);

    private static string? RowNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => throw new PlantIntegrationHistoryException("PnPID/número do desenho com formato inválido.")
    };

    private static PlantRow SnapshotRow(JsonElement e)
    {
        if (!JsonField(e, "table", out var table) || table.ValueKind != JsonValueKind.String ||
            !JsonField(e, "rowId", out var id) || id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
            throw new PlantIntegrationHistoryException("Snapshot contém linha sem Table/RowId.");
        var key = RowNumber(id);
        if (string.IsNullOrWhiteSpace(key))
            throw new PlantIntegrationHistoryException("Snapshot contém PnPID vazio.");
        string? drawing = null;
        if (JsonField(e, "drawingRowId", out var drawingId))
            drawing = RowNumber(drawingId);
        var data = JsonField(e, "values", out var rawValues) && rawValues.ValueKind == JsonValueKind.Object
            ? Values(rawValues)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return new PlantRow(table.GetString()!, key, drawing, data);
    }
    private static Dictionary<string, string?> Values(JsonElement e) { var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); foreach (var x in e.EnumerateObject()) values[x.Name] = x.Value.ValueKind switch { JsonValueKind.Null => null, JsonValueKind.String => x.Value.GetString(), _ => x.Value.GetRawText() }; return values; }

    private static IReadOnlyDictionary<string, string[]> Schema(IEnumerable<PlantRow> rows) => rows.GroupBy(x => x.Table, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.SelectMany(y => y.Values.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
    private static VersionSummary Summary(PartState x) => new((x.Version ?? x.Base!.Id).ToString(), x.UpdatedAtUtc!.Value, x.Rows.Count);
    private static string Revision(History h) => PartRevision(h.Pnid) + "|" + PartRevision(h.Piping); private static string PartRevision(PartState x) => x.Base is null ? "absent" : $"{x.Base.Id:N}:{x.Version?.ToString("N") ?? "base"}";
    private static IReadOnlyList<CategoryMapping> Deserialize(string json) { try { return JsonSerializer.Deserialize<List<CategoryMapping>>(json, JsonOptions) ?? []; } catch (JsonException x) { throw new PlantIntegrationValidationException("Configuração Plant inválida: " + x.Message); } }
    private static void Validate(IReadOnlyList<CategoryMapping> maps, IReadOnlyDictionary<string, string[]> pid, IReadOnlyDictionary<string, string[]> piping)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pt = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var tt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in maps) { if (string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Label) || !ids.Add(m.Id)) throw new PlantIntegrationValidationException("Cada categoria precisa de id e label únicos."); if (!pid.TryGetValue(m.PidTable ?? "", out var pc) || !piping.TryGetValue(m.PipingTable ?? "", out var tc)) throw new PlantIntegrationValidationException($"Categoria {m.Id} referencia tabela inexistente."); if (!pt.Add(m.PidTable) || !tt.Add(m.PipingTable)) throw new PlantIntegrationValidationException("Uma tabela só pode pertencer a uma categoria; isso evita duplicar heranças."); if (m.Attributes is null || m.Attributes.Count == 0) throw new PlantIntegrationValidationException($"Categoria {m.Id} não possui atributos."); var aIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var tagParts = 0; foreach (var a in m.Attributes) { if (string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name) || !aIds.Add(a.Id)) throw new PlantIntegrationValidationException($"Categoria {m.Id} possui atributo sem id/nome único."); if (!pc.Contains(a.PidCol, StringComparer.OrdinalIgnoreCase) || !tc.Contains(a.PipingCol, StringComparer.OrdinalIgnoreCase)) throw new PlantIntegrationValidationException($"Atributo {a.Id} referencia coluna inexistente."); if (a.IsTagPart) tagParts++; } if (m.IsComposedTag ? tagParts < 1 : tagParts != 1) throw new PlantIntegrationValidationException($"Categoria {m.Id} possui configuração de TAG inválida."); if (m.IsComposedTag && string.IsNullOrEmpty(m.Delimiter) || m.Delimiter?.Length > 16) throw new PlantIntegrationValidationException($"Categoria {m.Id} possui separador de TAG inválido."); }
    }
    private static Comparison Compare(CategoryMapping m, IReadOnlyList<PlantRow> pid, IReadOnlyList<PlantRow> piping)
    {
        var totals = new MutableTotals { Pid = pid.Count, Piping = piping.Count }; var items = new List<ResultItem>(); var left = Tagged(pid, m, true, out var taglessPid); var right = Tagged(piping, m, false, out var taglessPiping); totals.TaglessPid = taglessPid.Count; totals.Tagless3d = taglessPiping.Count;
        foreach (var x in taglessPid) items.Add(Item(m, null, "taglessPid", x, null, [])); foreach (var x in taglessPiping) items.Add(Item(m, null, "tagless3d", null, x, []));
        foreach (var tag in left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) { var l = left.GetValueOrDefault(tag, []); var r = right.GetValueOrDefault(tag, []); var pairs = Math.Min(l.Count, r.Count); for (var i = 0; i < pairs; i++) { var diff = Differences(m, l[i], r[i]); var classification = diff.Count == 0 ? "matched" : "divergent"; if (diff.Count == 0) totals.Matched++; else totals.Divergent++; items.Add(Item(m, tag, classification, l[i], r[i], diff)); } for (var i = pairs; i < l.Count; i++) { totals.Missing3d++; items.Add(Item(m, tag, "missing3d", l[i], null, [])); } for (var i = pairs; i < r.Count; i++) { totals.Extra3d++; items.Add(Item(m, tag, "extra3d", null, r[i], [])); } }
        return new(totals, new(m.Id, m.Label, totals.Pid, totals.Piping, totals.Matched, totals.Missing3d, totals.Extra3d, totals.Divergent, totals.TaglessPid, totals.Tagless3d), items);
    }
    private static Dictionary<string, List<PlantRow>> Tagged(IEnumerable<PlantRow> rows, CategoryMapping m, bool pid, out List<PlantRow> tagless) { var result = new Dictionary<string, List<PlantRow>>(StringComparer.Ordinal); tagless = []; foreach (var row in rows) { var tag = Tag(m, row, pid); if (string.IsNullOrWhiteSpace(tag)) { tagless.Add(row); continue; } if (!result.TryGetValue(tag, out var group)) result[tag] = group = []; group.Add(row); } foreach (var x in result.Values) x.Sort((a, b) => StringComparer.Ordinal.Compare(a.RowId, b.RowId)); return result; }
    // isFixedTag means that the mapping field is pinned in the UI; it is NOT a
    // literal tag value. Values always come from the actual configured columns.
    private static string? Tag(CategoryMapping m, PlantRow row, bool pid)
    {
        var parts = m.Attributes.Where(x => x.IsTagPart)
            .Select(x => row.Values.GetValueOrDefault(pid ? x.PidCol : x.PipingCol))
            .ToArray();
        // TAGs incompletas do Plant (ex.: "?-?" / "?-JUNTA-?") não
        // constituem identificadores: mantê-las na lista, mas impedir
        // pareamento falso P&ID x 3D e contabilizar como "sem TAG".
        return parts.Any(value => string.IsNullOrWhiteSpace(value) ||
                                  value.Contains('?')) ? null
            : m.IsComposedTag ? string.Join(m.Delimiter ?? "", parts) : parts[0];
    }
    private static List<AttributeDifference> Differences(CategoryMapping m, PlantRow p, PlantRow t) =>
        m.Attributes.Select(x => new AttributeDifference(x.Id, x.Name,
                p.Values.GetValueOrDefault(x.PidCol), t.Values.GetValueOrDefault(x.PipingCol)))
            .Where(x => !string.Equals(x.Pid, x.Piping, StringComparison.Ordinal)).ToList();
    private static ResultItem Item(CategoryMapping m, string? tag, string kind, PlantRow? p, PlantRow? t, IReadOnlyList<AttributeDifference> details) => new($"{m.Id}:{kind}:{p?.RowId ?? "-"}:{t?.RowId ?? "-"}", m.Id, tag, kind, p?.Table, t?.Table, p?.RowId, t?.RowId, p?.DrawingRowId, t?.DrawingRowId, details);
    private static async Task<Dictionary<string, VersionStatus>> VersionCounts(SqlConnection c, string table, Guid id, CancellationToken ct) { await using var q = new SqlCommand($"SELECT UPPER(Part),COUNT_BIG(1),MAX(CompletedAtUtc) FROM {table} WHERE ProjectId=@id AND UPPER(Status)=N'COMPLETED' AND UPPER(Part) IN(N'PNID',N'PIPING') GROUP BY UPPER(Part);", c); q.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id; var x = new Dictionary<string, VersionStatus>(StringComparer.OrdinalIgnoreCase); await using var r = await q.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) x[r.GetString(0)] = new(Convert.ToInt32(r.GetInt64(1)), r.IsDBNull(2) ? null : r.GetDateTime(2)); return x; }
    private string LocalConnection() => configuration.GetConnectionString("LocalConnection") ?? throw new PlantIntegrationConfigurationException("ConnectionStrings:LocalConnection não configurada.");
    private string DashboardConnection() { var cs = configuration.GetConnectionString("DashboardBimReadOnly"); if (string.IsNullOrWhiteSpace(cs)) { if (environment.IsProduction()) throw new PlantIntegrationConfigurationException("ConnectionStrings:DashboardBimReadOnly é obrigatória em produção."); cs = "Server=.\\SQLEXPRESS;Database=DB_DASHBOARD_BIM;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;ApplicationIntent=ReadOnly;"; } return new SqlConnectionStringBuilder(cs) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString; }
    private static int Number(object x) => x is long n ? checked((int)n) : Convert.ToInt32(x);
    private sealed record Baseline(Guid Id, string? Fingerprint, DateTime CompletedAtUtc, int RowCount);
    private sealed record Delta(Guid Id, Guid? ParentVersionId, long SequenceId, DateTime CompletedAtUtc, int RowCount);
    private sealed record Plan(string Part, Baseline? Base, IReadOnlyList<Delta> Deltas) { public string Revision => Base is null ? "absent" : $"{Base.Id:N}:{string.Join(',', Deltas.Select(x => x.Id.ToString("N")))}"; }
    private sealed record PartState(string Part, Baseline? Base, Guid? Version, Dictionary<RowKey, PlantRow> Rows, DateTime? UpdatedAtUtc);
    private sealed record History(PartState Pnid, PartState Piping); private sealed record CachedHistory(Guid ProjectId, string Revision, DateTime ExpiresUtc, History History);
    private sealed record RowKey(string Table, string RowId); private sealed record PlantRow(string Table, string RowId, string? DrawingRowId, Dictionary<string, string?> Values); private sealed record Comparison(MutableTotals Total, CategoryTotals Category, List<ResultItem> Items);
    private sealed class MutableTotals { public int Pid, Piping, Matched, Missing3d, Extra3d, Divergent, TaglessPid, Tagless3d; public void Add(MutableTotals x) { Pid += x.Pid; Piping += x.Piping; Matched += x.Matched; Missing3d += x.Missing3d; Extra3d += x.Extra3d; Divergent += x.Divergent; TaglessPid += x.TaglessPid; Tagless3d += x.Tagless3d; } public ResultTotals ToResult() => new(Pid, Piping, Matched, Missing3d, Extra3d, Divergent, TaglessPid, Tagless3d); }
}
public sealed class PlantIntegrationConfigurationException(string message) : InvalidOperationException(message);
public sealed class PlantIntegrationHistoryException(string message) : InvalidOperationException(message);
public sealed class PlantIntegrationValidationException(string message) : InvalidOperationException(message);
