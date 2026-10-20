using APS.WebApp.Services.Identity;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests.Permissions;

/// <summary>PermissionService (enforce, falha fechada, escopo de projeto) + [RequirePermission] + guardas do PermissionsController.</summary>
public class PermissionServiceTests
{
    private const string ProjA = "b.aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string ProjB = "b.bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    // Padrão: Enforce ligado com UM MasterAdmin configurado que NÃO é o usuário da sessão (user@na.com), porque
    // Enforce=true com MasterAdmins vazio permanece em safe mode.
    private static readonly string[] OutroMaster = { "outro-admin@na.com" };

    private static (PermissionService S, FakePermissionRepository R, DefaultHttpContext Ctx) Criar(bool enforce = true, bool logado = true,
        string[]? masters = null, FakeUserAccessor? acessor = null, FakeNaCurrentUserAccessor? acessorNa = null)
    {
        var ctx = new DefaultHttpContext { Session = new MemorySessionPerm() };
        if (logado) ctx.Session.SetString("AccessToken", "token");
        var cfg = new Dictionary<string, string?> { ["Permissions:Enforce"] = enforce ? "true" : "false" };
        masters ??= OutroMaster;
        for (int i = 0; i < masters.Length; i++) cfg[$"Permissions:MasterAdmins:{i}"] = masters[i];
        var repo = new FakePermissionRepository();
        repo.Projetos[ProjA] = 10; repo.Projetos[ProjB] = 20;
        var s = new PermissionService(repo, acessorNa ?? new FakeNaCurrentUserAccessor(), acessor ?? new FakeUserAccessor(),
            new ConfigurationBuilder().AddInMemoryCollection(cfg).Build(),
            new HttpContextAccessor { HttpContext = ctx }, NullLogger<PermissionService>.Instance);
        return (s, repo, ctx);
    }

