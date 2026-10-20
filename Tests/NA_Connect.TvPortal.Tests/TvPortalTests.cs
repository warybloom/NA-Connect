using System.Reflection;
using System.Text.Json;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

public class TvPortalTests
{
    private static UserPermissionData Data(params ProfileAssignment[] profiles) => new(false, profiles, [], new HashSet<int>());
    private static ProfileAssignment Tv => new(7, "TV", null, false, new HashSet<string> { "tv.view", "home.view", "settings.tv.configure" });
    private static ProfileAssignment Admin => new(1, "Admin", null, true, new HashSet<string>());
    [Fact] public void ExcessHistoricalGrantsDoNotExemptTv()
    {
        var identity = TvPortalPolicy.Identity(42, Data(Tv));
        Assert.True(identity.Restricted); Assert.True(identity.CanView); Assert.False(identity.CanConfigure);
    }
    [Fact] public void RealAdminKeepsAdministrativeInterface() => Assert.False(TvPortalPolicy.Identity(42, Data(Tv, Admin)).Restricted);
    [Fact] public void GlobalAdministratorWithoutTvKeepsDashboard()
    {
        var admin = new ProfileAssignment(2, "Administrador", null, false,
            new HashSet<string> { "permissions.manage", "tv.view", "settings.tv.configure" });
        var identity = TvPortalPolicy.Identity(45, Data(admin));
        Assert.False(identity.Restricted); Assert.True(identity.CanConfigure);
        var mixed = TvPortalPolicy.Identity(45, Data(Tv, admin));
        Assert.False(mixed.Restricted); Assert.True(mixed.CanConfigure);
    }
    [Fact] public void MixedAdministratorWithExplicitDenyRemainsRestrictedLikeUser45()
    {
        var admin = new ProfileAssignment(2, "Administrador", null, false,
            new HashSet<string> { "permissions.manage", "tv.view", "settings.tv.configure" });
        var data = Data(Tv, admin) with { Overrides = new[] {
            new PermissionOverride(null, "permissions.manage", "DENY"),
            new PermissionOverride(null, "settings.tv.configure", "DENY") } };
        var identity = TvPortalPolicy.Identity(45, data);
        Assert.True(identity.Restricted); Assert.True(identity.CanView); Assert.False(identity.CanConfigure);
        var corrected = TvPortalPolicy.Identity(45, data with { Assignments = new[] { Tv } });
        Assert.True(corrected.Restricted); Assert.True(corrected.CanView); Assert.False(corrected.CanConfigure);
    }
    [Fact] public void InactiveTvDesignationDoesNotBecomeCompatibilityAdmin()
    {
        var identity=TvPortalPolicy.Identity(42,Data(),true);
        Assert.True(identity.Restricted);Assert.False(identity.CanView);Assert.False(identity.CanConfigure);
    }
    [Fact] public void MixedNonAdminRoleDoesNotEscapeTvBarrier() => Assert.True(TvPortalPolicy.Identity(42, Data(Tv, new(8, "Other", null, false, new HashSet<string> { "home.view" }))).Restricted);
    [Fact] public void ProjectScopedTvRoleCanEnterPortal()
    {
        var data=Data(Tv with{ProjetoId=1}) with{ActiveProjects=new HashSet<int>{1}};
        Assert.True(TvPortalPolicy.Identity(42,data).CanView);
    }
    [Fact] public void DefaultDenyAndUserIsolation()
    {
        var grants = new TvPortalAssignments(1, [new("a", [42])]);
        Assert.True(TvPortalPolicy.Assigned(grants, "a", 42));
        Assert.False(TvPortalPolicy.Assigned(grants, "b", 42)); Assert.False(TvPortalPolicy.Assigned(grants, "a", 43));
    }
    [Theory][InlineData(9,false)][InlineData(10,true)][InlineData(300,true)][InlineData(301,false)]
    public void IntervalLimits(int seconds, bool expected) => Assert.Equal(expected,TvPortalPolicy.LocalIntervalValid(seconds));

