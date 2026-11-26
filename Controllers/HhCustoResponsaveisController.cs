using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// Somente leitura: HH realizado por responsável × documento × dia para o painel RESPONSÁVEL do Controle de HH / Custo.
/// Mesma permissão gerencial da tela (production.managerial.hh-custo.view) e mesmo gate de projeto. Não expõe custo por pessoa.
/// </summary>
[ApiController]
[Route("api/production/hh-custo")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HhCustoResponsaveisController(IProjectAccessService projects, IPermissionService permissions,
    IHhCustoResponsaveisReader reader, ILogger<HhCustoResponsaveisController> logger) : ControllerBase
{
    private static readonly string[] Keys = ["production.managerial.hh-custo.view"];

    [HttpGet("responsaveis")]
    [RequirePermission("production.managerial.hh-custo.view")]
    public async Task<IActionResult> Responsaveis([FromQuery] string projectId, CancellationToken ct)
    {
        if (projectId is not { Length: 38 } || !projectId.StartsWith("b.", StringComparison.Ordinal) || !Guid.TryParseExact(projectId[2..], "D", out _))
            return BadRequest(new { message = "Projeto inválido." });
        var access = await projects.CheckProjectAccessAsync(projectId, ct);
        if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar acesso." });
        var permission = await permissions.CheckAsync(Keys, projectId, ct);
        if (permission == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (permission == PermissionCheck.Denied) return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade." });
        if (permission != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar permissões." });
        try { return Ok(await reader.ReadHhCustoResponsaveisAsync(projectId, ct)); }
        catch (ProductionAuditScopeException ex) when (ex.Scope.Status is ProductionAuditScopeStatus.CrNotConfigured or ProductionAuditScopeStatus.CrAmbiguous)
        {
            return Conflict(new { code = ex.Scope.Status == ProductionAuditScopeStatus.CrNotConfigured ? "crOsNotConfigured" : "crOsAmbiguous", message = ex.Scope.Message, crOs = ex.Scope.CrOs });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha ao ler responsáveis do Controle de HH / Custo. Tipo={Type}", ex.GetType().Name);
            return StatusCode(503, new { message = "Responsáveis temporariamente indisponíveis." });
        }
    }
}