    private static (PermissionService S, FakePermissionRepository R, DefaultHttpContext Ctx) CriarNa(bool enforce = true, string[]? masters = null)
    {
        var na = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(42, "interno@na.com", "Interno", "N&A") };
        var resultado = Criar(enforce, logado: false, masters: masters, acessorNa: na);
        resultado.R.Usuarios[42] = new PermUser(42, "acc-42", "interno@na.com", "Interno", true, null);
        return resultado;
    }

    // ------------------------------------------------------------- safe mode / bootstrap ----
    private static readonly string[] AlgumasKeys = { "home.view", "robot.run", "permissions.manage", "production.managerial.rnc.view" };

    [Fact]
    public async Task CasoA_EnforceFalse_UsuarioSemNenhumaPermissao_AcessoPermitido()
    {
        var (s, r, _) = Criar(enforce: false);
        Assert.False(s.Enforced);
        foreach (var k in AlgumasKeys)
        {
            Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { k }, null, default));
            Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { k }, ProjA, default));
        }
        var eff = await s.GetEffectiveAsync(ProjA, default);
        Assert.False(eff.Enforced);
        Assert.Equal(PermissionCatalog.Keys.Count, eff.Permissions.Count);
        Assert.Equal(PermissionCatalog.Keys.Count, eff.AnyProject.Count);
        Assert.Empty(r.Escritas); // safe mode não grava nada
    }

    [Fact]
    public async Task CasoB_EnforceFalse_SchemaInexistente_OuBancoComErro_AcessoPermitido()
    {
        var (s, r, _) = Criar(enforce: false);
        r.Schema = false;
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "robot.run" }, null, default));
        var eff = await s.GetEffectiveAsync(null, default);
        Assert.False(eff.SchemaInstalled);
        Assert.Equal(PermissionCatalog.Keys.Count, eff.Permissions.Count);

        r.Lanca = true; // banco fora do ar
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "robot.run" }, ProjA, default));
        Assert.Equal(PermissionCatalog.Keys.Count, (await s.GetEffectiveAsync(ProjA, default)).Permissions.Count);
    }

    [Fact]
    public async Task CasoC_EnforceFalse_AutodeskEBancoFalhando_MeResponde200ComTudoLiberado()
    {
        var (s, r, ctx) = Criar(enforce: false, acessor: new FakeUserAccessor { Lanca = true });
        r.Lanca = true;
        var c = new PermissionsController(s, r, NullLogger<PermissionsController>.Instance) { ControllerContext = new ControllerContext { HttpContext = ctx } };
        var ok = Assert.IsType<OkObjectResult>(await c.Me(null, default));
        var eff = Assert.IsType<EffectivePermissions>(ok.Value);
        Assert.False(eff.Enforced);
        Assert.Equal(PermissionCatalog.Keys.Count, eff.Permissions.Count);
    }

    [Fact]
    public async Task EnforceTrue_ComMasterAdminsVazio_PermaneceEmSafeMode()
    {
        var (s, r, _) = Criar(enforce: true, masters: Array.Empty<string>());
        Assert.False(s.Enforced);
        r.Schema = false;
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "permissions.manage" }, null, default));
        Assert.False((await s.GetEffectiveAsync(null, default)).Enforced);
    }

    [Fact]
    public async Task CasoD_EnforceTrue_MasterAdminConfigurado_AcessoTotal_SemBanco_ENenhumDenyBloqueia()
    {
        // Sem schema (migração não aplicada).
        var (s, r, _) = Criar(masters: new[] { "USER@na.com" });
        r.Schema = false;
        // Sem dados de UserProjects, MasterAdmin mantém acesso global, mas não ganha projeto implícito.
        foreach (var k in AlgumasKeys) Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { k }, null, default));
        var eff = await s.GetEffectiveAsync(null, default);
        Assert.True(eff.Enforced);
        Assert.True(eff.IsMasterAdmin);
        Assert.Equal(PermissionCatalog.Keys.Count, eff.Permissions.Count);
        Assert.Contains("permissions.manage", eff.AnyProject);

        // Banco com erro.
        var (s2, r2, _) = Criar(masters: new[] { "user@na.com" });
        r2.Lanca = true;
        Assert.Equal(PermissionCheck.Allowed, await s2.CheckAsync(new[] { "permissions.manage" }, null, default));
        Assert.True((await s2.GetEffectiveAsync(null, default)).IsMasterAdmin);

        // DENY individual + nenhum perfil: continua liberado.
        var (s3, r3, _) = Criar(masters: new[] { "user@na.com" });
        r3.Dados[1] = PermissionResolverTests.Usuario(overrides: new[]
        {
            new PermissionOverride(null, "permissions.manage", PermissionEffects.Deny),
            new PermissionOverride(10, "robot.run", PermissionEffects.Deny),
        });
        Assert.Equal(PermissionCheck.Allowed, await s3.CheckAsync(new[] { "permissions.manage" }, null, default));
        Assert.Equal(PermissionCheck.Allowed, await s3.CheckAsync(new[] { "robot.run" }, ProjA, default));
    }

    [Fact]
    public async Task CasoD_MasterAdminPorAutodeskUserId()
    {
        var (s, r, _) = Criar(masters: new[] { "acc-1" });
        r.Schema = false;
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "permissions.manage" }, null, default));
    }

    [Fact]
    public async Task CasoE_EnforceTrue_UsuarioComumSemPermissao_Negado()
    {
        var (s, _, ctx) = Criar();
        Assert.True(s.Enforced);
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "production.view" }, ProjA, default));
        var f = Filtro(ctx, s);
        await new RequirePermissionAttribute("production.view").OnAuthorizationAsync(f);
        Assert.Equal(403, Assert.IsType<ObjectResult>(f.Result).StatusCode);
        var eff = await s.GetEffectiveAsync(null, default);
        Assert.True(eff.Enforced);
        Assert.False(eff.IsMasterAdmin);
        Assert.Empty(eff.Permissions);
    }

    [Fact]
    public async Task EnforceFalse_PreservaComportamentoAtual()
    {
        var (s, _, _) = Criar(enforce: false);
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "robot.run" }, null, default));
        var eff = await s.GetEffectiveAsync(null, default);
        Assert.False(eff.Enforced);
        Assert.Equal(PermissionCatalog.Keys.Count, eff.Permissions.Count);
    }

    [Fact]
    public async Task SemSessao_Unauthenticated_MesmoComEnforceFalse()
    {
        Assert.Equal(PermissionCheck.Unauthenticated, await Criar(enforce: false, logado: false).S.CheckAsync(new[] { "home.view" }, null, default));
        Assert.Equal(PermissionCheck.Unauthenticated, await Criar(logado: false).S.CheckAsync(new[] { "home.view" }, null, default));
    }

    [Fact]
    public async Task Enforce_SemSchema_FalhaFechada()
    {
        var (s, r, _) = Criar();
        r.Schema = false;
        Assert.Equal(PermissionCheck.Unavailable, await s.CheckAsync(new[] { "home.view" }, null, default));
        var eff = await s.GetEffectiveAsync(null, default);
        Assert.Empty(eff.Permissions);
        Assert.Empty(eff.AnyProject);
    }

    [Fact]
    public async Task Enforce_UsuarioSemPerfil_Negado_LoginAutodeskNaoBasta()
    {
        var (s, _, _) = Criar();
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "home.view" }, null, default));
        Assert.Empty((await s.GetEffectiveAsync(ProjA, default)).Permissions);
    }

    [Fact]
    public async Task Enforce_PerfilDeProjeto_SoNoProjeto_EAnyProjectParaListagens()
    {
        var (s, r, _) = Criar();
        r.Dados[1] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("CDE A", 10, "cde.view", "cde.docs.view") });
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "cde.docs.view" }, ProjA, default));
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "cde.docs.view" }, ProjB, default));
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "cde.docs.view" }, null, default));
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "cde.view" }, null, default, anyProject: true));
        // Projeto desconhecido: só globais.
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "cde.view" }, "b.desconhecido", default));
        var eff = await s.GetEffectiveAsync(ProjA, default);
        Assert.Contains("cde.docs.view", eff.Permissions);
        Assert.Contains("cde.view", eff.AnyProject);
    }

    [Fact]
    public async Task MasterAdminPorConfiguracao_PorEmail()
    {
        var (s, _, _) = Criar(masters: new[] { "USER@na.com" });
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "permissions.manage" }, null, default));
        Assert.True((await s.GetEffectiveAsync(null, default)).IsMasterAdmin);
    }

    // ----------------------------------------------------------- sessão N&A (ETAPA 03A) ----
    [Fact]
    public async Task SessaoNa_SemAccessToken_UsaUserIdInterno_SemAutodesk()
    {
        var na = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(42, "sessao@na.com", "Sessão", "N&A") };
        var legado = new FakeUserAccessor { Lanca = true };
        var (s, r, _) = Criar(logado: false, acessor: legado, acessorNa: na);
        r.Usuarios[42] = new PermUser(42, null, "interno@na.com", "Interno", true, null);
        r.Dados[42] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("Leitura", null, "home.view") });

        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "home.view" }, null, default));
        Assert.Equal(0, legado.Chamadas);
        Assert.Equal(42, (await s.GetCurrentUserAsync(default))!.UserId);
    }

    [Fact]
    public async Task SessaoNa_InexistenteOuInativa_NaoAutentica()
    {
        var inexistente = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(42, "", "", "N&A") };
        var (s1, _, _) = Criar(enforce: false, logado: false, acessorNa: inexistente);
        Assert.Equal(PermissionCheck.Unauthenticated, await s1.CheckAsync(new[] { "home.view" }, null, default));

        var inativo = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(43, "", "", "N&A") };
        var (s2, r2, _) = Criar(enforce: false, logado: false, acessorNa: inativo);
        r2.Usuarios[43] = new PermUser(43, null, "inativo@na.com", "Inativo", false, null);
        Assert.Equal(PermissionCheck.Unauthenticated, await s2.CheckAsync(new[] { "home.view" }, null, default));
    }

    [Fact]
    public async Task SessaoNa_SafeModeECacheLegadoDivergente_UsaAuthUserId()
    {
        var na = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(42, "sessao@na.com", "Sessão", "N&A") };
        var (s, r, ctx) = Criar(enforce: false, logado: false, acessorNa: na);
        ctx.Session.SetInt32(NaSessionKeys.UserId, 42);
        ctx.Session.SetInt32("Perm:UserId", 99);
        ctx.Session.SetString("Perm:UserKey", "stale");
        r.Usuarios[42] = new PermUser(42, "acc-42", "interno@na.com", "Interno", true, null);
        r.Lanca = true; // safe mode N&A não deve precisar carregar permissões para decidir.

        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "qualquer.key" }, null, default));
        Assert.Equal(42, ctx.Session.GetInt32("Perm:UserId"));
        Assert.Equal("acc-42|interno@na.com", ctx.Session.GetString("Perm:UserKey"));
    }

    [Fact]
    public async Task SessaoLegadaAutodesk_ContinuaNoFallback()
    {
        var legado = new FakeUserAccessor { Identidade = new AutodeskUserInfo("acc-1", "user@na.com", "Usuário") };
        var (s, _, _) = Criar(enforce: false, logado: true, acessor: legado);

        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "home.view" }, null, default));
        Assert.NotNull(await s.GetCurrentUserAsync(default));
    }

    // -------------------------------------------------------- gate positivo (ETAPA 03B) ----
    [Fact]
    public async Task GatePositivo_ExigeUsuarioProjetoEUserProjectAtivos()
    {
        var (s, r, _) = CriarNa();
        Assert.Equal(PermissionCheck.Allowed, await s.CheckProjectAccessAsync(ProjA, default));

        r.AcessoProjetoPadrao = false;
        Assert.Equal(PermissionCheck.Denied, await s.CheckProjectAccessAsync(ProjA, default)); // sem UserProjects

        r.UserProjects[(42, ProjA)] = false;
        Assert.Equal(PermissionCheck.Denied, await s.CheckProjectAccessAsync(ProjA, default)); // UserProjects inativo

        r.UserProjects[(42, ProjA)] = true;
        r.ProjetosInativos.Add(ProjA);
        Assert.Equal(PermissionCheck.Denied, await s.CheckProjectAccessAsync(ProjA, default)); // projeto inativo

        r.ProjetosInativos.Clear();
        Assert.Equal(PermissionCheck.Denied, await s.CheckProjectAccessAsync("b.inexistente", default));
    }

    [Fact]
    public async Task GatePositivo_PrecedePerfilGlobalEOverrideAllow()
    {
        var (s, r, _) = CriarNa();
        r.AcessoProjetoPadrao = false;
        r.Dados[42] = PermissionResolverTests.Usuario(
            new[] { PermissionResolverTests.Perfil("Global", null, "production.view") },
            new[] { new PermissionOverride(null, "robot.run", PermissionEffects.Allow) });

        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "production.view" }, ProjA, default));
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "robot.run" }, ProjA, default));
    }

    [Fact]
    public async Task GatePositivo_MasterAdminExigeVinculoEDepoisPreservaMaster()
    {
        var (s, r, _) = CriarNa(masters: new[] { "interno@na.com" });
        r.AcessoProjetoPadrao = false;

        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "production.view" }, ProjA, default));

        r.UserProjects[(42, ProjA)] = true;
        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "production.view" }, ProjA, default));
        Assert.True((await s.GetEffectiveAsync(ProjA, default)).IsMasterAdmin);
    }

    [Fact]
    public async Task SafeMode_PermissaoContinuaCompativel_MasGateExplicitoPermaneceDisponivel()
    {
        var (s, r, _) = CriarNa(enforce: false);
        r.AcessoProjetoPadrao = false;

        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "production.view" }, ProjA, default));
        Assert.Equal(PermissionCheck.Denied, await s.CheckProjectAccessAsync(ProjA, default));
    }

    [Fact]
    public async Task EffectiveAnyProject_NaoEAutorizaProjetoEspecifico()
    {
        var (s, r, _) = CriarNa();
        r.AcessoProjetoPadrao = false;
        r.Dados[42] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("Projeto A", 10, "production.view") });

        Assert.Equal(PermissionCheck.Allowed, await s.CheckAsync(new[] { "production.view" }, null, default, anyProject: true));
        Assert.Equal(PermissionCheck.Denied, await s.CheckAsync(new[] { "production.view" }, ProjA, default));
    }

    // ------------------------------------------------------------------ [RequirePermission] ----
    private static AuthorizationFilterContext Filtro(DefaultHttpContext ctx, IPermissionService service, string? query = null)
    {
        ctx.RequestServices = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        if (query != null) ctx.Request.QueryString = new QueryString(query);
        return new AuthorizationFilterContext(new ActionContext(ctx, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    [Fact]
    public async Task Atributo_EndpointBloqueado_403_E_LeProjetoDaQuery()
    {
        var (s, r, ctx) = Criar();
        r.Dados[1] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("Robô", null, "robot.view") });
        var negar = Filtro(ctx, s);
        await new RequirePermissionAttribute("robot.run").OnAuthorizationAsync(negar);
        Assert.Equal(403, Assert.IsType<ObjectResult>(negar.Result).StatusCode);

        var permitir = Filtro(ctx, s);
        await new RequirePermissionAttribute("robot.view").OnAuthorizationAsync(permitir);
        Assert.Null(permitir.Result);

        // Nova requisição (o cache de permissões é por requisição): perfil só no projeto A.
        var (s2, r2, ctx2) = Criar();
        r2.Dados[1] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("CDE A", 10, "cde.docs.view") });
        var comProjeto = Filtro(ctx2, s2, $"?projectId={ProjA}");
        await new RequirePermissionAttribute("cde.docs.view").OnAuthorizationAsync(comProjeto);
        Assert.Null(comProjeto.Result);
        var (s3, r3, ctx3) = Criar();
        r3.Dados[1] = r2.Dados[1];
        var outroProjeto = Filtro(ctx3, s3, $"?projectId={ProjB}");
        await new RequirePermissionAttribute("cde.docs.view").OnAuthorizationAsync(outroProjeto);
        Assert.Equal(403, Assert.IsType<ObjectResult>(outroProjeto.Result).StatusCode);
    }

    [Fact]
    public async Task Atributo_SemSessao401_Indisponivel503()
    {
        var (s, _, ctx) = Criar(logado: false);
        var f = Filtro(ctx, s);
        await new RequirePermissionAttribute("home.view").OnAuthorizationAsync(f);
        Assert.IsType<UnauthorizedObjectResult>(f.Result);

        var (s2, r2, ctx2) = Criar();
        r2.Schema = false;
        var f2 = Filtro(ctx2, s2);
        await new RequirePermissionAttribute("home.view").OnAuthorizationAsync(f2);
        Assert.Equal(503, Assert.IsType<ObjectResult>(f2.Result).StatusCode);
    }

    // --------------------------------------------------------------- PermissionsController ----
    private static (PermissionsController C, FakePermissionRepository R) Controller(bool master = false)
    {
        var (s, r, ctx) = Criar(masters: master ? new[] { "user@na.com" } : null);
        r.Perfis.Add(new PermProfile(1, "MasterAdmin", null, true, true, true, Array.Empty<string>()));
        r.Perfis.Add(new PermProfile(2, "Leitura", null, true, false, false, new[] { "home.view" }));
        r.Dados[1] = PermissionResolverTests.Usuario(new[] { new ProfileAssignment(2, "Leitura", null, false, new HashSet<string> { "home.view", "permissions.manage" }) });
        return (new PermissionsController(s, r, NullLogger<PermissionsController>.Instance) { ControllerContext = new ControllerContext { HttpContext = ctx } }, r);
    }

    [Fact]
    public async Task Admin_NaoAlteraOProprioAcesso()
    {
        var (c, r) = Controller(master: true); // nem MasterAdmin altera o próprio acesso
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.PerfisUsuario(1, null, new PerfisUsuarioRequest(new[] { 2 }), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.OverridesUsuario(1, null, new OverridesRequest(new[] { new PermissionOverrideDto("robot.run", "ALLOW") }), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.AtivoNoProjeto(1, ProjA, new AtivoRequest(true), default)).StatusCode);
        Assert.DoesNotContain(r.Escritas, e => !e.StartsWith("Audit"));
    }

    [Fact]
    public async Task Admin_NaoMaster_NaoAtribuiMasterAdmin_NemAllowDePermissionsManage_NemEditaPerfilProprio()
    {
        var (c, r) = Controller();
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.PerfisUsuario(7, null, new PerfisUsuarioRequest(new[] { 1 }), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.OverridesUsuario(7, null,
            new OverridesRequest(new[] { new PermissionOverrideDto("permissions.manage", "ALLOW") }), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.PermissoesPerfil(2, new PermissoesRequest(new[] { "robot.run" }), default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.PermissoesPerfil(1, new PermissoesRequest(new[] { "robot.run" }), default)).StatusCode); // sistema
        Assert.Empty(r.Escritas);
    }

    [Fact]
    public async Task Admin_ValidaCatalogoEEfeito_EAudita()
    {
        var (c, r) = Controller(master: true);
        Assert.IsType<BadRequestObjectResult>(await c.OverridesUsuario(7, null, new OverridesRequest(new[] { new PermissionOverrideDto("inventada.view", "ALLOW") }), default));
        Assert.IsType<BadRequestObjectResult>(await c.OverridesUsuario(7, null, new OverridesRequest(new[] { new PermissionOverrideDto("home.view", "MAYBE") }), default));
        Assert.IsType<BadRequestObjectResult>(await c.PerfisUsuario(7, ProjA, new PerfisUsuarioRequest(new[] { 1 }), default)); // MasterAdmin só global
        Assert.IsType<OkResult>(await c.OverridesUsuario(7, ProjA, new OverridesRequest(new[] { new PermissionOverrideDto("production.managerial.rnc.view", "DENY") }), default));
        Assert.Contains("OverridesUsuario", r.Escritas);
        Assert.Contains("Audit:OVERRIDE_CHANGED", r.Escritas);
    }

    [Fact]
    public async Task Admin_SchemaAusente_503ComOrientacao()
    {
        var (c, r) = Controller(master: true);
        r.Schema = false;
        var res = Assert.IsType<ObjectResult>(await c.Perfis(default));
        Assert.Equal(503, res.StatusCode);
    }
}