    [Theory]
    [InlineData("/api/tv-config", "GET")][InlineData("/api/tv-config", "PUT")]
    [InlineData("/api/tv-config/projects", "GET")][InlineData("/api/permissions/users", "GET")]
    [InlineData("/api/bi-chs001/ldp?projectId=P", "GET")][InlineData("/api/robot/run", "POST")]
    [InlineData("/api/reports/production/dashboard/ldp", "GET")][InlineData("/api/tv-portal/assignments", "PUT")]
    [InlineData("/api/tv-portal/session", "POST")]
    public async Task DirectApisAreDeniedEvenWithoutEnforcement(string path, string method)
    {
        await using var setup = await Setup.Create();
        var (status, passed) = await setup.Request(path, method);
        Assert.Equal(403, status); Assert.False(passed);
    }
    [Fact] public async Task HtmlDashboardRedirectsToIsolatedPortal()
    {
        await using var setup = await Setup.Create();
        var context = setup.Context("/dashboard", "GET"); context.Request.Headers.Accept = "text/html";
        await setup.Middleware.InvokeAsync(context, setup.Identity, setup.Config, setup.Assignments, setup.Repository);
        Assert.Equal(302, context.Response.StatusCode); Assert.Equal("/tv/iniciar", context.Response.Headers.Location);
    }
    [Theory][InlineData("wrong",0,"P")][InlineData("a",99,"P")][InlineData("a",0,"P")]
    public async Task SwappingPresentationSlideOrProjectIsDenied(string id,int index,string project)
    {
        await using var setup = await Setup.Create();
        Assert.Equal(403, (await setup.Request($"/api/tv-portal/data/{id}/{index}?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={project}","GET")).Status);
    }
    [Fact] public async Task ScopedReadOnlyAllowsSavedResourceAndProject()
    {
        await using var setup = await Setup.Create();
        var result = await setup.Request($"/api/tv-portal/data/a/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}","GET");
        Assert.Equal(200,result.Status); Assert.True(result.Passed);
        Assert.Equal("/api/bi-chs001/ldp",setup.RewrittenPath);
    }
    [Fact] public async Task UnassignedInactiveProjectAndInactivePresentationAreDenied()
    {
        await using var setup = await Setup.Create();
        var url=$"/api/tv-portal/data/a/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}";
        setup.ActiveProject=false; Assert.Equal(403,(await setup.Request(url,"GET")).Status);
        setup.ActiveProject=true;
        await setup.Assignments.SaveAsync(new(1,[]),default); Assert.Equal(403,(await setup.Request(url,"GET")).Status);
        await setup.Assignments.SaveAsync(new(2,[new("a",[42])]),default);
        var config=await setup.Config.ReadAsync(); await setup.Config.SaveAsync(config.Presentations.Select(p=>p with{Ativa=false}).ToArray(),config.Revision,null);
        Assert.Equal(403,(await setup.Request(url,"GET")).Status);
    }
    [Theory][InlineData("/api/robot/run")][InlineData("/api/bi-chs001/schedule/sincronizar")][InlineData("https://evil/")]
    public async Task GatewayNeverProxiesUnknownResource(string resource)
    {
        await using var setup=await Setup.Create();
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/0?resource={Uri.EscapeDataString(resource)}&projectId={TvTelas.ProjetoChs001}","GET")).Status);
    }
    [Fact] public async Task AssignmentStoreConflictsAndKeepsBackup()
    {
        await using var setup=await Setup.Create();
        await Assert.ThrowsAsync<TvConfigurationConflictException>(()=>setup.Assignments.SaveAsync(new(0,[]),default));
        await setup.Assignments.SaveAsync(new(1,[]),default);
        Assert.True(File.Exists(setup.Assignments.FilePath+".bak"));
    }
    [Fact] public async Task PortalListDefaultsToEmptyWithoutAssignment()
    {
        await using var setup=await Setup.Create();await setup.Assignments.SaveAsync(new(1,[]),default);
        var controller=new APS.WebApp.Controllers.TvPortalController(setup.Identity,setup.Config,setup.Assignments,setup.Repository);
        var response=Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Presentations(default));
        Assert.Empty(((System.Collections.IEnumerable)response.Value!).Cast<object>());
        var denied=Assert.IsType<Microsoft.AspNetCore.Mvc.StatusCodeResult>(await controller.Presentation("a",default));Assert.Equal(403,denied.StatusCode);
    }
    [Fact] public async Task PresentationMetadataRequiresActiveConfiguredProjectAndRejectsOtherId()
    {
        await using var setup=await Setup.Create();var controller=new APS.WebApp.Controllers.TvPortalController(setup.Identity,setup.Config,setup.Assignments,setup.Repository);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Presentation("a",default));
        Assert.Equal(403,Assert.IsType<Microsoft.AspNetCore.Mvc.StatusCodeResult>(await controller.Presentation("b",default)).StatusCode);
        setup.ActiveProject=false;Assert.Equal(403,Assert.IsType<Microsoft.AspNetCore.Mvc.StatusCodeResult>(await controller.Presentation("a",default)).StatusCode);
    }
    [Fact] public void DuplicateAndUnknownQueryParametersDenied()
    {
        Assert.False(TvPortalResources.ValidQuery(new QueryCollection(new Dictionary<string,StringValues>{{"projectId",new StringValues(new[]{"a","b"})}})));
        Assert.False(TvPortalResources.ValidQuery(new QueryCollection(new Dictionary<string,StringValues>{{"url","https://evil"}})));
    }
    [Fact] public void UnauditedSourcesStayUnavailableAndAllTenBiPagesHaveExplicitResources()
    {
        foreach(var id in new[]{"lista-documentos","controle-hh-custo","gestao-hh-custo","lancamentos-pessoas","lancamentos-disciplinas","lancamentos-documentos","gestao-a-vista"})
            Assert.Empty(TvPortalResources.ForScreen(id));
        var bi=TvTelas.Todas.Where(t=>t.Id.StartsWith("gestao-vista-tipo2-",StringComparison.Ordinal)).ToArray();Assert.Equal(10,bi.Length);
        foreach(var screen in bi)Assert.NotEmpty(TvPortalResources.ForScreen(screen.Id));
    }
    [Fact] public async Task ExpiredPortalSessionIs401()
    {
        await using var setup=await Setup.Create();var c=setup.Context("/api/tv-portal/data/a/0","GET");c.Session.Clear();
        await setup.Middleware.InvokeAsync(c,setup.Identity,setup.Config,setup.Assignments,setup.Repository);
        Assert.Equal(401,c.Response.StatusCode);Assert.False(setup.Passed);
    }
    [Fact] public async Task RealAdministratorRequestPassesWithoutTvScope()
    {
        await using var setup=await Setup.Create();((RepoProxy)setup.Repository).Handler=(m,a)=>Task.FromResult(Data(Tv,Admin));
        var passed=false;var middleware=new TvPortalMiddleware(c=>{passed=true;Assert.False(c.Items.ContainsKey(TvPortalPolicy.ScopeItem));return Task.CompletedTask;});
        await middleware.InvokeAsync(setup.Context("/api/tv-config","PUT"),setup.Identity,setup.Config,setup.Assignments,setup.Repository);Assert.True(passed);
    }
    [Fact] public async Task ScopeCannotAuthorizeAnotherProjectOrCapability()
    {
        var c=new DefaultHttpContext();c.Items[TvPortalPolicy.ScopeItem]=new TvReadScope(new HashSet<string>{"P"},new[]{"reports.gestao.view"});
        var http=new HttpContextAccessor{HttpContext=c};
        var inner=new PermissionService(null!,null!,null!,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),http,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PermissionService>.Instance);
        var service=new TvPortalPermissionService(inner,http);
        Assert.True(service.Enforced);
        Assert.Equal(PermissionCheck.Allowed,await service.CheckAsync(new[]{"reports.gestao.view"},"P",default));
        Assert.Equal(PermissionCheck.Denied,await service.CheckAsync(new[]{"reports.gestao.view"},"OTHER",default));
        Assert.Equal(PermissionCheck.Denied,await service.CheckAsync(new[]{"settings.tv.configure"},"P",default));
        Assert.Equal(PermissionCheck.Denied,await service.CheckProjectAccessAsync("OTHER",default));
    }
    [Fact] public async Task AdditionalProjectCannotBeInjected()
    {
        await using var setup=await Setup.Create();
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}&projetos=OTHER","GET")).Status);
    }
    [Fact] public async Task AssignedPresentationWorksWithoutUserProjectMembership()
    {
        await using var setup=await Setup.Create();
        var resource=$"/api/tv-portal/data/a/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}";
        // Fake Repository says user has ZERO membership in the project.
        Assert.Equal(200,(await setup.Request(resource,"GET")).Status);
        var controller=new APS.WebApp.Controllers.TvPortalController(setup.Identity,setup.Config,setup.Assignments,setup.Repository);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Presentation("a",default));
        // Direct API must remain inaccessible.
        Assert.Equal(403,(await setup.Request($"/api/bi-chs001/ldp?projectId={TvTelas.ProjetoChs001}","GET")).Status);
    }
    [Fact] public void ProjectSpecificTvDenyStillOverridesAssignedPresentation()
    {
        var denied=Data(Tv) with{Overrides=new[]{new PermissionOverride(1,"tv.view","DENY")}};
        Assert.False(TvPortalPolicy.CanReadAssignedSlideProject(denied,1));
        Assert.True(TvPortalPolicy.CanReadAssignedSlideProject(denied,2));
        Assert.False(TvPortalPolicy.CanReadAssignedSlideProject(Data(),2));
    }
    [Fact] public async Task ProjectDenyOverridesPresentationAssignment()
    {
        await using var setup=await Setup.Create();
        ((RepoProxy)setup.Repository).Handler=(m,a)=>m.Name switch{
            "CarregarDadosAsync"=>Task.FromResult(Data(Tv) with{Overrides=new[]{new PermissionOverride(1,"tv.view","DENY")}}),
            "ResolverProjetoIdAsync"=>Task.FromResult<int?>(1),
            "UsuarioPossuiAcessoAtivoProjetoAsync"=>Task.FromResult(true),_=>throw new NotImplementedException(m.Name)};
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}","GET")).Status);
    }

    // Programação Diária (19/10/2026): o slide salva projeto + data; o portal só lê a programação da semana dessa data,
    // do projeto do slide, e cada slide (mesma tela repetida) tem o seu escopo.
    private static async Task SlidesDiarios(Setup setup)
    {
        var config=await setup.Config.ReadAsync();
        await setup.Config.SaveAsync([new("a","TV",30,[
            new("gestao-a-vista-programacao-diaria","",TvTelas.ProjetoChs001,"CHS001",null,"2026-10-15"),
            new("gestao-a-vista-programacao-diaria","","b.33333333-3333-3333-3333-333333333333","BML011",null,"2026-10-21")])],config.Revision,null);
    }
    private const string ProgramacaoJamile="%2Fapi%2Freports%2Fgestao-vista-jamile%2Fprogramacao";
    [Fact] public async Task DailySlideReadsOnlyItsProjectAndWeek()
    {
        await using var setup=await Setup.Create(); await SlidesDiarios(setup);
        var ok=await setup.Request($"/api/tv-portal/data/a/0?resource={ProgramacaoJamile}&projectId={TvTelas.ProjetoChs001}&inicio=2026-10-11&fim=2026-10-17","GET");
        Assert.Equal(200,ok.Status); Assert.True(ok.Passed); Assert.Equal("/api/reports/gestao-vista-jamile/programacao",setup.RewrittenPath);
        // Outra semana no mesmo slide, projeto do OUTRO slide e troca de índice: negados.
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/0?resource={ProgramacaoJamile}&projectId={TvTelas.ProjetoChs001}&inicio=2026-10-18&fim=2026-10-24","GET")).Status);
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/0?resource={ProgramacaoJamile}&projectId=b.33333333-3333-3333-3333-333333333333&inicio=2026-10-11&fim=2026-10-17","GET")).Status);
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/1?resource={ProgramacaoJamile}&projectId={TvTelas.ProjetoChs001}&inicio=2026-10-18&fim=2026-10-24","GET")).Status);
        // Segundo slide (mesma tela, outro projeto/data): o seu próprio escopo.
        Assert.Equal(200,(await setup.Request($"/api/tv-portal/data/a/1?resource={ProgramacaoJamile}&projectId=b.33333333-3333-3333-3333-333333333333&inicio=2026-10-18&fim=2026-10-24","GET")).Status);
        // Recurso fora da lista da tela continua negado.
        Assert.Equal(403,(await setup.Request($"/api/tv-portal/data/a/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}","GET")).Status);
    }

    public class RepoProxy:DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler=null!;
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Handler(targetMethod!,args);
    }
    private class Setup:IAsyncDisposable
    {
        private readonly string directory=Path.Combine(Path.GetTempPath(),"tv-portal-test-"+Guid.NewGuid());
        public TvConfigurationStore Config=null!; public TvPortalAssignmentStore Assignments=null!;
        public IPermissionRepository Repository=null!; public TvPortalIdentityService Identity=null!;
        public TvPortalMiddleware Middleware=null!;public bool ActiveProject=true;public bool Passed;public string? RewrittenPath;
        public static async Task<Setup> Create()
        {
            var s=new Setup();Directory.CreateDirectory(s.directory);
            s.Config=new(Path.Combine(s.directory,"config.json"));s.Assignments=new(Path.Combine(s.directory,"assignments.json"));
            await s.Config.SaveAsync([new("a","TV",30,[new("gestao-vista-tipo2-resumo-30","",TvTelas.ProjetoChs001,null)])],0,null);
            await s.Assignments.SaveAsync(new(0,[new("a",[42])]),default);
            s.Repository=DispatchProxy.Create<IPermissionRepository,RepoProxy>();
            ((RepoProxy)s.Repository).Handler=(m,a)=>m.Name switch {
                "CarregarDadosAsync"=>Task.FromResult(Data(Tv)),
                "ResolverProjetoIdAsync"=>Task.FromResult<int?>(s.ActiveProject ? 1 : null),
                "UsuarioPossuiAcessoAtivoProjetoAsync"=>Task.FromResult(false),
                _=>throw new NotImplementedException(m.Name)};
            s.Identity=new(new Permissions(),s.Repository);
            s.Middleware=new(c=>{s.Passed=true;s.RewrittenPath=c.Request.Path;Assert.NotNull(c.Items[TvPortalPolicy.ScopeItem]);return Task.CompletedTask;});
            return s;
        }
        public DefaultHttpContext Context(string path,string method)
        {
            var c=new DefaultHttpContext();c.Session=new MemorySession();c.Session.SetInt32("Auth:UserId",42);
            var split=path.Split('?',2);c.Request.Path=split[0];if(split.Length>1)c.Request.QueryString=new("?"+split[1]);c.Request.Method=method;c.Response.Body=new MemoryStream();return c;
        }
        public async Task<(int Status,bool Passed)> Request(string path,string method)
        {Passed=false;var c=Context(path,method);await Middleware.InvokeAsync(c,Identity,Config,Assignments,Repository);Assert.False(c.Items.ContainsKey(TvPortalPolicy.ScopeItem));return(c.Response.StatusCode,Passed);}
        public ValueTask DisposeAsync(){Directory.Delete(directory,true);return ValueTask.CompletedTask;}
    }
    private class Permissions:IPermissionService
    {
        public bool Enforced=>false;
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct)=>Task.FromResult<CurrentPermissionUser?>(new(42,"","tv@test.invalid","TV",false));
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys,string? p,CancellationToken ct,bool anyProject=false)=>Task.FromResult(PermissionCheck.Allowed);
        public Task<EffectivePermissions> GetEffectiveAsync(string? p,CancellationToken ct)=>throw new NotImplementedException();
        public Task<int?> ResolverProjetoIdAsync(string? p,CancellationToken ct)=>throw new NotImplementedException();
    }
    private class MemorySession:ISession
    {
        private readonly Dictionary<string,byte[]> data=[];public bool IsAvailable=>true;public string Id=>"test";public IEnumerable<string> Keys=>data.Keys;
        public void Clear()=>data.Clear();public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;public Task LoadAsync(CancellationToken ct=default)=>Task.CompletedTask;
        public void Remove(string key)=>data.Remove(key);public void Set(string key,byte[] value)=>data[key]=value;
        public bool TryGetValue(string key,out byte[]? value)=>data.TryGetValue(key,out value);
    }
}
