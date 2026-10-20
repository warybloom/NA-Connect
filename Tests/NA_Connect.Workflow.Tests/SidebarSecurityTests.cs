using System.Reflection;
using System.Text.Json;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Sidebar;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Workflow.Tests;

public class SidebarSecurityTests
{
    [Fact]
    public void LayoutRejectsUnknownIdsDuplicatesCyclesAndTechnicalFields()
    {
        var rows = SidebarRules.Default.Items.ToArray();
        Assert.Equal(rows.Length, SidebarRules.Validate(rows).Count);
        var unknown = rows.ToArray(); unknown[0] = unknown[0] with { Id = "page:/arbitrary" };
        Assert.Throws<ArgumentException>(() => SidebarRules.Validate(unknown));
        Assert.Throws<ArgumentException>(() => SidebarRules.Validate(rows.Append(new SidebarItem("custom:" + Guid.NewGuid(), null, rows.Length, "Injected group", "Settings", false)).ToArray()));
        var duplicate = rows.ToArray(); duplicate[1] = duplicate[0];
        Assert.Throws<ArgumentException>(() => SidebarRules.Validate(duplicate));
        var cyclic = rows.ToArray(); cyclic[0] = cyclic[0] with { ParentId = cyclic[0].Id };
        Assert.Throws<ArgumentException>(() => SidebarRules.Validate(cyclic));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SidebarItem>(
            "{\"id\":\"page:/cde/documentos\",\"permissionKey\":\"permissions.manage\"}", SidebarRules.Json));
    }

    [Fact]
    public void FilterPreservesOnlyAllowedDescendantsAndTheirAncestors()
    {
        var rows = SidebarRules.Default.Items;
        var filtered = SidebarRules.Filter(rows, n => n.To == "/cde/documentos");
        Assert.Contains(filtered, n => n.Id == "page:/cde/documentos");
        Assert.DoesNotContain(filtered, n => n.Id == "page:/configuracoes/sidebar");
        Assert.All(filtered.Where(n => SidebarRules.ById[n.Id].Group), n => Assert.Contains(filtered, p => p.ParentId == n.Id));
        var hidden = rows.Select(n => n.Id == "page:/cde/documentos" ? n with { Hidden = true } : n).ToArray();
        Assert.Empty(SidebarRules.Filter(hidden, n => n.To == "/cde/documentos"));
    }

    [Fact]
    public async Task EnforceFalseDoesNotGrantEditingAndAnonymousCannotRead()
    {
        var (controller, store, repo, _) = Setup(false);
        var denied = Assert.IsType<ObjectResult>(await controller.Publish(new(0, SidebarRules.Default.Items), default));
        Assert.Equal(403, denied.StatusCode); Assert.Equal(0, store.Writes);
        repo.Data = Granted("permissions.manage", 99); // project-only grant cannot administer global sidebar
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Admin(default)).StatusCode);
        var (anonymous, _, _, _) = Setup(false, anonymous: true);
        Assert.IsType<UnauthorizedResult>(await anonymous.Get(null, default));
    }

    [Fact]
    public async Task AuthorizedWriteRequiresAntiforgeryAndRevisionMatches()
    {
        var (controller, store, _, csrf) = Setup(true);
        csrf.Valid = false;
        Assert.IsType<BadRequestObjectResult>(await controller.Publish(new(0, SidebarRules.Default.Items), default));
        Assert.Equal(0, store.Writes);
        csrf.Valid = true;
        Assert.IsType<OkObjectResult>(await controller.Publish(new(0, SidebarRules.Default.Items), default));
        Assert.Equal(1, store.Writes);
        Assert.IsType<ConflictObjectResult>(await controller.Publish(new(0, SidebarRules.Default.Items), default));
    }

    [Fact]
    public async Task SessionGetDoesNotExposeRestrictedItemsAndRestoreAppendsRevision()
    {
        var (controller, store, repo, _) = Setup(false);
        repo.Data = Granted("cde.docs.view");
        var config = Assert.IsType<SidebarConfiguration>(Assert.IsType<OkObjectResult>(await controller.Get(null, default)).Value);
        Assert.Contains(config.Items, n => n.Id == "page:/cde/documentos");
        Assert.DoesNotContain(config.Items, n => n.Id == "page:/configuracoes/sidebar");
        repo.Data = Granted("permissions.manage");
        Assert.IsType<OkObjectResult>(await controller.Restore(new(0, 0), default));
        Assert.Equal(1, store.Current.Revision); Assert.Equal(0, store.RestoredFrom);
    }

    [Fact]
    public async Task GlobalDenyOverridesGrantAndConfiguredMasterBypassesRepository()
    {
        var (controller, store, repo, _) = Setup(true);
        repo.Data = repo.Data with { Overrides = [new PermissionOverride(null, "permissions.manage", PermissionEffects.Deny)] };
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Publish(new(0, SidebarRules.Default.Items), default)).StatusCode);
        Assert.Equal(0, store.Writes);
        var (master, _, _, _) = Setup(false, master: true);
        Assert.IsType<OkObjectResult>(await master.Admin(default));
        Assert.IsType<OkObjectResult>(await master.Get("unregistered-project", default));
    }

    [Fact]
    public async Task AccessIsGlobalAndDistinguishesSessionPermissionAndDatabase()
    {
        static bool Allowed(IActionResult r) { var ok = Assert.IsType<OkObjectResult>(r); return (bool)ok.Value!.GetType().GetProperty("allowed")!.GetValue(ok.Value)!; }
        var (master, _, _, _) = Setup(false, master: true);
        Assert.True(Allowed(await master.GetAccess(default)));          // MasterAdmin, sem projeto
        var (global, _, _, _) = Setup(true);
        Assert.True(Allowed(await global.GetAccess(default)));          // permissions.manage global
        var (scoped, _, repo, _) = Setup(false);
        Assert.False(Allowed(await scoped.GetAccess(default)));         // usuário comum
        repo.Data = Granted("permissions.manage", 244);
        Assert.False(Allowed(await scoped.GetAccess(default)));         // concessão só de projeto não amplia (piloto)
        var (anonymous, _, _, _) = Setup(false, anonymous: true);
        Assert.IsType<UnauthorizedResult>(await anonymous.GetAccess(default));
        repo.Fail = true;
        Assert.Equal(503, Assert.IsType<ObjectResult>(await scoped.GetAccess(default)).StatusCode); // banco ≠ sem permissão
    }

    private static UserPermissionData Granted(string key, int? project = null) => new(false,
        [new ProfileAssignment(1, "Test", project, false, new HashSet<string> { key })], [], new HashSet<int>());

    private static (SidebarController, MemoryStore, RepoProxy, Csrf) Setup(bool allowed, bool anonymous = false, bool master = false)
    {
        var repository = DispatchProxy.Create<IPermissionRepository, RepoProxy>();
        var repo = (RepoProxy)(object)repository;
        repo.Data = allowed ? Granted("permissions.manage") : new(false, [], [], new HashSet<int>());
        var store = new MemoryStore(); var csrf = new Csrf();
        var controller = new SidebarController(store, new SafeModePermissions(anonymous, master), repository, csrf, NullLogger<SidebarController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (controller, store, repo, csrf);
    }

    public class RepoProxy : DispatchProxy
    {
        public UserPermissionData Data = new(false, [], [], new HashSet<int>());
        public bool Fail;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch {
            "CarregarDadosAsync" => Fail ? Task.FromException<UserPermissionData>(new InvalidOperationException("db")) : Task.FromResult(Data),
            _ => throw new NotSupportedException(method?.Name)
        };
    }
    private class SafeModePermissions(bool anonymous, bool master) : IPermissionService
    {
        public bool Enforced => false;
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => Task.FromResult<CurrentPermissionUser?>(anonymous ? null : new(1, "", "test@example.test", "Test", master));
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys, string? project, CancellationToken ct, bool anyProject = false) => Task.FromResult(PermissionCheck.Allowed);
        public Task<EffectivePermissions> GetEffectiveAsync(string? project, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? project, CancellationToken ct) => throw new NotSupportedException();
    }
    private class MemoryStore : ISidebarStore
    {
        public SidebarConfiguration Current = SidebarRules.Default; public int Writes; public long? RestoredFrom;
        public Task<SidebarConfiguration> ReadAsync(CancellationToken ct) => Task.FromResult(Current);
        public Task<IReadOnlyList<SidebarHistory>> HistoryAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SidebarHistory>>([]);
        public Task<SidebarConfiguration?> RevisionAsync(long revision, CancellationToken ct) => Task.FromResult<SidebarConfiguration?>(revision == 0 ? SidebarRules.Default : null);
        public Task<SidebarConfiguration> PublishAsync(IReadOnlyList<SidebarItem> items, long expected, string author, long? restoredFrom, CancellationToken ct) {
            if (Current.Revision != expected) throw new SidebarConflictException();
            Writes++; RestoredFrom = restoredFrom; return Task.FromResult(Current = new(expected + 1, items));
        }
    }
    private class Csrf : IAntiforgery
    {
        public bool Valid = true;
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => new("token", "cookie", "form", "X-Sidebar-CSRF");
        public AntiforgeryTokenSet GetTokens(HttpContext context) => GetAndStoreTokens(context);
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(Valid);
        public Task ValidateRequestAsync(HttpContext context) => Valid ? Task.CompletedTask : Task.FromException(new AntiforgeryValidationException("Invalid"));
        public void SetCookieTokenAndHeader(HttpContext context) { }
    }
}
