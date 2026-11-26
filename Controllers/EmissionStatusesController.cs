using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

/// <summary>Independent per-project definition of statuses proving issuance. Does not mutate LDP.</summary>
[ApiController]
[Route("api/planning/emission-statuses")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class EmissionStatusesController(
    IConfiguration configuration, IWebHostEnvironment environment,
    IProjectAccessService projectAccess, IPermissionService permissions,
    ILogger<EmissionStatusesController> logger) : ControllerBase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string[] Defaults = ["APPROVED", "APROVADO"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string FilePath => Path.Combine(configuration["EmissionStatuses:Directory"]
        ?? Path.Combine(environment.ContentRootPath, "App_Data"), "emission-statuses.json");

    public sealed record ProjectSettings(long Revision, string[] Statuses, string? UpdatedBy, DateTimeOffset? UpdatedAt);
    public sealed record SettingsStore(Dictionary<string, ProjectSettings> Projects);
    public sealed record SaveRequest(long Revision, string[] Statuses);

    private static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();
    private string ConnectionString => configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("Database connection missing");

    private async Task<IActionResult?> Authorize(string projectId, bool editing, CancellationToken ct)
    {
        if (!projectId.StartsWith("b.", StringComparison.Ordinal) ||
            projectId.Length != 38 || !Guid.TryParseExact(projectId[2..], "D", out _))
            return BadRequest(new { message = "Projeto inválido." });
        return await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId,
            editing ? ["planning.emission-statuses.edit", "planning.planner.management-documents.edit"] : ["planning.emission-statuses.view", "planning.planner.view", "reports.gestao.view", "reports.weekly.view", "reports.ldp.view", "reports.desvios.view", "reports.timesheet.view", "production.managerial.hh-custo.view", "production.financial.project.view", "production.financial.document.view"], ct);
    }

    private async Task<SettingsStore> ReadAsync(CancellationToken ct)
    {
        if (!System.IO.File.Exists(FilePath)) return new(new(StringComparer.Ordinal));
        await using var stream = System.IO.File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<SettingsStore>(stream, Json, ct)
            ?? new(new(StringComparer.Ordinal));
    }

    private async Task<bool> IsChs001Async(string projectId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Nome_Projeto FROM dbo.Projetos WHERE Project_ID_ACC=@id", conn);
        cmd.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = projectId;
        var name = Convert.ToString(await cmd.ExecuteScalarAsync(ct)) ?? "";
        return name.StartsWith("CHS001", StringComparison.OrdinalIgnoreCase)
            || name.Contains("CHS001 ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string[]> CatalogAsync(string projectId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT CAST(Document_Status AS NVARCHAR(200)) AS Status FROM dbo.VW_Ldp_Unificada
            WHERE Project_ID_ACC=@id AND Document_Status IS NOT NULL
            UNION
            SELECT CAST(Weekly_Progress_Status AS NVARCHAR(200)) AS Status FROM dbo.tb_WeeklyEngineeringSchedule
            WHERE Project_ID_ACC=@id AND Tipo_Registro='DADO' AND Weekly_Progress_Status IS NOT NULL
            """, conn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = projectId;
        var values = new SortedSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var status = Norm(reader.GetString(0));
            if (status.Length > 0) values.Add(status);
        }
        return values.ToArray();
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string projectId, [FromQuery] bool catalog, CancellationToken ct)
    {
        var denied = await Authorize(projectId, false, ct);
        if (denied != null) return denied;
        try
        {
            var catalogValues = catalog ? (await CatalogAsync(projectId, ct)).Concat(Defaults).Distinct(StringComparer.Ordinal).OrderBy(v=>v,StringComparer.Ordinal).ToArray() : Array.Empty<string>();
            await Gate.WaitAsync(ct);
            ProjectSettings? saved;
            try { saved = (await ReadAsync(ct)).Projects.GetValueOrDefault(projectId); }
            finally { Gate.Release(); }
            var proposed = saved?.Statuses ?? ((await IsChs001Async(projectId, ct))
                ? new[] { "APPROVED", "APROVADO", "CLIENT REVIEW" } : Defaults);
            return Ok(new { projectId, revision = saved?.Revision ?? 0,
                statuses = proposed, availableStatuses = catalogValues,
                configured = saved != null, updatedBy = saved?.UpdatedBy, updatedAt = saved?.UpdatedAt });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Emission statuses unavailable for project {ProjectId}", projectId);
            return StatusCode(503, new { message = "Não foi possível consultar os status de emissão." });
        }
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromQuery] string projectId, [FromBody] SaveRequest request, CancellationToken ct)
    {
        var denied = await Authorize(projectId, true, ct);
        if (denied != null) return denied;
        if (request?.Statuses is null || request.Statuses.Length > 100)
            return BadRequest(new { message = "Configuração inválida." });
        try
        {
            var selected = request.Statuses.Select(Norm).Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            var catalog = (await CatalogAsync(projectId, ct)).Concat(Defaults).Distinct(StringComparer.Ordinal).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            if (selected.Except(catalog, StringComparer.Ordinal).Any())
                return BadRequest(new { message = "Há status que não pertencem à LDP nem à programação deste projeto." });
            if (selected.Any(v => v is "CANCELADO" or "CANCELED"))
                return BadRequest(new { message = "Status cancelado não pode comprovar emissão." });
            await Gate.WaitAsync(ct);
            try
            {
                var store = await ReadAsync(ct);
                var revision = store.Projects.GetValueOrDefault(projectId)?.Revision ?? 0;
                if (request.Revision != revision)
                    return Conflict(new { message = "Configuração alterada por outro usuário; recarregue a página." });
                var user = await permissions.GetCurrentUserAsync(ct);
                store.Projects[projectId] = new(revision + 1, selected, user?.Email, DateTimeOffset.UtcNow);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await System.IO.File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(store, Json), ct);
                    if (System.IO.File.Exists(FilePath))
                        System.IO.File.Replace(tmp, FilePath, FilePath + ".bak", true);
                    else System.IO.File.Move(tmp, FilePath);
                }
                finally { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); }
                return Ok(new { projectId, revision = revision + 1, statuses = selected });
            }
            finally { Gate.Release(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Emission statuses failed to save for project {ProjectId}", projectId);
            return StatusCode(503, new { message = "Falha ao salvar os status de emissão." });
        }
    }
}
