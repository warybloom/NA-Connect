using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.PlantIntegration;

public sealed partial class PlantIntegrationService
{
    public async Task<PhaseResponse> GetPhasesAsync(string projectId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT PhasesJson,UpdatedAtUtc FROM dbo.PlantMeasurementPhases WHERE ProjectIdAcc=@id", cn);
        q.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = projectId;
        await using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new([],null);
        List<MeasurementPhase> phases;
        try { phases = JsonSerializer.Deserialize<List<MeasurementPhase>>(r.GetString(0), JsonOptions) ?? []; }
        catch (JsonException ex) { throw new PlantIntegrationValidationException("Fases persistidas inválidas: " + ex.Message); }
        return new(phases.OrderBy(x=>x.SortOrder).ThenBy(x=>x.Name).ToArray(),r.GetDateTime(1));
    }

    public async Task<PhaseResponse> SavePhasesAsync(string projectId, IReadOnlyList<MeasurementPhase> phases, CancellationToken ct)
    {
        if (phases.Count > 100) throw new PlantIntegrationValidationException("Máximo de 100 fases.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phase in phases)
        {
            if (phase is null || string.IsNullOrWhiteSpace(phase.Id) || phase.Id.Length > 100 ||
                string.IsNullOrWhiteSpace(phase.Name) || phase.Name.Length > 160 ||
                !ids.Add(phase.Id) || phase.SortOrder < 0 || phase.SortOrder > 100000)
                throw new PlantIntegrationValidationException("ID, nome ou ordem inválida/duplicada para uma fase.");
            if (phase.Expected is null || phase.Weights is null || phase.Expected.Count>100 || phase.Weights.Count>100 ||
                phase.Expected.Any(k => string.IsNullOrWhiteSpace(k.Key) || k.Key.Length > 80 || k.Value<0 || k.Value>10000000 || k.Value!=Math.Truncate(k.Value)) ||
                phase.Weights.Any(k=>string.IsNullOrWhiteSpace(k.Key) || k.Key.Length>80 || k.Value<0 || k.Value>100000))
                throw new PlantIntegrationValidationException("As metas devem ser contagens inteiras não negativas e pesos não negativos.");
        }
        var json = JsonSerializer.Serialize(phases.OrderBy(x=>x.SortOrder), JsonOptions);
        await using var cn = new SqlConnection(LocalConnection());
        await cn.OpenAsync(ct);
        await using var q = new SqlCommand("""
            UPDATE dbo.PlantMeasurementPhases WITH (UPDLOCK, SERIALIZABLE)
               SET PhasesJson=@json,UpdatedAtUtc=SYSUTCDATETIME() WHERE ProjectIdAcc=@id;
            IF @@ROWCOUNT=0
               INSERT dbo.PlantMeasurementPhases(ProjectIdAcc,PhasesJson,CreatedAtUtc,UpdatedAtUtc)
               VALUES(@id,@json,SYSUTCDATETIME(),SYSUTCDATETIME());
            """,cn);
        q.Parameters.Add("@id",SqlDbType.NVarChar,100).Value=projectId;
        q.Parameters.Add("@json",SqlDbType.NVarChar,-1).Value=json;
        await q.ExecuteNonQueryAsync(ct);
        return await GetPhasesAsync(projectId,ct);
    }

    public async Task<MeasurementResponse> GetMeasurementAsync(
        string projectId, Guid dashboardId, string phaseId, CancellationToken ct)
    {
        var phases = await GetPhasesAsync(projectId,ct);
        var phase = phases.Phases.FirstOrDefault(x=>x.Id==phaseId)
            ?? throw new PlantIntegrationValidationException("Fase não cadastrada neste projeto.");
        var mappings = await GetConfigurationAsync(projectId,ct);
        if (mappings.Count==0)
            return new(phase.Id,phase.Name,"no-mapping",
                phase.Expected.Select(k=>new MeasurementCategory(k.Key,k.Key,k.Value,null,null)).ToArray());
        var history = await HistoryAsync(dashboardId,ct);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            return new(phase.Id,phase.Name,"no-baseline",
                mappings.Select(m=>new MeasurementCategory(m.Id,m.Label,
                    phase.Expected.TryGetValue(m.Id,out var val)?val:null,null,null)).ToArray());
        var filterSet = (await GetFiltersAsync(projectId,ct)).Filters;
        var drawsPid = DrawingIndex(history.Pnid);
        var draws3d = DrawingIndex(history.Piping);
        var categories = new List<MeasurementCategory>();
        foreach (var m in mappings)
        {
            var rules = filterSet.GetValueOrDefault(m.Id,[]);
            var p=EffectiveRows(history.Pnid.Rows.Values.Where(x=>x.Table.Equals(m.PidTable,StringComparison.OrdinalIgnoreCase)),
                "pid",rules,drawsPid);
            var t=EffectiveRows(history.Piping.Rows.Values.Where(x=>x.Table.Equals(m.PipingTable,StringComparison.OrdinalIgnoreCase)),
                "piping",rules,draws3d);
            categories.Add(new(m.Id,m.Label,phase.Expected.TryGetValue(m.Id,out var value)?value:null,p.Count,t.Count));
        }
        return new(phase.Id,phase.Name,"ready",categories);
    }
}
