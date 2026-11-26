using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.PlantIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

// These endpoints reuse the login, project authorization, DI and mapping routes of the existing controller.
public sealed partial class PlantIntegrationController
{
    public sealed record FiltersRequest(Dictionary<string, List<PlantIntegrationService.FilterRule>>? Filters);
    public sealed record PhasesRequest(List<PlantIntegrationService.MeasurementPhase>? Phases);

    [HttpGet("projects/{projectIdAcc}/filters")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Filters(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try { return Ok(await service.GetFiltersAsync(projectIdAcc, ct)); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantCategoryFilters ainda não aplicada." }); }
        catch (PlantIntegrationValidationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("projects/{projectIdAcc}/filters")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> SaveFilters(string projectIdAcc, [FromBody] FiltersRequest? request, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (request?.Filters is null) return BadRequest(new { message = "filters é obrigatório." });
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return NotFound(new { message = "Projeto sem vínculo com Dashboard BIM." });
            return Ok(await service.SaveFiltersAsync(projectIdAcc,link.Value,request.Filters,ct));
        }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantCategoryFilters ainda não aplicada." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/inventory/{category}")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Inventory(string projectIdAcc, string category, string side = "pid",
        int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (side is not ("pid" or "piping") || limit is < 1 or > 500 || offset < 0)
            return BadRequest(new { message = "side deve ser pid ou piping; limite 1-500; offset não negativo." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return Ok(new {
                status = "unlinked", category, side, rawCount = 0, includedCount = 0, excludedCount = 0,
                items = Array.Empty<object>(), totalItems = 0, offset, limit, revisionToken = (string?)null
            });
            return Ok(await service.GetInventoryAsync(projectIdAcc,link.Value,category,side,offset,limit,ct));
        }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration ou histórico indisponível." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/phases")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Phases(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try { return Ok(await service.GetPhasesAsync(projectIdAcc,ct)); }
        catch (PlantIntegrationValidationException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantMeasurementPhases ainda não aplicada." }); }
    }

    [HttpPut("projects/{projectIdAcc}/phases")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> SavePhases(string projectIdAcc, [FromBody] PhasesRequest? request, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (request?.Phases is null) return BadRequest(new { message = "phases é obrigatório." });
        try { return Ok(await service.SavePhasesAsync(projectIdAcc,request.Phases,ct)); }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantMeasurementPhases ainda não aplicada." }); }
    }

    [HttpGet("projects/{projectIdAcc}/measurement")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Measurement(string projectIdAcc, string phaseId, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (string.IsNullOrWhiteSpace(phaseId)) return BadRequest(new { message = "phaseId obrigatório." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc,ct);
            if (link is null) return Ok(new { phaseId, phaseName = (string?)null, status = "unlinked", categories = Array.Empty<object>() });
            return Ok(await service.GetMeasurementAsync(projectIdAcc,link.Value,phaseId,ct));
        }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantMeasurementPhases ainda não aplicada." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }
}
