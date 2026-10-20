using System.Net;
using APS.WebApp.Areas.Authentication.Controllers;
using APS.WebApp.Services;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services;

namespace NA_Connect.Planning.Tests.Permissions;

public sealed class NaSessionIdentityTests
{
    private static (DefaultHttpContext Context, SessionNaCurrentUserAccessor Accessor) Contexto()
    {
        var context = new DefaultHttpContext { Session = new MemorySessionPerm() };
        var http = new HttpContextAccessor { HttpContext = context };
        return (context, new SessionNaCurrentUserAccessor(http));
    }

    [Fact]
    public async Task Accessor_RetornaIdentidadeDaSessao_SemAutodesk()
    {
        var (context, accessor) = Contexto();
        context.Session.SetInt32(NaSessionKeys.UserId, 42);
        context.Session.SetString(NaSessionKeys.Email, "user@na.com");
        context.Session.SetString(NaSessionKeys.Nome, "Usuário N&A");
        context.Session.SetString(NaSessionKeys.Provider, "Autodesk");

        var user = await accessor.GetAsync();

        Assert.Equal(new NaCurrentUser(42, "user@na.com", "Usuário N&A", "Autodesk"), user);
    }

    [Fact]
    public async Task Accessor_SemUserId_RetornaNull()
    {
        var (_, accessor) = Contexto();
        Assert.Null(await accessor.GetAsync());
    }

    [Fact]
    public async Task Accessor_UserIdZeroOuBytesInvalidos_RetornaNull()
    {
        var (context, accessor) = Contexto();
        context.Session.SetInt32(NaSessionKeys.UserId, 0);
        Assert.Null(await accessor.GetAsync());

        context.Session.Set(NaSessionKeys.UserId, new byte[] { 1, 2, 3 });
        Assert.Null(await accessor.GetAsync());
    }

    [Fact]
    public async Task Accessor_UserIdSemNomeEmail_RetornaIdentidadePrevisivel()
    {
        var (context, accessor) = Contexto();
        context.Session.SetInt32(NaSessionKeys.UserId, 9);

        var user = await accessor.GetAsync();

        Assert.Equal(new NaCurrentUser(9, "", "", ""), user);
    }

    [Fact]
    public void CheckStatus_AceitaSessaoNovaEAntiga()
    {
        var (context, controller) = CriarController();
        context.Session.SetInt32(NaSessionKeys.UserId, 42);
        Assert.True(IsAuthenticated(controller.CheckStatus()));

        context.Session.Clear();
        context.Session.SetString("AccessToken", "legacy");
        Assert.True(IsAuthenticated(controller.CheckStatus()));

        context.Session.Clear();
        Assert.False(IsAuthenticated(controller.CheckStatus()));
    }

    [Fact]
    public void Logout_LimpaIdentidadeNovaETokensLegados()
    {
        var (context, controller) = CriarController();
        context.Session.SetInt32(NaSessionKeys.UserId, 42);
        context.Session.SetString("AccessToken", "legacy");
        context.Session.SetString("RefreshToken", "refresh");

        controller.Logout();

        Assert.False(context.Session.Keys.Any());
    }

    [Fact]
    public async Task GetUserName_UsaSessaoNaSemChamarAutodesk()
    {
        var (context, controller) = CriarController();
        context.Session.SetInt32(NaSessionKeys.UserId, 42);
        context.Session.SetString(NaSessionKeys.Email, "user@na.com");
        context.Session.SetString(NaSessionKeys.Nome, "Usuário N&A");

        var result = await controller.GetUserName(new FakeUserAccessor { Lanca = true },
            new SessionNaCurrentUserAccessor(new HttpContextAccessor { HttpContext = context }), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Usuário N&A", ok.Value!.GetType().GetProperty("userName")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task Callback_MantemTokensEGravaSessaoNa()
    {
        var context = new DefaultHttpContext { Session = new MemorySessionPerm() };
        var handler = new NoopHandler();
        var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APS:ClientId"] = "client",
            ["APS:ClientSecret"] = "secret",
            ["APS:CallbackUrl"] = "https://localhost/callback"
        }).Build();
        var autodesk = new AutodeskApiService(client,
            new TokenManager(client, configuration, new MemoryCache(new MemoryCacheOptions())),
            configuration, NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        var repository = new FakePermissionRepository { ProximoUserId = 73 };
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUserAccessor>(new FakeUserAccessor
                { Identidade = new AutodeskUserInfo("acc-73", "user@na.com", "Usuário N&A") })
            .AddSingleton<IPermissionRepository>(repository)
            .AddSingleton<APS.WebApp.Services.Permissions.Sync.PermissionLoginSync>()
            .BuildServiceProvider();
        context.RequestServices = services;
        var controller = new OAuthController(configuration, autodesk, client, repository,
            NullLogger<OAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = await controller.Callback("authorization-code");

        Assert.IsType<RedirectResult>(result);
        Assert.Equal("access-token", context.Session.GetString("AccessToken"));
        Assert.Equal("refresh-token", context.Session.GetString("RefreshToken"));
        Assert.Equal(73, context.Session.GetInt32(NaSessionKeys.UserId));
        Assert.Equal("user@na.com", context.Session.GetString(NaSessionKeys.Email));
        Assert.Equal("Autodesk", context.Session.GetString(NaSessionKeys.Provider));
    }

    private static bool IsAuthenticated(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var property = ok.Value!.GetType().GetProperty("isAuthenticated");
        return Assert.IsType<bool>(property!.GetValue(ok.Value));
    }

    private static (DefaultHttpContext Context, OAuthController Controller) CriarController()
    {
        var context = new DefaultHttpContext { Session = new MemorySessionPerm() };
        var handler = new NoopHandler();
        var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().Build();
        var autodesk = new AutodeskApiService(client,
            new TokenManager(client, configuration, new MemoryCache(new MemoryCacheOptions())),
            configuration, NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        var controller = new OAuthController(configuration, autodesk, client, new FakePermissionRepository(),
            NullLogger<OAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        return (context, controller);
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"access-token","refresh_token":"refresh-token"}""")
            });
    }
}
