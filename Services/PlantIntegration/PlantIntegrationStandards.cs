using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.PlantIntegration;

// Persisted configuration belongs to N&A Connect.  DCF history is read-only and is
// deliberately never used as a destination for standards or line-weight writes.
public sealed partial class PlantIntegrationService
{
    public sealed record StandardSource(string Table, string Column);
    public sealed record StandardCatalog(string Id, string Name, string Kind, string TargetCategory,
        IReadOnlyList<string> Values, StandardSource Pid, StandardSource Piping, bool Enabled);
    public sealed record StandardsResponse(IReadOnlyList<StandardCatalog> Catalogs, string TagLineColumn,
        DateTime? UpdatedAtUtc);
    public sealed record QualityIssue(string Kind, int Count);
    public sealed record QualityCategory(string Id, string Label, int Total, int Valid, int Invalid,
        int Unchecked, IReadOnlyList<QualityIssue> Issues);
    public sealed record QualityResponse(string Status, IReadOnlyList<QualityCategory> Categories,
        DateTime? UpdatedAtUtc);
    public sealed record LineWeight(string Key, string Label, decimal Percent);
    public sealed record LineWeightsResponse(IReadOnlyList<LineWeight> Weights, DateTime? UpdatedAtUtc);

    private const string DefaultTagLineColumn = "TagLine";
    private const string DefaultLineWeightPhase = "_default";
    private static readonly IReadOnlyDictionary<string, string> LineWeightKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modeling"] = "Modelagem",
            ["valves"] = "Válvulas",
            ["instruments"] = "Instrumentos",
            ["equipment"] = "Equipamentos",
            ["tieIns"] = "Tie-ins",
            ["materials"] = "Materiais",
            ["supports"] = "Suportes"
        };

    public async Task<StandardsResponse> GetStandardsAsync(string projectId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT StandardsJson,UpdatedAtUtc FROM dbo.PlantIntegrationStandards WHERE ProjectIdAcc=@id", cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await q.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new([], DefaultTagLineColumn, null);
        var config = DeserializeStandards(reader.GetString(0));
        return new(config.Catalogs, NormalizeTagLineColumn(config.TagLineColumn), reader.GetDateTime(1));
    }

    public async Task<StandardsResponse> SaveStandardsAsync(string projectId, Guid dashboardId,
        IReadOnlyList<StandardCatalog> catalogs, string? tagLineColumn, CancellationToken ct)
    {
        var history = await HistoryAsync(dashboardId, ct);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            throw new PlantIntegrationHistoryException("Aguardando versão-base COMPLETED para validar os padrões.");

        var mappings = await GetConfigurationAsync(projectId, ct);
        if (mappings.Count == 0)
            throw new PlantIntegrationValidationException("Salve primeiro o DE/PARA e TAGs do projeto.");

        var tagColumn = NormalizeTagLineColumn(tagLineColumn);
        ValidateStandards(catalogs, tagColumn, mappings, Schema(history.Pnid.Rows.Values), Schema(history.Piping.Rows.Values));
        var json = JsonSerializer.Serialize(new StandardsDocument(catalogs, tagColumn), JsonOptions);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("""
            UPDATE dbo.PlantIntegrationStandards WITH (UPDLOCK, SERIALIZABLE)
               SET StandardsJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@id;
            IF @@ROWCOUNT=0
               INSERT dbo.PlantIntegrationStandards(ProjectIdAcc,StandardsJson,CreatedAtUtc,UpdatedAtUtc)
               VALUES(@id,@json,SYSUTCDATETIME(),SYSUTCDATETIME());
            """, cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        q.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
        await q.ExecuteNonQueryAsync(ct);
        return await GetStandardsAsync(projectId, ct);
    }

    public async Task<QualityResponse> GetQualityAsync(string projectId, Guid dashboardId, CancellationToken ct)
    {
        var history = await HistoryAsync(dashboardId, ct);
        if (history.Pnid.Base is null || history.Piping.Base is null) return new("no-baseline", [], null);

        var mappings = await GetConfigurationAsync(projectId, ct);
        if (mappings.Count == 0) return new("no-mapping", [], null);

        StandardsResponse standards;
        try { standards = await GetStandardsAsync(projectId, ct); }
        catch (PlantIntegrationValidationException) { return new("no-standards", [], null); }
        var pidSchema = Schema(history.Pnid.Rows.Values);
        var pipingSchema = Schema(history.Piping.Rows.Values);
        // An old or manually edited JSON document must never turn into a successful
        // quality result.  Ignore it and report no standards instead.
        var usable = IsValidStandards(standards.Catalogs, standards.TagLineColumn, mappings, pidSchema, pipingSchema)
            ? standards.Catalogs.Where(x => x.Enabled).ToArray()
            : Array.Empty<StandardCatalog>();
        if (usable.Length == 0) return new("no-standards", [], standards.UpdatedAtUtc);

        var filters = (await GetFiltersAsync(projectId, ct)).Filters;
        var pidDrawings = DrawingIndex(history.Pnid);
        var pipingDrawings = DrawingIndex(history.Piping);
        var categories = new List<QualityCategory>();
        foreach (var mapping in mappings)
        {
            var categoryStandards = usable.Where(x => x.TargetCategory.Equals(mapping.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (categoryStandards.Length == 0) continue;
            var rules = filters.GetValueOrDefault(mapping.Id, []);
            var pidRows = EffectiveRows(history.Pnid.Rows.Values.Where(x => x.Table.Equals(mapping.PidTable, StringComparison.OrdinalIgnoreCase)), "pid", rules, pidDrawings);
            var pipingRows = EffectiveRows(history.Piping.Rows.Values.Where(x => x.Table.Equals(mapping.PipingTable, StringComparison.OrdinalIgnoreCase)), "piping", rules, pipingDrawings);
            var summary = new QualityAccumulator(mapping.Id, mapping.Label);
            foreach (var row in pidRows) summary.Add(row, categoryStandards, true);
            foreach (var row in pipingRows) summary.Add(row, categoryStandards, false);
            categories.Add(summary.ToResponse());
        }

        return new("ready", categories, standards.UpdatedAtUtc);
    }

    public async Task<LineWeightsResponse> GetLineWeightsAsync(string projectId, string? phaseId, CancellationToken ct)
    {
        var phaseKey = NormalizePhaseKey(phaseId);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT WeightsJson,UpdatedAtUtc FROM dbo.PlantLineWeights WHERE ProjectIdAcc=@id", cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await q.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new([], null);
        var all = DeserializeLineWeights(reader.GetString(0));
        var byPhase = all.WeightsByPhase ?? new Dictionary<string, IReadOnlyList<LineWeight>>(StringComparer.Ordinal);
        return new(byPhase.GetValueOrDefault(phaseKey, []).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray(), reader.GetDateTime(1));
    }

    public async Task<LineWeightsResponse> SaveLineWeightsAsync(string projectId, string? phaseId,
        IReadOnlyList<LineWeight> weights, CancellationToken ct)
    {
        var phaseKey = NormalizePhaseKey(phaseId);
        if (phaseKey != DefaultLineWeightPhase)
        {
            var phases = await GetPhasesAsync(projectId, ct);
            if (!phases.Phases.Any(x => x.Id.Equals(phaseKey, StringComparison.Ordinal)))
                throw new PlantIntegrationValidationException("Fase não cadastrada neste projeto.");
        }
        var normalized = NormalizeLineWeights(weights);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        var current = await ReadLineWeightsAsync(cn, projectId, ct);
        var next = new Dictionary<string, IReadOnlyList<LineWeight>>(current.WeightsByPhase ?? new Dictionary<string, IReadOnlyList<LineWeight>>(StringComparer.Ordinal), StringComparer.Ordinal);
        next[phaseKey] = normalized;
        var json = JsonSerializer.Serialize(new LineWeightsDocument(next), JsonOptions);
        await using var q = new SqlCommand("""
            UPDATE dbo.PlantLineWeights WITH (UPDLOCK, SERIALIZABLE)
               SET WeightsJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@id;
            IF @@ROWCOUNT=0
               INSERT dbo.PlantLineWeights(ProjectIdAcc,WeightsJson,CreatedAtUtc,UpdatedAtUtc)
               VALUES(@id,@json,SYSUTCDATETIME(),SYSUTCDATETIME());
            """, cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        q.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
        await q.ExecuteNonQueryAsync(ct);
        return await GetLineWeightsAsync(projectId, phaseId, ct);
    }

    // Exposed for the isolated C# harness.  The same validation is used before a SQL write.
    public static IReadOnlyList<LineWeight> NormalizeLineWeights(IReadOnlyList<LineWeight>? weights)
    {
        if (weights is null || weights.Count > LineWeightKeys.Count)
            throw new PlantIntegrationValidationException("Pesos de linha inválidos.");
        if (weights.Count == 0) return [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal total = 0;
        var result = new List<LineWeight>(weights.Count);
        foreach (var weight in weights)
        {
            if (weight is null || string.IsNullOrWhiteSpace(weight.Key) || !LineWeightKeys.TryGetValue(weight.Key, out var label) ||
                !keys.Add(weight.Key) || weight.Percent < 0 || weight.Percent > 100 || decimal.Round(weight.Percent, 4) != weight.Percent)
                throw new PlantIntegrationValidationException("Chave ou percentual de peso de linha inválido.");
            total += weight.Percent;
            result.Add(new(weight.Key, label, weight.Percent));
        }
        if (total != 100m) throw new PlantIntegrationValidationException("A soma dos pesos de linha ativos deve ser 100.");
        return result.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeTagLineColumn(string? value)
    {
        var result = string.IsNullOrWhiteSpace(value) ? DefaultTagLineColumn : value.Trim();
        if (result.Length > 256) throw new PlantIntegrationValidationException("Nome do atributo TagLine inválido.");
        return result;
    }

    private static string NormalizePhaseKey(string? phaseId)
    {
        var result = string.IsNullOrWhiteSpace(phaseId) ? DefaultLineWeightPhase : phaseId.Trim();
        if (result.Length > 100) throw new PlantIntegrationValidationException("phaseId inválido.");
        return result;
    }

    private static void ValidateStandards(IReadOnlyList<StandardCatalog>? catalogs, string tagLineColumn,
        IReadOnlyList<CategoryMapping> mappings, IReadOnlyDictionary<string, string[]> pid,
        IReadOnlyDictionary<string, string[]> piping)
    {
        if (catalogs is null || catalogs.Count > 100) throw new PlantIntegrationValidationException("Máximo de 100 catálogos.");
        var mapById = mappings.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogs)
        {
            if (catalog is null || string.IsNullOrWhiteSpace(catalog.Id) || catalog.Id.Length > 100 ||
                string.IsNullOrWhiteSpace(catalog.Name) || catalog.Name.Length > 160 ||
                string.IsNullOrWhiteSpace(catalog.Kind) || catalog.Kind.Length > 80 ||
                string.IsNullOrWhiteSpace(catalog.TargetCategory) || catalog.TargetCategory.Length > 80 || !ids.Add(catalog.Id) ||
                catalog.Pid is null || catalog.Piping is null || catalog.Values is null || catalog.Values.Count is < 1 or > 500)
                throw new PlantIntegrationValidationException("Catálogo de padrões inválido ou duplicado.");
            if (!mapById.TryGetValue(catalog.TargetCategory, out var mapping))
                throw new PlantIntegrationValidationException($"Categoria de destino sem DE/PARA: {catalog.TargetCategory}.");
            ValidateSource(catalog.Pid, pid, "P&ID", catalog.Id);
            ValidateSource(catalog.Piping, piping, "3D", catalog.Id);
            if (!catalog.Pid.Table.Equals(mapping.PidTable, StringComparison.OrdinalIgnoreCase) ||
                !catalog.Piping.Table.Equals(mapping.PipingTable, StringComparison.OrdinalIgnoreCase))
                throw new PlantIntegrationValidationException($"Catálogo {catalog.Id} deve usar as tabelas mapeadas da categoria {mapping.Id}.");
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in catalog.Values)
                if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || !values.Add(value.Trim()))
                    throw new PlantIntegrationValidationException($"Valor inválido ou duplicado no catálogo {catalog.Id}.");
        }

        // TagLine pode estar em uma classe PnP ancestral ou ainda nao ter sido
        // sincronizado no Plant. O administrador deve poder cadastrar os padroes
        // enquanto configura o atributo; a consistencia permanece 'nao avaliada'
        // ate o vinculo linha->componente ser comprovado no DCF.
        _ = tagLineColumn;
    }

    private static bool IsValidStandards(IReadOnlyList<StandardCatalog> catalogs, string tagLineColumn,
        IReadOnlyList<CategoryMapping> mappings, IReadOnlyDictionary<string, string[]> pid,
        IReadOnlyDictionary<string, string[]> piping)
    {
        try { ValidateStandards(catalogs, tagLineColumn, mappings, pid, piping); return true; }
        catch (PlantIntegrationValidationException) { return false; }
    }

    private static void ValidateSource(StandardSource source, IReadOnlyDictionary<string, string[]> schema,
        string side, string catalogId)
    {
        if (string.IsNullOrWhiteSpace(source.Table) || source.Table.Length > 160 ||
            string.IsNullOrWhiteSpace(source.Column) || source.Column.Length > 256 ||
            !schema.TryGetValue(source.Table, out var columns) || !columns.Contains(source.Column, StringComparer.OrdinalIgnoreCase))
            throw new PlantIntegrationValidationException($"Campo {side} do catálogo {catalogId} não existe no schema atual.");
    }

    private static StandardsDocument DeserializeStandards(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<StandardsDocument>(json, JsonOptions);
            return document is null ? new([], DefaultTagLineColumn) : new(document.Catalogs ?? [], NormalizeTagLineColumn(document.TagLineColumn));
        }
        catch (JsonException ex) { throw new PlantIntegrationValidationException("Padrões persistidos inválidos: " + ex.Message); }
    }

    private static async Task<LineWeightsDocument> ReadLineWeightsAsync(SqlConnection cn, string projectId, CancellationToken ct)
    {
        await using var q = new SqlCommand("SELECT WeightsJson FROM dbo.PlantLineWeights WHERE ProjectIdAcc=@id", cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        var json = await q.ExecuteScalarAsync(ct) as string;
        return json is null
            ? new(new Dictionary<string, IReadOnlyList<LineWeight>>(StringComparer.Ordinal))
            : DeserializeLineWeights(json);
    }

    private static LineWeightsDocument DeserializeLineWeights(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<LineWeightsDocument>(json, JsonOptions);
            return document is null
                ? new(new Dictionary<string, IReadOnlyList<LineWeight>>(StringComparer.Ordinal))
                : new(document.WeightsByPhase ?? new Dictionary<string, IReadOnlyList<LineWeight>>(StringComparer.Ordinal));
        }
        catch (JsonException ex) { throw new PlantIntegrationValidationException("Pesos de linha persistidos inválidos: " + ex.Message); }
    }

    private sealed record StandardsDocument(IReadOnlyList<StandardCatalog>? Catalogs, string? TagLineColumn);
    private sealed record LineWeightsDocument(IReadOnlyDictionary<string, IReadOnlyList<LineWeight>>? WeightsByPhase);

    private sealed class QualityAccumulator(string id, string label)
    {
        private readonly Dictionary<string, int> issues = new(StringComparer.OrdinalIgnoreCase);
        private int total, valid, invalid, uncheckedCount;

        public void Add(PlantRow row, IReadOnlyList<StandardCatalog> standards, bool pid)
        {
            total++;
            var checkedFields = 0;
            var sourceMissing = false;
            var failed = false;
            foreach (var standard in standards)
            {
                var column = pid ? standard.Pid.Column : standard.Piping.Column;
                if (!row.Values.TryGetValue(column, out var raw))
                {
                    sourceMissing = true;
                    continue; // source row does not carry the configured attribute: unchecked, not valid
                }
                checkedFields++;
                var allowed = standard.Values.Any(x => string.Equals(x.Trim(), raw?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    failed = true;
                    var kind = string.IsNullOrWhiteSpace(raw) ? standard.Id + ":empty" : standard.Id + ":not-in-catalog";
                    issues[kind] = issues.GetValueOrDefault(kind) + 1;
                }
            }
            if (failed) invalid++;
            else if (checkedFields == 0 || sourceMissing) uncheckedCount++;
            else valid++;
        }

        public QualityCategory ToResponse() => new(id, label, total, valid, invalid, uncheckedCount,
            issues.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => new QualityIssue(x.Key, x.Value)).ToArray());
    }
}
