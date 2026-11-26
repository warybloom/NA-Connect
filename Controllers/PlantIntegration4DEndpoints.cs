using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.PlantIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

// F7: 4D por projeto real do N&A. Nunca retorna dados simulados.
public sealed partial class PlantIntegrationController
{
    public sealed record Plant4DRequest(string? CategoryId, string? Grouping,
        IReadOnlyList<PlantIntegrationService.Plant4DLine>? Rows);

    [HttpPost("projects/{projectIdAcc}/4d/calculate")]
    [RequirePermission("plant3d.view")]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Calculate4D(string projectIdAcc, [FromBody] Plant4DRequest? body,
        CancellationToken ct)
    {
        if (!IsAccProjectId(projectIdAcc)) return BadRequest(new { message = "ProjectIdAcc inválido." });
        if (await ProjectAccessError(projectIdAcc, ct) is { } denied) return denied;
        if (body?.Rows is null || string.IsNullOrWhiteSpace(body.CategoryId))
            return BadRequest(new { message = "Envie categoria do DE/PARA e linhas reais do cronograma." });
        try
        {
            var dashboard = await service.GetDashboardProjectLinkAsync(projectIdAcc, ct);
            if (dashboard is null) return NotFound(new { message = "Projeto não vinculado ao Dashboard BIM." });
            return Ok(await service.Calculate4DAsync(projectIdAcc, dashboard.Value, body.CategoryId,
                body.Grouping ?? "mes", body.Rows, ct));
        }
        catch (PlantIntegrationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (PlantIntegrationHistoryException ex) { return Conflict(new { message = ex.Message }); }
        catch (PlantIntegrationConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 208)
        { return StatusCode(503, new { message = "Dados Plant 3D indisponíveis. Verifique as migrações." }); }
    }
}
