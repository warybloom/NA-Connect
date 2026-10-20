using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CurvaSReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IGestaoVistaReportRepository repository,
    ILogger<CurvaSReportsController> logger) : ControllerBase
{
    [HttpGet("curva-s")]
    public async Task<IActionResult> CurvaS([FromQuery] string? hubId, [FromQuery] string projectId, CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId,
                ["reports.curvas.view", "cde.curva-s.view", "production.managerial.curva-s.view", "production.operational.curva-s.view"], cancellationToken);
            if (authorization is not null) return authorization;
            var report = await repository.ReadAsync(projectId, cancellationToken);
            return Ok(PmoCurvaSRules.Build(projectId, report.DataReferencia, report.Documentos, report.Planning.PlannedHhTotal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta de Curva S. Ref={Ref} Detalhe={Detalhe}", HttpContext.TraceIdentifier, ReportErrors.Describe(ex));
            return StatusCode(503, new { message = $"Relatório temporariamente indisponível. (ref: {HttpContext.TraceIdentifier})" });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
