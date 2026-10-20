using System.Net;
using APS.WebApp.Areas.Authentication.Controllers;
using APS.WebApp.Controllers;
using APS.WebApp.Services;
using APS.WebApp.Services.AutodeskAuth;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Planning.Tests.Permissions;
using NA_Connect.Services;

namespace NA_Connect.Planning.Tests.LocalAuth;

/// <summary>ETAPA 06A — login N&A local (e-mail + senha) sem Autodesk.</summary>
public sealed class NaLocalAuthTests
{
    private const string Senha = "Correta#2026";
    private static readonly DateTimeOffset Inicio = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    // ---------- A / B / C ----------

    [Fact]
    public async Task A_UsuarioAtivoSenhaCorreta_200ComSessaoNa()
    {
        var t = await Cenario.ComSenhaAsync();

        var result = await t.LoginAsync("Interno@NA.com ", Senha);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<NaLoginResponse>(ok.Value);
        Assert.Equal(new NaLoginResponse(42, "interno@na.com", "Usuário Interno", "N&A", false), dto);
        Assert.Equal(42, t.Session.GetInt32(NaSessionKeys.UserId));
        Assert.Equal("interno@na.com", t.Session.GetString(NaSessionKeys.Email));
        Assert.Equal("Usuário Interno", t.Session.GetString(NaSessionKeys.Nome));
        Assert.Equal("N&A", t.Session.GetString(NaSessionKeys.Provider));
    }

    [Fact]
    public async Task B_LoginLocalFuncionaSemAccessToken()
    {
        var t = await Cenario.ComSenhaAsync();

        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        Assert.Null(t.Session.GetString("AccessToken"));
        Assert.Equal(42, await t.UserIdNaAsync());
    }

