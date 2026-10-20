using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports/cap")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CapReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IInternalActionReportRepository internalRepository,
    IExternalActionReportRepository externalRepository, ILogger<CapReportsController> logger) : ControllerBase
{
    [HttpGet("internal")]
    public Task<IActionResult> Internal([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => ReadAsync("CAP Interno", projectId, status, search, page, pageSize, ["reports.cap-interno.view"],
            (p, s, se, pg, ps, ct) => internalRepository.ReadAsync(p, s, se, pg, ps, ct), cancellationToken);

    [HttpGet("external")]
    public Task<IActionResult> External([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => ReadAsync("CAP Externo", projectId, status, search, page, pageSize, ["reports.cap-externo.view"],
            (p, s, se, pg, ps, ct) => externalRepository.ReadAsync(p, s, se, pg, ps, ct), cancellationToken);

    private async Task<IActionResult> ReadAsync(string label, string projectId, string? status, string? search,
        int page, int pageSize, IReadOnlyCollection<string> permissionKeys, Func<string, string?, string?, int, int, CancellationToken, Task<ActionLogReport>> read,
        CancellationToken cancellationToken)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 ||
            status is { Length: > 200 } || search is { Length: > 100 })
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, permissionKeys, cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await read(projectId, Empty(status), Empty(search), page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta de {Label}. Tipo={ErrorType}", label, ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
