using System.Text.Json;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Fluxo real da tela Configuração de Telas para TV contra o controller: abrir (GET) → salvar (PUT com o JSON
/// camelCase exatamente como o frontend envia) → reabrir (novo controller/store = app reiniciado) → conflito → inválido.
/// </summary>
public class TvConfigurationControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tv-ctrl-tests-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_dir, "tv-configuration.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best-effort */ } }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed class Perms(bool enforced = false, string[]? keys = null, bool falhar = false) : IPermissionService
    {
        public bool Enforced => enforced;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false) => throw new NotSupportedException();
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => falhar
            ? throw new InvalidOperationException("banco indisponível")
            : Task.FromResult(new EffectivePermissions(enforced, true, false, "tv@na.com", null, keys ?? Array.Empty<string>(), keys ?? Array.Empty<string>()));
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) =>
            Task.FromResult<CurrentPermissionUser?>(new(1, "acc", "planejador@na.com", "Planejador", false));
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult<int?>(null);
    }

    private TvConfigurationController Controller(Perms? perms = null) =>
        new(new TvConfigurationStore(Arquivo), perms ?? new Perms(), NullLogger<TvConfigurationController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    /// Corpo serializado como o ASP.NET devolve ao navegador (camelCase) e lido de volta como o frontend lê.
    private static JsonElement Corpo(IActionResult r) =>
        JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(r).Value, Web)).RootElement;

    // Payload IDÊNTICO ao de tvApi.ts → salvarConfiguracaoTv(revision, presentations).
    private static SalvarTvConfiguration DoFrontend(long revision, int segundos = 30) => JsonSerializer.Deserialize<SalvarTvConfiguration>($$"""
        { "revision": {{revision}}, "presentations": [ { "id": "sala-de-reuniao", "name": "Sala de Reunião", "secondsPerScreen": {{segundos}},
          "slides": [ { "screenId": "ldp", "hubId": "b.11111111-1111-1111-1111-111111111111", "projectId": "b.22222222-2222-2222-2222-222222222222", "projectName": "CHS001 - ETHANOL" },
                      { "screenId": "hold", "hubId": "b.11111111-1111-1111-1111-111111111111", "projectId": "b.22222222-2222-2222-2222-222222222222", "projectName": null } ] } ] }
        """, Web)!;

    [Fact]
    public async Task Abrir_Criar_Salvar_Fechar_Reabrir_ConfiguracaoPermanece()
    {
        // 1. abrir: sem arquivo → 200 com configuração vazia e a lista de telas permitidas (formato lido pelo frontend)
        var abrir = Corpo(await Controller().Get(default));
        Assert.Equal(0, abrir.GetProperty("configuration").GetProperty("revision").GetInt64());
        Assert.Equal(0, abrir.GetProperty("configuration").GetProperty("presentations").GetArrayLength());
        Assert.Equal(41, abrir.GetProperty("telasPermitidas").GetArrayLength());           // telas implementadas do Controle de Produção + painel PMO + 10 Gestão à Vista Tipo 2
        Assert.Equal(41, abrir.GetProperty("telasAutorizadas").GetArrayLength());          // permissões não aplicadas → todas

        // 2. salvar
        var salvar = await Controller().Save(DoFrontend(0), default);
        Assert.IsType<OkObjectResult>(salvar);
        Assert.Equal(1, Corpo(salvar).GetProperty("configuration").GetProperty("revision").GetInt64());

        // 3. fechar/reabrir (controller e store novos = app reiniciado)
        var cfg = Corpo(await Controller().Get(default)).GetProperty("configuration");
        Assert.Equal("planejador@na.com", cfg.GetProperty("savedBy").GetString());
        var ap = Assert.Single(cfg.GetProperty("presentations").EnumerateArray());
        Assert.Equal("Sala de Reunião", ap.GetProperty("name").GetString());
        Assert.Equal(30, ap.GetProperty("secondsPerScreen").GetInt32());
        Assert.Equal(new[] { "ldp", "hold" }, ap.GetProperty("slides").EnumerateArray().Select(s => s.GetProperty("screenId").GetString()));

        // 4. editar e salvar de novo com a revisão lida
        Assert.IsType<OkObjectResult>(await Controller().Save(DoFrontend(1, 45), default));
        Assert.Equal(45, Corpo(await Controller().Get(default)).GetProperty("configuration").GetProperty("presentations")[0].GetProperty("secondsPerScreen").GetInt32());
    }

    [Fact]
    public async Task Get_TelasAutorizadas_SoAsDoPerfil_FalhaDePermissaoNaoLiberaNada()
    {
        var operacional = Corpo(await Controller(new Perms(true, new[] { "production.operational.ldp.view", "reports.gestao.view" })).Get(default));
        var ids = operacional.GetProperty("telasAutorizadas").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("ldp", ids);
        Assert.Contains("gestao-a-vista-visao-geral", ids);
        Assert.DoesNotContain("gestao-a-vista-custo-disciplina", ids);                   // custo exige a key gerencial
        Assert.DoesNotContain("resumo-tv-teste", ids);
        Assert.Equal(41, operacional.GetProperty("telasPermitidas").GetArrayLength());    // validação continua com o catálogo inteiro
        // Falha ao resolver permissões: null (o frontend exige a permissão local; nada é liberado por falta da lista).
        var falha = Corpo(await Controller(new Perms(true, falhar: true)).Get(default));
        Assert.Equal(JsonValueKind.Null, falha.GetProperty("telasAutorizadas").ValueKind);
    }

    private static SalvarTvConfiguration ItemUnico(string screen, string projetos) => JsonSerializer.Deserialize<SalvarTvConfiguration>($$"""
        { "revision": 0, "presentations": [ { "id": "tv-engenharia", "name": "TV ENGENHARIA", "secondsPerScreen": 30, "ativa": true,
          "slides": [ { "screenId": "{{screen}}", "hubId": "", "projectId": "b.22222222-2222-2222-2222-222222222222", "projectName": "CHS001", "projetos": {{projetos}} } ] } ] }
        """, Web)!;

    [Fact]
    public async Task Put_TelaDaJamile_ConsolidadaComDoisProjetos_Aceita_UnicaTelaDeUmProjeto_EEmDesenvolvimento_400()
    {
        const string dois = """[ { "hubId": "", "projectId": "b.22222222-2222-2222-2222-222222222222", "projectName": "CHS001" }, { "hubId": "", "projectId": "b.33333333-3333-3333-3333-333333333333", "projectName": "BML" } ]""";
        Assert.IsType<BadRequestObjectResult>(await Controller().Save(ItemUnico("lancamentos-pessoas", dois), default));  // só um projeto por item
        Assert.IsType<BadRequestObjectResult>(await Controller().Save(ItemUnico("boletim-medicao", "null"), default));   // em desenvolvimento
        Assert.IsType<OkObjectResult>(await Controller().Save(ItemUnico("gestao-a-vista-visao-geral", dois), default));    // consolidado: 1 slide, 2 projetos
        var slide = Corpo(await Controller().Get(default)).GetProperty("configuration").GetProperty("presentations")[0].GetProperty("slides")[0];
        Assert.Equal("gestao-a-vista-visao-geral", slide.GetProperty("screenId").GetString());
        Assert.Equal(2, slide.GetProperty("projetos").GetArrayLength());
    }

    [Fact]
    public async Task Put_GestaoVistaTipo2_SoChs001_OutroProjetoPorApi_400ComMensagem()
    {
        // Mesmo payload do frontend, chamado direto na API: tela Tipo 2 com outro projeto é recusada com mensagem clara.
        var outro = Assert.IsType<BadRequestObjectResult>(await Controller().Save(ItemUnico("gestao-vista-tipo2-resumo-30", "null"), default));
        Assert.Contains("exclusiva do projeto CHS001", Corpo(outro).GetProperty("message").GetString());
        Assert.False(File.Exists(Arquivo));
        var chs001 = JsonSerializer.Deserialize<SalvarTvConfiguration>("""
            { "revision": 0, "presentations": [ { "id": "tv-engenharia", "name": "TV ENGENHARIA", "secondsPerScreen": 30, "ativa": true,
              "slides": [ { "screenId": "gestao-vista-tipo2-ldp-engineering-30", "hubId": "", "projectId": "b.fcdb7ebd-1b23-440d-a04b-90391098522e", "projectName": "CHS001 - ETHANOL PLANT", "projetos": null } ] } ] }
            """, Web)!;
        Assert.IsType<OkObjectResult>(await Controller().Save(chs001, default));
        var abrir = Corpo(await Controller().Get(default));
        Assert.Contains("gestao-vista-tipo2-ldp-engineering-30", abrir.GetProperty("telasPermitidas").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("gestao-vista-tipo2-ldp-engineering-30", abrir.GetProperty("configuration").GetProperty("presentations")[0].GetProperty("slides")[0].GetProperty("screenId").GetString());
    }

    [Fact]
    public async Task RevisaoDesatualizada_409_Invalido_400()
    {
        await Controller().Save(DoFrontend(0), default);
        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(await Controller().Save(DoFrontend(0), default)).StatusCode);
        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(await Controller().Save(DoFrontend(1, 5), default)).StatusCode);
    }

    [Fact]
    public async Task ArquivoCorrompido_503ComMensagem()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Arquivo, "{ quebrado");
        var r = Assert.IsType<ObjectResult>(await Controller().Get(default));
        Assert.Equal(503, r.StatusCode);
    }

    [Fact]
    public void ViteProxy_EncaminhaTodaApiAoBackend()
    {
        // Causa do erro "Não foi possível carregar os dados": sem regra de proxy, o Vite devolvia index.html para /api/tv-config.
        var vite = File.ReadAllText(Path.Combine(RaizRepo(), "ClientApp", "vite.config.ts"));
        Assert.Matches(@"['""]/api['""]:\s*\{\s*target:\s*backendTarget", vite);       // aspas simples ou duplas
    }

    private static string RaizRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "AccIntegration.WebApp.csproj"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
