using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.AutodeskAuth;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.RobotRun;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record SaveProjectAuditTemplate(int? PackageId);

[ApiController]
[Route("api/document-quality")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DocumentQualityController(
    IOcrTemplateRepository templates,
    DocumentQualityRunQueue runs,
    IDocumentQualityReportStore reports,
    IProjectAccessService projectAccess,
    IPermissionService permissions,
    IAutodeskUserTokenProvider autodeskUserTokens,
    IRobotProjectDirectory projectDirectory,
    ILogger<DocumentQualityController> logger) : ControllerBase
{
    [HttpGet("configuration")]
    [RequirePermission("settings.projects.view", "quality.documents.view")]
    public async Task<IActionResult> Configuration([FromQuery] string projectId, [FromQuery] string? hubId, CancellationToken cancellationToken)
    {
        if (await EnsureProjectAccess(projectId, cancellationToken) is { } denied) return denied;
        try { return Ok(await templates.GetProjectConfigurationAsync(projectId, cancellationToken)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPut("configuration")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> SaveConfiguration([FromQuery] string projectId, [FromQuery] string? hubId,
        [FromBody] SaveDocumentQualityProjectConfiguration configuration, CancellationToken cancellationToken)
    {
        if (await EnsureProjectAccess(projectId, cancellationToken) is { } denied) return denied;
        try { return Ok(await templates.SaveProjectConfigurationAsync(projectId, configuration, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Coluna "Template Auditoria" da Configuração do Projeto: PacoteOcrId de cada projeto (config.ProjetoConfiguracao).</summary>
    [HttpGet("project-templates")]
    [RequirePermission("settings.projects.view", AnyProject = true)]
    public async Task<IActionResult> ProjectTemplates(CancellationToken cancellationToken) =>
        Ok(await templates.GetProjectTemplatesAsync(cancellationToken));

    /// <summary>Escolhe o template de auditoria do projeto (só o PacoteOcrId; a pasta de auditoria é preservada). null = sem template.</summary>
    [HttpPut("project-template")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> SaveProjectTemplate([FromQuery] string projectId, [FromBody] SaveProjectAuditTemplate body, CancellationToken cancellationToken)
    {
        if (await EnsureProjectAccess(projectId, cancellationToken) is { } denied) return denied;
        try { return Ok(await templates.SaveProjectTemplateAsync(projectId, body?.PackageId, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// AUDITAR AGORA: enfileira a auditoria manual e responde 202. A autorização do projeto é N&A-first;
    /// o hub ACC é resolvido no servidor e o token Autodesk só é exigido porque esta operação lê os PDFs no ACC.
    /// </summary>
    [HttpPost("runs")]
    [HttpPost("audits")]
    [RequirePermission("quality.documents.execute")]
    public async Task<IActionResult> Run([FromQuery] string projectId, [FromQuery] string? hubId, CancellationToken cancellationToken)
    {
        var accContext = await EnsureAccContext(projectId, cancellationToken);
        if (accContext.Error is not null) return accContext.Error;
        string user;
        try { user = (await permissions.GetCurrentUserAsync(cancellationToken))?.Email ?? "manual"; }
        catch (Exception ex) when (ex is not OperationCanceledException) { user = "manual"; }
        var start = runs.Start(new DocumentQualityRunRequest(accContext.HubId!, projectId, accContext.Token!, user));
        if (start.Started) logger.LogInformation("[DOC_QUALITY] Execução {ExecutionId} enfileirada para {ProjectId} por {User}.", start.Status.ExecutionId, projectId, user);
        return Accepted(Url.Action(nameof(RunStatus), new { executionId = start.Status.ExecutionId, projectId }),
            new { executionId = start.Status.ExecutionId, status = start.Status.Status, alreadyRunning = !start.Started });
    }

    /// <summary>Execução mais recente do projeto. É dado interno já persistido e não depende de sessão Autodesk.</summary>
    [HttpGet("runs/latest")]
    [RequirePermission("quality.documents.view")]
    public async Task<IActionResult> LatestRun([FromQuery] string projectId, [FromQuery] string? hubId, CancellationToken cancellationToken)
    {
        if (await EnsureProjectAccess(projectId, cancellationToken) is { } denied) return denied;
        var latest = await reports.GetLatestAsync(projectId, cancellationToken);
        return latest == null ? NoContent() : Ok(latest);
    }

    [HttpGet("runs/{executionId:guid}")]
    [RequirePermission("quality.documents.view")]
    public async Task<IActionResult> RunStatus(Guid executionId, [FromQuery] string projectId, [FromQuery] string? hubId, CancellationToken cancellationToken)
    {
        if (await EnsureProjectAccess(projectId, cancellationToken) is { } denied) return denied;
        var run = runs.Get(executionId);
        if (run == null || !string.Equals(run.ProjectIdAcc, projectId, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = "Execução não encontrada (o servidor pode ter sido reiniciado). Inicie a auditoria novamente." });
        return Ok(new
        {
            executionId = run.ExecutionId, status = run.Status, total = run.Total, processed = run.Processed, currentFile = run.CurrentFile,
            queuedAtUtc = run.QueuedAtUtc, startedAtUtc = run.StartedAtUtc, finishedAtUtc = run.FinishedAtUtc, error = run.Error, result = run.Result,
        });
    }

    [HttpGet("audits/{executionId:guid}/report.csv")]
    [RequirePermission("settings.ocr.view")]
    public async Task<IActionResult> Report(Guid executionId, [FromQuery] string projectId, [FromQuery] string? hubId, CancellationToken cancellationToken)
    {
        if (await EnsureProjectAccess(projectId, cancellationToken) is { } denied) return denied;
        var path = reports.GetPath(projectId, executionId);
        return path == null ? NotFound(new { message = "Relatório não encontrado." })
            : PhysicalFile(path, "text/csv; charset=utf-8", $"Auditoria_Qualidade_Documentos_{executionId:N}.csv");
    }

    private async Task<IActionResult?> EnsureProjectAccess(string projectId, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Projeto inválido." });
        return await projectAccess.CheckProjectAccessAsync(projectId, ct) switch
        {
            PermissionCheck.Allowed => null,
            PermissionCheck.Unauthenticated => Unauthorized(new { message = "Sessão expirada." }),
            PermissionCheck.Denied => StatusCode(403, new { message = "Projeto não autorizado." }),
            _ => StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." }),
        };
    }

    private async Task<(IActionResult? Error, string? HubId, string? Token)> EnsureAccContext(string projectId, CancellationToken ct)
    {
        if (await EnsureProjectAccess(projectId, ct) is { } denied) return (denied, null, null);

        var project = (await projectDirectory.ReadAsync(ct))
            .SingleOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        if (project is null) return (NotFound(new { message = "Projeto não encontrado no cadastro interno." }), null, null);

        var hub = NormalizeAccId(project.HubId);
        if (hub is null) return (BadRequest(new { message = "Projeto sem Account_ID_ACC/Hub válido no cadastro." }), null, null);

        var tokenResult = await autodeskUserTokens.GetValidAccessTokenAsync(ct);
        if (!tokenResult.IsValid)
            return (Conflict(new { code = "autodeskReconnectRequired", message = "Reconecte a Autodesk para continuar." }), null, null);

        return (null, hub, tokenResult.AccessToken);
    }

    private static string? NormalizeAccId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var raw = value.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return Guid.TryParseExact(raw, "D", out _) ? "b." + raw : null;
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
