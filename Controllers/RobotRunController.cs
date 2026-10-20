using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.RobotRun;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// Contrato N&amp;A-first: o frontend envia só Project_ID_ACC (projectId/projectIds) ou allProjects=true. HubId é aceito por compatibilidade com
/// clientes antigos e IGNORADO — o hub de cada projeto é resolvido no backend pelo cadastro (IRobotProjectDirectory: dbo.Projetos + Hubs_Conta).
/// </summary>
public sealed record RobotRunRequest(string? HubId, string? ProjectId, bool AllProjects = false, IReadOnlyList<string>? ProjectIds = null);

/// <summary>
/// Disparo MANUAL do robô (botão "Iniciar Robô"). Não cria Worker nem processo: solicita ao <see cref="ManualRobotRunner"/> — que executa o mesmo
/// GrdAutomationWorker.ProcessarCicloCompleto — sob o portão único de execução (409 se já houver ciclo em andamento, automático ou manual).
/// Segurança por projeto: gate positivo N&amp;A (UserProjects ativo) + robot.run no projeto. O frontend nunca é a segurança.
/// </summary>
[ApiController]
[Route("api/worker")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RobotRunController(IProjectAccessService access, IPermissionService permissions, ManualRobotRunner runner,
    IRobotProjectDirectory directory, ILogger<RobotRunController> logger) : ControllerBase
{
    private static readonly string[] RunKeys = ["robot.run"];

    /// <summary>Estado real (em memória) da execução: monitor global, botão e tela de configuração consultam o MESMO endpoint.</summary>
    [RequirePermission("robot.view")]
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        if (await permissions.GetCurrentUserAsync(cancellationToken) is null) return Unauthorized(new { message = "Sessão expirada." });
        return Ok(runner.Status);
    }

    [RequirePermission("robot.run", AnyProject = true)]
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RobotRunRequest request, CancellationToken cancellationToken)
    {
        if (await permissions.GetCurrentUserAsync(cancellationToken) is null) return Unauthorized(new { message = "Sessão expirada." });
        if (request is null) return BadRequest(new { message = "Requisição inválida." });
        IReadOnlyList<string> ids;
        if (request.AllProjects)
        {
            if (request.ProjectId != null || request.ProjectIds != null)
                return BadRequest(new { message = "Não combine allProjects com projectId/projectIds." });
            ids = Array.Empty<string>();
        }
        else
        {
            if (request.ProjectId != null && request.ProjectIds != null)
                return BadRequest(new { message = "Informe projectId ou projectIds, não ambos." });
            ids = request.ProjectIds ?? (request.ProjectId == null ? Array.Empty<string>() : new[] { request.ProjectId });
            if (ids.Count == 0 || ids.Any(id => !IsAccId(id)))
                return BadRequest(new { message = "Informe projectId/projectIds (Project_ID_ACC) válidos ou allProjects=true." });
        }

        var registered = await directory.ReadAsync(cancellationToken);
        var selected = new List<ManualRobotProject>();
        if (request.AllProjects)
        {
            // "Executar todos permitidos": só os ativos do cadastro em que o usuário tem UserProject ativo + robot.run (os demais são ignorados).
            foreach (var project in registered.Where(p => p.Active))
            {
                var decision = await Authorize(project, cancellationToken);
                if (decision == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
                if (decision == PermissionCheck.Unavailable) return StatusCode(503, new { message = "Não foi possível validar o acesso aos projetos." });
                if (decision == PermissionCheck.Allowed && HasHub(project)) selected.Add(Manual(project));
            }
            if (selected.Count == 0) return BadRequest(new { message = "Nenhum projeto ativo permitido para execução." });
        }
        else
        {
            foreach (var projectId in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var project = registered.FirstOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
                if (project == null) return NotFound(new { message = "Projeto não registrado no N&A Connect (tabela Projetos)." });
                var decision = await Authorize(project, cancellationToken);
                if (decision == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
                if (decision == PermissionCheck.Denied) return StatusCode(403, new { message = $"Projeto não autorizado: {project.Name}." });
                if (decision != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
                if (!project.Active) return BadRequest(new { message = "Projeto inativo: o robô só processa projetos ativos." });
                if (!HasHub(project)) return BadRequest(new { message = $"Projeto sem hub (Account_ID_ACC) válido no cadastro: {project.Name}." });
                selected.Add(Manual(project));
            }
        }

        var result = runner.TryStartProjects(selected);
        if (!result.Started) return Conflict(new { message = "Robô já está em execução.", status = result.Status });
        logger.LogWarning("Execução manual do robô iniciada. Projetos={Count} AllProjects={All}", selected.Count, request.AllProjects);
        return Accepted(result.Status);
    }

    /// <summary>Gate positivo do projeto (UserProjects ativo) + robot.run no projeto.</summary>
    private async Task<PermissionCheck> Authorize(RegisteredRobotProject project, CancellationToken ct)
    {
        var gate = await access.CheckProjectAccessAsync(project.ProjectId, ct);
        if (gate != PermissionCheck.Allowed) return gate;
        return await permissions.CheckAsync(RunKeys, project.ProjectId, ct);
    }

    private static ManualRobotProject Manual(RegisteredRobotProject p) => new(p.ProjectId, p.Name) { TipoLdp = p.TipoLdp };
    /// <summary>Hub resolvido no cadastro (Account_ID_ACC, com ou sem "b."): o Worker usa o mesmo valor ao processar o projeto.</summary>
    private static bool HasHub(RegisteredRobotProject p) => !string.IsNullOrWhiteSpace(p.HubId) && Guid.TryParse(p.HubId.Replace("b.", ""), out _);
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
