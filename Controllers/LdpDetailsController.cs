using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LdpDetailsController(IProjectAccessService projectAccess, IPermissionService permissions, ILdpDetailsReportRepository repository,
    ILogger<LdpDetailsController> logger) : ControllerBase
{
    [HttpGet("ldp")]
    public async Task<IActionResult> Ldp([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? discipline = null, [FromQuery] string? documentStatus = null, [FromQuery] string? gateFase = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 200 ||
            discipline is { Length: > 200 } || documentStatus is { Length: > 200 } || gateFase is { Length: > 50 } || search is { Length: > 100 })
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.ldp.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, Empty(discipline), Empty(documentStatus), Empty(gateFase), Empty(search), page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta de LDP. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
