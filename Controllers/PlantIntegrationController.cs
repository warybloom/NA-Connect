using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.PlantIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/plant-integration")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class PlantIntegrationController(
    IConfiguration configuration,
    IHostEnvironment environment,
    IProjectAccessService projectAccess,
    IPermissionService permissions,
    IPermissionRepository permissionRepository) : ControllerBase
{
    private readonly PlantIntegrationService service = new(configuration, environment);
    public sealed record LinkRequest(string? DashboardProjectId);
    public sealed record ConfigurationRequest(IReadOnlyList<PlantIntegrationService.CategoryMapping>? Mapping);

    [HttpGet("projects")]
    [RequirePermission("plant3d.view", AnyProject = true)]
    public async Task<IActionResult> Projects(CancellationToken ct)
    {
        var user = await permissions.GetCurrentUserAsync(ct);
        if (user?.UserId is not int userId) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            var allowed = await permissionRepository.ListarProjetosPermitidosAsync(userId, ct);
            var projects = await service.ListProjectsAsync(allowed.Where(p => p.IsAtivo).Select(p => p.ProjectIdAcc).ToArray(), ct);
            return Ok(new { projects = projects.Select(p => new { projectIdAcc = p.ProjectIdAcc, name = p.Name, dashboardProjectId = p.DashboardProjectId }) });
        }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Integração ainda não preparada: aplique a migration PlantProjectLinks." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("catalog")]
    [RequirePermission("plant3d.database.view", AnyProject = true)]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        try { return Ok(new { projects = await service.ListDashboardCatalogAsync(ct) }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpPut("projects/{projectIdAcc}/link")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> Link(string projectIdAcc, [FromBody] LinkRequest? body, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc deve usar o formato b.<guid>." });
        if (!Guid.TryParse(body?.DashboardProjectId, out var dashboardProjectId)) return BadRequest(new { message = "DashboardProjectId inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            if (!await service.ProjectExistsAsync(projectIdAcc, ct)) return NotFound(new { message = "Projeto N&A não encontrado." });
            if (!await service.DashboardProjectExistsAsync(dashboardProjectId, ct)) return NotFound(new { message = "Projeto Dashboard BIM não encontrado." });
            await service.LinkAsync(projectIdAcc, dashboardProjectId, ct);
            return Ok(new { projectIdAcc, dashboardProjectId });
        }
        catch (SqlException ex) when (ex.Number is 208 or 2627 or 2601) { return StatusCode(409, new { message = "Não foi possível persistir o vínculo; aplique a migration e confirme que o projeto Dashboard não está vinculado." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/status")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Status(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc deve usar o formato b.<guid>." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var dashboardProjectId = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (dashboardProjectId is null) return NotFound(new { message = "Projeto N&A sem vínculo com Dashboard BIM." });
            var status = await service.GetStatusAsync(dashboardProjectId.Value, ct);
            var filters = await service.GetFiltersAsync(projectIdAcc, ct);
            return Ok(new { projectIdAcc, dashboardProjectId, pnId = status.PnIdBaseline, piping = status.PipingBaseline,
                pnIdDelta = status.PnIdDelta, pipingDelta = status.PipingDelta,
                filtersUpdatedAtUtc = filters.UpdatedAtUtc, message = status.Message });
        }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Schema de versões do Dashboard BIM não está disponível." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/schema")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Schema(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc deve usar o formato b.<guid>." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var dashboardProjectId = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (dashboardProjectId is null) return NotFound(new { message = "Projeto N&A sem vínculo com Dashboard BIM." });
            var schema = await service.GetSchemaAsync(dashboardProjectId.Value, ct);
            return Ok(new { projectIdAcc, dashboardProjectId, pid = schema.Pid, piping = schema.Piping, latest = schema.Latest });
        }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Schema de versões do Dashboard BIM não está disponível." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/configuration")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Configuration(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc deve usar o formato b.<guid>." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try { return Ok(new { mapping = await service.GetConfigurationAsync(projectIdAcc, ct) }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Integração ainda não preparada: aplique a migration PlantCategoryMappings." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpPut("projects/{projectIdAcc}/configuration")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> PutConfiguration(string projectIdAcc, [FromBody] ConfigurationRequest? body, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc deve usar o formato b.<guid>." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (body?.Mapping is null) return BadRequest(new { message = "mapping é obrigatório." });
        try
        {
            var dashboardProjectId = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (dashboardProjectId is null) return NotFound(new { message = "Projeto N&A sem vínculo com Dashboard BIM." });
            await service.SaveConfigurationAsync(projectIdAcc, dashboardProjectId.Value, body.Mapping, ct);
            return Ok(new { success = true });
        }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Integração ainda não preparada: aplique as migrations PlantIntegration." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/results")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Results(string projectIdAcc, int limit = 100, int offset = 0, string? category = null, CancellationToken ct = default)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc deve usar o formato b.<guid>." });
        if (limit is < 1 or > 500 || offset < 0) return BadRequest(new { message = "limit deve estar entre 1 e 500 e offset não pode ser negativo." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var dashboardProjectId = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (dashboardProjectId is null)
                return Ok(PlantIntegrationService.EmptyResults(projectIdAcc, null, "unlinked", limit, offset));
            return Ok(await service.GetResultsAsync(projectIdAcc, dashboardProjectId.Value, limit, offset, category, ct));
        }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Schema de versões do Dashboard BIM não está disponível." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    private async Task<IActionResult?> ProjectAccessError(string projectIdAcc, CancellationToken ct) => await projectAccess.CheckProjectAccessAsync(projectIdAcc, ct) switch
    {
        PermissionCheck.Allowed => null,
        PermissionCheck.Unauthenticated => Unauthorized(new { message = "Sessão expirada." }),
        PermissionCheck.Denied => StatusCode(403, new { message = "Projeto não autorizado." }),
        _ => StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." }),
    };

    private static bool IsAccProjectId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
