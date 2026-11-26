using System.Security.Claims;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/information-quality-audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InformationQualityAuditController(
    InformationQualityAuditService service,
    InformationQualityRedistributionService redistributionService,
    InformationQualityProgressService progressService,
    InformationQualityRoutingControlService routingControlService,
    IProjectAccessService projectAccess,
    IPermissionService permissions,
    IAntiforgery antiforgery,
    ILogger<InformationQualityAuditController> logger) : ControllerBase
{
    private string[] KeysForRequest()
    {
        var path = HttpContext.Request.Path.Value ?? "";
        var edit = HttpMethods.IsPut(Request.Method)
            || (HttpMethods.IsPost(Request.Method) && !path.EndsWith("-preview", StringComparison.OrdinalIgnoreCase));
        if (path.Contains("routing-control", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(Request.Method))
            edit = true;
        if (path.Contains("progress-", StringComparison.OrdinalIgnoreCase))
            return [edit ? "quality.progress.edit" : "quality.progress.view"];
        return [edit ? "quality.audit.edit" : "quality.audit.view"];
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccId(projectId))
            return BadRequest(new { message = "Project ID inválido." });

        var denied = await ValidateAccessAsync(projectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await service.ReadAsync(projectId, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao executar auditoria da informação. ProjectId={ProjectId}", projectId);
            return StatusCode(503, new { message = "Auditoria da informação temporariamente indisponível." });
        }
    }

    [HttpGet("redistribution-preview")]
    public async Task<IActionResult> RedistributionPreview(
        [FromQuery] string projectId,
        [FromQuery] string documentCode,
        CancellationToken ct)
    {
        if (!IsAccId(projectId))
            return BadRequest(new { message = "Project ID inválido." });
        if (string.IsNullOrWhiteSpace(documentCode))
            return BadRequest(new { message = "Documento de origem é obrigatório." });

        var denied = await ValidateAccessAsync(projectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await redistributionService.PreviewAsync(projectId, documentCode, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao simular redistribuição. ProjectId={ProjectId} Documento={Documento}",
                projectId, documentCode);
            return StatusCode(503, new { message = "Não foi possível simular a redistribuição." });
        }
    }

    [HttpPost("redistribution-batch-preview")]
    public async Task<IActionResult> RedistributionBatchPreview(
        [FromBody] RedistributionBatchRequest request,
        CancellationToken ct)
    {
        if (!IsAccId(request.ProjectId))
            return BadRequest(new { message = "Project ID inválido." });
        if (request.DocumentCodes is null || request.DocumentCodes.Count == 0)
            return BadRequest(new { message = "Selecione ao menos um número de documento." });

        var denied = await ValidateAccessAsync(request.ProjectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await redistributionService.PreviewBatchAsync(request.ProjectId, request.DocumentCodes, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao simular redistribuição em lote. ProjectId={ProjectId} Qtd={Quantidade}",
                request.ProjectId, request.DocumentCodes.Count);
            return StatusCode(503, new { message = "Não foi possível simular a redistribuição em lote." });
        }
    }

    [HttpPost("redistribution-confirm")]
    public async Task<IActionResult> RedistributionConfirm(
        [FromBody] RedistributionConfirmRequest request,
        CancellationToken ct)
    {
        if (!IsAccId(request.ProjectId))
            return BadRequest(new { message = "Project ID inválido." });
        if (string.IsNullOrWhiteSpace(request.DocumentCode))
            return BadRequest(new { message = "Documento de origem é obrigatório." });

        var denied = await ValidateAccessAsync(request.ProjectId, ct);
        if (denied is not null) return denied;

        try
        {
            var decision = await redistributionService.ConfirmAsync(
                request.ProjectId,
                request.DocumentCode,
                CurrentUser(),
                ct);
            return Ok(decision);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            logger.LogInformation(ex,
                "Redistribuição concorrente/idempotente detectada. ProjectId={ProjectId} Documento={Documento}",
                request.ProjectId, request.DocumentCode);

            var existing = (await redistributionService.ListAsync(request.ProjectId, ct))
                .FirstOrDefault(x => string.Equals(
                    x.SourceDocumentCode,
                    request.DocumentCode.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            return existing is null
                ? Conflict(new { message = "Este documento já possui redistribuição ativa." })
                : Ok(existing);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao confirmar redistribuição. ProjectId={ProjectId} Documento={Documento}",
                request.ProjectId, request.DocumentCode);
            return StatusCode(503, new { message = "Não foi possível confirmar a redistribuição." });
        }
    }

    [HttpPost("redistribution-batch-confirm")]
    public async Task<IActionResult> RedistributionBatchConfirm(
        [FromBody] RedistributionBatchRequest request,
        CancellationToken ct)
    {
        if (!IsAccId(request.ProjectId))
            return BadRequest(new { message = "Project ID inválido." });
        if (request.DocumentCodes is null || request.DocumentCodes.Count == 0)
            return BadRequest(new { message = "Selecione ao menos um número de documento." });

        var denied = await ValidateAccessAsync(request.ProjectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await redistributionService.ConfirmBatchAsync(
                request.ProjectId,
                request.DocumentCodes,
                CurrentUser(),
                ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao confirmar redistribuição em lote. ProjectId={ProjectId} Qtd={Quantidade}",
                request.ProjectId, request.DocumentCodes.Count);
            return StatusCode(503, new { message = "Não foi possível confirmar a redistribuição em lote." });
        }
    }

    [HttpGet("redistributions")]
    public async Task<IActionResult> Redistributions(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        if (!IsAccId(projectId))
            return BadRequest(new { message = "Project ID inválido." });

        var denied = await ValidateAccessAsync(projectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await redistributionService.ListAsync(projectId, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao consultar redistribuições. ProjectId={ProjectId}", projectId);
            return StatusCode(503, new { message = "Não foi possível carregar o histórico de redistribuições." });
        }
    }

    [HttpGet("activity-disciplines")]
    public async Task<IActionResult> ActivityDisciplines(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        if (!IsAccId(projectId))
            return BadRequest(new { message = "Project ID inválido." });

        var denied = await ValidateAccessAsync(projectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await redistributionService.ActivityDisciplineOptionsAsync(projectId, ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao carregar disciplinas para atividade AT. ProjectId={ProjectId}", projectId);
            return StatusCode(503, new { message = "Não foi possível carregar as disciplinas do projeto." });
        }
    }

    [HttpPut("activity-discipline")]
    public async Task<IActionResult> SaveActivityDiscipline(
        [FromBody] ActivityDisciplineMappingRequest request,
        CancellationToken ct)
    {
        if (!IsAccId(request.ProjectId))
            return BadRequest(new { message = "Project ID inválido." });
        if (string.IsNullOrWhiteSpace(request.ActivityCode))
            return BadRequest(new { message = "Atividade é obrigatória." });
        if (string.IsNullOrWhiteSpace(request.Discipline))
            return BadRequest(new { message = "Disciplina é obrigatória." });

        var denied = await ValidateAccessAsync(request.ProjectId, ct);
        if (denied is not null) return denied;

        try
        {
            return Ok(await redistributionService.SaveActivityDisciplineMappingAsync(
                request.ProjectId,
                request.ActivityCode,
                request.Discipline,
                CurrentUser(),
                ct));
        }
        catch (SqlException ex) when (ex.Number == 50001)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao salvar atividade AT. ProjectId={ProjectId} Atividade={Atividade}",
                request.ProjectId, request.ActivityCode);
            return StatusCode(503, new { message = "Não foi possível salvar a associação AT → disciplina." });
        }
    }

    [HttpGet("progress-settings")]
    public async Task<IActionResult> ProgressSettings([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Project ID inválido." });
        var denied = await ValidateAccessAsync(projectId, ct); if (denied is not null) return denied;
        return Ok(await progressService.SettingsAsync(projectId, ct));
    }

    [HttpPut("progress-settings")]
    public async Task<IActionResult> SaveProgressSettings([FromBody] ProgressSettingsRequest request, CancellationToken ct)
    {
        if (!IsAccId(request.ProjectId)) return BadRequest(new { message = "Project ID inválido." });
        var denied = await ValidateAccessAsync(request.ProjectId, ct); if (denied is not null) return denied;
        try { await progressService.SaveSettingsAsync(request, CurrentUser(), ct); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("progress-preview")]
    public async Task<IActionResult> ProgressPreview([FromQuery] string projectId, [FromQuery] string documentCode, CancellationToken ct)
    {
        if (!IsAccId(projectId) || string.IsNullOrWhiteSpace(documentCode)) return BadRequest(new { message = "Project ID e documento são obrigatórios." });
        var denied = await ValidateAccessAsync(projectId, ct); if (denied is not null) return denied;
        return Ok(await progressService.PreviewAsync(projectId, documentCode, ct));
    }

    [HttpPost("progress-confirm")]
    public async Task<IActionResult> ProgressConfirm([FromBody] RedistributionConfirmRequest request, CancellationToken ct)
    {
        if (!IsAccId(request.ProjectId) || string.IsNullOrWhiteSpace(request.DocumentCode)) return BadRequest(new { message = "Project ID e documento são obrigatórios." });
        var denied = await ValidateAccessAsync(request.ProjectId, ct); if (denied is not null) return denied;
        try { return Ok(await progressService.ConfirmAsync(request.ProjectId, request.DocumentCode, CurrentUser(), ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Conflict(new { message = "Este documento já possui decisão ativa." }); }
    }

    [HttpGet("progress-decisions")]
    public async Task<IActionResult> ProgressDecisions([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Project ID inválido." });
        var denied = await ValidateAccessAsync(projectId, ct); if (denied is not null) return denied;
        return Ok(await progressService.DecisionsAsync(projectId, ct));
    }

    // Emite token antiforgery exclusivamente para usuario com permissao de edicao no projeto.
    // O token nao substitui a verificacao de RBAC feita pelo endpoint apply.
    [HttpGet("routing-control/token")]
    public async Task<IActionResult> RoutingControlToken([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Project ID inválido." });
        var denied = await ValidateAccessAsync(projectId, ct);
        if (denied is not null) return denied;
        var editAccess = await permissions.CheckAsync(["quality.audit.edit"], projectId, ct);
        if (editAccess == PermissionCheck.Unauthenticated) return Unauthorized();
        if (editAccess == PermissionCheck.Denied) return NotFound();
        if (editAccess != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível verificar a permissão." });
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpGet("routing-control")]
    public async Task<IActionResult> RoutingControl([FromQuery] string projectId, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Project ID inválido." });
        var denied = await ValidateAccessAsync(projectId, ct); if (denied is not null) return denied;
        try { return Ok(await routingControlService.ReadAsync(projectId, ct)); }
        catch (RoutingControlSchemaException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpPost("routing-control/preview")]
    public async Task<IActionResult> RoutingControlPreview([FromBody] RoutingControlRequest request, CancellationToken ct)
    {
        var invalid = ValidateRoutingRequest(request); if (invalid is not null) return invalid;
        var denied = await ValidateAccessAsync(request.ProjectId, ct); if (denied is not null) return denied;
        try { return Ok(await routingControlService.PreviewAsync(request, ct)); }
        catch (RoutingControlSchemaException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpPost("routing-control/apply")]
    public async Task<IActionResult> RoutingControlApply([FromBody] RoutingControlRequest request, CancellationToken ct)
    {
        var invalid = ValidateRoutingRequest(request); if (invalid is not null) return invalid;
        var denied = await ValidateAccessAsync(request.ProjectId, ct); if (denied is not null) return denied;
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            var actor = AuthenticatedUser();
            if (actor is null) return Unauthorized(new { message = "Não foi possível identificar o usuário autenticado." });
            return Ok(await routingControlService.ApplyAsync(request, actor, ct));
        }
        catch (AntiforgeryValidationException) { return BadRequest(new { message = "Token antiforgery inválido. Recarregue a página." }); }
        catch (RoutingControlConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (RoutingControlSchemaException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    private IActionResult? ValidateRoutingRequest(RoutingControlRequest request)
    {
        if (!IsAccId(request.ProjectId)) return BadRequest(new { message = "Project ID inválido." });
        if (string.IsNullOrWhiteSpace(request.SourceDocumentCode)) return BadRequest(new { message = "Documento de origem é obrigatório." });
        if (!RoutingControlOperations.IsValid(request.Operation)) return BadRequest(new { message = "Operação inválida." });
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5) return BadRequest(new { message = "Justificativa com ao menos 5 caracteres é obrigatória." });
        if (string.IsNullOrWhiteSpace(request.Version)) return BadRequest(new { message = "Versão é obrigatória." });
        if (request.Operation == RoutingControlOperations.EditDePara && string.IsNullOrWhiteSpace(request.DestinationDocumentCode))
            return BadRequest(new { message = "Documento de destino é obrigatório para editar De/Para." });
        return null;
    }

    private async Task<IActionResult?> ValidateAccessAsync(string projectId, CancellationToken ct)
    {
        var access = await projectAccess.CheckProjectAccessAsync(projectId, ct);
        if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar acesso." });

        var keys = KeysForRequest();
        var permission = await permissions.CheckAsync(keys, projectId, ct);
        if (permission == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (permission == PermissionCheck.Denied)
            return NotFound();
        if (permission != PermissionCheck.Allowed)
            return StatusCode(503, new { message = "Não foi possível validar permissões." });

        return null;
    }

    private string CurrentUser()
        => User.Identity?.Name
           ?? User.FindFirstValue(ClaimTypes.Email)
           ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.FindFirstValue("preferred_username")
           ?? "usuario-autenticado";

    private string? AuthenticatedUser()
        => User.Identity?.IsAuthenticated == true
            ? User.Identity.Name
              ?? User.FindFirstValue(ClaimTypes.Email)
              ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? User.FindFirstValue("preferred_username")
            : null;

    private static bool IsAccId(string? value)
        => value is { Length: 38 }
        && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
