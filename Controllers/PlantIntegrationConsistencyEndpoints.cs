using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.PlantIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

public sealed partial class PlantIntegrationController
{
    [HttpGet("projects/{projectIdAcc}/consistency")]
    [RequirePermission("plant3d.view")]
    public async Task<IActionResult> Consistency(string projectIdAcc,
        string? phaseId = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        if (!IsAccProjectId(projectIdAcc))
            return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (limit is < 1 or > 500 || offset < 0)
            return BadRequest(new { message = "limit deve estar entre 1 e 500; offset não pode ser negativo." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        try
        {
            var dashboard = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (dashboard is null)
                return Ok(new { status = "unlinked", message = "Projeto não associado ao Dashboard BIM.", totalLines = 0 });
            return Ok(await service.GetConsistencyAsync(projectIdAcc, dashboard.Value, phaseId, limit, offset, ct));
        }
        catch (PlantIntegrationValidationException e) { return BadRequest(new { message = e.Message }); }
        catch (PlantIntegrationHistoryException e) { return Conflict(new { message = e.Message }); }
        catch (PlantIntegrationConfigurationException e) { return StatusCode(503, new { message = e.Message }); }
        catch (SqlException e) when (e.Number == 208)
        { return StatusCode(503, new { message = "Migração ou histórico do Plant 3D indisponível." }); }
    }
}
