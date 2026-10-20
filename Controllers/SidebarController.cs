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
            var config=await store.ReadAsync(ct);
            var keys=a.Data is null?new HashSet<string>():project is null?PermissionResolver.EffectiveAnyProject(a.Data):PermissionResolver.Effective(a.Data,project);
            return Ok(new SidebarConfiguration(config.Revision,SidebarRules.Filter(config.Items,n=>n.PermissionKeys.Contains("permissions.manage")?a.Manage:master||n.PermissionKeys.Any(keys.Contains))));
        }catch(Exception ex) when(ex is not OperationCanceledException){return Unavailable(ex);}
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
