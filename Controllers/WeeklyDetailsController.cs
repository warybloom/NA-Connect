using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports/weekly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WeeklyDetailsController(IProjectAccessService projectAccess, IPermissionService permissions, IWeeklyDetailsReportRepository repository,
    ILogger<WeeklyDetailsController> logger) : ControllerBase
{
    [HttpGet("details")]
    public async Task<IActionResult> Details([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? disciplina = null, [FromQuery] string? status = null, [FromQuery] string? responsavel = null,
        [FromQuery] string? fase = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 ||
            disciplina is { Length: > 200 } || status is { Length: > 200 } || responsavel is { Length: > 200 } ||
            fase is { Length: > 200 } || search is { Length: > 100 })
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.weekly.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, Empty(disciplina), Empty(status), Empty(responsavel), Empty(fase), Empty(search), page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta de detalhe Weekly. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
