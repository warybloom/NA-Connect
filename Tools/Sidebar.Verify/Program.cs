using System.Reflection;
using System.Text.Json;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Sidebar;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

var checks=0;
void Check(bool pass,string message){if(!pass)throw new Exception(message);Console.WriteLine("PASS "+message);checks++;}
void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Accepted "+name);}
var defaults=SidebarRules.Default.Items;
Check(SidebarRules.Validate(defaults).Count==83,"official default catalog");
Check(SidebarRules.Catalog.All(n=>n.Group||n.To is not null&&n.PermissionKeys.Length>0),"fixed routes and permissions");
Check(SidebarRules.Catalog.SelectMany(n=>n.PermissionKeys).All(PermissionCatalog.Existe),"all keys exist in production permission catalog");
foreach(var mutation in new (string Name, Func<SidebarItem[],SidebarItem[]> Change)[] {
    ("unknown ID", a=>{a[0]=a[0] with {Id="unknown"};return a;}),
    ("duplicate ID", a=>{a[1]=a[1] with {Id=a[0].Id};return a;}),
    ("self cycle", a=>{a[0]=a[0] with {ParentId=a[0].Id};return a;}),
    ("page parent", a=>{a[0]=a[0] with {ParentId=a.First(n=>n.Id.StartsWith("page:")).Id};return a;}),
    ("bad icon", a=>{a[0]=a[0] with {Icon="<script>"};return a;}),
    ("duplicate order", a=>{var i=Array.FindIndex(a,1,n=>n.ParentId is null);a[i]=a[i] with {Order=a[0].Order};return a;}),
    ("missing node", a=>a.Skip(1).ToArray())
}) Reject(()=>SidebarRules.Validate(mutation.Change(defaults.ToArray())),mutation.Name);
try{JsonSerializer.Deserialize<SidebarItem>("{\"id\":\"page:/cde/documentos\",\"to\":\"/evil\"}",SidebarRules.Json);throw new Exception("route injection");}catch(JsonException){Check(true,"JSON route/key fields rejected");}
Check(SidebarRules.Filter(defaults,_=>false).Count==0,"GET removes all restricted nodes");
var docs=SidebarRules.Filter(defaults,n=>n.PermissionKeys.Contains("cde.docs.view"));
Check(docs.Count==2 && docs.Any(n=>n.Id=="page:/cde/documentos"),"GET returns permitted leaf and ancestors only");
var hidden=defaults.Select(n=>n.Id=="group:CDE"?n with {Hidden=true}:n).ToArray();
Check(SidebarRules.Filter(hidden,_=>true).All(n=>n.Id!="page:/cde/documentos"),"hidden parent respected");
Check(typeof(SidebarController).GetMethod("Publish")!.GetCustomAttribute<RequirePermissionAttribute>()!.Keys.SequenceEqual(new[]{"permissions.manage"}),"publish declares permissions.manage");
var session=new FakePermissions();var store=new MemoryStore();var repo=DispatchProxy.Create<IPermissionRepository,RepositoryProxy>();
var proxy=(RepositoryProxy)(object)repo;var csrf=new FakeAntiforgery();
var controller=new SidebarController(store,session,repo,csrf,NullLogger<SidebarController>.Instance){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
var ct=CancellationToken.None;
Check(await controller.Get(null,ct) is UnauthorizedResult,"anonymous runtime GET denied");
session.User=new(7,"acc","test@na.com","Test",false);
Check(await controller.Publish(new(0,defaults),ct) is ObjectResult {StatusCode:403},"publish denied with Enforce=false");
Check(await controller.Restore(new(0,0),ct) is ObjectResult {StatusCode:403},"restore denied with Enforce=false");
Check(await controller.Admin(ct) is ObjectResult {StatusCode:403},"full structure denied with Enforce=false");
proxy.Data=new(false,[new(1,"Project admin",10,false,new HashSet<string>{"permissions.manage"})],[],new HashSet<int>(),new HashSet<int>{10});
Check(await controller.Publish(new(0,defaults),ct) is ObjectResult {StatusCode:403},"project-only manage cannot edit global menu");
var normal=(OkObjectResult)await controller.Get(null,ct);Check(((SidebarConfiguration)normal.Value!).Items.Count==0,"project-only manage cannot leak manager through runtime GET");
proxy.Data=new(false,[],[new(null,"permissions.manage","ALLOW")],new HashSet<int>());
csrf.Valid=false;
Check(await controller.Publish(new(0,defaults),ct) is BadRequestObjectResult,"antiforgery required after authorization");Check(store.Writes==0,"denied requests never write");
csrf.Valid=true;
Check(await controller.Publish(new(0,defaults),ct) is OkObjectResult,"strict permission permits publishing");
Check(await controller.Publish(new(0,defaults),ct) is ConflictObjectResult,"stale revision returns 409");
Check(await controller.Restore(new(1,0),ct) is OkObjectResult && store.Current.Revision==2 && store.Restored==0,"restore creates new revision");
proxy.Data=new(false,[],[new(null,"permissions.manage","DENY")],new HashSet<int>());session.User=session.User with {IsMasterAdmin=true};
Check(await controller.Publish(new(2,defaults),ct) is OkObjectResult,"master bypass independent of global enforcement");
proxy.Fail=true;session.User=session.User with {IsMasterAdmin=false};
Check(await controller.Admin(ct) is ObjectResult {StatusCode:503},"permission failure closes administrative access");
Check(await controller.Get(null,ct) is ObjectResult {StatusCode:503},"permission failure exposes no runtime structure");
Console.WriteLine(checks+" backend checks passed (no DB or network calls).");

public class RepositoryProxy:DispatchProxy {
    public bool Fail;public UserPermissionData Data=new(false,[],[],new HashSet<int>());
    protected override object? Invoke(MethodInfo? method,object?[]? args) {
        if(Fail)throw new InvalidOperationException("unavailable");
        return method!.Name switch {"CarregarDadosAsync"=>Task.FromResult(Data),"UsuarioPossuiAcessoAtivoProjetoAsync"=>Task.FromResult(true),"ResolverProjetoIdAsync"=>Task.FromResult<int?>(10),_=>throw new NotSupportedException(method.Name)};
    }
}
public sealed class FakePermissions:IPermissionService {
    public CurrentPermissionUser? User;public bool Enforced=>false;
    public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct)=>Task.FromResult(User);
    public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys,string? projectIdAcc,CancellationToken ct,bool anyProject=false)=>Task.FromResult(PermissionCheck.Allowed);
    public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc,CancellationToken ct)=>throw new NotSupportedException();
    public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc,CancellationToken ct)=>Task.FromResult<int?>(null);
}
public sealed class FakeAntiforgery:IAntiforgery {
    public bool Valid=true;
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext c)=>new("token","cookie","field","X-Sidebar-CSRF");
    public AntiforgeryTokenSet GetTokens(HttpContext c)=>GetAndStoreTokens(c);
    public Task<bool> IsRequestValidAsync(HttpContext c)=>Task.FromResult(Valid);
    public Task ValidateRequestAsync(HttpContext c)=>Valid?Task.CompletedTask:Task.FromException(new AntiforgeryValidationException("bad"));
    public void SetCookieTokenAndHeader(HttpContext c){}
}
public sealed class MemoryStore:ISidebarStore {
    public SidebarConfiguration Current=SidebarRules.Default;public int Writes;public long? Restored;
    public Task<SidebarConfiguration> ReadAsync(CancellationToken ct)=>Task.FromResult(Current);
    public Task<IReadOnlyList<SidebarHistory>> HistoryAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<SidebarHistory>>([]);
    public Task<SidebarConfiguration?> RevisionAsync(long revision,CancellationToken ct)=>Task.FromResult<SidebarConfiguration?>(revision==0?SidebarRules.Default:null);
    public Task<SidebarConfiguration> PublishAsync(IReadOnlyList<SidebarItem> items,long expectedRevision,string author,long? restoredFrom,CancellationToken ct) {
        if(Current.Revision!=expectedRevision)throw new SidebarConflictException();Writes++;Restored=restoredFrom;Current=new(Current.Revision+1,items);return Task.FromResult(Current);
    }
}
