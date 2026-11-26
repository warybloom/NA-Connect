using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.PlantIntegration;

// CONTRATO F6 — Central de Relatórios Plant. Partial separado: reaproveita o MESMO histórico em cache, os MESMOS filtros globais
// (EffectiveRows) e o MESMO Compare dos Resultados; nada é escrito no Dashboard BIM (apenas N&A: PlantReportConfigs/UserPreferences).
// Nenhum valor é inventado: categoria/tabela sem origem confiável → modo "unmapped" com o motivo.
public sealed partial class PlantIntegrationService
{
    public const int ReportMaxPage = 500, ReportMaxExportRows = 250_000, ReportMaxFields = 80, ReportMaxColumnsListed = 500, ReportMaxSearch = 100;

    public sealed record ReportSource(string? CategoryId, string? PidTable, string? PipingTable);
    public sealed record ReportConfigResponse(IReadOnlyDictionary<string, ReportSource> Sources, DateTime? UpdatedAtUtc);
    public sealed record ReportField(string Id, string Label, string Group);
    public sealed record ReportDescriptor(string Kind, string Label, string Mode, string? CategoryId, string? PidTable,
        string? PipingTable, string? Reason, IReadOnlyList<ReportField> Fields, IReadOnlyList<string> DefaultFields);
    public sealed record ReportCatalogResponse(string Status, IReadOnlyList<ReportDescriptor> Reports, int MaxExportRows, int MaxPage);
    public sealed record ReportPreferences(IReadOnlyList<string> Fields, string? Classification, string? Search);
    public sealed record ReportPreferencesResponse(ReportPreferences? Preferences, DateTime? UpdatedAtUtc);
    public sealed record ReportDataResponse(string Status, string Kind, string Mode, IReadOnlyList<ReportField> Columns,
        IReadOnlyList<IReadOnlyList<string>> Rows, int Total, int Offset, int Limit, int FilterRules, string? Reason, string? RevisionToken);
    public sealed record ReportExport(string Status, string Kind, IReadOnlyList<ReportField> Columns, IEnumerable<string[]> Rows,
        int Total, bool Truncated, string? Reason);

    private sealed record ReportPlan(string Kind, string Label, string Mode, CategoryMapping? Map, string? PidTable, string? PipingTable, string? Reason);
    private sealed record ReportSession(string Status, ReportPlan Plan, IReadOnlyList<ReportField> Fields, int FilterRules, string? Revision,
        Func<IReadOnlyList<ReportField>, string?, string?, IEnumerable<string[]>> Rows);

    private static readonly (string Kind, string Label, string? DefaultCategory)[] ReportKinds =
    [
        ("lines", "Linhas", "line"), ("equipment", "Equipamentos", "equip"), ("instruments", "Instrumentos", "inst"),
        ("valves", "Válvulas", "valve"), ("tie-ins", "Tie-ins", "tiein"), ("materials", "Materiais", null), ("supports", "Suportes", null),
    ];

