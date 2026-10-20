using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using APS.WebApp.Areas.Authentication.Controllers;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Integração real (Kestrel em 127.0.0.1, cookies HTTP reais, NaAuthController real, sessão em memória como em produção).
/// "Restart" = nova instância do host com cache de sessão E chaves de Data Protection novas, mesmos arquivos em disco.
/// Só fixtures em memória/pasta temporária: nenhum banco, conta, token real ou arquivo do checkout é tocado.
/// </summary>
public class TvDeviceSessionTests
{
    private const string Pres = "a";
    private static readonly string Data0 = $"/api/tv-portal/data/{Pres}/0?resource=%2Fapi%2Fbi-chs001%2Fldp&projectId={TvTelas.ProjetoChs001}";

    [Fact]
    public async Task SemDispositivo_RestartDoBackendDerrubaSessaoTv_Diagnostico()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTv();
        Assert.Equal(HttpStatusCode.OK, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        await f.Restart();
        // Mesmo cookie de sessão, poll de 15 s em dia: após restart a sessão em memória não existe mais.
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
    }

    [Fact]
    public async Task Registro_CookieHttpOnlySecureStrict_TokenNuncaGravadoEmClaro()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTv();
        var r = await tv.Send(HttpMethod.Post, "/api/tv-portal/device", new { label = "TV Recepção" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var set = Assert.Single(r.Headers.GetValues("Set-Cookie"), c => c.StartsWith(TvDeviceSessionService.CookieName + "=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", set); Assert.Contains("secure", set); Assert.Contains("samesite=strict", set); Assert.Contains("path=/", set);
        Assert.Contains("expires=", set);
        Assert.DoesNotContain("domain=", set);
        var token = tv.Cookie(TvDeviceSessionService.CookieName)!;
        Assert.True(TvDeviceSessionStore.WellFormed(token));
        var arquivo = await File.ReadAllTextAsync(f.DeviceFile);
        Assert.DoesNotContain(token, arquivo);
        Assert.Contains(TvDeviceSessionStore.Hash(token), arquivo);
        var status = await (await tv.Get("/api/tv-portal/device")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.GetProperty("registered").GetBoolean());
        // A expiração anunciada respeita o ocioso configurado (14 dias), nunca "infinita".
        Assert.Equal(f.Clock.Now.AddDays(14), status.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Restart_ComDispositivoRestauraApenasLeituraDoPortal()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        await f.Restart();
        Assert.Equal(HttpStatusCode.OK, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tv.Get(Data0)).StatusCode);
        // Sem o cookie de sessão antigo (TV reiniciada/navegador fechado): só o cookie de dispositivo basta.
        tv.Forget(".AspNetCore.Session");
        await f.Restart();
        Assert.Equal(HttpStatusCode.OK, (await tv.Get("/api/tv-portal/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tv.Get(Data0)).StatusCode);
        // A sessão restaurada continua sendo o perfil TV restrito: nada administrativo abre.
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Get("/api/tv-portal/assignments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Get("/api/tv-portal/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Get("/api/tv-config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Send(HttpMethod.Put, "/api/tv-config", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Get("/api/tv-portal/data/a/0?resource=%2Fapi%2Freports%2Fcurva-s&projectId=" + TvTelas.ProjetoChs001)).StatusCode);
    }

    [Fact]
    public async Task CookieDeDispositivo_NaoRestauraSessaoForaDasLeiturasDoPortal()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        tv.Forget(".AspNetCore.Session");
        await f.Restart();
        Assert.Equal("anon", await (await tv.Get("/api/probe/whoami")).Content.ReadAsStringAsync());
        Assert.Equal("anon", await (await tv.Get("/api/tv-config")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Send(HttpMethod.Post, "/api/tv-portal/device", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get("/api/tv-portal/assignments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get("/api/tv-portal/devices")).StatusCode);
        Assert.Null(tv.Cookie(".AspNetCore.Session"));
    }

    [Fact]
    public async Task RevogacaoPeloAdmin_EncerraNaProximaRequisicaoEApósRestart()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        var admin = await f.Login(Fixture.AdminEmail);
        var lista = await (await admin.Get("/api/tv-portal/devices")).Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(lista.GetProperty("devices").EnumerateArray());
        Assert.False(item.TryGetProperty("tokenHash", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Send(HttpMethod.Delete, "/api/tv-portal/devices/" + item.GetProperty("id").GetString(), null, crossSite: true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Send(HttpMethod.Delete, "/api/tv-portal/devices/" + item.GetProperty("id").GetString())).StatusCode);
        var r = await tv.Get($"/api/tv-portal/presentations/{Pres}");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Null(tv.Cookie(TvDeviceSessionService.CookieName));
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get("/api/tv-portal/session")).StatusCode);
        Assert.StartsWith("revogado-admin:", (await f.Record()).RevokedReason);
    }

    [Fact]
    public async Task RevogacaoPersistida_CookieAntigoNaoVoltaAposRestart()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        var token = tv.Cookie(TvDeviceSessionService.CookieName)!;
        await f.Services.GetRequiredService<TvDeviceSessionStore>().RevokeAsync((await f.Record()).Id, "teste", default);
        await f.Restart();
        var copia = f.Client(); copia.Set(TvDeviceSessionService.CookieName, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copia.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        Assert.Null(copia.Cookie(TvDeviceSessionService.CookieName));
    }

    [Fact]
    public async Task ExpiracaoOciosa_RevogaEExigeLogin()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        f.Clock.Now = f.Clock.Now.AddDays(14).AddMinutes(1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        Assert.Equal("expirado", (await f.Record()).RevokedReason);
    }

    [Fact]
    public async Task RenovacaoDeslizanteEnquantoTvAtiva_LimitadaAoTetoAbsoluto()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        var inicio = f.Clock.Now;
        f.Clock.Now = inicio.AddMinutes(30);
        var semRenovar = await tv.Get($"/api/tv-portal/presentations/{Pres}");
        Assert.False(semRenovar.Headers.TryGetValues("Set-Cookie", out var c0) && c0.Any(c => c.StartsWith(TvDeviceSessionService.CookieName, StringComparison.Ordinal)));
        // TV ligada: a cada 10 dias (menos que o ocioso de 14) continua válida, renovando.
        for (var dia = 10; dia <= 170; dia += 10)
        {
            f.Clock.Now = inicio.AddDays(dia);
            var r = await tv.Get($"/api/tv-portal/presentations/{Pres}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains(r.Headers.GetValues("Set-Cookie"), c => c.StartsWith(TvDeviceSessionService.CookieName + "=", StringComparison.Ordinal));
            Assert.Equal(Min(f.Clock.Now.AddDays(14), inicio.AddDays(180)), (await f.Record()).ExpiresAt);
        }
        f.Clock.Now = inicio.AddDays(180).AddMinutes(1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    }

    [Theory]
    [InlineData("usuario-inativo")]
    [InlineData("senha-alterada")]
    [InlineData("senha-troca-obrigatoria")]
    [InlineData("perfil-admin")]
    [InlineData("sem-tv-view")]
    [InlineData("perfil-comum")]
    public async Task MudancaNaConta_RevogaSemElevarPrivilegio(string mudanca)
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        switch (mudanca)
        {
            case "usuario-inativo": f.TvAtivo = false; break;
            case "senha-alterada": f.TvCredencial = f.TvCredencial with { PasswordUpdatedAt = f.TvCredencial.PasswordUpdatedAt.AddSeconds(1) }; break;
            case "senha-troca-obrigatoria": f.TvCredencial = f.TvCredencial with { MustChangePassword = true }; break;
            case "perfil-admin": f.TvPerfis = [Fixture.Tv, Fixture.Admin]; break;
            case "sem-tv-view": f.TvPerfis = [new(7, "TV", null, false, new HashSet<string> { "home.view" })]; break;
            case "perfil-comum": f.TvPerfis = [new(8, "Leitura", null, false, new HashSet<string> { "home.view", "tv.view" })]; break;
        }
        foreach (var antes in new[] { false, true })
        {
            if (antes) { tv.Forget(".AspNetCore.Session"); await f.Restart(); }
            Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
            Assert.Equal("anon", await (await tv.Get("/api/tv-config")).Content.ReadAsStringAsync());
        }
        Assert.NotNull((await f.Record()).RevokedAt);
        Assert.Null(tv.Cookie(TvDeviceSessionService.CookieName));
    }

    [Fact]
    public async Task RegistroRecusadoForaDaContaTvRestrita()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().Send(HttpMethod.Post, "/api/tv-portal/device", new { })).StatusCode);
        var admin = await f.Login(Fixture.AdminEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Send(HttpMethod.Post, "/api/tv-portal/device", new { })).StatusCode);
        Assert.Null(admin.Cookie(TvDeviceSessionService.CookieName));
        var tv = await f.LoginTv();
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Send(HttpMethod.Post, "/api/tv-portal/device", new { }, crossSite: true)).StatusCode);
        await f.Services.GetRequiredService<TvPortalAssignmentStore>().SaveAsync(new(1, []), default);
        Assert.Equal(HttpStatusCode.Forbidden, (await tv.Send(HttpMethod.Post, "/api/tv-portal/device", new { })).StatusCode);
        Assert.Null(tv.Cookie(TvDeviceSessionService.CookieName));
        Assert.False(File.Exists(f.DeviceFile));
    }

    [Fact]
    public async Task RecursoDesligado_CookieIgnoradoERegistroInexistente()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        f.Options.Enabled = false;
        await f.Restart();
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        var tv2 = await f.LoginTv();
        Assert.Equal(HttpStatusCode.NotFound, (await tv2.Send(HttpMethod.Post, "/api/tv-portal/device", new { })).StatusCode);
        var status = await (await tv2.Get("/api/tv-portal/device")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(status.GetProperty("available").GetBoolean());
    }

    [Theory]
    [InlineData("forjado")]
    [InlineData("malformado")]
    public async Task CookieForjadoOuMalformado_Nega(string tipo)
    {
        await using var f = await Fixture.Create();
        await f.LoginTvComDispositivo();
        var c = f.Client();
        c.Set(TvDeviceSessionService.CookieName, tipo == "forjado" ? TvDeviceSessionStore.NewToken() : "abc");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        Assert.Null(c.Cookie(TvDeviceSessionService.CookieName));
        Assert.Null((await f.Record()).RevokedAt); // tentativa externa não derruba a TV legítima
    }

    [Fact]
    public async Task CookieDeOutraTvNaoAssumeSessaoMarcadaDeOutroDispositivo()
    {
        await using var f = await Fixture.Create();
        var tv1 = await f.LoginTvComDispositivo();
        var tv2 = await f.LoginTvComDispositivo();
        tv1.Set(TvDeviceSessionService.CookieName, tv2.Cookie(TvDeviceSessionService.CookieName)!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv1.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
    }

    [Fact]
    public async Task LogoutEEsquecer_RevogamODispositivo()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        var token = tv.Cookie(TvDeviceSessionService.CookieName)!;
        Assert.Equal(HttpStatusCode.Redirect, (await tv.Get("/Authentication/OAuth/Logout")).StatusCode);
        Assert.Null(tv.Cookie(TvDeviceSessionService.CookieName));
        Assert.Equal("logout", (await f.Record()).RevokedReason);
        var copia = f.Client(); copia.Set(TvDeviceSessionService.CookieName, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copia.Get("/api/tv-portal/session")).StatusCode);

        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        var tv2 = await f.LoginTvComDispositivo();
        Assert.Equal(HttpStatusCode.NoContent, (await tv2.Send(HttpMethod.Delete, "/api/tv-portal/device")).StatusCode);
        Assert.Null(tv2.Cookie(TvDeviceSessionService.CookieName));
        Assert.Equal("esquecido-na-tv", (await f.Record()).RevokedReason);
    }

    [Fact]
    public async Task OutrosLogins_NaoRecebemCookieNemMudamDeComportamento()
    {
        await using var f = await Fixture.Create();
        var admin = await f.Login(Fixture.AdminEmail);
        foreach (var path in new[] { "/api/tv-config", "/api/tv-portal/session", "/api/tv-portal/device", "/api/tv-portal/assignments" })
        {
            var r = await admin.Get(path);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.False(r.Headers.TryGetValues("Set-Cookie", out var cs) && cs.Any(c => c.Contains(TvDeviceSessionService.CookieName, StringComparison.Ordinal)));
        }
        Assert.Equal("admin-data", await (await admin.Get("/api/tv-config")).Content.ReadAsStringAsync());
        // TV que não optou pelo dispositivo: comportamento original (expira no restart, nenhum cookie extra).
        var tv = await f.LoginTv();
        Assert.Equal(HttpStatusCode.OK, (await tv.Get(Data0)).StatusCode);
        Assert.Null(tv.Cookie(TvDeviceSessionService.CookieName));
        Assert.False(File.Exists(f.DeviceFile));
    }

    [Fact]
    public async Task RegistroCorrompido_FalhaFechado503()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        tv.Forget(".AspNetCore.Session");
        await f.Restart();
        await File.WriteAllTextAsync(f.DeviceFile, "{ corrompido");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        Assert.NotNull(tv.Cookie(TvDeviceSessionService.CookieName)); // indisponibilidade não apaga a TV
    }

