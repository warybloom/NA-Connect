using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.PlantIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

public sealed partial class PlantIntegrationController
{
    public sealed record StandardsRequest(IReadOnlyList<PlantIntegrationService.StandardCatalog>? Catalogs, string? TagLineColumn);
    public sealed record LineWeightsRequest(IReadOnlyList<PlantIntegrationService.LineWeight>? Weights);

    [HttpGet("projects/{projectIdAcc}/standards")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Standards(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try { return Ok(await service.GetStandardsAsync(projectIdAcc, ct)); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantIntegrationStandards ainda não aplicada." }); }
        catch (PlantIntegrationValidationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("projects/{projectIdAcc}/standards")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> SaveStandards(string projectIdAcc, [FromBody] StandardsRequest? request, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (request?.Catalogs is null) return BadRequest(new { message = "catalogs é obrigatório." });
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return NotFound(new { message = "Projeto sem vínculo com Dashboard BIM." });
            return Ok(await service.SaveStandardsAsync(projectIdAcc, link.Value, request.Catalogs, request.TagLineColumn, ct));
        }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantIntegrationStandards ainda não aplicada." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/standards/quality")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> StandardsQuality(string projectIdAcc, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var link = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (link is null) return Ok(new PlantIntegrationService.QualityResponse("unlinked", [], null));
            return Ok(await service.GetQualityAsync(projectIdAcc, link.Value, ct));
        }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (PlantIntegrationValidationException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration ou histórico PlantIntegration indisponível." }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectIdAcc}/line-weights")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> LineWeights(string projectIdAcc, string? phaseId, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try { return Ok(await service.GetLineWeightsAsync(projectIdAcc, phaseId, ct)); }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantLineWeights ainda não aplicada." }); }
    }

    [HttpPut("projects/{projectIdAcc}/line-weights")]
    [RequirePermission("plant3d.database.edit")]
    public async Task<IActionResult> SaveLineWeights(string projectIdAcc, string? phaseId,
        [FromBody] LineWeightsRequest? request, CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (request?.Weights is null) return BadRequest(new { message = "weights é obrigatório." });
        try { return Ok(await service.SaveLineWeightsAsync(projectIdAcc, phaseId, request.Weights, ct)); }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208) { return StatusCode(503, new { message = "Migration PlantLineWeights ou PlantMeasurementPhases ainda não aplicada." }); }
    }
}
