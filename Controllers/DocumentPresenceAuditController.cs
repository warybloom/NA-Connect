using APS.WebApp.Services.AutodeskAuth;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/document-presence-audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DocumentPresenceAuditController(
    DocumentPresenceAuditService service,
    DocumentPresenceAuditStore store,
    IProjectAccessService projectAccess,
    IAutodeskUserTokenProvider autodeskUserTokens,
    ILogger<DocumentPresenceAuditController> logger) : ControllerBase
{
    [HttpGet("latest")]
    [RequirePermission("quality.ldp-acc.view")]
    public async Task<IActionResult> Latest([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccProjectId(projectId)) return BadRequest(new { message = "Project_ID_ACC inválido." });

        var access = await projectAccess.CheckProjectAccessAsync(projectId, ct);
        if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });

        var latest = await store.GetLatestAsync(projectId, ct);
        return latest is null ? NoContent() : Ok(latest);
    }

    [HttpPost("run")]
    [RequirePermission("quality.ldp-acc.run")]
    public async Task<IActionResult> Run([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccProjectId(projectId)) return BadRequest(new { message = "Project_ID_ACC inválido." });

        var access = await projectAccess.CheckProjectAccessAsync(projectId, ct);
        if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });

        var token = await autodeskUserTokens.GetValidAccessTokenAsync(ct);
        if (!token.IsValid)
            return Conflict(new { code = "autodeskReconnectRequired", message = "Reconecte a Autodesk para auditar os documentos no ACC." });

        try
        {
            return Ok(await service.RunAsync(projectId, token.AccessToken!, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("[AUDITORIA_LDP_ACC] Falha ACC. Projeto={ProjectId} Status={Status}", projectId, ex.StatusCode);
            return StatusCode(502, new { message = "Não foi possível ler as pastas do ACC. Verifique a conexão Autodesk e as permissões nas pastas." });
        }
    }

    private static bool IsAccProjectId(string? value) =>
        value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
