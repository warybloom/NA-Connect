using System.Net;
using APS.WebApp.Areas.Authentication.Controllers;
using APS.WebApp.Services;
using APS.WebApp.Services.AutodeskAuth;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Planning.Tests.Permissions;
using NA_Connect.Services;

namespace NA_Connect.Planning.Tests.AutodeskAuth;

/// <summary>ETAPA 07A — IAutodeskUserTokenProvider (token 3-legged do usuário, refresh e single-flight).</summary>
public sealed class AutodeskUserTokenProviderTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    // ---------- A / B / C ----------

    [Fact]
    public async Task A_TokenValidoComExpiracaoDistante_RetornaSemHttp()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(50));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.True(r.IsValid);
        Assert.Equal("access-1", r.AccessToken);
        Assert.Equal(0, t.Handler.Chamadas);
    }

    [Fact]
    public async Task B_TokenProximoDaExpiracao_ExecutaRefresh()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.Add(AutodeskUserTokenPolicy.RefreshMargin).AddSeconds(-1));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(1, t.Handler.Chamadas);
        Assert.Equal("access-2", r.AccessToken);
        var corpo = t.Handler.Corpos.Single();
        Assert.Contains("grant_type=refresh_token", corpo);
        Assert.Contains("refresh_token=refresh-1", corpo);
        Assert.Equal("Basic", t.Handler.Autorizacoes.Single()?.Scheme);
        Assert.Equal(AutodeskUserTokenPolicy.TokenEndpoint, t.Handler.Urls.Single());
    }

    [Fact]
    public async Task C_TokenExpiradoComRefresh_ExecutaRefresh()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-10));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(1, t.Handler.Chamadas);
        Assert.True(r.IsValid);
        Assert.Equal("access-2", r.AccessToken);
    }

    [Fact]
    public async Task C2_AccessTokenAusenteComRefresh_ExecutaRefresh()
    {
        var t = new Cenario();
        t.Session.SetString(AutodeskSessionKeys.RefreshToken, "refresh-1");
        t.Session.SetString(AutodeskSessionKeys.Connected, "true");

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(1, t.Handler.Chamadas);
        Assert.Equal("access-2", r.AccessToken);
    }

    // ---------- D / E / F / G ----------

    [Fact]
    public async Task D_NovoAccessToken_GravadoNoNamespaceENaChaveLegada()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal("access-2", t.Session.GetString(AutodeskSessionKeys.AccessToken));
        Assert.Equal("access-2", t.Session.GetString("AccessToken"));
        Assert.Equal("true", t.Session.GetString(AutodeskSessionKeys.Connected));
    }

    [Fact]
    public async Task E_RefreshTokenRotacionado_GravadoNoNamespaceENaChaveLegada()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal("refresh-2", t.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Equal("refresh-2", t.Session.GetString("RefreshToken"));
    }

    [Fact]
    public async Task F_ExpiresInNovo_PersisteNovoExpiresAtUtc()
    {
        var t = new Cenario();
        t.Handler.ExpiresIn = 1800;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        var r = await t.Provider.GetValidAccessTokenAsync();

        var gravado = DateTimeOffset.Parse(t.Session.GetString(AutodeskSessionKeys.ExpiresAt)!);
        Assert.Equal(Agora.AddSeconds(1800), gravado);
        Assert.Equal(TimeSpan.Zero, gravado.Offset);
        Assert.Equal(Agora.AddSeconds(1800), r.ExpiresAt);
    }

    [Fact]
    public async Task G_RespostaSemRefreshToken_PreservaORefreshAnterior()
    {
        var t = new Cenario();
        t.Handler.RefreshTokenNaResposta = null;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal("access-2", r.AccessToken);
        Assert.Equal("refresh-1", t.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Equal("refresh-1", t.Session.GetString("RefreshToken"));
    }

    [Fact]
    public async Task G2_RespostaComRefreshTokenVazio_NaoSobrescreveTokenValido()
    {
        var t = new Cenario();
        t.Handler.RefreshTokenNaResposta = "";
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal("refresh-1", t.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Equal("refresh-1", t.Session.GetString("RefreshToken"));
    }

    // ---------- H / I / J ----------

    [Fact]
    public async Task H_SemRefreshTokenETokenVencido_ReconnectRequiredSemHttp()
    {
        var t = new Cenario();
        t.SessaoNa();
        t.Session.SetString(AutodeskSessionKeys.AccessToken, "access-1");
        t.Session.SetString(AutodeskSessionKeys.ExpiresAt, Agora.AddMinutes(-1).ToString("o"));
        t.Session.SetString(AutodeskSessionKeys.Connected, "true");

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(AutodeskUserTokenStatus.ReconnectRequired, r.Status);
        Assert.True(r.RequiresReconnect);
        Assert.Equal("false", t.Session.GetString(AutodeskSessionKeys.Connected));
        Assert.Equal(0, t.Handler.Chamadas);
        t.AssertSessaoNaIntacta();
    }

    [Fact]
    public async Task H2_SemNenhumTokenAutodesk_NotConnectedSemCriarChaves()
    {
        var t = new Cenario();
        t.SessaoNa();

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(AutodeskUserTokenStatus.NotConnected, r.Status);
        Assert.True(r.RequiresReconnect);
        Assert.DoesNotContain(t.Session.Keys, k => k.StartsWith("Autodesk:", StringComparison.Ordinal));
        Assert.Equal(0, t.Handler.Chamadas);
        t.AssertSessaoNaIntacta();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task I_RefreshInvalidoOuRevogado_DesconectaAutodeskEPreservaSessaoNa(HttpStatusCode status)
    {
        var t = new Cenario();
        t.SessaoNa();
        t.Handler.Status = status;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(AutodeskUserTokenStatus.ReconnectRequired, r.Status);
        Assert.Equal("false", t.Session.GetString(AutodeskSessionKeys.Connected));
        Assert.Null(t.Session.GetString(AutodeskSessionKeys.AccessToken));
        Assert.Null(t.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Null(t.Session.GetString(AutodeskSessionKeys.ExpiresAt));
        Assert.Null(t.Session.GetString("RefreshToken"));
        t.AssertSessaoNaIntacta();

        // Desconectada: próximas chamadas não tentam de novo nem caem no AccessToken legado.
        var r2 = await t.Provider.GetValidAccessTokenAsync();
        Assert.Equal(AutodeskUserTokenStatus.ReconnectRequired, r2.Status);
        Assert.Equal(1, t.Handler.Chamadas);
    }

    [Fact]
    public async Task J_FalhaHttpAutodesk_IntegracaoMantidaESessaoNaPreservada()
    {
        var t = new Cenario();
        t.SessaoNa();
        t.Handler.Status = HttpStatusCode.ServiceUnavailable;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(AutodeskUserTokenStatus.Unavailable, r.Status);
        Assert.False(r.RequiresReconnect);
        Assert.Equal("true", t.Session.GetString(AutodeskSessionKeys.Connected));
        Assert.Equal("refresh-1", t.Session.GetString(AutodeskSessionKeys.RefreshToken));
        t.AssertSessaoNaIntacta();
    }

    [Fact]
    public async Task J2_ExcecaoDeRede_IntegracaoMantidaESessaoNaPreservada()
    {
        var t = new Cenario();
        t.SessaoNa();
        t.Handler.Lanca = true;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(AutodeskUserTokenStatus.Unavailable, r.Status);
        Assert.Equal("refresh-1", t.Session.GetString(AutodeskSessionKeys.RefreshToken));
        t.AssertSessaoNaIntacta();
    }

    [Fact]
    public async Task J3_FalhaTransitoriaComTokenAindaNaoVencido_DevolveTokenAtual()
    {
        var t = new Cenario();
        t.Handler.Status = HttpStatusCode.InternalServerError;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(2)); // dentro da margem, mas ainda válido

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.True(r.IsValid);
        Assert.Equal("access-1", r.AccessToken);
        Assert.Equal(1, t.Handler.Chamadas);
    }

    // ---------- K / L ----------

    [Fact]
    public async Task K_DuasRequisicoesSimultaneas_UmUnicoRefreshHttp()
    {
        // Duas requisições da MESMA sessão com snapshots independentes (como a DistributedSession carrega por request).
        var handler = new TokenHandler { Atraso = TimeSpan.FromMilliseconds(150) };
        var coordinator = new AutodeskUserTokenRefreshCoordinator(new MemoryCache(new MemoryCacheOptions()));
        var a = new Cenario(handler, coordinator, sessionId: "sessao-k");
        var b = new Cenario(handler, coordinator, sessionId: "sessao-k");
        a.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));
        b.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        var resultados = await Task.WhenAll(
            Task.Run(() => a.Provider.GetValidAccessTokenAsync()),
            Task.Run(() => b.Provider.GetValidAccessTokenAsync()));

        Assert.Equal(1, handler.Chamadas);
        Assert.All(resultados, r => Assert.Equal("access-2", r.AccessToken));
        Assert.Equal("refresh-2", a.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Equal("refresh-2", b.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Equal(0, coordinator.ActiveGates);
    }

    [Fact]
    public async Task K2_SessoesDiferentes_NaoCompartilhamResultado()
    {
        var handler = new TokenHandler();
        var coordinator = new AutodeskUserTokenRefreshCoordinator(new MemoryCache(new MemoryCacheOptions()));
        var a = new Cenario(handler, coordinator, sessionId: "sessao-a");
        var b = new Cenario(handler, coordinator, sessionId: "sessao-b");
        a.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));
        b.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await a.Provider.GetValidAccessTokenAsync();
        await b.Provider.GetValidAccessTokenAsync();

        Assert.Equal(2, handler.Chamadas);
    }

    [Fact]
    public async Task L_RequisicaoComSnapshotAntigo_AdotaTokenRenovadoSemNovoRefresh()
    {
        var handler = new TokenHandler();
        var coordinator = new AutodeskUserTokenRefreshCoordinator(new MemoryCache(new MemoryCacheOptions()));
        var a = new Cenario(handler, coordinator, sessionId: "sessao-l");
        var b = new Cenario(handler, coordinator, sessionId: "sessao-l");
        a.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));
        b.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1)); // snapshot anterior ao commit de A

        await a.Provider.GetValidAccessTokenAsync();
        var rb = await b.Provider.GetValidAccessTokenAsync();

        Assert.Equal(1, handler.Chamadas);
        Assert.Equal("access-2", rb.AccessToken);
        Assert.Equal("refresh-2", b.Session.GetString("RefreshToken"));
    }

    [Fact]
    public async Task L2_MesmaSessaoAposRefresh_NaoRenovaDeNovo()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await t.Provider.GetValidAccessTokenAsync();
        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(1, t.Handler.Chamadas);
        Assert.Equal("access-2", r.AccessToken);
    }

    [Fact]
    public async Task L3_RefreshPreservaCacheDeUserInfo_SemNovaChamadaUserInfo()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));
        var accessor = t.UserInfoAccessor();
        t.Handler.UserInfoJson = """{"sub":"acc-1","email":"user@na.com","name":"Usuário"}""";
        Assert.Equal("acc-1", (await accessor.GetAsync(default))!.AutodeskUserId);
        var antes = t.Handler.Chamadas;

        await t.Provider.GetValidAccessTokenAsync();
        t.Handler.UserInfoJson = """{"sub":"OUTRO","email":"x@x.com","name":"X"}""";
        var depois = await accessor.GetAsync(default);

        Assert.Equal("acc-1", depois!.AutodeskUserId);
        Assert.Equal(antes + 1, t.Handler.Chamadas); // só o refresh; nenhum UserInfo novo
    }

    // ---------- M ----------

    [Fact]
    public async Task M_ForceRefresh_ForcaExatamenteUmaRenovacao()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(50)); // válido por relógio

        var r = await t.Provider.ForceRefreshAsync("access-1");
        // Segunda requisição que recebeu 401 com o MESMO token antigo: já substituído, não renova de novo.
        var r2 = await t.Provider.ForceRefreshAsync("access-1");

        Assert.Equal(1, t.Handler.Chamadas);
        Assert.Equal("access-2", r.AccessToken);
        Assert.Equal("access-2", r2.AccessToken);
    }

    [Fact]
    public async Task M2_ForceRefreshConcorrenteComMesmoTokenRecusado_UmUnicoHttp()
    {
        var handler = new TokenHandler { Atraso = TimeSpan.FromMilliseconds(150) };
        var coordinator = new AutodeskUserTokenRefreshCoordinator(new MemoryCache(new MemoryCacheOptions()));
        var a = new Cenario(handler, coordinator, sessionId: "sessao-m");
        var b = new Cenario(handler, coordinator, sessionId: "sessao-m");
        a.Conectar("access-1", "refresh-1", Agora.AddMinutes(50));
        b.Conectar("access-1", "refresh-1", Agora.AddMinutes(50));

        var rs = await Task.WhenAll(
            Task.Run(() => a.Provider.ForceRefreshAsync("access-1")),
            Task.Run(() => b.Provider.ForceRefreshAsync("access-1")));

        Assert.Equal(1, handler.Chamadas);
        Assert.All(rs, r => Assert.Equal("access-2", r.AccessToken));
    }

    [Fact]
    public async Task M3_ForceRefreshRecusado_ReconnectRequiredSemLogoutNa()
    {
        var t = new Cenario();
        t.SessaoNa();
        t.Handler.Status = HttpStatusCode.BadRequest;
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(50));

        var r = await t.Provider.ForceRefreshAsync("access-1");

        Assert.Equal(AutodeskUserTokenStatus.ReconnectRequired, r.Status);
        t.AssertSessaoNaIntacta();
    }

    // ---------- Compatibilidade legada ----------

    [Fact]
    public async Task Legado_SessaoAnteriorA07A_UsaChavesLegadasSemHttp()
    {
        var t = new Cenario();
        t.Session.SetString("AccessToken", "legacy-access");
        t.Session.SetString("RefreshToken", "legacy-refresh");

        var r = await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal("legacy-access", r.AccessToken);
        Assert.Equal(0, t.Handler.Chamadas);

        // Recusa (401 futuro) usa o refresh legado e passa a gravar o namespace.
        var f = await t.Provider.ForceRefreshAsync("legacy-access");
        Assert.Equal("access-2", f.AccessToken);
        Assert.Contains("refresh_token=legacy-refresh", t.Handler.Corpos.Single());
        Assert.Equal("access-2", t.Session.GetString(AutodeskSessionKeys.AccessToken));
    }

    [Fact]
    public async Task SemHttpContext_NotConnected()
    {
        var handler = new TokenHandler();
        var provider = new AutodeskUserTokenProvider(new EmptyAccessor(), new FakeFactory(handler), Cenario.Config(),
            new AutodeskUserTokenRefreshCoordinator(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<AutodeskUserTokenProvider>.Instance, new FixedTime(Agora));

        Assert.Equal(AutodeskUserTokenStatus.NotConnected, (await provider.GetValidAccessTokenAsync()).Status);
        Assert.Equal(0, handler.Chamadas);
    }

    // ---------- N ----------

    [Fact]
    public async Task N_CallbackAuthorizationCode_MantemDualWriteEGravaNamespaceEExpiresAt()
    {
        var context = new DefaultHttpContext { Session = new MemorySessionPerm() };
        var handler = new TokenHandler { AccessTokenNaResposta = "access-cb", RefreshTokenNaResposta = "refresh-cb", ExpiresIn = 3599 };
        var client = new HttpClient(handler);
        var configuration = Cenario.Config();
        var autodesk = new AutodeskApiService(client,
            new TokenManager(client, configuration, new MemoryCache(new MemoryCacheOptions())),
            configuration, NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        var repository = new FakePermissionRepository { ProximoUserId = 73 };
        context.RequestServices = new ServiceCollection()
            .AddSingleton<ICurrentUserAccessor>(new FakeUserAccessor { Identidade = new AutodeskUserInfo("acc-73", "user@na.com", "Usuário N&A") })
            .AddSingleton<IPermissionRepository>(repository)
            .AddSingleton<APS.WebApp.Services.Permissions.Sync.PermissionLoginSync>()
            .BuildServiceProvider();
        var controller = new OAuthController(configuration, autodesk, client, repository, NullLogger<OAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var antes = DateTimeOffset.UtcNow;
        var result = await controller.Callback("authorization-code");
        var depois = DateTimeOffset.UtcNow;

        Assert.IsType<RedirectResult>(result);
        Assert.Contains("grant_type=authorization_code", handler.Corpos.First());
        // Legado
        Assert.Equal("access-cb", context.Session.GetString("AccessToken"));
        Assert.Equal("refresh-cb", context.Session.GetString("RefreshToken"));
        // Namespace Autodesk
        Assert.Equal("access-cb", context.Session.GetString(AutodeskSessionKeys.AccessToken));
        Assert.Equal("refresh-cb", context.Session.GetString(AutodeskSessionKeys.RefreshToken));
        Assert.Equal("true", context.Session.GetString(AutodeskSessionKeys.Connected));
        Assert.Equal("acc-73", context.Session.GetString(AutodeskSessionKeys.UserId));
        var exp = DateTimeOffset.Parse(context.Session.GetString(AutodeskSessionKeys.ExpiresAt)!);
        Assert.InRange(exp, antes.AddSeconds(3599), depois.AddSeconds(3599));
        // Sessão N&A
        Assert.Equal(73, context.Session.GetInt32(NaSessionKeys.UserId));
        Assert.Equal("user@na.com", context.Session.GetString(NaSessionKeys.Email));
        Assert.Equal("Usuário N&A", context.Session.GetString(NaSessionKeys.Nome));
        Assert.Equal("Autodesk", context.Session.GetString(NaSessionKeys.Provider));
    }

    [Fact]
    public async Task N2_CallbackSemExpiresIn_NaoInventaExpiresAt()
    {
        var context = new DefaultHttpContext { Session = new MemorySessionPerm() };
        var handler = new TokenHandler { ExpiresIn = null };
        var client = new HttpClient(handler);
        var configuration = Cenario.Config();
        var autodesk = new AutodeskApiService(client,
            new TokenManager(client, configuration, new MemoryCache(new MemoryCacheOptions())),
            configuration, NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        var controller = new OAuthController(configuration, autodesk, client, new FakePermissionRepository(), NullLogger<OAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        await controller.Callback("authorization-code");

        Assert.Equal("access-2", context.Session.GetString(AutodeskSessionKeys.AccessToken));
        Assert.Null(context.Session.GetString(AutodeskSessionKeys.ExpiresAt));
        Assert.Equal("true", context.Session.GetString(AutodeskSessionKeys.Connected));
    }

    // ---------- O ----------

    [Fact]
    public async Task O_TokenManager2Legged_ContinuaClientCredentialsSemSessao()
    {
        var handler = new TokenHandler { AccessTokenNaResposta = "app-token" };
        var manager = new TokenManager(new HttpClient(handler), Cenario.Config(), new MemoryCache(new MemoryCacheOptions()));

        var token = await manager.GetTokenAsync();
        var outra = await manager.GetTokenAsync();

        Assert.Equal("app-token", token);
        Assert.Equal("app-token", outra);
        Assert.Equal(1, handler.Chamadas);
        Assert.Contains("grant_type=client_credentials", handler.Corpos.Single());
        Assert.DoesNotContain("refresh_token", handler.Corpos.Single());
    }

    [Fact]
    public async Task O2_ProviderNaoUsaTokenManagerNemClientCredentials()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await t.Provider.GetValidAccessTokenAsync();

        Assert.DoesNotContain("client_credentials", t.Handler.Corpos.Single());
        Assert.DoesNotContain(typeof(AutodeskUserTokenProvider).GetConstructors().Single().GetParameters(),
            p => p.ParameterType == typeof(TokenManager));
    }

    // ---------- 07A.1: refresh sem retry automático ----------

    [Fact]
    public async Task R1_RefreshUsaClienteNomeadoSemRetry()
    {
        var t = new Cenario();
        t.Conectar("access-1", "refresh-1", Agora.AddMinutes(-1));

        await t.Provider.GetValidAccessTokenAsync();

        Assert.Equal(new[] { AutodeskUserTokenPolicy.RefreshHttpClientName }, t.Factory.Nomes);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task R2_Refresh5xxComRetryGlobalAtivo_ExatamenteUmPost(HttpStatusCode status)
    {
        var handler = new TokenHandler { Status = status };
        using var sp = ContainerComRetryGlobal(handler, out var context);
        AutodeskUserTokenSession.Save(context.Session, "access-1", "refresh-1", Agora.AddMinutes(-1));
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAutodeskUserTokenProvider>();

        var r = await provider.GetValidAccessTokenAsync();

        Assert.Equal(AutodeskUserTokenStatus.Unavailable, r.Status);
        Assert.Equal(1, handler.Chamadas);
        Assert.Contains("grant_type=refresh_token", handler.Corpos.Single());
        Assert.Equal("refresh-1", context.Session.GetString(AutodeskSessionKeys.RefreshToken));
    }

    [Fact]
    public async Task R3_ForceRefresh5xxComRetryGlobalAtivo_ExatamenteUmPostPorTentativa()
    {
        var handler = new TokenHandler { Status = HttpStatusCode.ServiceUnavailable };
        using var sp = ContainerComRetryGlobal(handler, out var context);
        AutodeskUserTokenSession.Save(context.Session, "access-1", "refresh-1", Agora.AddMinutes(50));
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAutodeskUserTokenProvider>();

        var r1 = await provider.ForceRefreshAsync("access-1");
        Assert.Equal(1, handler.Chamadas);
        var r2 = await provider.ForceRefreshAsync("access-1");
        Assert.Equal(2, handler.Chamadas); // uma tentativa do provider = um POST

        Assert.Equal(AutodeskUserTokenStatus.Unavailable, r1.Status);
        Assert.Equal(AutodeskUserTokenStatus.Unavailable, r2.Status);
    }

    [Fact]
    public async Task R4_RefreshComSucessoViaDiReal_UmPostEDualWrite()
    {
        var handler = new TokenHandler();
        using var sp = ContainerComRetryGlobal(handler, out var context);
        AutodeskUserTokenSession.Save(context.Session, "access-1", "refresh-1", Agora.AddMinutes(-1));
        using var scope = sp.CreateScope();

        var r = await scope.ServiceProvider.GetRequiredService<IAutodeskUserTokenProvider>().GetValidAccessTokenAsync();

        Assert.Equal("access-2", r.AccessToken);
        Assert.Equal(1, handler.Chamadas);
        Assert.Equal("refresh-2", context.Session.GetString("RefreshToken"));
    }

    [Fact]
    public async Task R5_RetryGlobalContinuaAtivoNosDemaisClientes()
    {
        var handler = new TokenHandler { Status = HttpStatusCode.ServiceUnavailable };
        using var sp = ContainerComRetryGlobal(handler, out _);
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        using var resposta = await factory.CreateClient().GetAsync("https://developer.api.autodesk.com/project/v1/hubs");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal(1 + MaxRetriesTeste, handler.Chamadas);
    }

    private const int MaxRetriesTeste = 3;

    /// <summary>Mesma composição de Program.cs: retry global via ConfigureHttpClientDefaults + AddAutodeskUserTokenProvider.</summary>
    private static ServiceProvider ContainerComRetryGlobal(TokenHandler handler, out DefaultHttpContext context)
    {
        context = new DefaultHttpContext { Session = new IdSession("sessao-di") };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(Cenario.Config());
        services.AddSingleton<IHttpContextAccessor>(new FixedAccessor(context));
        services.AddSingleton<TimeProvider>(new FixedTime(Agora));
        services.Configure<APS.WebApp.Services.Http.AutodeskRetryOptions>(o =>
        {
            o.MaxRetryAttempts = MaxRetriesTeste;
            o.BaseDelaySeconds = 0.001;
            o.MaxDelaySeconds = 0.01;
        });
        services.AddTransient<APS.WebApp.Services.Http.AutodeskRetryHandler>();
        services.AddHttpClient();
        services.ConfigureHttpClientDefaults(b =>
        {
            b.AddHttpMessageHandler<APS.WebApp.Services.Http.AutodeskRetryHandler>();
            b.ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        services.AddAutodeskUserTokenProvider();
        return services.BuildServiceProvider();
    }

    // ---------- infraestrutura ----------

    private sealed class Cenario
    {
        public TokenHandler Handler { get; }
        public ISession Session { get; }
        public DefaultHttpContext Context { get; }
        public AutodeskUserTokenProvider Provider { get; }
        public FakeFactory Factory { get; }

        public Cenario(TokenHandler? handler = null, AutodeskUserTokenRefreshCoordinator? coordinator = null, string sessionId = "t")
        {
            Handler = handler ?? new TokenHandler();
            Session = new IdSession(sessionId);
            Context = new DefaultHttpContext { Session = Session };
            Factory = new FakeFactory(Handler);
            Provider = new AutodeskUserTokenProvider(new FixedAccessor(Context), Factory, Config(),
                coordinator ?? new AutodeskUserTokenRefreshCoordinator(new MemoryCache(new MemoryCacheOptions())),
                NullLogger<AutodeskUserTokenProvider>.Instance, new FixedTime(Agora));
        }

        public static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APS:ClientId"] = "client",
            ["APS:ClientSecret"] = "secret",
            ["APS:CallbackUrl"] = "https://localhost/callback",
            ["APS:Scope"] = "data:read",
        }).Build();

        public void Conectar(string access, string refresh, DateTimeOffset expiresAt) =>
            AutodeskUserTokenSession.Save(Session, access, refresh, expiresAt);

        public void SessaoNa()
        {
            Session.SetInt32(NaSessionKeys.UserId, 42);
            Session.SetString(NaSessionKeys.Email, "user@na.com");
            Session.SetString(NaSessionKeys.Nome, "Usuário N&A");
            Session.SetString(NaSessionKeys.Provider, "Autodesk");
        }

        public void AssertSessaoNaIntacta()
        {
            Assert.Equal(42, Session.GetInt32(NaSessionKeys.UserId));
            Assert.Equal("user@na.com", Session.GetString(NaSessionKeys.Email));
            Assert.Equal("Usuário N&A", Session.GetString(NaSessionKeys.Nome));
            Assert.Equal("Autodesk", Session.GetString(NaSessionKeys.Provider));
        }

        public SessionCurrentUserAccessor UserInfoAccessor()
        {
            var client = new HttpClient(Handler);
            var config = Config();
            var autodesk = new AutodeskApiService(client, new TokenManager(client, config, new MemoryCache(new MemoryCacheOptions())),
                config, NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
            return new SessionCurrentUserAccessor(new FixedAccessor(Context), autodesk,
                NullLogger<SessionCurrentUserAccessor>.Instance);
        }
    }

    /// <summary>Sessão em memória com Id configurável (o lock single-flight é por Session.Id).</summary>
    private sealed class IdSession(string id) : ISession
    {
        private readonly Dictionary<string, byte[]> _v = new();
        public bool IsAvailable => true;
        public string Id => id;
        public IEnumerable<string> Keys => _v.Keys;
        public void Clear() => _v.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _v.Remove(key);
        public void Set(string key, byte[] value) => _v[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _v.TryGetValue(key, out value!);
    }

    /// <summary>Registra os nomes pedidos ao IHttpClientFactory; todos os clientes usam o handler simulado.</summary>
    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> Nomes { get; } = new();
        public HttpClient CreateClient(string name)
        {
            lock (Nomes) Nomes.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    /// <summary>HttpContextAccessor real usa AsyncLocal estático: dois cenários no mesmo teste se sobrescreveriam.</summary>
    private sealed class FixedAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }

    private sealed class EmptyAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Simula o endpoint /authentication/v2/token (e UserInfo, quando configurado).</summary>
    private sealed class TokenHandler : HttpMessageHandler
    {
        private int _chamadas;
        public int Chamadas => _chamadas;
        public List<string> Corpos { get; } = new();
        public List<string> Urls { get; } = new();
        public List<System.Net.Http.Headers.AuthenticationHeaderValue?> Autorizacoes { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Lanca { get; set; }
        public TimeSpan Atraso { get; set; }
        public string AccessTokenNaResposta { get; set; } = "access-2";
        public string? RefreshTokenNaResposta { get; set; } = "refresh-2";
        public int? ExpiresIn { get; set; } = 3600;
        public string? UserInfoJson { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _chamadas);
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Corpos) { Corpos.Add(corpo); Urls.Add(request.RequestUri!.ToString()); Autorizacoes.Add(request.Headers.Authorization); }
            if (Atraso > TimeSpan.Zero) await Task.Delay(Atraso, cancellationToken);
            if (Lanca) throw new HttpRequestException("Autodesk indisponível");
            if (UserInfoJson is not null && request.RequestUri!.ToString() == AutodeskUserInfoParser.Endpoint)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UserInfoJson) };
            if (Status != HttpStatusCode.OK)
                return new HttpResponseMessage(Status) { Content = new StringContent("""{"error":"invalid_grant"}""") };

            var campos = new Dictionary<string, object?> { ["access_token"] = AccessTokenNaResposta, ["token_type"] = "Bearer" };
            if (RefreshTokenNaResposta is not null) campos["refresh_token"] = RefreshTokenNaResposta;
            if (ExpiresIn is not null) campos["expires_in"] = ExpiresIn;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(campos))
            };
        }
    }
}
