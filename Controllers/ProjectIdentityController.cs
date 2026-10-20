using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.ProjectIdentity;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// Logo do Projeto (Identidade Visual) — fonte ÚNICA consumida por Home, sidebar, TV e demais telas.
/// Leitura: qualquer usuário com sessão (logo não é dado de negócio). Alteração: settings.projects.edit (Configuração do Projeto).
/// </summary>
[ApiController]
[Route("api/project-identity")]
public sealed class ProjectIdentityController(ProjectIdentityStore store, IPermissionService permissions, ILogger<ProjectIdentityController> logger) : ControllerBase
{
    // Sessão = identidade N&A (Auth:UserId, login N&A ou Autodesk) OU token Autodesk legado. A logo não depende da Autodesk:
    // exigir só AccessToken deixava o usuário N&A sem logos (índice e imagem respondiam 401).
    private bool SemSessao
    {
        get
        {
            var session = HttpContext.Session;
            if (session is null) return true;
            try { if (session.GetInt32(APS.WebApp.Services.Identity.NaSessionKeys.UserId) is > 0) return false; }
            catch (ArgumentException) { /* bytes inválidos: tratar como ausente */ }
            return string.IsNullOrWhiteSpace(session.GetString("AccessToken"));
        }
    }

    [HttpGet]
    [PermissionExempt("Lista quais projetos têm logo (identidade visual); exige sessão; não expõe dado de negócio.")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (SemSessao) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            var itens = await store.ListAsync(ct);
            return Ok(itens.Select(i => new { projectId = i.ProjectId, version = i.Version, updatedAt = i.UpdatedAt }));
        }
        catch (ProjectIdentityException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpGet("{projectId}/logo")]
    [PermissionExempt("Imagem da logo do projeto (identidade visual); exige sessão.")]
    public async Task<IActionResult> Logo(string projectId, CancellationToken ct)
    {
        if (SemSessao) return Unauthorized();
        try
        {
            var logo = await store.ReadAsync(projectId, ct);
            if (logo is null) return NotFound();
            // URL versionada (?v=) pelo frontend: pode ficar em cache; trocar a logo muda a versão.
            Response.Headers.CacheControl = "private, max-age=86400";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(logo.Value.Bytes, logo.Value.Info.ContentType);
        }
        catch (ProjectIdentityException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpPut("{projectId}/logo")]
    [RequirePermission("settings.projects.edit")]
    [RequestSizeLimit(ProjectIdentityStore.TamanhoMaximo + 64 * 1024)]
    public async Task<IActionResult> Upload(string projectId, IFormFile? arquivo, CancellationToken ct)
    {
        if (!ProjectIdentityStore.EhAccId(projectId)) return BadRequest(new { message = "Projeto inválido." });
        if (arquivo is null || arquivo.Length == 0) return BadRequest(new { message = "Selecione uma imagem." });
        if (arquivo.Length > ProjectIdentityStore.TamanhoMaximo) return BadRequest(new { message = "A logo deve ter no máximo 1 MB." });
        try
        {
            using var ms = new MemoryStream();
            await arquivo.CopyToAsync(ms, ct);
            var info = await store.SaveAsync(projectId, ms.ToArray(), await Usuario(ct), ct);
            return Ok(new { projectId = info.ProjectId, version = info.Version, updatedAt = info.UpdatedAt });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (ProjectIdentityException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpDelete("{projectId}/logo")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> Remove(string projectId, CancellationToken ct)
    {
        try { return await store.DeleteAsync(projectId, ct) ? NoContent() : NotFound(); }
        catch (ProjectIdentityException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    private async Task<string?> Usuario(CancellationToken ct)
    {
        try { return (await permissions.GetCurrentUserAsync(ct))?.Email; }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug("Logo: usuário não identificado ({Tipo}).", ex.GetType().Name); return null; }
    }
}
