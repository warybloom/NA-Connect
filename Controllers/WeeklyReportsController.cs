using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WeeklyReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IWeeklyReportRepository repository,
    ILogger<WeeklyReportsController> logger) : ControllerBase
{
    [HttpGet("weekly")]
    public async Task<IActionResult> Weekly([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 ||
            (!string.IsNullOrEmpty(status) && status is not ("OK" or "COM_ALERTAS" or "SEM_EXCEL" or "PADRAO_INCORRETO" or "PADRAO_AMBIGUO")))
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.weekly.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, string.IsNullOrEmpty(status) ? null : status, page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Nunca registrar corpo HTTP, token, diagnóstico SQL ou connection string.
            logger.LogWarning("Falha na consulta Weekly. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