    private static readonly IReadOnlyDictionary<string, string> ReportClassLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["matched"] = "Conforme", ["divergent"] = "Divergente", ["missing3d"] = "Falta no 3D", ["extra3d"] = "Extra no 3D",
        ["taglessPid"] = "P&ID sem TAG", ["tagless3d"] = "3D sem TAG",
    };

    public static bool IsReportKind(string? kind) => ReportKinds.Any(x => x.Kind.Equals(kind, StringComparison.Ordinal));

    // ───────── configuração (por projeto) ─────────
    public async Task<ReportConfigResponse> GetReportConfigAsync(string projectId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT ConfigJson,UpdatedAtUtc FROM dbo.PlantReportConfigs WHERE ProjectIdAcc=@id", cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        await using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new(new Dictionary<string, ReportSource>(), null);
        return new(ParseReportSources(r.GetString(0)), r.GetDateTime(1));
    }

    private static Dictionary<string, ReportSource> ParseReportSources(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ReportSource>>>(json, JsonOptions);
            var map = doc is not null && doc.TryGetValue("sources", out var s) ? s : new Dictionary<string, ReportSource>();
            return new(map.Where(x => IsReportKind(x.Key)), StringComparer.Ordinal);
        }
        catch (JsonException) { throw new PlantIntegrationValidationException("Configuração de relatórios inválida no banco N&A."); }
    }

    public async Task<ReportConfigResponse> SaveReportConfigAsync(string projectId, Guid dashboardId,
        IReadOnlyDictionary<string, ReportSource> sources, CancellationToken ct)
    {
        var history = await HistoryAsync(dashboardId, ct);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            throw new PlantIntegrationHistoryException("Aguardando versão-base COMPLETED para validar as tabelas dos relatórios.");
        var mappings = await GetConfigurationAsync(projectId, ct);
        var clean = ValidateReportSources(sources, mappings, Schema(history.Pnid.Rows.Values), Schema(history.Piping.Rows.Values));
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["sources"] = clean }, JsonOptions);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("""
            UPDATE dbo.PlantReportConfigs WITH (UPDLOCK, SERIALIZABLE) SET ConfigJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@id;
            IF @@ROWCOUNT=0 INSERT dbo.PlantReportConfigs(ProjectIdAcc,ConfigJson,CreatedAtUtc,UpdatedAtUtc) VALUES(@id,@json,SYSUTCDATETIME(),SYSUTCDATETIME());
            """, cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        q.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
        await q.ExecuteNonQueryAsync(ct);
        return await GetReportConfigAsync(projectId, ct);
    }

    internal static Dictionary<string, ReportSource> ValidateReportSources(IReadOnlyDictionary<string, ReportSource> sources,
        IReadOnlyList<CategoryMapping> mappings, IReadOnlyDictionary<string, string[]> pid, IReadOnlyDictionary<string, string[]> piping)
    {
        var clean = new Dictionary<string, ReportSource>(StringComparer.Ordinal);
        foreach (var (kind, raw) in sources)
        {
            if (!IsReportKind(kind)) throw new PlantIntegrationValidationException("Relatório desconhecido: " + kind);
            if (raw is null) continue;
            string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            var category = Norm(raw.CategoryId); var pidTable = Norm(raw.PidTable); var pipingTable = Norm(raw.PipingTable);
            if (category is not null && (pidTable is not null || pipingTable is not null))
                throw new PlantIntegrationValidationException($"{kind}: escolha categoria do DE/PARA OU tabelas, não ambos.");
            if (category is not null)
            {
                var m = mappings.FirstOrDefault(x => x.Id.Equals(category, StringComparison.OrdinalIgnoreCase))
                    ?? throw new PlantIntegrationValidationException($"{kind}: categoria '{category}' não existe no DE/PARA.");
                category = m.Id;
            }
            if (pidTable is not null) pidTable = pid.Keys.FirstOrDefault(x => x.Equals(pidTable, StringComparison.OrdinalIgnoreCase))
                ?? throw new PlantIntegrationValidationException($"{kind}: tabela P&ID inexistente no schema.");
            if (pipingTable is not null) pipingTable = piping.Keys.FirstOrDefault(x => x.Equals(pipingTable, StringComparison.OrdinalIgnoreCase))
                ?? throw new PlantIntegrationValidationException($"{kind}: tabela 3D inexistente no schema.");
            if (category is null && pidTable is null && pipingTable is null) continue;
            clean[kind] = new(category, pidTable, pipingTable);
        }
        return clean;
    }

    // ───────── preferências do usuário (campos/filtros) ─────────
    public async Task<ReportPreferencesResponse> GetReportPreferencesAsync(string projectId, int userId, string kind, CancellationToken ct)
    {
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT PreferencesJson,UpdatedAtUtc FROM dbo.PlantReportUserPreferences WHERE ProjectIdAcc=@p AND UserId=@u AND ReportKind=@k", cn);
        q.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        q.Parameters.Add("@k", SqlDbType.NVarChar, 40).Value = kind;
        await using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new(null, null);
        try { return new(JsonSerializer.Deserialize<ReportPreferences>(r.GetString(0), JsonOptions), r.GetDateTime(1)); }
        catch (JsonException) { return new(null, null); } // preferência corrompida nunca bloqueia o relatório
    }

    public async Task<ReportPreferencesResponse> SaveReportPreferencesAsync(string projectId, int userId, string kind,
        ReportPreferences prefs, CancellationToken ct)
    {
        if (!IsReportKind(kind)) throw new PlantIntegrationValidationException("Relatório desconhecido.");
        var fields = (prefs.Fields ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (fields.Length > ReportMaxFields || fields.Any(x => x.Length > 300)) throw new PlantIntegrationValidationException("Campos selecionados inválidos.");
        var search = prefs.Search?.Trim();
        if (search?.Length > ReportMaxSearch) throw new PlantIntegrationValidationException("Busca excede o limite.");
        var cls = string.IsNullOrWhiteSpace(prefs.Classification) ? null : prefs.Classification.Trim();
        if (cls is not null && !ReportClassLabels.ContainsKey(cls)) throw new PlantIntegrationValidationException("Situação inválida.");
        var json = JsonSerializer.Serialize(new ReportPreferences(fields, cls, string.IsNullOrEmpty(search) ? null : search), JsonOptions);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("""
            UPDATE dbo.PlantReportUserPreferences WITH (UPDLOCK, SERIALIZABLE) SET PreferencesJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@p AND UserId=@u AND ReportKind=@k;
            IF @@ROWCOUNT=0 INSERT dbo.PlantReportUserPreferences(ProjectIdAcc,UserId,ReportKind,PreferencesJson,CreatedAtUtc,UpdatedAtUtc) VALUES(@p,@u,@k,@json,SYSUTCDATETIME(),SYSUTCDATETIME());
            """, cn);
        q.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        q.Parameters.Add("@k", SqlDbType.NVarChar, 40).Value = kind;
        q.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
        await q.ExecuteNonQueryAsync(ct);
        return await GetReportPreferencesAsync(projectId, userId, kind, ct);
    }

    // ───────── catálogo / dados / exportação ─────────
    public async Task<ReportCatalogResponse> GetReportCatalogAsync(string projectId, Guid dashboardId, CancellationToken ct)
    {
        var s = await PrepareReportsAsync(projectId, dashboardId, null, ct);
        return new(s.Status, s.Descriptors, ReportMaxExportRows, ReportMaxPage);
    }

    public static ReportCatalogResponse UnlinkedReportCatalog() => new("unlinked", [], ReportMaxExportRows, ReportMaxPage);

    public async Task<ReportDataResponse> GetReportDataAsync(string projectId, Guid dashboardId, string kind, IReadOnlyList<string>? fieldIds,
        string? classification, string? search, int offset, int limit, CancellationToken ct)
    {
        var s = await PrepareReportsAsync(projectId, dashboardId, kind, ct);
        if (s.Session is null) return new(s.Status, kind, "unmapped", [], [], 0, offset, limit, 0, null, null);
        if (s.Session.Plan.Mode == "unmapped")
            return new("unmapped", kind, "unmapped", [], [], 0, offset, limit, 0, s.Session.Plan.Reason, s.Session.Revision);
        var cols = SelectReportFields(s.Session, fieldIds);
        var page = new List<IReadOnlyList<string>>(Math.Min(limit, 64)); var total = 0;
        foreach (var row in s.Session.Rows(cols, classification, search)) { if (total >= offset && total < offset + limit) page.Add(row); total++; }
        return new(s.Session.Status, kind, s.Session.Plan.Mode, cols, page, total, offset, limit, s.Session.FilterRules, s.Session.Plan.Reason, s.Session.Revision);
    }

    public async Task<ReportExport> GetReportExportAsync(string projectId, Guid dashboardId, string kind, IReadOnlyList<string>? fieldIds,
        string? classification, string? search, CancellationToken ct)
    {
        var s = await PrepareReportsAsync(projectId, dashboardId, kind, ct);
        if (s.Session is null) return new(s.Status, kind, [], [], 0, false, null);
        if (s.Session.Plan.Mode == "unmapped") return new("unmapped", kind, [], [], 0, false, s.Session.Plan.Reason);
        var cols = SelectReportFields(s.Session, fieldIds);
        var total = s.Session.Rows(cols, classification, search).Take(ReportMaxExportRows + 1).Count();
        return new(s.Session.Status, kind, cols, s.Session.Rows(cols, classification, search).Take(ReportMaxExportRows),
            Math.Min(total, ReportMaxExportRows), total > ReportMaxExportRows, s.Session.Plan.Reason);
    }

    private static IReadOnlyList<ReportField> SelectReportFields(ReportSession s, IReadOnlyList<string>? requested)
    {
        var wanted = (requested ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0) return s.Fields.Where(f => DefaultReportFields(s.Fields, s.Plan).Contains(f.Id)).ToArray();
        if (wanted.Length > ReportMaxFields) throw new PlantIntegrationValidationException($"Máximo de {ReportMaxFields} campos por relatório.");
        var byId = s.Fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
        return wanted.Select(id => byId.TryGetValue(id, out var f) ? f
            : throw new PlantIntegrationValidationException("Campo não disponível neste relatório: " + id)).ToArray();
    }

    private static HashSet<string> DefaultReportFields(IReadOnlyList<ReportField> fields, ReportPlan plan)
    {
        var fixedIds = plan.Mode == "mapped" ? new[] { "tag", "classification", "pidDrawing", "pipingDrawing" } : new[] { "side", "rowId", "drawing" };
        var rest = fields.Where(f => f.Id.StartsWith("attr:", StringComparison.Ordinal) || f.Id.StartsWith("col:", StringComparison.Ordinal))
            .Take(plan.Mode == "mapped" ? 12 : 8).Select(f => f.Id);
        return fixedIds.Concat(rest).ToHashSet(StringComparer.Ordinal);
    }

    private sealed record Prepared(string Status, ReportSession? Session, IReadOnlyList<ReportDescriptor> Descriptors);

    private async Task<Prepared> PrepareReportsAsync(string projectId, Guid dashboardId, string? onlyKind, CancellationToken ct)
    {
        if (onlyKind is not null && !IsReportKind(onlyKind)) throw new PlantIntegrationValidationException("Relatório desconhecido.");
        var h = await HistoryAsync(dashboardId, ct);
        if (h.Pnid.Base is null || h.Piping.Base is null) return new("no-baseline", null, []);
        var mappings = await GetConfigurationAsync(projectId, ct);
        var pidSchema = Schema(h.Pnid.Rows.Values); var pipingSchema = Schema(h.Piping.Rows.Values);
        if (mappings.Count > 0) Validate(mappings, pidSchema, pipingSchema);
        // Sem a migration 005 os relatórios ainda funcionam com os padrões (DE/PARA); só a escolha de tabelas fica indisponível.
        ReportConfigResponse config;
        try { config = await GetReportConfigAsync(projectId, ct); }
        catch (SqlException ex) when (ex.Number == 208) { config = new(new Dictionary<string, ReportSource>(), null); }
        var filters = (await GetFiltersAsync(projectId, ct)).Filters;
        var revision = Revision(h);
        var descriptors = new List<ReportDescriptor>(); ReportSession? chosen = null;
        foreach (var (kind, label, defaultCategory) in ReportKinds)
        {
            if (onlyKind is not null && kind != onlyKind) continue;
            var plan = ResolveReportPlan(kind, label, defaultCategory, config.Sources.GetValueOrDefault(kind), mappings, pidSchema, pipingSchema);
            var fields = BuildReportFields(plan, pidSchema, pipingSchema);
            var session = BuildReportSession(plan, fields, h, mappings, filters, revision);
            descriptors.Add(new(kind, label, plan.Mode, plan.Map?.Id, plan.PidTable, plan.PipingTable, plan.Reason, fields, DefaultReportFields(fields, plan).ToArray()));
            if (onlyKind is not null) chosen = session;
        }
        return new("ready", chosen, descriptors);
    }

    private static ReportPlan ResolveReportPlan(string kind, string label, string? defaultCategory, ReportSource? src,
        IReadOnlyList<CategoryMapping> mappings, IReadOnlyDictionary<string, string[]> pid, IReadOnlyDictionary<string, string[]> piping)
    {
        var pidTable = src?.PidTable; var pipingTable = src?.PipingTable;
        if (!string.IsNullOrWhiteSpace(pidTable) || !string.IsNullOrWhiteSpace(pipingTable))
        {
            var okPid = string.IsNullOrWhiteSpace(pidTable) ? null : pid.Keys.FirstOrDefault(x => x.Equals(pidTable, StringComparison.OrdinalIgnoreCase));
            var okPiping = string.IsNullOrWhiteSpace(pipingTable) ? null : piping.Keys.FirstOrDefault(x => x.Equals(pipingTable, StringComparison.OrdinalIgnoreCase));
            if (okPid is null && okPiping is null)
                return new(kind, label, "unmapped", null, null, null, "As tabelas configuradas não existem mais no schema atual do Dashboard BIM.");
            return new(kind, label, "table", null, okPid, okPiping, null);
        }
        var categoryId = src?.CategoryId ?? defaultCategory;
        if (categoryId is null)
            return new(kind, label, "unmapped", null, null, null, $"Não mapeado: não há tabela de {label.ToLowerInvariant()} configurada. Escolha a tabela em Configuração de Relatórios.");
        var map = mappings.FirstOrDefault(x => x.Id.Equals(categoryId, StringComparison.OrdinalIgnoreCase));
        return map is null
            ? new(kind, label, "unmapped", null, null, null, mappings.Count == 0 ? "Sem DE/PARA salvo: nada a extrair." : $"Não mapeado: a categoria '{categoryId}' não existe no DE/PARA. Escolha outra categoria ou uma tabela em Configuração de Relatórios.")
            : new(kind, label, "mapped", map, null, null, null);
    }

    private static IReadOnlyList<ReportField> BuildReportFields(ReportPlan plan, IReadOnlyDictionary<string, string[]> pid, IReadOnlyDictionary<string, string[]> piping)
    {
        var f = new List<ReportField>();
        if (plan.Mode == "mapped" && plan.Map is { } m)
        {
            f.Add(new("tag", "TAG", "Identificação")); f.Add(new("classification", "Situação P&ID × 3D", "Identificação"));
            f.Add(new("pidDrawing", "Desenho P&ID", "Identificação")); f.Add(new("pipingDrawing", "Desenho 3D", "Identificação"));
            f.Add(new("pidRowId", "PnPID P&ID", "Identificação")); f.Add(new("pipingRowId", "PnPID 3D", "Identificação"));
            foreach (var a in m.Attributes)
            {
                f.Add(new($"attr:{a.Id}:pid", $"{a.Name} (P&ID)", "Campos DE/PARA"));
                f.Add(new($"attr:{a.Id}:piping", $"{a.Name} (3D)", "Campos DE/PARA"));
            }
        }
        else if (plan.Mode == "table")
        {
            f.Add(new("side", "Origem", "Identificação")); f.Add(new("table", "Tabela", "Identificação"));
            f.Add(new("rowId", "PnPID", "Identificação")); f.Add(new("drawing", "Desenho", "Identificação"));
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan.PidTable is not null && pid.TryGetValue(plan.PidTable, out var pc)) foreach (var c in pc) cols.Add(c);
            if (plan.PipingTable is not null && piping.TryGetValue(plan.PipingTable, out var tc)) foreach (var c in tc) cols.Add(c);
            foreach (var c in cols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(ReportMaxColumnsListed)) f.Add(new("col:" + c, c, "Colunas da tabela"));
        }
        return f;
    }

    private static ReportSession BuildReportSession(ReportPlan plan, IReadOnlyList<ReportField> fields, History h,
        IReadOnlyList<CategoryMapping> mappings, Dictionary<string, List<FilterRule>> filters, string revision)
    {
        if (plan.Mode == "unmapped") return new("unmapped", plan, fields, 0, revision, (_, _, _) => []);
        if (plan.Mode == "mapped")
        {
            var map = plan.Map!;
            var rules = filters.GetValueOrDefault(map.Id, []);
            // EXATAMENTE a mesma seleção filtrada de GetResultsAsync: a exportação nunca omite as regras globais.
            var p = EffectiveRows(h.Pnid.Rows.Values.Where(x => x.Table.Equals(map.PidTable, StringComparison.OrdinalIgnoreCase)), "pid", rules, DrawingIndex(h.Pnid)).OrderBy(x => x.RowId, StringComparer.Ordinal).ToArray();
            var t = EffectiveRows(h.Piping.Rows.Values.Where(x => x.Table.Equals(map.PipingTable, StringComparison.OrdinalIgnoreCase)), "piping", rules, DrawingIndex(h.Piping)).OrderBy(x => x.RowId, StringComparer.Ordinal).ToArray();
            var items = Compare(map, p, t).Items;
            var pById = p.ToDictionary(x => x.RowId, StringComparer.Ordinal); var tById = t.ToDictionary(x => x.RowId, StringComparer.Ordinal);
            var attrs = map.Attributes.ToDictionary(a => a.Id, StringComparer.Ordinal);
            return new("ready", plan, fields, rules.Count(r => r.Enabled), revision, (cols, cls, q) =>
            {
                var getters = cols.Select(c => MappedGetter(c.Id, attrs)).ToArray();
                return items.Where(i => string.IsNullOrEmpty(cls) || i.Classification == cls).Select(i =>
                {
                    var pr = i.PidRowId is null ? null : pById.GetValueOrDefault(i.PidRowId); var tr = i.PipingRowId is null ? null : tById.GetValueOrDefault(i.PipingRowId);
                    return getters.Select(g => g(i, pr, tr)).ToArray();
                }).Where(r => SearchMatches(r, q));
            });
        }
        // Tabela escolhida: se ela é a tabela de uma categoria do DE/PARA, valem as regras globais dessa categoria (mesmo filtro do
        // Inventário/Resultados); senão, regras salvas sob a chave do relatório ("materials"/"supports"). Nunca extrai "sem filtro" por omissão.
        var sources = new List<(string Side, string Label, PartState State, string Table, IReadOnlyList<FilterRule> Rules)>();
        IReadOnlyList<FilterRule> RulesFor(bool pid, string table) =>
            filters.GetValueOrDefault(mappings.FirstOrDefault(m => (pid ? m.PidTable : m.PipingTable).Equals(table, StringComparison.OrdinalIgnoreCase))?.Id ?? plan.Kind, []);
        if (plan.PidTable is not null) sources.Add(("pid", "P&ID", h.Pnid, plan.PidTable, RulesFor(true, plan.PidTable)));
        if (plan.PipingTable is not null) sources.Add(("piping", "3D", h.Piping, plan.PipingTable, RulesFor(false, plan.PipingTable)));
        var activeRules = sources.SelectMany(s => s.Rules).Count(r => r.Enabled);
        return new("ready", plan, fields, activeRules, revision, (cols, _, q) =>
        {
            var getters = cols.Select(c => TableGetter(c.Id)).ToArray();
            return sources.SelectMany(s =>
            {
                var drawings = DrawingIndex(s.State);
                var rows = EffectiveRows(s.State.Rows.Values.Where(x => x.Table.Equals(s.Table, StringComparison.OrdinalIgnoreCase)), s.Side, s.Rules, drawings).OrderBy(x => x.RowId, StringComparer.Ordinal);
                return rows.Select(row => { var drawing = DrawingName(row, drawings); return getters.Select(g => g(s.Label, row, drawing)).ToArray(); });
            }).Where(r => SearchMatches(r, q));
        });
    }

    private static bool SearchMatches(string[] row, string? q) =>
        string.IsNullOrWhiteSpace(q) || row.Any(c => c.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));

    private static Func<ResultItem, PlantRow?, PlantRow?, string> MappedGetter(string id, IReadOnlyDictionary<string, AttributeMapping> attrs)
    {
        switch (id)
        {
            case "tag": return (i, _, _) => i.Tag ?? "";
            case "classification": return (i, _, _) => ReportClassLabels.GetValueOrDefault(i.Classification, i.Classification);
            case "pidDrawing": return (i, _, _) => i.PidDrawing ?? "";
            case "pipingDrawing": return (i, _, _) => i.PipingDrawing ?? "";
            case "pidRowId": return (i, _, _) => i.PidRowId ?? "";
            case "pipingRowId": return (i, _, _) => i.PipingRowId ?? "";
        }
        var parts = id.Split(':');
        if (parts.Length == 3 && parts[0] == "attr" && attrs.TryGetValue(parts[1], out var a))
            return parts[2] == "pid" ? (_, p, _) => p?.Values.GetValueOrDefault(a.PidCol) ?? "" : (_, _, t) => t?.Values.GetValueOrDefault(a.PipingCol) ?? "";
        throw new PlantIntegrationValidationException("Campo não disponível neste relatório: " + id);
    }

    private static Func<string, PlantRow, string?, string> TableGetter(string id) => id switch
    {
        "side" => (side, _, _) => side,
        "table" => (_, r, _) => r.Table,
        "rowId" => (_, r, _) => r.RowId,
        "drawing" => (_, _, d) => d ?? "",
        _ when id.StartsWith("col:", StringComparison.Ordinal) => (_, r, _) => r.Values.GetValueOrDefault(id[4..]) ?? "",
        _ => throw new PlantIntegrationValidationException("Campo não disponível neste relatório: " + id),
    };
}

