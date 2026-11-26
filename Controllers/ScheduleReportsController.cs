using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ScheduleReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IScheduleReportRepository repository,
    ILogger<ScheduleReportsController> logger) : ControllerBase
{
    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? disciplina = null, [FromQuery] string? fase = null, [FromQuery] string? status = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 200 ||
            disciplina is { Length: > 200 } || fase is { Length: > 50 } || status is { Length: > 100 } || search is { Length: > 100 })
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.cronograma.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, Empty(disciplina), Empty(fase), Empty(status), Empty(search), page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta de Cronograma. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
