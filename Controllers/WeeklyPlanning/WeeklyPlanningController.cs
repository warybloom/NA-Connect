using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.WeeklyPlanning;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers.WeeklyPlanning;

[ApiController]
[Route("api/weekly-planning")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WeeklyPlanningController(IConfiguration configuration, IProjectReportAccess access,
    ILogger<WeeklyPlanningController> logger) : PlanningReadControllerBase(access)
{
    // No shared startup edits required: IConfiguration and project access already exist in DI.
    [RequirePermission("planning.weekly.view", "planning.weekly.current.view")]
    [HttpGet("documents")]
    public Task<IActionResult> Documents([FromQuery] string hubId, [FromQuery] string projectId,
        [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromQuery] string? discipline = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default)
        => Read(false, hubId, projectId, start, end, discipline, page, pageSize, cancellationToken);

    [RequirePermission("planning.weekly.view", "planning.weekly.current.view")]
    [HttpGet("legacy")]
    public Task<IActionResult> Legacy([FromQuery] string hubId, [FromQuery] string projectId,
        [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromQuery] string? discipline = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default)
        => Read(true, hubId, projectId, start, end, discipline, page, pageSize, cancellationToken);

    private async Task<IActionResult> Read(bool legacy, string hubId, string projectId, DateOnly start,
        DateOnly end, string? discipline, int page, int pageSize, CancellationToken ct)
    {
        try
        {
            var denied = await Guard(hubId, projectId, start, end, discipline, page, pageSize, ct);
            if (denied != null) return denied;
            var reader = new PlanningSourceReader(configuration);
            if (legacy) return Ok(await reader.Legacy(projectId, start, end, discipline, page, pageSize, ct));
            return Ok(await reader.Documents(projectId, start, end, discipline, page, pageSize, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Consulta semanal indisponível. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Fonte de planejamento temporariamente indisponível." });
        }
    }
}
