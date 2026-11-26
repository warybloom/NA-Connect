using APS.WebApp.Services.Sidebar;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
namespace APS.WebApp.Controllers;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SidebarPublishRequest(long Revision, IReadOnlyList<SidebarItem> Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SidebarRestoreRequest(long Revision, long SourceRevision);
[ApiController]
[Route("api/sidebar")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class SidebarController(ISidebarStore store, IPermissionService permissions, IPermissionRepository repository,
    IAntiforgery antiforgery, ILogger<SidebarController> logger) : ControllerBase
{
    private ISidebarProfileStore ProfileStore => store as ISidebarProfileStore
        ?? throw new InvalidOperationException("Store de sidebar por perfil não configurado.");
    private async Task<(CurrentPermissionUser? User, UserPermissionData? Data, bool Manage)> Access(CancellationToken ct)
    {
        var user=await permissions.GetCurrentUserAsync(ct);
        if(user is null) return (null,null,false);
        if(user.IsMasterAdmin) return (user,null,true);
        var data=user.UserId is int id?await repository.CarregarDadosAsync(id,false,ct):null;
        return (user,data,data is not null && PermissionResolver.Decide(data,"permissions.manage",null).Allowed);
    }
    private IActionResult Unavailable(Exception ex) {logger.LogWarning("Sidebar indisponível ({Type})",ex.GetType().Name);return StatusCode(503,new {message="Menu oficial indisponível. Verifique a aplicação manual do schema."});}
    private IActionResult Forbidden()=>StatusCode(403,new {message="Gerenciar o menu oficial exige permissions.manage ou master admin."});
    private static SidebarConfiguration Merge(IReadOnlyList<SidebarConfiguration> layouts)
    {
        if(layouts.Count==0) return new(0,[]);
        var visible=new HashSet<string>(StringComparer.Ordinal);
        foreach(var layout in layouts)
        {
            var byId=layout.Items.ToDictionary(x=>x.Id,StringComparer.Ordinal);
            bool IsVisible(SidebarItem item)=>!item.Hidden && (item.ParentId is null || byId.TryGetValue(item.ParentId,out var parent) && IsVisible(parent));
            foreach(var item in layout.Items.Where(IsVisible)) visible.Add(item.Id);
        }
        // Metadados e ordenação vêm deterministicamente do primeiro layout aplicável.
        return new(layouts[0].Revision,layouts[0].Items.Select(x=>x with{Hidden=!visible.Contains(x.Id)}).ToArray());
    }
    private async Task<SidebarConfiguration> EffectiveLayout(CurrentPermissionUser user,UserPermissionData? data,int? project,CancellationToken ct)
    {
        var global=await store.ReadAsync(ct);
        var assignments=(data?.Assignments??[])
            .Where(x=>x.ProjetoId is null || x.ProjetoId==project)
            .GroupBy(x=>x.ProfileId).Select(x=>x.First()).OrderBy(x=>x.ProfileId).ToArray();
        if(user.IsMasterAdmin && assignments.All(x=>!x.IsMasterAdmin))
            assignments=[new ProfileAssignment(1,"MasterAdmin",null,true,new HashSet<string>()) ,.. assignments];
        if(assignments.Length==0) return global;
        var layouts=new List<SidebarConfiguration>();
        foreach(var assignment in assignments)
        {
            var saved=await ProfileStore.ReadProfileAsync(assignment.ProfileId,ct);
            layouts.Add(saved.Configured?saved.Configuration:assignment.IsMasterAdmin?SidebarRules.Default:global);
        }
        return Merge(layouts);
    }
    // Ferramenta GLOBAL: a decisão não usa projeto. 401 = sem sessão, 200 allowed=false = sem permissão, 503 = banco.
    [HttpGet("access")]
    [PermissionExempt("Sessão obrigatória e decisão estrita interna independente do enforcement global.")]
    public async Task<IActionResult> GetAccess(CancellationToken ct) {
        try {var a=await Access(ct);return a.User is null?Unauthorized():Ok(new {allowed=a.Manage,canManage=a.Manage,email=a.User.Email});}
        catch(Exception ex) when(ex is not OperationCanceledException){logger.LogWarning("Sidebar: permissões indisponíveis ({Type})",ex.GetType().Name);return StatusCode(503,new {message="Não foi possível verificar suas permissões no banco de dados."});}
    }
    [HttpGet]
    [PermissionExempt("Sessão obrigatória; configurações filtradas no servidor pelo resolvedor estrito.")]
    public async Task<IActionResult> Get([FromQuery]string? projectId,CancellationToken ct) {
        try {
            var a=await Access(ct);if(a.User is null)return Unauthorized();
            int? project=null;
            var master=a.User.IsMasterAdmin || a.Data is not null && PermissionResolver.IsMasterAdmin(a.Data);
            if(!master && !string.IsNullOrWhiteSpace(projectId)) {
                if(a.User.UserId is not int id || !await repository.UsuarioPossuiAcessoAtivoProjetoAsync(id,projectId,ct))return NotFound();
                project=await repository.ResolverProjetoIdAsync(projectId,ct);if(project is null)return NotFound();
            }
            SidebarConfiguration config;
            try {config=await EffectiveLayout(a.User,a.Data,project,ct);}
            catch(SidebarProfileSchemaUnavailableException) {config=await store.ReadAsync(ct);}
            var keys=a.Data is null?new HashSet<string>():project is null?PermissionResolver.EffectiveAnyProject(a.Data):PermissionResolver.Effective(a.Data,project);
            return Ok(new SidebarConfiguration(config.Revision,SidebarRules.Filter(config.Items,n=>
                SidebarRules.IsMasterOnly(n) ? master : n.PermissionKeys.Contains("permissions.manage") ? a.Manage : master || n.PermissionKeys.Any(keys.Contains))));
        }catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpGet("profiles")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Profiles(CancellationToken ct) {
        try {
            var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();
            if(!await ProfileStore.SchemaInstalledAsync(ct))throw new SidebarProfileSchemaUnavailableException();
            var rows=new List<object>();
            foreach(var profile in (await repository.ListarPerfisAsync(ct)).Where(x=>x.Ativo).OrderBy(x=>x.ProfileId)) {
                var saved=await ProfileStore.ReadProfileAsync(profile.ProfileId,ct);
                rows.Add(new {profileId=profile.ProfileId,name=profile.Name,isMasterAdmin=profile.IsMasterAdmin,ativo=profile.Ativo,revision=saved.Configuration.Revision,configured=saved.Configured});
            }
            return Ok(new {profiles=rows});
        }catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpGet("profiles/{profileId:int}")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Profile(int profileId,CancellationToken ct) {
        try {
            var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();
            var profile=(await repository.ListarPerfisAsync(ct)).SingleOrDefault(x=>x.ProfileId==profileId && x.Ativo);if(profile is null)return NotFound();
            var saved=await ProfileStore.ReadProfileAsync(profileId,ct);
            var configuration=saved.Configured?saved.Configuration:profile.IsMasterAdmin?SidebarRules.Default:await store.ReadAsync(ct);
            return Ok(new {configuration,profileId=profile.ProfileId,profileName=profile.Name,isMasterAdmin=profile.IsMasterAdmin,configured=saved.Configured,csrfToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
        }catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpPut("profiles/{profileId:int}/publish")]
    [RequestSizeLimit(131072)]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> PublishProfile(int profileId,[FromBody]SidebarPublishRequest request,CancellationToken ct) {
        try {
            var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();
            var profile=(await repository.ListarPerfisAsync(ct)).SingleOrDefault(x=>x.ProfileId==profileId && x.Ativo);if(profile is null)return NotFound();
            if(profile.IsMasterAdmin && !a.User.IsMasterAdmin && !(a.Data is not null && PermissionResolver.IsMasterAdmin(a.Data)))return Forbid();
            await antiforgery.ValidateRequestAsync(HttpContext);
            return Ok(await ProfileStore.PublishProfileAsync(profileId,SidebarRules.Validate(request.Items),request.Revision,a.User.Email,ct));
        }catch(AntiforgeryValidationException){return BadRequest(new {message="Token antiforgery inválido. Recarregue a página."});}
        catch(ArgumentException ex){return BadRequest(new {message=ex.Message});}
        catch(SidebarConflictException){return Conflict(new {message="O menu deste perfil mudou. Recarregue antes de publicar."});}
        catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpGet("admin")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Admin(CancellationToken ct) {
        try {var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();
            return Ok(new {configuration=await store.ReadAsync(ct),catalog=SidebarRules.Catalog,icons=SidebarRules.Icons,csrfToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
        }catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpGet("token")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Token(CancellationToken ct) {
        try {var a=await Access(ct);return a.User is null?Unauthorized():!a.Manage?Forbidden():Ok(new {token=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});}
        catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpGet("catalog")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Catalog(CancellationToken ct) {
        try {var a=await Access(ct);return a.User is null?Unauthorized():!a.Manage?Forbidden():Ok(SidebarRules.Catalog);}
        catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpGet("history")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> History(CancellationToken ct) {
        try {var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();return Ok(await store.HistoryAsync(ct));}
        catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpPut]
    [HttpPut("publish")]
    [RequestSizeLimit(131072)]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Publish([FromBody]SidebarPublishRequest request,CancellationToken ct) {
        try {var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();await antiforgery.ValidateRequestAsync(HttpContext);
            return Ok(await store.PublishAsync(SidebarRules.Validate(request.Items),request.Revision,a.User.Email,null,ct));
        }catch(AntiforgeryValidationException){return BadRequest(new {message="Token antiforgery inválido. Recarregue a página."});}
        catch(ArgumentException ex){return BadRequest(new {message=ex.Message});}
        catch(SidebarConflictException){return Conflict(new {message="O menu mudou. Recarregue antes de publicar."});}
        catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
    [HttpPost("restore")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Restore([FromBody]SidebarRestoreRequest request,CancellationToken ct) {
        try {var a=await Access(ct);if(a.User is null)return Unauthorized();if(!a.Manage)return Forbidden();await antiforgery.ValidateRequestAsync(HttpContext);
            if(request.SourceRevision<0)return BadRequest();var source=await store.RevisionAsync(request.SourceRevision,ct);if(source is null)return NotFound();
            return Ok(await store.PublishAsync(source.Items,request.Revision,a.User.Email,request.SourceRevision,ct));
        }catch(AntiforgeryValidationException){return BadRequest(new {message="Token antiforgery inválido. Recarregue a página."});}
        catch(ArgumentException ex){return BadRequest(new {message=ex.Message});}
        catch(SidebarConflictException){return Conflict(new {message="O menu mudou. Recarregue antes de restaurar."});}
        catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
    }
}