/// <summary>Formatação segura de planilhas: neutraliza injeção de fórmula (CSV/Excel) e escapa CSV (RFC 4180).</summary>
public static class PlantReportFormat
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r', '\n', '|', '%'];

    /// <summary>Texto que começaria uma fórmula no Excel/Calc ganha apóstrofo inicial (vira texto literal).</summary>
    public static string NeutralizeFormula(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var trimmed = value.TrimStart(' ');
        return trimmed.Length > 0 && Array.IndexOf(FormulaStarts, trimmed[0]) >= 0 ? "'" + value : value;
    }

    public static string CsvCell(string? value)
    {
        var safe = NeutralizeFormula(value);
        return safe.IndexOfAny(['"', ';', ',', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"") + "\"" : safe;
    }

    /// <summary>CSV com BOM UTF-8 e separador ';' (Excel pt-BR). Os rótulos de cabeçalho também são neutralizados.</summary>
    public static async Task WriteCsvAsync(Stream body, IEnumerable<string> header, IEnumerable<string[]> rows, CancellationToken ct)
    {
        await using var w = new StreamWriter(body, new UTF8Encoding(true), 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        await w.WriteLineAsync(string.Join(';', header.Select(CsvCell)));
        var n = 0;
        foreach (var row in rows)
        {
            await w.WriteLineAsync(string.Join(';', row.Select(CsvCell)));
            if (++n % 2000 == 0) { ct.ThrowIfCancellationRequested(); await w.FlushAsync(ct); }
        }
        await w.FlushAsync(ct);
    }

    public static string SafeFileName(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')).Trim('_');
}
