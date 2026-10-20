using System.Net;
using System.Text.Json;
using APS.WebApp.Controllers;
using APS.WebApp.Services;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services;

namespace NA_Connect.Planning.Tests.Permissions;

/// <summary>Identidade Autodesk via UserInfo OIDC (substitui o /userprofile/v1/users/@me, que responde 410 Gone).</summary>
public class AutodeskUserIdentityTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Endpoint_EhOUserInfoAtual()
    {
        Assert.Equal("https://api.userprofile.autodesk.com/userinfo", AutodeskUserInfoParser.Endpoint);
        Assert.DoesNotContain("userprofile/v1/users/@me", AutodeskUserInfoParser.Endpoint);
    }

    [Fact]
    public void Parse_MapeiaCamposOidc_SubEmailName()
    {
        var u = AutodeskUserInfoParser.Parse(Json("""{"sub":"ABC123","email":"Fulano@NA.com","name":"Fulano de Tal","email_verified":true}"""));
        Assert.Equal(new AutodeskUserInfo("ABC123", "Fulano@NA.com", "Fulano de Tal"), u);
    }

    [Fact]
    public void Parse_SemName_UsaGivenFamily_ENaoUsaCamposDoEndpointAntigo()
    {
        var u = AutodeskUserInfoParser.Parse(Json("""{"sub":"X","email":"a@b.com","given_name":"Ana","family_name":"Silva","userId":"OLD","emailId":"old@x.com","firstName":"Old"}"""));
        Assert.Equal("X", u.AutodeskUserId);
        Assert.Equal("a@b.com", u.Email);
        Assert.Equal("Ana Silva", u.Nome);
    }

    [Fact]
    public void Parse_CamposAusentes_FicamVazios_NaoInventa()
    {
        Assert.Equal(new AutodeskUserInfo("", "", ""), AutodeskUserInfoParser.Parse(Json("""{"userId":"OLD","emailId":"old@x.com"}""")));
    }

    // --------------------------------------------------------- SessionCurrentUserAccessor ----
    private sealed class Handler(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        public int Chamadas { get; private set; }
        public List<Uri?> Urls { get; } = new();
        public List<string?> Bearers { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Chamadas++; Urls.Add(request.RequestUri); Bearers.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo) });
        }
    }

    private static (SessionCurrentUserAccessor A, Handler H, DefaultHttpContext Ctx) Acessor(HttpStatusCode status, string corpo, string? token = "tok-1")
    {
        var h = new Handler(status, corpo);
        var http = new HttpClient(h);
        var cfg = new ConfigurationBuilder().Build();
        var api = new AutodeskApiService(http, new TokenManager(http, cfg, new MemoryCache(new MemoryCacheOptions())), cfg,
            NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        var ctx = new DefaultHttpContext { Session = new MemorySessionPerm() };
        if (token != null) ctx.Session.SetString("AccessToken", token);
        return (new SessionCurrentUserAccessor(new HttpContextAccessor { HttpContext = ctx }, api, NullLogger<SessionCurrentUserAccessor>.Instance), h, ctx);
    }

    [Fact]
    public async Task Acessor_ChamaUserInfo_ComBearerDaSessao_ECacheiaPorToken()
    {
        var (a, h, ctx) = Acessor(HttpStatusCode.OK, """{"sub":"S1","email":"u@na.com","name":"U"}""");
        Assert.Equal(new AutodeskUserInfo("S1", "u@na.com", "U"), await a.GetAsync(default));
        Assert.Equal(new AutodeskUserInfo("S1", "u@na.com", "U"), await a.GetAsync(default));
        Assert.Equal(1, h.Chamadas); // 2ª chamada veio do cache da sessão
        Assert.Equal(AutodeskUserInfoParser.Endpoint, h.Urls[0]!.ToString());
        Assert.Equal("tok-1", h.Bearers[0]);

        ctx.Session.SetString("AccessToken", "tok-2"); // novo login → nova consulta
        await a.GetAsync(default);
        Assert.Equal(2, h.Chamadas);
    }

    [Fact]
    public async Task Acessor_SemSessao_Null_SemChamarAutodesk()
    {
        var (a, h, _) = Acessor(HttpStatusCode.OK, "{}", token: null);
        Assert.Null(await a.GetAsync(default));
        Assert.Equal(0, h.Chamadas);
    }

    [Fact]
    public async Task Acessor_TokenSemScope_403_ExigeNovoLogin_ENaoRepeteAChamada()
    {
        var (a, h, _) = Acessor(HttpStatusCode.Forbidden, """{"error":"insufficient_scope"}""");
        var ex = await Assert.ThrowsAsync<AutodeskUserInfoException>(() => a.GetAsync(default));
        Assert.True(ex.ExigeNovoLogin);
        await Assert.ThrowsAsync<AutodeskUserInfoException>(() => a.GetAsync(default));
        Assert.Equal(1, h.Chamadas);
    }

    [Fact]
    public async Task Acessor_ErroTransitorio_NaoMarcaComoNovoLogin()
    {
        var (a, h, _) = Acessor(HttpStatusCode.BadGateway, "");
        var ex = await Assert.ThrowsAsync<AutodeskUserInfoException>(() => a.GetAsync(default));
        Assert.False(ex.ExigeNovoLogin);
        await Assert.ThrowsAsync<AutodeskUserInfoException>(() => a.GetAsync(default));
        Assert.Equal(2, h.Chamadas); // tenta de novo na próxima requisição
    }

    // ------------------------------------------------------------ administração sem scope ----
    [Fact]
    public async Task Administracao_TokenSemScope_401ComMensagemDeNovoLogin_NaoIndisponivel()
    {
        var ctx = new DefaultHttpContext { Session = new MemorySessionPerm() };
        ctx.Session.SetString("AccessToken", "tok");
        var repo = new FakePermissionRepository();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Permissions:Enforce"] = "false" }).Build();
        var s = new PermissionService(repo, new FakeNaCurrentUserAccessor(), new FakeUserAccessor { SemScope = true }, cfg, new HttpContextAccessor { HttpContext = ctx },
            NullLogger<PermissionService>.Instance);
        var c = new PermissionsController(s, repo, NullLogger<PermissionsController>.Instance) { ControllerContext = new ControllerContext { HttpContext = ctx } };

        var res = Assert.IsType<ObjectResult>(await c.Perfis(default));
        Assert.Equal(401, res.StatusCode);
        Assert.Contains("sair e entrar UMA vez", JsonSerializer.Serialize(res.Value));

        // /me continua funcionando em safe mode (Enforce=false) mesmo sem identidade.
        Assert.IsType<OkObjectResult>(await c.Me(null, default));
    }
}
