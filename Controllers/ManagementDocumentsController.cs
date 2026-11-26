using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Planning;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// Documentos de Gestão (Painel de Planejamento → Painel do Planejador). Leitura da classificação também serve às telas
/// Controle de HH / Custo (Engenharia/Gestão); a gravação em lote exige planning.planner.management-documents.edit NO projeto.
/// Persistência: Migrations/Planning/20261029_ManagementDocuments.sql (manual). Sem a migração, GET devolve schemaAvailable=false.
/// </summary>
[ApiController]
[Route("api/planning/management-documents")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagementDocumentsController(ManagementDocumentsStore store, IProjectAccessService projects, IPermissionService permissions,
    ILogger<ManagementDocumentsController> logger) : ControllerBase
{
    public const string EditKey = "planning.planner.management-documents.edit";
    private static readonly string[] Read = ["planning.planner.view", "production.managerial.hh-custo.view"];
    private static readonly string[] Planner = ["planning.planner.view"];
    private static readonly string[] Edit = [EditKey];

    public sealed record BatchRequest(string? ProjectId, List<string?>? DocumentKeys, string? Target);

    private async Task<IActionResult?> Gate(string[] keys, string? projectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId) || projectId.Length > 100) return BadRequest(new { message = "Projeto obrigatório." });
        var access = await projects.CheckProjectAccessAsync(projectId, ct);
        if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
        return await permissions.CheckAsync(keys, projectId, ct) switch
        {
            PermissionCheck.Allowed => null,
            PermissionCheck.Unauthenticated => Unauthorized(new { message = "Sessão expirada." }),
            PermissionCheck.Denied => StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade." }),
            _ => StatusCode(503, new { message = "Não foi possível validar permissões." })
        };
    }

    private async Task<bool> CanEdit(string projectId, CancellationToken ct) =>
        await permissions.CheckAsync(Edit, projectId, ct) == PermissionCheck.Allowed;

    private IActionResult Failure(Exception ex)
    {
        if (ex is ArgumentException) return BadRequest(new { message = ex.Message });
        if (ex is ManagementDocumentsSchemaMissingException) return Conflict(new { code = "schemaMissing", message = ex.Message });
        logger.LogError(ex, "Falha na API de Documentos de Gestão. Ref={Ref}", HttpContext.TraceIdentifier);
        return StatusCode(503, new { message = $"Documentos de Gestão indisponíveis. (ref: {HttpContext.TraceIdentifier})" });
    }

    /// <summary>Classificação vigente do projeto (chaves marcadas como Gestão).</summary>
    [HttpGet]
    [RequirePermission("planning.planner.view", "production.managerial.hh-custo.view")]
    public async Task<IActionResult> Classification([FromQuery] string projectId, CancellationToken ct)
    {
        var denied = await Gate(Read, projectId, ct); if (denied is not null) return denied;
        try
        {
            var keys = await store.ReadKeysAsync(projectId, ct);
            return Ok(new { projectId, schemaAvailable = keys is not null, documentKeys = keys ?? [] });
        }
        catch (Exception ex) { return Failure(ex); }
    }

    /// <summary>Documentos reais da LDP atual do projeto (código, título, disciplina, status) com a marcação atual.</summary>
    [HttpGet("candidates")]
    [RequirePermission("planning.planner.view")]
    public async Task<IActionResult> Candidates([FromQuery] string projectId, CancellationToken ct)
    {
        var denied = await Gate(Planner, projectId, ct); if (denied is not null) return denied;
        try
        {
            var (available, c) = await store.CandidatesAsync(projectId, ct);
            return Ok(new { projectId, schemaAvailable = available, canEdit = available && await CanEdit(projectId, ct),
                documentos = c.Documentos, linhasSemCodigo = c.LinhasSemCodigo, classificadosForaDaLdp = c.ClassificadosForaDaLdp });
        }
        catch (Exception ex) { return Failure(ex); }
    }

    /// <summary>Envia (GESTAO) ou retorna (ENGENHARIA) documentos em lote. projectId vai na query (validado pelo atributo) e, se repetido no corpo, precisa coincidir.</summary>
    [HttpPost("batch")]
    [RequirePermission(EditKey)]
    public async Task<IActionResult> Batch([FromQuery] string projectId, [FromBody] BatchRequest body, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body.ProjectId) && !string.Equals(body.ProjectId.Trim(), projectId?.Trim(), StringComparison.Ordinal))
            return BadRequest(new { message = "Projeto divergente na requisição." });
        var target = ManagementDocumentRules.NormalizeTarget(body.Target);
        if (target is null) return BadRequest(new { message = "Destino inválido (GESTAO ou ENGENHARIA)." });
        var denied = await Gate(Edit, projectId, ct); if (denied is not null) return denied;
        try
        {
            var user = await permissions.GetCurrentUserAsync(ct);
            var by = string.IsNullOrWhiteSpace(user?.Email) ? user?.Nome : user.Email;
            return Ok(await store.ApplyAsync(projectId!, body.DocumentKeys ?? [], target, by, ct));
        }
        catch (Exception ex) { return Failure(ex); }
    }
}
