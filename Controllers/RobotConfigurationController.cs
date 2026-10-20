using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.RobotRun;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record SaveRobotConfiguration(long ExpectedRevision, RobotDefinition Robot);
public sealed record RunConfiguredRobot(string RobotId, long ExpectedRevision);

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RobotConfigurationController(RobotConfigurationStore store, IRobotProjectDirectory directory,
    IProjectReportAccess access, IProjectAccessService projectAccess, IPermissionService permissions, RobotQueueRunner runner, ILogger<RobotConfigurationController> logger) : ControllerBase
{
    [RequirePermission("robot.view")]
    [HttpGet("api/robot-config")]
    public Task<IActionResult> Get(CancellationToken ct) => Guard(async token =>
    {
        var configuration = await store.ReadAsync(ct);
        var projects = await directory.ReadAsync(ct);
        return Ok(new { configuration, projects, stages = RobotStageCatalog.Stages,
            storage = "App_Data/robot-configuration.json", cancellationAvailable = false });
    });

    [RequirePermission("robot.configure")]
    [HttpPut("api/robot-config/{robotId}")]
    public Task<IActionResult> Save(string robotId, [FromBody] SaveRobotConfiguration request, CancellationToken ct) => Guard(async token =>
    {
        if (request?.Robot == null) return BadRequest(new { message = "Configuração obrigatória." });
        var proposed = RobotConfigurationRules.Validate(new(1, 0, null, [request.Robot])).Robots[0];
        var projects = await directory.ReadAsync(ct);
        var current = await store.ReadAsync(ct);
        // Includes removed/disabled projects: cannot remove another project's configuration without access.
        var ids = current.Robots.SelectMany(r => r.Projects).Select(p => p.ProjectId).Concat(proposed.Projects.Select(p => p.ProjectId)).Distinct();
        var denied = await Authorize(ids, projects, token, "robot.configure", ct);
        if (denied != null) return denied;
        foreach (var item in proposed.Projects)
        {
            var project = projects.Single(p => p.ProjectId == item.ProjectId);
            if (item.Enabled && !project.Active) return BadRequest(new { message = $"Projeto inativo: {project.Name}." });
            if (item.Stages.Length > 0) RobotStageCatalog.Validate(item.Stages, project.TipoLdp);
        }
        return Ok(await store.SaveAsync(robotId, proposed, request.ExpectedRevision, ct));
    });

    [RequirePermission("robot.run")]
    [HttpPost("api/worker/run-configured")]
    public Task<IActionResult> Run([FromBody] RunConfiguredRobot request, CancellationToken ct) => Guard(async token =>
    {
        var config = await store.ReadAsync(ct);
        if (request == null || request.ExpectedRevision != config.Revision) throw new RobotConfigurationConflictException();
        var robot = config.Robots.SingleOrDefault(r => r.Id == request.RobotId);
        if (robot == null) return NotFound(new { message = "Robô não encontrado." });
        var projects = await directory.ReadAsync(ct);
        var queue = RobotConfigurationRules.BuildQueue(robot, projects);
        var denied = await Authorize(queue.Select(p => p.ProjectId), projects, token, "robot.run", ct);
        if (denied != null) return denied;
        var result = runner.TryStart(robot.Id, queue);
        return result.Started ? Accepted(result.Status) : Conflict(new { message = "Robô já está em execução.", status = result.Status });
    });

    private async Task<IActionResult?> Authorize(IEnumerable<string> ids, IReadOnlyList<RegisteredRobotProject> projects, string token, string permissionKey, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            var project = projects.SingleOrDefault(p => p.ProjectId == id);
            if (project == null) return BadRequest(new { message = "Projeto não registrado no cadastro atual." });
            string Acc(string value) => value.StartsWith("b.") ? value : "b." + value;
            if (!Guid.TryParse(project.HubId.Replace("b.", ""), out _)) return BadRequest(new { message = "Projeto sem hub válido no cadastro." });
            // Gate positivo N&A: projeto ativo para o usuário, seja login local ou Autodesk.
            var projetoNac = await projectAccess.CheckProjectAccessAsync(Acc(project.ProjectId), ct);
            if (projetoNac == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (projetoNac == PermissionCheck.Denied) return StatusCode(403, new { message = $"Sem acesso ao projeto {project.Name}." });
            if (projetoNac != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso aos projetos." });

            // Usuário Autodesk mantém TAMBÉM o gate ACC original.
            // Login N&A local não possui AccessToken Autodesk: o gate N&A é obrigatório.
            if (!string.IsNullOrWhiteSpace(token))
            {
                var decision = await access.CheckAsync(Acc(project.HubId), Acc(project.ProjectId), token, ct);
                if (decision == ProjectReportAccess.Unauthenticated) return Unauthorized(new { message = "Sessão Autodesk expirada. Entre novamente." });
                if (decision == ProjectReportAccess.Denied) return StatusCode(403, new { message = $"Sem acesso ao projeto {project.Name}." });
                if (decision != ProjectReportAccess.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso aos projetos." });
            }
            // A permissão da etapa é validada abaixo mesmo sem token Autodesk.
            var internalDecision = await permissions.CheckAsync([permissionKey], Acc(project.ProjectId), ct);
            if (internalDecision == PermissionCheck.Unauthenticated) return Unauthorized();
            if (internalDecision == PermissionCheck.Denied) return NotFound();
            if (internalDecision != PermissionCheck.Allowed) return StatusCode(503);
        }
        return null;
    }
    private async Task<IActionResult> Guard(Func<string, Task<IActionResult>> action)
    {
        try
        {
            // Auth:UserId é a identidade da sessão N&A; nunca confiar somente em token ausente.
            if (await permissions.GetCurrentUserAsync(HttpContext.RequestAborted) is null)
                return Unauthorized(new { message = "Sessão expirada." });
            var tokenAutodesk = HttpContext.Session.GetString("AccessToken") ?? string.Empty;
            return await action(tokenAutodesk);
        }
        catch (RobotConfigurationConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (RobotConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Configuração do robô indisponível. Tipo={Type}", ex.GetType().Name);
            return StatusCode(503, new { message = "Falha ao consultar ou salvar configuração do robô. Nenhuma execução iniciada por esta falha." });
        }
    }
}