    [Fact]
    public async Task LimiteDeDispositivosPorUsuario_RevogaOsMaisAntigos()
    {
        await using var f = await Fixture.Create();
        f.Options.MaxDevicesPerUser = 2;
        var tvs = new List<Client>();
        for (var i = 0; i < 3; i++) { tvs.Add(await f.LoginTvComDispositivo()); f.Clock.Now = f.Clock.Now.AddMinutes(1); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await tvs[0].Get("/api/tv-portal/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tvs[1].Get("/api/tv-portal/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tvs[2].Get("/api/tv-portal/session")).StatusCode);
    }

    [Fact]
    public async Task NavegadorRecusaCookieSecure_SessaoComumContinuaEAvisa()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTv();
        Assert.Equal(HttpStatusCode.OK, (await tv.Send(HttpMethod.Post, "/api/tv-portal/device", new { })).StatusCode);
        tv.Forget(TvDeviceSessionService.CookieName); // http fora de localhost: o navegador descarta cookie Secure
        var status = await (await tv.Get("/api/tv-portal/device")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.GetProperty("cookieRejected").GetBoolean());
        Assert.False(status.GetProperty("registered").GetBoolean());
        // Nenhuma regressão: a sessão comum segue funcionando como antes do recurso.
        Assert.Equal(HttpStatusCode.OK, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tv.Get(Data0)).StatusCode);
    }

    [Fact]
    public async Task CookieDevolvido_PromoveERevogacaoPassaAValerNaSessaoAtual()
    {
        await using var f = await Fixture.Create();
        var tv = await f.LoginTvComDispositivo();
        var status = await (await tv.Get("/api/tv-portal/device")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.GetProperty("registered").GetBoolean());
        Assert.False(status.GetProperty("cookieRejected").GetBoolean());
        // Já promovida: sumir com o cookie numa sessão de dispositivo encerra (não rebaixa silenciosamente).
        tv.Forget(TvDeviceSessionService.CookieName);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.Get($"/api/tv-portal/presentations/{Pres}")).StatusCode);
    }

    [Fact]
    public void ContratoDePermissao_ControllerNovoDeclaraIsencaoComMotivo()
    {
        var exempt = typeof(TvDeviceSessionController).GetCustomAttribute<PermissionExemptAttribute>();
        Assert.False(string.IsNullOrWhiteSpace(exempt?.Motivo));
        // Toda action HTTP declarada; nenhuma rota administrativa fica no allowlist da TV.
        Assert.All(typeof(TvDeviceSessionController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            m => Assert.NotEmpty(m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()));
    }

    // ------------------------------------------------------------------------------------------------------------
    public sealed class FakeClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class OnlyTvControllers : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo t) => base.IsController(t)
            && (t == typeof(TvPortalController) || t == typeof(TvDeviceSessionController) || t == typeof(NaAuthController));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string TvEmail = "tv@fixture.invalid", AdminEmail = "admin@fixture.invalid", Senha = "Fixture#Senha-123";
        public static ProfileAssignment Tv => new(7, "TV", null, false, new HashSet<string> { "tv.view" });
        public static ProfileAssignment Admin => new(1, "Admin", null, true, new HashSet<string>());
        private readonly string dir = Path.Combine(Path.GetTempPath(), "tv-device-test-" + Guid.NewGuid().ToString("N"));
        private WebApplication? app;
        public readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        public readonly TvDeviceSessionOptions Options = new() { Enabled = true };
        public bool TvAtivo = true;
        public ProfileAssignment[] TvPerfis = [Tv];
        public NaLocalCredential TvCredencial = null!, AdminCredencial = null!;
        public string BaseUrl = "";
        public IServiceProvider Services => app!.Services;
        public string DeviceFile => Path.Combine(dir, "tv-device-sessions.json");
        private PermUser TvUser => new(42, null, TvEmail, "TV Fixture", TvAtivo, null);
        private static readonly PermUser AdminUser = new(1, null, AdminEmail, "Admin Fixture", true, null);

        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.dir);
            var hasher = new PasswordHasher<PermUser>();
            f.TvCredencial = new(42, hasher.HashPassword(f.TvUser, Senha), 0, null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), false);
            f.AdminCredencial = new(1, hasher.HashPassword(AdminUser, Senha), 0, null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), false);
            await new TvConfigurationStore(Path.Combine(f.dir, "tv-configuration.json"))
                .SaveAsync([new(Pres, "TV", 30, [new("gestao-vista-tipo2-resumo-30", "", TvTelas.ProjetoChs001, null)])], 0, null);
            await new TvPortalAssignmentStore(Path.Combine(f.dir, "tv-portal-assignments.json")).SaveAsync(new(0, [new(Pres, [42])]), default);
            await f.Start();
            return f;
        }

        public async Task Restart() { await app!.StopAsync(); await app.DisposeAsync(); await Start(); }

        private async Task Start()
        {
            var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            b.WebHost.UseUrls("http://127.0.0.1:0");
            b.Logging.ClearProviders();
            if (Environment.GetEnvironmentVariable("TV_TEST_LOG") == "1") b.Logging.AddConsole();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Permissions:Enforce"] = "false" });
            // Restart real: cache de sessão em memória e chaves de Data Protection novas a cada instância.
            b.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            b.Services.AddDistributedMemoryCache();
            b.Services.AddSession(o => { o.IdleTimeout = TimeSpan.FromMinutes(60); o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; });
            b.Services.AddHttpContextAccessor();
            var repository = DispatchProxy.Create<IPermissionRepository, TvPortalTests.RepoProxy>();
            ((TvPortalTests.RepoProxy)repository).Handler = (m, a) => m.Name switch
            {
                "ObterUsuarioAsync" => Task.FromResult<PermUser?>((int)a![0]! switch { 42 => TvUser, 1 => AdminUser, _ => null }),
                "CarregarDadosAsync" => Task.FromResult(new UserPermissionData(false, (int)a![0]! == 1 ? [Admin] : TvPerfis, [], new HashSet<int>())),
                "ResolverProjetoIdAsync" => Task.FromResult<int?>(1),
                "UsuarioPossuiAcessoAtivoProjetoAsync" => Task.FromResult(false),
                "ListarUsuariosAsync" => Task.FromResult<IReadOnlyList<PermUser>>([TvUser, AdminUser]),
                _ => throw new NotImplementedException(m.Name)
            };
            var credentials = DispatchProxy.Create<INaLocalCredentialRepository, TvPortalTests.RepoProxy>();
            ((TvPortalTests.RepoProxy)credentials).Handler = (m, a) => m.Name switch
            {
                "ObterUsuarioPorEmailAsync" => Task.FromResult<PermUser?>((string)a![0]! switch { TvEmail => TvUser, AdminEmail => AdminUser, _ => null }),
                "ObterCredencialAsync" => Task.FromResult<NaLocalCredential?>((int)a![0]! switch { 42 => TvCredencial, 1 => AdminCredencial, _ => null }),
                "RegistrarSucessoAsync" => Task.CompletedTask,
                _ => throw new NotImplementedException(m.Name)
            };
            b.Services.AddSingleton(repository);
            b.Services.AddSingleton(credentials);
            b.Services.AddSingleton<INaLocalAuthService>(new NaLocalAuthService(credentials, NullLogger<NaLocalAuthService>.Instance));
            b.Services.AddScoped<INaCurrentUserAccessor, SessionNaCurrentUserAccessor>();
            b.Services.AddScoped<IPermissionService>(sp => new PermissionService(repository, sp.GetRequiredService<INaCurrentUserAccessor>(), null!,
                sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHttpContextAccessor>(), NullLogger<PermissionService>.Instance));
            b.Services.AddScoped(sp => new TvPortalIdentityService(sp.GetRequiredService<IPermissionService>(), repository));
            b.Services.AddSingleton(new TvConfigurationStore(Path.Combine(dir, "tv-configuration.json")));
            b.Services.AddSingleton(new TvPortalAssignmentStore(Path.Combine(dir, "tv-portal-assignments.json")));
            b.Services.AddSingleton(new TvDeviceSessionStore(DeviceFile, Clock));
            b.Services.AddSingleton<IOptions<TvDeviceSessionOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
            b.Services.AddScoped(sp => new TvDeviceSessionService(sp.GetRequiredService<TvDeviceSessionStore>(),
                sp.GetRequiredService<IOptions<TvDeviceSessionOptions>>(), repository, credentials, Clock));
            b.Services.AddControllers().ConfigureApplicationPartManager(m =>
            {
                m.ApplicationParts.Add(new AssemblyPart(typeof(TvPortalController).Assembly));
                foreach (var p in m.FeatureProviders.OfType<ControllerFeatureProvider>().ToArray()) m.FeatureProviders.Remove(p);
                m.FeatureProviders.Add(new OnlyTvControllers());
            });
            app = b.Build();
            app.UseSession();
            app.UseMiddleware<TvDeviceSessionMiddleware>();
            app.UseMiddleware<TvPortalMiddleware>();
            app.UseRouting();
            app.MapControllers();
            app.MapGet("/Authentication/OAuth/Logout", (HttpContext c) => { c.Session.Clear(); return Results.Redirect("/"); });
            app.MapGet("/api/probe/whoami", (HttpContext c) => c.Session.GetInt32(NaSessionKeys.UserId)?.ToString() ?? "anon");
            // Endpoint administrativo de exemplo SEM checagem própria: só a barreira TV o protege.
            app.MapMethods("/api/tv-config", ["GET", "PUT"], (HttpContext c) => c.Session.GetInt32(NaSessionKeys.UserId) is null ? "anon" : "admin-data");
            app.MapGet("/api/bi-chs001/ldp", (HttpContext c) => c.Items.ContainsKey(TvPortalPolicy.ScopeItem) ? Results.Ok("ldp") : Results.StatusCode(418));
            app.MapGet("/api/reports/curva-s", () => "curva");
            await app.StartAsync();
            BaseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        }

        public Client Client() => new(() => BaseUrl);
        public async Task<Client> Login(string email)
        {
            var c = Client();
            var r = await c.Send(HttpMethod.Post, "/Authentication/Na/Login", new { email, password = Senha });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return c;
        }
        public Task<Client> LoginTv() => Login(TvEmail);
        public async Task<Client> LoginTvComDispositivo()
        {
            var c = await LoginTv();
            Assert.Equal(HttpStatusCode.OK, (await c.Send(HttpMethod.Post, "/api/tv-portal/device", new { label = "TV" })).StatusCode);
            Assert.NotNull(c.Cookie(TvDeviceSessionService.CookieName));
            return c;
        }
        public async Task<TvDeviceRecord> Record() =>
            (await new TvDeviceSessionStore(DeviceFile, Clock).ReadAsync(default)).Devices.OrderByDescending(d => d.CreatedAt).First();

        public async ValueTask DisposeAsync()
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Cliente com "pote" de cookies manual: o HttpClient não envia cookie Secure por http; o navegador
    /// trata 127.0.0.1/localhost e o túnel HTTPS como origem segura.</summary>
    private sealed class Client(Func<string> baseUrl)
    {
        private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        private readonly Dictionary<string, string> jar = new(StringComparer.Ordinal);
        public string? Cookie(string name) => jar.TryGetValue(name, out var v) ? v : null;
        public void Set(string name, string value) => jar[name] = value;
        public void Forget(string name) => jar.Remove(name);
        public Task<HttpResponseMessage> Get(string path) => Send(HttpMethod.Get, path);
        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool crossSite = false)
        {
            using var request = new HttpRequestMessage(method, baseUrl() + path);
            if (jar.Count > 0) request.Headers.Add("Cookie", string.Join("; ", jar.Select(kv => $"{kv.Key}={kv.Value}")));
            request.Headers.Add("Sec-Fetch-Site", crossSite ? "cross-site" : "same-origin");
            request.Headers.Add("Accept", "application/json");
            if (body is not null) request.Content = JsonContent.Create(body);
            var response = await Http.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';', 2)[0].Split('=', 2);
                    var expired = cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || pair[1].Length == 0;
                    if (expired) jar.Remove(pair[0]); else jar[pair[0]] = pair[1];
                }
            return response;
        }
    }
}