    [Fact]
    public async Task C_LoginLocalNaoCriaChavesAutodesk_ENaoHerdaTokensAnteriores()
    {
        var t = await Cenario.ComSenhaAsync();
        // Sessão anterior com outro usuário Autodesk no mesmo navegador.
        AutodeskUserTokenSession.Save(t.Session, "access-outro", "refresh-outro", Inicio.AddHours(1));
        t.Session.SetString("Auth:AutodeskUserId", "acc-outro");

        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        foreach (var chave in new[] { "AccessToken", "RefreshToken", "Auth:AutodeskUserId", AutodeskSessionKeys.Connected,
                     AutodeskSessionKeys.AccessToken, AutodeskSessionKeys.RefreshToken, AutodeskSessionKeys.ExpiresAt, AutodeskSessionKeys.UserId })
            Assert.False(t.Session.Keys.Contains(chave), chave);
        Assert.Equal(new[] { NaSessionKeys.Email, NaSessionKeys.Nome, NaSessionKeys.Provider, NaSessionKeys.UserId },
            t.Session.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    // ---------- D / E / F / G ----------

    [Fact]
    public async Task D_SenhaErrada_401EIncrementaFalhas()
    {
        var t = await Cenario.ComSenhaAsync();

        var result = await t.LoginAsync("interno@na.com", "errada-123");

        AssertGenerico(result);
        Assert.Equal(1, t.Repo.Credenciais[42].FailedAttempts);
        Assert.False(t.Session.Keys.Any());
    }

    [Fact]
    public async Task E_CincoFalhas_BloqueiaInclusiveSenhaCorreta()
    {
        var t = await Cenario.ComSenhaAsync();

        for (var i = 0; i < NaLocalAuthPolicy.MaxFailedAttempts; i++)
            AssertGenerico(await t.LoginAsync("interno@na.com", "errada-123"));

        var credencial = t.Repo.Credenciais[42];
        Assert.Equal(5, credencial.FailedAttempts);
        Assert.Equal(Inicio.UtcDateTime + NaLocalAuthPolicy.LockoutDuration, credencial.LockoutUntil);

        var bloqueado = await t.LoginAsync("interno@na.com", Senha);
        AssertGenerico(bloqueado);
        Assert.Null(t.Session.GetInt32(NaSessionKeys.UserId));
        Assert.Equal(5, t.Repo.Credenciais[42].FailedAttempts); // tentativa bloqueada não verifica nem conta
    }

    [Fact]
    public async Task F_AposBloqueioExpirado_LoginCorretoZeraContador()
    {
        var t = await Cenario.ComSenhaAsync();
        for (var i = 0; i < NaLocalAuthPolicy.MaxFailedAttempts; i++) await t.LoginAsync("interno@na.com", "errada-123");

        t.Relogio.Avancar(NaLocalAuthPolicy.LockoutDuration + TimeSpan.FromSeconds(1));
        var result = await t.LoginAsync("interno@na.com", Senha);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, t.Repo.Credenciais[42].FailedAttempts);
        Assert.Null(t.Repo.Credenciais[42].LockoutUntil);
    }

    [Fact]
    public async Task F2_FalhaAposBloqueioExpirado_RecomecaContagem()
    {
        var t = await Cenario.ComSenhaAsync();
        for (var i = 0; i < NaLocalAuthPolicy.MaxFailedAttempts; i++) await t.LoginAsync("interno@na.com", "errada-123");

        t.Relogio.Avancar(NaLocalAuthPolicy.LockoutDuration + TimeSpan.FromSeconds(1));
        await t.LoginAsync("interno@na.com", "errada-123");

        Assert.Equal(1, t.Repo.Credenciais[42].FailedAttempts);
        Assert.Null(t.Repo.Credenciais[42].LockoutUntil);
    }

    [Fact]
    public async Task G_EmailInexistente_MesmaRespostaQueSenhaIncorreta()
    {
        var t = await Cenario.ComSenhaAsync();

        var inexistente = Assert.IsType<UnauthorizedObjectResult>(await t.LoginAsync("ninguem@na.com", "errada-123"));
        var senhaErrada = Assert.IsType<UnauthorizedObjectResult>(await t.LoginAsync("interno@na.com", "errada-123"));

        Assert.Equal(senhaErrada.StatusCode, inexistente.StatusCode);
        Assert.Equal(Corpo(senhaErrada), Corpo(inexistente));
    }

    // ---------- H / I / J ----------

    [Fact]
    public async Task H_UsuarioInativo_NaoAutentica()
    {
        var t = await Cenario.ComSenhaAsync();
        t.Repo.Usuarios[42] = t.Repo.Usuarios[42] with { Ativo = false };

        AssertGenerico(await t.LoginAsync("interno@na.com", Senha));
        Assert.False(t.Session.Keys.Any());
    }

    [Fact]
    public async Task I_UsuarioSemCredencial_NaoAutentica()
    {
        var t = new Cenario();

        AssertGenerico(await t.LoginAsync("interno@na.com", Senha));
        Assert.False(t.Session.Keys.Any());
    }

    [Fact]
    public async Task J_HashNaoContemSenha_FormatoIdentityV3()
    {
        var t = await Cenario.ComSenhaAsync();

        var hash = t.Repo.Credenciais[42].PasswordHash;

        Assert.DoesNotContain(Senha, hash);
        Assert.NotEqual(Senha, hash);
        Assert.Equal(0x01, Convert.FromBase64String(hash)[0]); // formato V3 do PasswordHasher (PBKDF2 + salt)
        Assert.True(hash.Length <= 512);
        // Mesmo valor gera hash diferente (salt aleatório).
        await t.Servico.SetPasswordAsync(42, Senha, false, default);
        Assert.NotEqual(hash, t.Repo.Credenciais[42].PasswordHash);
    }

    [Fact]
    public async Task J2_Unavailable_503SemCriarSessao()
    {
        var t = new Cenario();
        t.Repo.Lanca = true;

        var result = Assert.IsType<ObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        Assert.Equal(503, result.StatusCode);
        Assert.False(t.Session.Keys.Any());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("        ")]
    [InlineData("curta1")]
    [InlineData("aaaaaaaa")]
    [InlineData("12345678")]
    [InlineData("password")]
    public void PoliticaDeSenha_RejeitaVaziaCurtaOuTrivial(string? senha) =>
        Assert.NotNull(NaLocalAuthPolicy.ValidarSenha(senha));

    [Fact]
    public void PoliticaDeSenha_AceitaSenhaRazoavel() => Assert.Null(NaLocalAuthPolicy.ValidarSenha("Obra-Norte-77"));

    // ---------- K / L ----------

    [Fact]
    public async Task K_EndpointAdministrativo_DefineSenhaELoginFunciona()
    {
        var t = new Cenario();
        var (controller, permRepo) = t.PermissionsController(adminComManage: true);

        var result = await controller.DefinirSenha(42, new DefinirSenhaRequest("Nova-Senha-2026", MustChangePassword: true), t.Servico, default);

        Assert.IsType<NoContentResult>(result);
        Assert.True(t.Repo.Credenciais[42].MustChangePassword);
        Assert.DoesNotContain("Nova-Senha-2026", string.Join("|", permRepo.Escritas));
        var ok = Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", "Nova-Senha-2026"));
        Assert.True(Assert.IsType<NaLoginResponse>(ok.Value).MustChangePassword);
    }

    [Fact]
    public async Task K2_EndpointAdministrativo_SenhaFracaOuUsuarioInexistente()
    {
        var t = new Cenario();
        var (controller, _) = t.PermissionsController(adminComManage: true);

        Assert.IsType<BadRequestObjectResult>(await controller.DefinirSenha(42, new DefinirSenhaRequest("curta"), t.Servico, default));
        Assert.IsType<NotFoundObjectResult>(await controller.DefinirSenha(999, new DefinirSenhaRequest("Nova-Senha-2026"), t.Servico, default));
        Assert.Empty(t.Repo.Credenciais);
    }

    [Fact]
    public async Task K3_EndpointAdministrativo_RedefineZeraBloqueio()
    {
        var t = await Cenario.ComSenhaAsync();
        for (var i = 0; i < NaLocalAuthPolicy.MaxFailedAttempts; i++) await t.LoginAsync("interno@na.com", "errada-123");
        var (controller, _) = t.PermissionsController(adminComManage: true);

        Assert.IsType<NoContentResult>(await controller.DefinirSenha(42, new DefinirSenhaRequest("Nova-Senha-2026"), t.Servico, default));

        Assert.Equal(0, t.Repo.Credenciais[42].FailedAttempts);
        Assert.Null(t.Repo.Credenciais[42].LockoutUntil);
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", "Nova-Senha-2026"));
    }

    [Fact]
    public async Task L_EndpointAdministrativoSemPermissionsManage_Negado_MesmoComEnforceFalse()
    {
        var t = new Cenario();
        var (controller, _) = t.PermissionsController(adminComManage: false);

        var result = Assert.IsType<ObjectResult>(
            await controller.DefinirSenha(42, new DefinirSenhaRequest("Nova-Senha-2026"), t.Servico, default));

        Assert.Equal(403, result.StatusCode);
        Assert.Empty(t.Repo.Credenciais);
    }

    [Fact]
    public async Task L2_EndpointAdministrativo_NaoAlteraAPropriaSenha()
    {
        var t = new Cenario();
        var (controller, _) = t.PermissionsController(adminComManage: true);

        var result = Assert.IsType<ObjectResult>(
            await controller.DefinirSenha(Cenario.AdminId, new DefinirSenhaRequest("Nova-Senha-2026"), t.Servico, default));

        Assert.Equal(403, result.StatusCode);
        Assert.Empty(t.Repo.Credenciais);
    }

    [Fact]
    public async Task L3_MasterAdminConfigurado_PodeDefinirSenha()
    {
        var t = new Cenario();
        var (controller, _) = t.PermissionsController(adminComManage: false, masterAdmin: true);

        Assert.IsType<NoContentResult>(await controller.DefinirSenha(42, new DefinirSenhaRequest("Nova-Senha-2026"), t.Servico, default));
    }

    // ---------- 06B: cadastro manual de usuário N&A (sem ACC) ----------

    [Fact]
    public async Task S1_CadastroManualSemPermissionsManage_Negado_MesmoComEnforceFalse()
    {
        var t = new Cenario();
        var (controller, permRepo) = t.PermissionsController(adminComManage: false);

        var result = Assert.IsType<ObjectResult>(
            await controller.CadastrarUsuario(new UsuarioRequest("teste.na@example.invalid", "Usuário Teste N&A"), default));

        Assert.Equal(403, result.StatusCode);
        Assert.Empty(permRepo.Escritas);
    }

    [Fact]
    public async Task S2_CadastroManualComPermissionsManage_CriaUsuarioSemVinculo()
    {
        var t = new Cenario();
        var (controller, permRepo) = t.PermissionsController(adminComManage: true);

        var ok = Assert.IsType<OkObjectResult>(
            await controller.CadastrarUsuario(new UsuarioRequest("teste.na@example.invalid", "Usuário Teste N&A"), default));

        Assert.Equal(50, ok.Value!.GetType().GetProperty("userId")!.GetValue(ok.Value));
        Assert.Empty(permRepo.UserProjects); // cadastro não concede projeto
    }

    [Fact]
    public async Task S3_UsuarioNaSemAccUserId_LoginCriaSomenteSessaoNa()
    {
        var t = new Cenario();
        t.Repo.Usuarios[77] = new PermUser(77, null, "teste.na@example.invalid", "Usuário Teste N&A", true, null);
        await t.Servico.SetPasswordAsync(77, "Teste-NA-Local-1", false, default);

        var ok = Assert.IsType<OkObjectResult>(await t.LoginAsync("teste.na@example.invalid", "Teste-NA-Local-1"));

        Assert.Equal(77, Assert.IsType<NaLoginResponse>(ok.Value).UserId);
        Assert.DoesNotContain(t.Session.Keys, k => k.StartsWith("Autodesk:", StringComparison.Ordinal) || k is "AccessToken" or "RefreshToken" or "Auth:AutodeskUserId");
        Assert.Null(t.Repo.Usuarios[77].AccUserId);
    }

    // ---------- Primeiro acesso: troca obrigatória da própria senha ----------

    private const string SenhaNova = "Primeiro-Acesso-2026";

    private static async Task<Cenario> ComTrocaPendenteAsync()
    {
        var t = new Cenario();
        await t.Servico.SetPasswordAsync(42, Senha, mustChangePassword: true, default);
        return t;
    }

    private static bool Autenticado(OAuthController c) =>
        (bool)Assert.IsType<OkObjectResult>(c.CheckStatus()).Value!.GetType().GetProperty("isAuthenticated")!.GetValue(
            Assert.IsType<OkObjectResult>(c.CheckStatus()).Value)!;

    [Fact]
    public async Task F_G_LoginComSenhaInicial_MustChangeTrue_SessaoPermanece_MasSistemaNaoLibera()
    {
        var t = await ComTrocaPendenteAsync();

        var ok = Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        Assert.True(Assert.IsType<NaLoginResponse>(ok.Value).MustChangePassword);
        Assert.Equal(42, t.Session.GetInt32(NaSessionKeys.UserId));
        Assert.Equal("true", t.Session.GetString(NaLocalAuthPolicy.MustChangePasswordSessionKey));
        Assert.False(Autenticado(t.OAuth())); // não dá para pular a troca indo direto para "/"
    }

    [Fact]
    public async Task H_I_J_K_L_N_TrocaPropriaSemPermissionsManage_AtualizaHash_LiberaSemSegundoLogin()
    {
        var t = await ComTrocaPendenteAsync();
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));
        var hashAntes = t.Repo.Credenciais[42].PasswordHash;
        t.Relogio.Avancar(TimeSpan.FromMinutes(3));

