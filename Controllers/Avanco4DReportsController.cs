using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class Avanco4DReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IAvanco4DReportRepository repository,
    ILogger<Avanco4DReportsController> logger) : ControllerBase
{
    [HttpGet("avanco-4d")]
    public async Task<IActionResult> Avanco4D([FromQuery] string? hubId, [FromQuery] string projectId, CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.avanco4d.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta de Avanço 4D. Ref={Ref} Detalhe={Detalhe}", HttpContext.TraceIdentifier, ReportErrors.Describe(ex));
            return StatusCode(503, new { message = $"Relatório temporariamente indisponível. (ref: {HttpContext.TraceIdentifier})" });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
