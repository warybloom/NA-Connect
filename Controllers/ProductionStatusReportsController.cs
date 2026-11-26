using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductionStatusReportsController(IProjectAccessService projectAccess, IPermissionService permissions,
    IProductionStatusReportRepository repository,
    ILogger<ProductionStatusReportsController> logger) : ControllerBase
{
    [HttpGet("production-status")]
    public async Task<IActionResult> ProductionStatus([FromQuery] string? hubId, [FromQuery] string projectId,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var acesso = await projectAccess.CheckProjectAccessAsync(projectId, cancellationToken);
            if (acesso == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (acesso == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
            if (acesso != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
            var permissao = await permissions.CheckAsync(["reports.producao.view"], projectId, cancellationToken);
            if (permissao == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (permissao == PermissionCheck.Denied) return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = "reports.producao.view" });
            if (permissao != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar permissões." });
            return Ok(await repository.ReadAsync(projectId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Nunca registrar corpo HTTP, token, diagnóstico SQL ou connection string.
            logger.LogWarning("Falha na consulta de Mapa de Calor (Status). Ref={Ref} Detalhe={Detalhe}", HttpContext.TraceIdentifier, ReportErrors.Describe(ex));
            return StatusCode(503, new { message = $"Relatório temporariamente indisponível. (ref: {HttpContext.TraceIdentifier})" });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
