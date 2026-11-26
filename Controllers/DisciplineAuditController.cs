using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Planning;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record DisciplineStandardSave(
    string StandardName,
    string? PortugueseName,
    string? EnglishName,
    string[]? Aliases);

public sealed record DisciplineAliasSave(string Alias, int DisciplineStandardId);

[ApiController]
[Route("api/discipline-audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DisciplineAuditController(
    DisciplineAuditService service,
    DisciplineStandardRepository standards,
    IProjectAccessService projectAccess,
    IPermissionService permissionService,
    INaCurrentUserAccessor currentUser,
    IPermissionRepository permissionRepository) : ControllerBase
{
    [HttpGet]
    [RequirePermission("quality.disciplines.view", "planning.coordination.view", AnyProject = true)]
    public async Task<IActionResult> Get(
        [FromQuery] string? projectId,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
        {
            var identity = await currentUser.GetAsync(ct);
            if (identity is null) return Unauthorized(new { message = "Sessão expirada." });

            var user = await permissionRepository.ObterUsuarioAsync(identity.UserId, ct);
            if (user is null || !user.Ativo) return Unauthorized(new { message = "Sessão expirada." });

            // Padronização é uma função global. Quem possui planning.coordination.edit pode auditar
            // TODOS os projetos ativos; demais usuários continuam limitados aos próprios UserProjects.
            var global = await permissionService.CheckAsync(
                new[] { "planning.coordination.edit" }, null, ct, anyProject: true);

            if (global == PermissionCheck.Allowed)
            {
                var allProjects = await service.ListAllActiveProjectsAsync(ct);
                return Ok(await service.ReadAsync(allProjects, "ALL_ACTIVE_PROJECTS", ct));
            }

            var allowed = await permissionRepository.ListarProjetosPermitidosAsync(user.UserId, ct);
            var projects = allowed
                .Where(x => x.IsAtivo && IsAccProjectId(x.ProjectIdAcc))
                .Select(x => new DisciplineAuditProject(x.ProjectIdAcc, x.NomeProjeto))
                .ToArray();

            return Ok(await service.ReadAsync(projects, "ALL_ALLOWED_PROJECTS", ct));
        }

        if (!IsAccProjectId(projectId))
            return BadRequest(new { message = "Project_ID_ACC inválido." });

        var access = await projectAccess.CheckProjectAccessAsync(projectId!, ct);
        if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });

        var identity2 = await currentUser.GetAsync(ct);
        if (identity2 is null) return Unauthorized(new { message = "Sessão expirada." });
        var allowed2 = await permissionRepository.ListarProjetosPermitidosAsync(identity2.UserId, ct);
        var projectName = allowed2.FirstOrDefault(x =>
            string.Equals(x.ProjectIdAcc, projectId, StringComparison.OrdinalIgnoreCase))?.NomeProjeto ?? projectId!;

        return Ok(await service.ReadAsync(projectId!, projectName, ct));
    }

    [HttpGet("standards")]
    [RequirePermission("quality.disciplines.view", "planning.coordination.view", AnyProject = true)]
    public async Task<IActionResult> Standards(CancellationToken ct) =>
        Ok(await standards.ListAsync(ct));

    [HttpPost("standards")]
    [RequirePermission("quality.disciplines.edit", "planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> CreateStandard([FromBody] DisciplineStandardSave body, CancellationToken ct)
    {
        try
        {
            var id = await standards.CreateAsync(
                body.StandardName, body.PortugueseName, body.EnglishName, body.Aliases, ct);
            return Ok(new { disciplineStandardId = id });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("standards/{id:int}")]
    [RequirePermission("quality.disciplines.edit", "planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> UpdateStandard(
        int id,
        [FromBody] DisciplineStandardSave body,
        CancellationToken ct)
    {
        try
        {
            await standards.RenameAndCascadeAsync(
                id, body.StandardName, body.PortugueseName, body.EnglishName, ct);
            return Ok(new { ok = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("aliases")]
    [RequirePermission("quality.disciplines.edit", "planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> AssignAlias([FromBody] DisciplineAliasSave body, CancellationToken ct)
    {
        try
        {
            await standards.AssignAliasAsync(body.Alias, body.DisciplineStandardId, ct);
            return Ok(new { ok = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    private static bool IsAccProjectId(string? value) =>
        value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
