using System.Reflection;
using APS.WebApp.Areas.Authentication.Controllers;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public class TvLocalLoginTests
{
    [Fact]
    public async Task ValidLocalPasswordCreatesSessionAndResolvesRestrictedTvWithoutAutodesk()
    {
        // In-memory fixture only: no real credential, account, token or database mutation.
        var user = new PermUser(42, null, "fixture@example.invalid", "TV fixture", true, null);
        var password = Guid.NewGuid().ToString("N");
        var hasher = new PasswordHasher<PermUser>();
        var credential = new NaLocalCredential(42, hasher.HashPassword(user, password), 0, null, DateTime.UtcNow, false);
        var successes = 0;
        var credentials = DispatchProxy.Create<INaLocalCredentialRepository, TvPortalTests.RepoProxy>();
        ((TvPortalTests.RepoProxy)credentials).Handler = (m, a) => m.Name switch {
            "ObterUsuarioPorEmailAsync" => Task.FromResult<PermUser?>(user),
            "ObterCredencialAsync" => Task.FromResult<NaLocalCredential?>(credential),
            "RegistrarSucessoAsync" => Success(),
            _ => throw new InvalidOperationException(m.Name)
        };
        Task Success() { successes++; return Task.CompletedTask; }
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Session = new MemorySession();
        http.HttpContext.Session.SetString("AccessToken", "obsolete-fixture");
        var auth = new NaLocalAuthService(credentials, NullLogger<NaLocalAuthService>.Instance, hasher);
        var controller = new NaAuthController(auth, NullLogger<NaAuthController>.Instance) {
            ControllerContext = new ControllerContext { HttpContext = http.HttpContext }
        };
        var response = Assert.IsType<OkObjectResult>(await controller.Login(new(user.Email, password), default));
        Assert.False(Assert.IsType<NaLoginResponse>(response.Value).MustChangePassword);
        Assert.Equal(1, successes);
        Assert.Equal(42, http.HttpContext.Session.GetInt32(NaSessionKeys.UserId));
        Assert.Null(http.HttpContext.Session.GetString("AccessToken"));
        var accessor = new SessionNaCurrentUserAccessor(http);
        Assert.Equal(42, (await accessor.GetAsync())!.UserId);
        var repository = DispatchProxy.Create<IPermissionRepository, TvPortalTests.RepoProxy>();
        ((TvPortalTests.RepoProxy)repository).Handler = (m, a) => m.Name switch {
            "ObterUsuarioAsync" => Task.FromResult<PermUser?>(user),
            "CarregarDadosAsync" => Task.FromResult(new UserPermissionData(false,
                new[] { new ProfileAssignment(7, "TV", null, false, new HashSet<string> { "tv.view" }) },
                [], new HashSet<int>())),
            _ => throw new InvalidOperationException(m.Name)
        };
        var permissions = new PermissionService(repository, accessor, null!, new ConfigurationBuilder().Build(),
            http, NullLogger<PermissionService>.Instance);
        Assert.False(permissions.Enforced);
        var identity = await new TvPortalIdentityService(permissions, repository).ReadAsync(default);
        Assert.NotNull(identity);
        Assert.True(identity.Restricted);
        Assert.True(identity.CanView);
        Assert.False(identity.CanConfigure);
    }

    private sealed class MemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> data = [];
        public bool IsAvailable => true;
        public string Id => "isolated-fixture";
        public IEnumerable<string> Keys => data.Keys;
        public void Clear() => data.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => data.Remove(key);
        public void Set(string key, byte[] value) => data[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => data.TryGetValue(key, out value);
    }
}
