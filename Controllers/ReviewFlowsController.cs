using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Planning;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/review-flows")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewFlowsController(ReviewFlowsStore store, IProjectAccessService projects, IPermissionService permissions,
    ILogger<ReviewFlowsController> logger) : ControllerBase
{
    private static readonly string[] Edit = ["planning.coordination.edit"];
    private static readonly string[] View = ["planning.review-flows.view"];
    // A Gestão à Vista e o portal TV consomem somente o fluxo efetivo do projeto.
    // Esse contrato de leitura não pode exigir acesso à administração de planejamento.
    private static readonly string[] EffectiveView = ["planning.review-flows.view", "reports.gestao.view", "tv.view"];

    private async Task<IActionResult?> Gate(string[] keys, string? projectId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var access = await projects.CheckProjectAccessAsync(projectId, ct);
            if (access == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (access == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
            if (access != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
        }
        var permission = await permissions.CheckAsync(keys, projectId, ct, anyProject: projectId is null);
        return permission switch
        {
            PermissionCheck.Allowed => null,
            PermissionCheck.Unauthenticated => Unauthorized(new { message = "Sessão expirada." }),
            PermissionCheck.Denied => StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade." }),
            _ => StatusCode(503, new { message = "Não foi possível validar permissões." })
        };
    }

    private IActionResult Failure(Exception ex)
    {
        if (ex is ArgumentException) return BadRequest(new { message = ex.Message });
        if (ex is KeyNotFoundException) return NotFound(new { message = ex.Message });
        if (ex is ReviewFlowConflictException) return Conflict(new { message = ex.Message });
        logger.LogError(ex, "Falha na API de fluxos de revisão. Ref={Ref}", HttpContext.TraceIdentifier);
        return StatusCode(503, new { message = $"Fluxos de revisão indisponíveis. Confira a migração planning.ReviewFlow. (ref: {HttpContext.TraceIdentifier})" });
    }

    [HttpGet]
    [RequirePermission("planning.review-flows.view", AnyProject = true)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = await Gate(View, null, ct); if (denied is not null) return denied;
        try
        {
            var (flows, bindings) = store.Read();
            var canEdit = await permissions.CheckAsync(Edit, null, ct, anyProject: true) == PermissionCheck.Allowed;
            var visible = new List<ReviewFlowBinding>();
            foreach (var b in bindings)
            {
                if (b.Scope == "project")
                {
                    if (await projects.CheckProjectAccessAsync(b.ProjectId, ct) == PermissionCheck.Allowed) visible.Add(b);
                }
                else
                {
                    var allowed = new List<string>();
                    foreach (var p in b.ProjectIds)
                        if (await projects.CheckProjectAccessAsync(p, ct) == PermissionCheck.Allowed) allowed.Add(p);
                    // Editores preservam IDs ocultos para não removê-los ao salvar; leitores recebem apenas projetos autorizados.
                    if (allowed.Count > 0) visible.Add(canEdit ? b : new ReviewFlowBinding
                    {
                        Id = b.Id, FlowId = b.FlowId, Scope = b.Scope, GroupName = b.GroupName,
                        ProjectIds = allowed, Version = b.Version
                    });
                }
            }
            var visibleFlows = canEdit ? flows : flows.Where(f => visible.Any(b => b.FlowId == f.Id)).ToList();
            return Ok(new { flows = visibleFlows, bindings = visible });
        }
        catch (Exception ex) { return Failure(ex); }
    }

    [HttpGet("effective")]
    [RequirePermission("planning.review-flows.view", "reports.gestao.view", "tv.view", AnyProject = true)]
    public async Task<IActionResult> Effective([FromQuery] string? projectIds, CancellationToken ct)
    {
        var ids = (projectIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        if (ids.Length is 0 or > 100) return BadRequest(new { message = "Selecione de 1 a 100 projetos." });
        try
        {
            var (flows, bindings) = store.Read();
            var result = new List<object>();
            foreach (var id in ids)
            {
                var denied = await Gate(EffectiveView, id, ct); if (denied is not null) return denied;
                var binding = bindings.FirstOrDefault(b => b.Scope == "project" && b.ProjectId == id)
                    ?? bindings.FirstOrDefault(b => b.Scope == "group" && b.ProjectIds.Contains(id));
                var flow = flows.FirstOrDefault(f => f.Id == binding?.FlowId && f.Active);
                result.Add(new { projectId = id, flowId = flow?.Id, flowName = flow?.Name,
                    source = flow is null ? "none" : binding!.Scope, groupName = flow is not null && binding!.Scope == "group" ? binding.GroupName : null,
                    stages = flow?.Stages.OrderBy(s => s.Order).Select(s => new { s.Id, s.Name, s.Order, applicable = true, s.StatusInFlowValues }).ToArray() ?? [] });
            }
            return Ok(new { projects = result });
        }
        catch (Exception ex) { return Failure(ex); }
    }

    [HttpPost]
    [RequirePermission("planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> Create([FromBody] ReviewFlow body, CancellationToken ct)
    {
        var denied = await Gate(Edit, null, ct); if (denied is not null) return denied;
        try { return Ok(store.Save(body, null)); } catch (Exception ex) { return Failure(ex); }
    }
    [HttpPut("{id}")]
    [RequirePermission("planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> Update(string id, [FromBody] ReviewFlow body, CancellationToken ct)
    {
        var denied = await Gate(Edit, null, ct); if (denied is not null) return denied;
        try { return Ok(store.Save(body, id)); } catch (Exception ex) { return Failure(ex); }
    }
    [HttpPost("bindings")]
    [RequirePermission("planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> CreateBinding([FromBody] ReviewFlowBinding body, CancellationToken ct)
        => await SaveBinding(body, null, ct);
    [HttpPut("bindings/{id}")]
    [RequirePermission("planning.coordination.edit", AnyProject = true)]
    public async Task<IActionResult> UpdateBinding(string id, [FromBody] ReviewFlowBinding body, CancellationToken ct)
        => await SaveBinding(body, id, ct);

    private async Task<IActionResult> SaveBinding(ReviewFlowBinding body, string? id, CancellationToken ct)
    {
        var denied = await Gate(Edit, null, ct); if (denied is not null) return denied;
        if (body.Scope == "project")
        {
            denied = await Gate(Edit, body.ProjectId, ct); if (denied is not null) return denied;
        }
        else if (body.Scope == "group")
        {
            // IDs já vinculados e ocultos ao editor podem ser reenviados sem perder a associação.
            var previous = id is null ? null : store.Read().Bindings.FirstOrDefault(b => b.Id == id);
            var preserved = previous?.ProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var projectId in body.ProjectIds.Where(p => !preserved.Contains(p)))
            { denied = await Gate(Edit, projectId, ct); if (denied is not null) return denied; }
        }
        try { return Ok(store.Save(body, id)); } catch (Exception ex) { return Failure(ex); }
    }

    private sealed record Candidate(string Portuguese, string English, string Kind, string[] Aliases);
    private static readonly Candidate[] Catalog =
    [
        new("QUALIDADE", "QUALITY", "etapa", ["QUALITY", "QUALIDADE"]),
        new("VERIFICAÇÃO", "VERIFICATION", "etapa", ["VERIFICATION", "VERIFICAÇÃO"]),
        new("VERIFICAÇÃO", "CHECKER", "candidato para função", ["CHECKER"]),
        new("VERIFICADOR", "CHECKER", "função", ["CHECKER", "VERIFICADOR"]),
        new("APROVAÇÃO", "APPROVAL", "etapa", ["APPROVAL", "APROVAÇÃO"]),
        new("AUTORIZAÇÃO", "AUTHORIZATION", "etapa", ["AUTHORIZATION", "AUTORIZAÇÃO"]),
        new("REVISÃO", "REVIEW", "etapa", ["REVIEW", "REVISÃO"]),
        new("APROVADO", "APPROVED", "estado final", ["APPROVED", "APROVADO"])
    ];

    [HttpGet("catalog")]
    [RequirePermission("planning.review-flows.view", AnyProject = true)]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
    {
        var denied = await Gate(View, null, ct); if (denied is not null) return denied;
        return Ok(new { candidates = Catalog.Select(c => new { portuguese = c.Portuguese, english = c.English, kind = c.Kind, aliases = c.Aliases }) });
    }

    [HttpGet("suggestions")]
    [RequirePermission("planning.review-flows.view", AnyProject = true)]
    public async Task<IActionResult> Suggestions([FromQuery] string? projectIds, CancellationToken ct)
    {
        var ids = (projectIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        if (ids.Length is 0 or > 100) return BadRequest(new { message = "Selecione de 1 a 100 projetos." });
        try
        {
            var (flows, bindings) = store.Read();
            var rows = new List<object>();
            foreach (var id in ids)
            {
                var denied = await Gate(View, id, ct); if (denied is not null) return denied;
                var binding = bindings.FirstOrDefault(b => b.Scope == "project" && b.ProjectId == id)
                    ?? bindings.FirstOrDefault(b => b.Scope == "group" && b.ProjectIds.Contains(id));
                var flow = flows.FirstOrDefault(f => f.Id == binding?.FlowId && f.Active);
                foreach (var (status, count) in store.Statuses(id))
                {
                    var matches = Catalog.Where(c => c.Aliases.Any(a => ReviewFlowRules.Key(a) == ReviewFlowRules.Key(status))).ToArray();
                    var mapped = ReviewFlowRules.Resolve(flow, status);
                    var history = flows.SelectMany(f => f.Stages).Where(s => s.StatusInFlowValues.Any(a => ReviewFlowRules.Key(a) == ReviewFlowRules.Key(status)))
                        .Select(s => s.Name).Distinct().ToArray();
                    rows.Add(new { projectId = id, original = status, count, portuguese = matches.Length == 1 ? matches[0].Portuguese : null,
                        english = matches.FirstOrDefault()?.English, kind = matches.FirstOrDefault()?.Kind,
                        candidates = matches.Select(c => c.Portuguese).Concat(history).Distinct().ToArray(),
                        suggestedStage = mapped?.Name ?? history.FirstOrDefault() ?? (matches.Length == 1 ? matches[0].Portuguese : null),
                        mappedStageId = mapped?.Id, situation = mapped is not null ? "mapeado" : matches.Length > 0 || history.Length > 0 ? "pendente" : "sem correspondência" });
                }
            }
            return Ok(new { statuses = rows });
        }
        catch (Exception ex) { return Failure(ex); }
    }
}
