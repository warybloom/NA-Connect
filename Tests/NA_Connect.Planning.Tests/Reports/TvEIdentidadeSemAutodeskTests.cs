using System.Text.Json;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.ProjectIdentity;
using APS.WebApp.Services.Tv;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Planning.Tests.Permissions;

namespace NA_Connect.Planning.Tests.Reports;

/// <summary>Telas para TV e Logo do Projeto para usuário N&A sem Autodesk (sem hubId, sem AccessToken).</summary>
public sealed class TvEIdentidadeSemAutodeskTests : IDisposable
{
    private const string Projeto = "b.c3e20d9d-779e-423c-9381-abec38cf11cb";
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tv-na-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best-effort */ } }

    // ---------- Telas para TV ----------

    private sealed class Perms : IPermissionService
    {
        public bool Enforced => false;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false) => throw new NotSupportedException();
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) =>
            Task.FromResult<CurrentPermissionUser?>(new(42, "", "teste.na@example.invalid", "Usuário Teste N&A", false));
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult<int?>(null);
    }

    private TvConfigurationController Tv() =>
        new(new TvConfigurationStore(Path.Combine(_dir, "tv-configuration.json")), new Perms(), NullLogger<TvConfigurationController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static SalvarTvConfiguration Payload(long revision, string hubId) => JsonSerializer.Deserialize<SalvarTvConfiguration>($$"""
        { "revision": {{revision}}, "presentations": [ { "id": "sala", "name": "Sala", "secondsPerScreen": 30,
          "slides": [ { "screenId": "ldp", "hubId": "{{hubId}}", "projectId": "{{Projeto}}", "projectName": "BML011.013 - Projeto" } ] } ] }
        """, Web)!;

    [Fact]
    public async Task TV_UsuarioNaSemHub_SalvaEReabre_ProjectIdAccPreservado()
    {
        Assert.IsType<OkObjectResult>(await Tv().Save(Payload(0, ""), default));

        var corpo = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(await Tv().Get(default)).Value, Web)).RootElement;
        var slide = corpo.GetProperty("configuration").GetProperty("presentations")[0].GetProperty("slides")[0];
        Assert.Equal(Projeto, slide.GetProperty("projectId").GetString());
        Assert.Equal("", slide.GetProperty("hubId").GetString()); // vazio, nunca um hub inventado
    }

    [Fact]
    public async Task TV_ConfiguracaoComHubAntigo_ContinuaValida_HubInvalidoRecusado()
    {
        Assert.IsType<OkObjectResult>(await Tv().Save(Payload(0, Hub), default));
        Assert.IsType<BadRequestObjectResult>(await Tv().Save(Payload(1, "nao-e-um-hub"), default));
    }

    // ---------- Projetos elegíveis da TV (fonte administrativa interna) ----------

    private sealed class Usuario(CurrentPermissionUser? eu) : IPermissionService
    {
        public bool Enforced => false;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false) => throw new NotSupportedException();
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => Task.FromResult(eu);
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult<int?>(null);
    }

    private sealed class Elegiveis : ITvProjetosElegiveisRepository
    {
        public int Chamadas;
        public Task<IReadOnlyList<TvProjetoElegivel>> ListarAsync(CancellationToken ct)
        {
            Chamadas++;
            return Task.FromResult<IReadOnlyList<TvProjetoElegivel>>(new[]
            {
                new TvProjetoElegivel(244, "b.22222222-2222-2222-2222-222222222222", "CHS001 - ETHANOL PLANT", 2, true),
                new TvProjetoElegivel(232, Projeto, "BML011.013 - P-0602.1-MODULAR PROJECT", 1, true),
            });
        }
    }

    private async Task<(IActionResult R, Elegiveis Fonte)> ProjetosTv(CurrentPermissionUser? eu, bool comChave)
    {
        var repo = new FakePermissionRepository();
        if (eu?.UserId is int id)
            repo.Dados[id] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("Perfil", null, comChave ? "settings.tv.configure" : "production.view") });
        // Nenhum UserProjects para ninguém: a configuração administrativa não depende disso.
        var fonte = new Elegiveis();
        var controller = new TvConfigurationController(new TvConfigurationStore(Path.Combine(_dir, "tv.json")), new Usuario(eu), NullLogger<TvConfigurationController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (await controller.Projects(fonte, repo, default), fonte);
    }

    [Fact]
    public async Task TV_AdminComChaveSemUserProjects_ListaProjetosElegiveis_ComProjectIdAcc()
    {
        var (r, fonte) = await ProjetosTv(new CurrentPermissionUser(7, "", "admin@na.com", "Admin", false), comChave: true);

        var json = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(r).Value, Web);
        Assert.Contains("\"projectIdAcc\":\"" + Projeto + "\"", json);
        Assert.Contains("\"nomeProjeto\":\"CHS001 - ETHANOL PLANT\"", json);
        Assert.Contains("\"isAtivo\":true", json);
        Assert.DoesNotContain("hubId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fonte.Chamadas);
    }

    [Fact]
    public async Task TV_SemChaveSettingsTvConfigure_403_MesmoComEnforceFalse_SemConsultarFonte()
    {
        var (r, fonte) = await ProjetosTv(new CurrentPermissionUser(8, "", "leitor@na.com", "Leitor", false), comChave: false);

        Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
        Assert.Equal(0, fonte.Chamadas);
    }

    [Fact]
    public async Task TV_MasterAdminConfigurado_200_SemSessao_401()
    {
        Assert.IsType<OkObjectResult>((await ProjetosTv(new CurrentPermissionUser(9, "", "master@na.com", "Master", true), comChave: false)).R);
        Assert.IsType<UnauthorizedObjectResult>((await ProjetosTv(null, comChave: false)).R);
    }

    [Fact]
    public async Task TV_AdministradorSoNoEscopoDeUmProjeto_403_MesmoPerfilGlobal_200()
    {
        // Reprodução do teste real: UserId 44 tem "Administrador" (contém settings.tv.configure) SÓ no projeto 232.
        // settings.tv.configure é GLOBAL no catálogo e o resolvedor documenta: "Sem projeto, só os globais".
        async Task<IActionResult> Com(int? projetoDoPerfil)
        {
            var repo = new FakePermissionRepository();
            repo.Dados[44] = PermissionResolverTests.Usuario(new[] { PermissionResolverTests.Perfil("Administrador", projetoDoPerfil, "settings.tv.configure") });
            var controller = new TvConfigurationController(new TvConfigurationStore(Path.Combine(_dir, "tv.json")),
                new Usuario(new CurrentPermissionUser(44, "", "usuario.na@example.invalid", "Usuário N&A", false)), NullLogger<TvConfigurationController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            return await controller.Projects(new Elegiveis(), repo, default);
        }

        Assert.Equal(403, Assert.IsType<ObjectResult>(await Com(232)).StatusCode);
        Assert.IsType<OkObjectResult>(await Com(null));
    }

    [Fact]
    public void TV_CriterioElegivel_ProjetoJaSincronizado_SemAutodeskNemUserProjects()
    {
        var sql = SqlTvProjetosElegiveisRepository.Sql;
        Assert.Contains("dbo.Ldp_Unificada", sql);
        Assert.Contains("Data_Sincronizacao IS NOT NULL", sql);
        Assert.Contains("dbo.Projetos", sql);
        Assert.DoesNotContain("UserProjects", sql);
        Assert.DoesNotContain("Hub", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Logo do Projeto ----------

    private ProjectIdentityController Logo(Action<ISession> sessao)
    {
        var ctx = new DefaultHttpContext { Session = new MemorySessionPerm() };
        sessao(ctx.Session);
        return new ProjectIdentityController(new ProjectIdentityStore(Path.Combine(_dir, "identity")), new Perms(),
            NullLogger<ProjectIdentityController>.Instance) { ControllerContext = new ControllerContext { HttpContext = ctx } };
    }

    [Fact]
    public async Task Logo_SessaoNaSemAccessToken_ListaEImagemDisponiveis()
    {
        await new ProjectIdentityStore(Path.Combine(_dir, "identity")).SaveAsync(Projeto, Png, "adm@na.com");
        var controller = Logo(s => { s.SetInt32(NaSessionKeys.UserId, 42); s.SetString(NaSessionKeys.Provider, "N&A"); });

        var lista = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(await controller.List(default)).Value, Web);
        Assert.Contains(Projeto, lista);
        Assert.IsType<FileContentResult>(await controller.Logo(Projeto, default));
    }

    [Fact]
    public async Task Logo_SessaoAutodeskLegada_MesmaLogo_SemSessao401_SemLogoFallback404()
    {
        await new ProjectIdentityStore(Path.Combine(_dir, "identity")).SaveAsync(Projeto, Png, "adm@na.com");

        var autodesk = Logo(s => s.SetString("AccessToken", "token"));
        var na = Logo(s => s.SetInt32(NaSessionKeys.UserId, 42));
        var bytesAutodesk = Assert.IsType<FileContentResult>(await autodesk.Logo(Projeto, default)).FileContents;
        var bytesNa = Assert.IsType<FileContentResult>(await na.Logo(Projeto, default)).FileContents;
        Assert.Equal(bytesAutodesk, bytesNa); // provider de login não influencia a logo

        Assert.IsType<UnauthorizedObjectResult>(await Logo(_ => { }).List(default));
        Assert.IsType<UnauthorizedResult>(await Logo(_ => { }).Logo(Projeto, default));
        Assert.IsType<NotFoundResult>(await na.Logo("b.99999999-9999-9999-9999-999999999999", default)); // sem logo → fallback do frontend
    }
}
