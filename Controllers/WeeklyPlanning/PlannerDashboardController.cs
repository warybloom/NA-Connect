using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.WeeklyPlanning;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers.WeeklyPlanning;

[ApiController]
[Route("api/planner-dashboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlannerDashboardController(IProjectReportAccess access,
    ILogger<PlannerDashboardController> logger) : PlanningReadControllerBase(access)
{
    [RequirePermission("planning.planner.view")]
    [HttpGet("preview")]
    public Task<IActionResult> Preview([FromQuery] string hubId, [FromQuery] string projectId,
        [FromQuery] DateOnly start, [FromQuery] DateOnly end, CancellationToken cancellationToken = default)
        => Read(false, hubId, projectId, start, end, cancellationToken);

    [RequirePermission("planning.planner.view")]
    [HttpGet("preview.xlsx")]
    public Task<IActionResult> PreviewExcel([FromQuery] string hubId, [FromQuery] string projectId,
        [FromQuery] DateOnly start, [FromQuery] DateOnly end, CancellationToken cancellationToken = default)
        => Read(true, hubId, projectId, start, end, cancellationToken);

    private async Task<IActionResult> Read(bool excel, string hubId, string projectId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        try
        {
            var denied = await Guard(hubId, projectId, start, end, null, 1, 100, ct);
            if (denied != null) return denied;
            var data = WeeklyPlanningExportService.Preview(projectId, start, end);
            if (!excel) return Ok(data);
            return File(new WeeklyPlanningExportService().Export(data),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PREVIEW-programacao-{start:yyyy-MM-dd}.xlsx");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Preview semanal indisponível. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Preview temporariamente indisponível." });
        }
    }
}
