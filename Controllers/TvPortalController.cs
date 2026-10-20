using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController, Route("api/tv-portal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TvPortalController(TvPortalIdentityService identities, TvConfigurationStore configurations,
    TvPortalAssignmentStore assignments, IPermissionRepository repository) : ControllerBase
{
    private const string ScopedProfile = "Controle de Produção + Configuração TV (Restrito)";
    private async Task<bool> ScopedAsync(int userId, CancellationToken ct)
    {
        var dados=await repository.CarregarDadosAsync(userId,false,ct);
        return !PermissionResolver.IsMasterAdmin(dados)
            && dados.Assignments.Any(a=>a.ProjetoId is null && a.ProfileName==ScopedProfile);
    }
    private async Task<HashSet<string>> ScopedPresentationIdsAsync(int userId, TvConfiguration config, CancellationToken ct)
    {
        var allowed=new HashSet<string>(StringComparer.Ordinal);
        foreach(var presentation in config.Presentations)
        {
            bool ok=true;
            foreach(var slide in presentation.Slides)
            {
                if(slide.ScreenId.StartsWith("gestao-vista-tipo2-",StringComparison.Ordinal)
                    || slide.ScreenId=="resumo-tv-teste") {ok=false;break;}
                var projects=slide.Projetos is {Count:>0} ps ? ps.Select(x=>x.ProjectId) : new[]{slide.ProjectId};
                foreach(var project in projects.Distinct(StringComparer.Ordinal))
                    if(!await repository.UsuarioPossuiAcessoAtivoProjetoAsync(userId,project,ct)) {ok=false;break;}
                if(!ok)break;
            }
            if(ok)allowed.Add(presentation.Id);
        }
        return allowed;
    }
    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        return identity is null ? Unauthorized() : Ok(new { restricted = identity.Restricted, canConfigure = identity.CanConfigure });
    }

    [HttpGet("presentations")]
    public async Task<IActionResult> Presentations(CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.Restricted || !identity.CanView) return StatusCode(403);
        var config = await configurations.ReadAsync(ct);
        var grants = await assignments.ReadAsync(ct);
        return Ok(config.Presentations.Where(p => p.EstaAtiva && TvPortalPolicy.Assigned(grants, p.Id, identity.UserId))
            .Select(p => new { p.Id, p.Name, supported = TvPortalResources.Supported(p) }));
    }

    [HttpGet("presentations/{id}")]
    public async Task<IActionResult> Presentation(string id, CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.Restricted || !identity.CanView) return StatusCode(403);
        var config = await configurations.ReadAsync(ct);
        var grants = await assignments.ReadAsync(ct);
        var p = config.Presentations.SingleOrDefault(p => p.Id == id && p.EstaAtiva);
        if (p is null || !TvPortalPolicy.Assigned(grants, id, identity.UserId)) return StatusCode(403);
        if (!TvPortalResources.Supported(p)) return Conflict(new { message = "NÃO APTO: esta apresentação contém uma fonte ainda sem adaptador restrito auditado." });
        // Atribuição de apresentação é independente do vínculo administrativo perm.UserProjects.
        // Autorizar exclusivamente projetos ATIVOS explicitamente configurados nos slides.
        foreach (var project in p.Slides.SelectMany(s => s.Projetos is { Count: > 0 } ps ? ps.Select(p => p.ProjectId) : new[] { s.ProjectId }).Distinct(StringComparer.Ordinal))
            if (!await identities.CanViewProjectAsync(identity.UserId, project, ct)) return StatusCode(403);
        return Ok(p);
    }

    [HttpGet("assignments")]
    public async Task<IActionResult> Assignments(CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.CanConfigure) return StatusCode(403);
        var config = await configurations.ReadAsync(ct);
        var scoped = await ScopedAsync(identity.UserId,ct);
        var visibleIds = scoped ? await ScopedPresentationIdsAsync(identity.UserId,config,ct) : null;
        var users = new List<object>();
        foreach (var user in (await repository.ListarUsuariosAsync(ct)).Where(u => u.Ativo))
        {
            var data = await repository.CarregarDadosAsync(user.UserId, false, ct);
            if (TvPortalPolicy.Identity(user.UserId, data).Restricted)
                users.Add(new { user.UserId, name = user.DisplayName ?? user.Email });
        }
        var current=await assignments.ReadAsync(ct);
        return Ok(new { assignments = visibleIds is null ? current
                : current with {Grants=current.Grants.Where(g=>visibleIds.Contains(g.PresentationId)).ToArray()},
            users, presentations = config.Presentations
                .Where(p=>visibleIds is null || visibleIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.EstaAtiva, supported = TvPortalResources.Supported(p) }) });
    }

    [HttpPut("assignments")]
    public async Task<IActionResult> SaveAssignments([FromBody] TvPortalAssignments request, CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.CanConfigure || Request.Headers["Sec-Fetch-Site"] == "cross-site") return StatusCode(403);
        var config = await configurations.ReadAsync(ct);
        if (request.Grants is null || request.Grants.Any(g => g is null || !config.Presentations.Any(p => p.Id == g.PresentationId) || g.UserIds is null))
            return BadRequest(new { message = "Apresentação inválida." });
        foreach (var id in request.Grants.SelectMany(g => g.UserIds).Distinct())
        {
            var user = await repository.ObterUsuarioAsync(id, ct);
            if (user?.Ativo != true || !TvPortalPolicy.Identity(id, await repository.CarregarDadosAsync(id, false, ct)).Restricted)
                return BadRequest(new { message = "Atribua somente usuários TV ativos." });
        }
        if (await ScopedAsync(identity.UserId,ct))
        {
            var permitted=await ScopedPresentationIdsAsync(identity.UserId,config,ct);
            if (request.Grants.Any(g=>!permitted.Contains(g.PresentationId)))
                return StatusCode(403, new {message="Apresentação fora do escopo dos projetos autorizados."});
            var existing=await assignments.ReadAsync(ct);
            if(existing.Revision!=request.Revision)
                return Conflict(new {message="As atribuições mudaram. Recarregue."});
            request=request with {Grants=existing.Grants.Where(g=>!permitted.Contains(g.PresentationId))
                .Concat(request.Grants).ToArray()};
        }
        try { return Ok(await assignments.SaveAsync(request, ct)); }
        catch (TvConfigurationConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