        // Sem PermissionsController/permissions.manage: o controller de login só conhece a sessão.
        var result = await t.Controller.ChangePassword(new NaChangePasswordRequest(SenhaNova, SenhaNova), default);

        Assert.IsType<NoContentResult>(result);
        var depois = t.Repo.Credenciais[42];
        Assert.NotEqual(hashAntes, depois.PasswordHash);                                   // I
        Assert.False(depois.MustChangePassword);                                            // L
        Assert.Equal(0, depois.FailedAttempts);
        Assert.Null(depois.LockoutUntil);
        Assert.Equal(Inicio.UtcDateTime.AddMinutes(3), depois.PasswordUpdatedAt);
        Assert.DoesNotContain(SenhaNova, depois.PasswordHash);
        Assert.Equal(42, t.Session.GetInt32(NaSessionKeys.UserId));                        // G
        Assert.Null(t.Session.GetString(NaLocalAuthPolicy.MustChangePasswordSessionKey));
        Assert.True(Autenticado(t.OAuth()));                                                // N: entra sem segundo login

        AssertGenerico(await t.LoginAsync("interno@na.com", Senha));                        // J
        var novo = Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", SenhaNova)); // K
        Assert.False(Assert.IsType<NaLoginResponse>(novo.Value).MustChangePassword);
    }

    [Fact]
    public async Task M_TrocaPropria_SoAlteraOUsuarioDaSessao()
    {
        var t = await ComTrocaPendenteAsync();
        t.Repo.Usuarios[77] = new PermUser(77, null, "outro@na.com", "Outro", true, null);
        await t.Servico.SetPasswordAsync(77, "Outro-Usuario-2026", mustChangePassword: true, default);
        var hashOutro = t.Repo.Credenciais[77].PasswordHash;
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        Assert.IsType<NoContentResult>(await t.Controller.ChangePassword(new NaChangePasswordRequest(SenhaNova, SenhaNova), default));

        Assert.Equal(hashOutro, t.Repo.Credenciais[77].PasswordHash);
        Assert.True(t.Repo.Credenciais[77].MustChangePassword);
        Assert.DoesNotContain(typeof(NaChangePasswordRequest).GetProperties(), p => p.Name.Contains("UserId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task M2_TrocaSemSessao_401_ESemPendencia_409()
    {
        var t = await Cenario.ComSenhaAsync(); // MustChangePassword = false
        Assert.IsType<UnauthorizedObjectResult>(await t.Controller.ChangePassword(new NaChangePasswordRequest(SenhaNova, SenhaNova), default));

        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));
        var hash = t.Repo.Credenciais[42].PasswordHash;
        var conflito = Assert.IsType<ConflictObjectResult>(await t.Controller.ChangePassword(new NaChangePasswordRequest(SenhaNova, SenhaNova), default));
        Assert.Equal(409, conflito.StatusCode);
        Assert.Equal(hash, t.Repo.Credenciais[42].PasswordHash);
    }

    [Fact]
    public async Task TrocaPropria_ValidacoesMantemPendencia()
    {
        var t = await ComTrocaPendenteAsync();
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));
        var hash = t.Repo.Credenciais[42].PasswordHash;

        Assert.IsType<BadRequestObjectResult>(await t.Controller.ChangePassword(new NaChangePasswordRequest(SenhaNova, "Diferente-2026"), default));
        Assert.IsType<BadRequestObjectResult>(await t.Controller.ChangePassword(new NaChangePasswordRequest("curta", "curta"), default));
        Assert.IsType<BadRequestObjectResult>(await t.Controller.ChangePassword(new NaChangePasswordRequest(Senha, Senha), default)); // igual à inicial

        Assert.Equal(hash, t.Repo.Credenciais[42].PasswordHash);
        Assert.True(t.Repo.Credenciais[42].MustChangePassword);
        Assert.Equal("true", t.Session.GetString(NaLocalAuthPolicy.MustChangePasswordSessionKey));
    }

    [Fact]
    public async Task PermissionsMe_ComSessaoNaSemAccessToken_200_SemSessao_401()
    {
        var t = new Cenario();
        var (controller, _) = t.PermissionsController(adminComManage: true);
        var sessao = controller.ControllerContext.HttpContext.Session;

        Assert.IsType<UnauthorizedObjectResult>(await controller.Me(null, default));

        sessao.SetInt32(NaSessionKeys.UserId, Cenario.AdminId);
        Assert.Null(sessao.GetString("AccessToken"));
        Assert.IsType<OkObjectResult>(await controller.Me(null, default));
    }

    [Fact]
    public async Task I_LoteDePerfis_SemPermissionsManage_403_MesmoComEnforceFalse()
    {
        var t = new Cenario();
        var lote = new APS.WebApp.Services.Permissions.Bulk.BulkPermissionService(new NA_Connect.Planning.Tests.Permissions.BulkPermissionTests.FakeBulkRepo(),
            new FakePermissionRepository(), new NA_Connect.Planning.Tests.Permissions.PermissionSyncTests.FakeSyncRepository());
        var req = new APS.WebApp.Services.Permissions.Bulk.BulkPerfisRequest(new[] { 42 }, new[] { "b.00000000-0000-0000-0000-000000000001" }, new[] { 2 }, "ADD");

        var (semPermissao, _) = t.PermissionsController(adminComManage: false);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await semPermissao.LotePerfisPrevia(req, lote, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await semPermissao.LotePerfisAplicar(req, lote, default)).StatusCode);

        var (admin, _) = t.PermissionsController(adminComManage: true);
        var r = await admin.LotePerfisPrevia(req, lote, default);
        Assert.False(r is ObjectResult { StatusCode: 403 }, "com permissions.manage a checagem estrita não bloqueia");
    }

    // ---------- M / N / O / P ----------

    [Fact]
    public async Task M_UsuarioLocalSemUserProjects_MeProjectsVazio_ELoginNaoCriaVinculo()
    {
        var t = await Cenario.ComSenhaAsync();
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));
        var permRepo = new FakePermissionRepository();
        permRepo.Usuarios[42] = t.Repo.Usuarios[42];
        permRepo.ProjetosPermitidos.Add(new(1, "b.one", "Projeto 1", 1, true));

        var me = new MeProjectsController(new SessionNaCurrentUserAccessor(new FixedAccessor(t.Context)), permRepo);
        var ok = Assert.IsType<OkObjectResult>((await me.Projects(default)).Result);

        Assert.Empty(Assert.IsType<MeProjectsController.MeProjectDto[]>(ok.Value));
        Assert.Empty(permRepo.UserProjects);
        Assert.Empty(permRepo.Escritas);
    }

    [Fact]
    public async Task N_CheckStatus_SemAutodesk()
    {
        var t = await Cenario.ComSenhaAsync();
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        var ok = Assert.IsType<OkObjectResult>(t.OAuth().CheckStatus());

        Assert.True((bool)ok.Value!.GetType().GetProperty("isAuthenticated")!.GetValue(ok.Value)!);
        Assert.Equal(0, t.Http.Chamadas);
    }

    [Fact]
    public async Task O_GetUserName_SemAutodesk()
    {
        var t = await Cenario.ComSenhaAsync();
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));

        var result = await t.OAuth().GetUserName(new FakeUserAccessor { Lanca = true },
            new SessionNaCurrentUserAccessor(new FixedAccessor(t.Context)), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Usuário Interno", ok.Value!.GetType().GetProperty("userName")!.GetValue(ok.Value));
        Assert.Equal("interno@na.com", ok.Value!.GetType().GetProperty("email")!.GetValue(ok.Value));
        Assert.Equal(0, t.Http.Chamadas);
    }

    [Fact]
    public async Task P_Logout_LimpaSessaoLocal()
    {
        var t = await Cenario.ComSenhaAsync();
        Assert.IsType<OkObjectResult>(await t.LoginAsync("interno@na.com", Senha));
        var oauth = t.OAuth();

        oauth.Logout();

        Assert.False(t.Session.Keys.Any());
        var ok = Assert.IsType<OkObjectResult>(oauth.CheckStatus());
        Assert.False((bool)ok.Value!.GetType().GetProperty("isAuthenticated")!.GetValue(ok.Value)!);
    }

    // ---------- infraestrutura ----------

    private static void AssertGenerico(IActionResult result)
    {
        var r = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, r.StatusCode);
        Assert.Equal(NaAuthController.MensagemGenerica, r.Value!.GetType().GetProperty("message")!.GetValue(r.Value));
    }

    private static string Corpo(ObjectResult r) => System.Text.Json.JsonSerializer.Serialize(r.Value);

    private sealed class Cenario
    {
        public const int AdminId = 1;
        public FakeCredentialRepository Repo { get; } = new();
        public Relogio Relogio { get; } = new(Inicio);
        public DefaultHttpContext Context { get; } = new() { Session = new MemorySessionPerm() };
        public ISession Session => Context.Session;
        public NaLocalAuthService Servico { get; }
        public NaAuthController Controller { get; }
        public ContadorHttp Http { get; } = new();

        public Cenario()
        {
            Repo.Usuarios[42] = new PermUser(42, null, "interno@na.com", "Usuário Interno", true, null);
            Repo.Usuarios[AdminId] = new PermUser(AdminId, null, "admin@na.com", "Admin", true, null);
            Servico = new NaLocalAuthService(Repo, NullLogger<NaLocalAuthService>.Instance, null, Relogio);
            Controller = new NaAuthController(Servico, NullLogger<NaAuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Context }
            };
        }

        public static async Task<Cenario> ComSenhaAsync()
        {
            var t = new Cenario();
            Assert.Equal(NaSetPasswordStatus.Success, (await t.Servico.SetPasswordAsync(42, Senha, false, default)).Status);
            return t;
        }

        public Task<IActionResult> LoginAsync(string email, string senha) =>
            Controller.Login(new NaLoginRequest(email, senha), default);

        public async Task<int?> UserIdNaAsync() =>
            (await new SessionNaCurrentUserAccessor(new FixedAccessor(Context)).GetAsync())?.UserId;

        public OAuthController OAuth()
        {
            var client = new HttpClient(Http);
            var config = new ConfigurationBuilder().Build();
            var autodesk = new AutodeskApiService(client, new TokenManager(client, config, new MemoryCache(new MemoryCacheOptions())),
                config, NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
            return new OAuthController(config, autodesk, client, new FakePermissionRepository(), NullLogger<OAuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Context }
            };
        }

        /// <summary>PermissionsController real com PermissionService real em Enforce=false; sessão N&A do administrador.</summary>
        public (PermissionsController Controller, FakePermissionRepository Repo) PermissionsController(bool adminComManage, bool masterAdmin = false)
        {
            var ctx = new DefaultHttpContext { Session = new MemorySessionPerm() };
            var permRepo = new FakePermissionRepository();
            permRepo.Usuarios[AdminId] = Repo.Usuarios[AdminId];
            permRepo.Usuarios[42] = Repo.Usuarios[42];
            permRepo.Dados[AdminId] = PermissionResolverTests.Usuario(adminComManage
                ? new[] { PermissionResolverTests.Perfil("Administrador", null, "permissions.manage") }
                : new[] { PermissionResolverTests.Perfil("Leitura", null, "production.view") });
            var cfg = new Dictionary<string, string?> { ["Permissions:Enforce"] = "false" };
            if (masterAdmin) cfg["Permissions:MasterAdmins:0"] = "admin@na.com";
            var na = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(AdminId, "admin@na.com", "Admin", "N&A") };
            var service = new PermissionService(permRepo, na, new FakeUserAccessor(),
                new ConfigurationBuilder().AddInMemoryCollection(cfg).Build(), new FixedAccessor(ctx), NullLogger<PermissionService>.Instance);
            var controller = new PermissionsController(service, permRepo, NullLogger<PermissionsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = ctx }
            };
            return (controller, permRepo);
        }
    }

    private sealed class FixedAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }

    private sealed class Relogio(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _agora = inicio;
        public void Avancar(TimeSpan delta) => _agora += delta;
        public override DateTimeOffset GetUtcNow() => _agora;
    }

    /// <summary>Qualquer chamada HTTP (Autodesk) é contada; login local não deve gerar nenhuma.</summary>
    private sealed class ContadorHttp : HttpMessageHandler
    {
        public int Chamadas;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Chamadas);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    /// <summary>auth.UserCredentials + perm.Users em memória; falhas seguem NaLocalAuthPolicy.AposFalha (mesma regra do SQL).</summary>
    private sealed class FakeCredentialRepository : INaLocalCredentialRepository
    {
        public Dictionary<int, PermUser> Usuarios { get; } = new();
        public Dictionary<int, NaLocalCredential> Credenciais { get; } = new();
        public bool Lanca { get; set; }

        private void Falhar() { if (Lanca) throw new InvalidOperationException("banco fora"); }

        public Task<PermUser?> ObterUsuarioPorEmailAsync(string email, CancellationToken ct)
        {
            Falhar();
            return Task.FromResult(Usuarios.Values.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct)
        {
            Falhar();
            return Task.FromResult(Usuarios.TryGetValue(userId, out var u) ? u : null);
        }

        public Task<NaLocalCredential?> ObterCredencialAsync(int userId, CancellationToken ct)
        {
            Falhar();
            return Task.FromResult(Credenciais.TryGetValue(userId, out var c) ? c : null);
        }

        public Task<(int FailedAttempts, DateTime? LockoutUntil)> RegistrarFalhaAsync(int userId, DateTime agoraUtc, CancellationToken ct)
        {
            var c = Credenciais[userId];
            var (falhas, bloqueio) = NaLocalAuthPolicy.AposFalha(c.FailedAttempts, c.LockoutUntil, agoraUtc);
            Credenciais[userId] = c with { FailedAttempts = falhas, LockoutUntil = bloqueio };
            return Task.FromResult((falhas, bloqueio));
        }

        public Task RegistrarSucessoAsync(int userId, CancellationToken ct)
        {
            Credenciais[userId] = Credenciais[userId] with { FailedAttempts = 0, LockoutUntil = null };
            return Task.CompletedTask;
        }

        public Task DefinirSenhaAsync(int userId, string passwordHash, bool mustChangePassword, DateTime agoraUtc, CancellationToken ct)
        {
            Falhar();
            Credenciais[userId] = new NaLocalCredential(userId, passwordHash, 0, null, agoraUtc, mustChangePassword);
            return Task.CompletedTask;
        }
    }
}
