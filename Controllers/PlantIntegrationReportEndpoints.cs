using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.PlantIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

// F6 — Central de Relatórios Plant 3D. Somente leitura do histórico DCF; config/preferências ficam no banco N&A (migration 005).
public sealed partial class PlantIntegrationController
{
    public sealed record ReportConfigRequest(Dictionary<string, PlantIntegrationService.ReportSource>? Sources);
    public sealed record ReportPreferencesRequest(IReadOnlyList<string>? Fields, string? Classification, string? Search);

    private const string ReportMigrationMessage = "Migration PlantReports (005) ainda não aplicada.";

    [HttpGet("projects/{projectIdAcc}/reports")]
    [RequirePermission("plant3d.consolidated.view")]
    public async Task<IActionResult> ReportCatalog(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return Ok(PlantIntegrationService.UnlinkedReportCatalog());
            return Ok(await service.GetReportCatalogAsync(projectIdAcc, link.Value, ct));
        }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    [HttpGet("projects/{projectIdAcc}/reports/config")]
    [RequirePermission("plant3d.consolidated.view")]
    public async Task<IActionResult> ReportConfig(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try { return Ok(await service.GetReportConfigAsync(projectIdAcc, ct)); }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    [HttpPut("projects/{projectIdAcc}/reports/config")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> SaveReportConfig(string projectIdAcc, [FromBody] ReportConfigRequest? body, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (body?.Sources is null) return BadRequest(new { message = "sources é obrigatório." });
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return NotFound(new { message = "Projeto sem vínculo com Dashboard BIM." });
            return Ok(await service.SaveReportConfigAsync(projectIdAcc, link.Value, body.Sources, ct));
        }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    [HttpGet("projects/{projectIdAcc}/reports/{kind}/preferences")]
    [RequirePermission("plant3d.consolidated.view")]
    public async Task<IActionResult> ReportPreferences(string projectIdAcc, string kind, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (!PlantIntegrationService.IsReportKind(kind)) return NotFound(new { message = "Relatório desconhecido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        var user = await permissions.GetCurrentUserAsync(ct);
        if (user?.UserId is not int userId) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(await service.GetReportPreferencesAsync(projectIdAcc, userId, kind, ct)); }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    [HttpPut("projects/{projectIdAcc}/reports/{kind}/preferences")]
    [RequirePermission("plant3d.consolidated.view")]
    public async Task<IActionResult> SaveReportPreferences(string projectIdAcc, string kind, [FromBody] ReportPreferencesRequest? body, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (!PlantIntegrationService.IsReportKind(kind)) return NotFound(new { message = "Relatório desconhecido." });
        if (body?.Fields is null) return BadRequest(new { message = "fields é obrigatório." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        var user = await permissions.GetCurrentUserAsync(ct);
        if (user?.UserId is not int userId) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(await service.SaveReportPreferencesAsync(projectIdAcc, userId, kind, new(body.Fields, body.Classification, body.Search), ct)); }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    /// <summary>Página de dados (≤ 500 linhas). fields = ids separados por vírgula; os filtros globais do projeto SEMPRE se aplicam.</summary>
    [HttpGet("projects/{projectIdAcc}/reports/{kind}/data")]
    [RequirePermission("plant3d.consolidated.view")]
    public async Task<IActionResult> ReportData(string projectIdAcc, string kind, string? fields = null, string? classification = null,
        string? q = null, int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (limit is < 1 or > PlantIntegrationService.ReportMaxPage || offset < 0)
            return BadRequest(new { message = $"limit deve estar entre 1 e {PlantIntegrationService.ReportMaxPage} e offset não pode ser negativo." });
        if (!PlantIntegrationService.IsReportKind(kind)) return NotFound(new { message = "Relatório desconhecido." });
        if (q?.Length > PlantIntegrationService.ReportMaxSearch) return BadRequest(new { message = "Busca excede o limite." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return Ok(new PlantIntegrationService.ReportDataResponse("unlinked", kind, "unmapped", [], [], 0, offset, limit, 0, null, null));
            return Ok(await service.GetReportDataAsync(projectIdAcc, link.Value, kind, SplitFields(fields), classification, q, offset, limit, ct));
        }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    /// <summary>Exportação CSV em streaming (limite de 250 mil linhas, informado em X-Plant-Report-Truncated). XLSX é gerado no cliente a partir de /data.</summary>
    [HttpGet("projects/{projectIdAcc}/reports/{kind}/export")]
    [RequirePermission("plant3d.consolidated.view")]
    public async Task<IActionResult> ReportExport(string projectIdAcc, string kind, string? fields = null, string? classification = null,
        string? q = null, CancellationToken ct = default)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (!PlantIntegrationService.IsReportKind(kind)) return NotFound(new { message = "Relatório desconhecido." });
        if (q?.Length > PlantIntegrationService.ReportMaxSearch) return BadRequest(new { message = "Busca excede o limite." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return NotFound(new { message = "Projeto sem vínculo com Dashboard BIM." });
            var export = await service.GetReportExportAsync(projectIdAcc, link.Value, kind, SplitFields(fields), classification, q, ct);
            if (export.Status != "ready") return Conflict(new { message = export.Reason ?? "Relatório indisponível: " + export.Status });
            if (export.Columns.Count == 0) return Conflict(new { message = "Nenhum campo selecionado para exportar." });
            Response.Headers["X-Plant-Report-Truncated"] = export.Truncated ? "true" : "false";
            Response.Headers["X-Plant-Report-Total"] = export.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["Access-Control-Expose-Headers"] = "X-Plant-Report-Truncated, X-Plant-Report-Total";
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = $"attachment; filename=\"plant-{PlantReportFormat.SafeFileName(kind)}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv\"";
            await PlantReportFormat.WriteCsvAsync(Response.Body, export.Columns.Select(c => c.Label), export.Rows, ct);
            return new EmptyResult();
        }
        catch (Exception ex) when (ReportError(ex) is { } result) { return result; }
    }

    private static IReadOnlyList<string>? SplitFields(string? fields) =>
        string.IsNullOrWhiteSpace(fields) ? null : fields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private IActionResult? ReportError(Exception ex) => ex switch
    {
        PlantIntegrationValidationException e => BadRequest(new { message = e.Message }),
        PlantIntegrationHistoryException e => Conflict(new { message = e.Message }),
        PlantIntegrationConfigurationException e => StatusCode(503, new { message = e.Message }),
        SqlException { Number: 208 } => StatusCode(503, new { message = ReportMigrationMessage }),
        _ => null,
    };
}
