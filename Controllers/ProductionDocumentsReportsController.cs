using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductionDocumentsReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IProductionDocumentsReportRepository repository,
    ILogger<ProductionDocumentsReportsController> logger) : ControllerBase
{
    [HttpGet("documents")]
    public async Task<IActionResult> Documents([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? documentStatus = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 ||
            (documentStatus is { Length: > 100 }))
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.producao.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, string.IsNullOrEmpty(documentStatus) ? null : documentStatus, page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Nunca registrar corpo HTTP, token, diagnóstico SQL ou connection string.
            logger.LogWarning("Falha na consulta de Produção/Documentos. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
