using System.Reflection;
using System.Text.Json;
using APS.WebApp.Controllers;
using APS.WebApp.Services;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services;

namespace NA_Connect.BiChs001.Tests;

/// <summary>
/// Contrato da API isolada (GET status/tarefas/trabalho-diario/trabalho-semanal, POST sincronizar): permissões, restrição ao
/// CHS001, erros e dados reais da versão gravada no banco de TESTE (clone).
/// </summary>
public sealed class ApiContratoTests
{
    private sealed class Acesso(PermissionCheck r) : IProjectAccessService
    {
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult(r);
    }

    private sealed class Permissoes(PermissionCheck r) : IPermissionService
    {
        public List<IReadOnlyCollection<string>> Pedidas { get; } = new();
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false)
        { Pedidas.Add(anyOfKeys); return Task.FromResult(r); }
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
    }

    private static (BiChs001ScheduleController C, Permissoes P) Criar(PermissionCheck acesso = PermissionCheck.Allowed, PermissionCheck perm = PermissionCheck.Allowed)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LocalConnection"] = Ambiente.ConexaoTeste }).Build();
        var api = new AutodeskApiService(new HttpClient(), new TokenManager(new HttpClient(), cfg, new MemoryCache(new MemoryCacheOptions())), cfg,
            NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        var p = new Permissoes(perm);
        var c = new BiChs001ScheduleController(new Acesso(acesso), p, cfg, api, NullLogger<BiChs001ScheduleController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (c, p);
    }

    private static JsonElement Corpo(IActionResult r)
    {
        var ok = Assert.IsType<OkObjectResult>(r);
        return JsonSerializer.SerializeToElement(ok.Value);
    }

    private static int Status(IActionResult r) => r switch { ObjectResult o => o.StatusCode ?? 200, StatusCodeResult s => s.StatusCode, _ => 0 };

    [Fact]
    public void TodaActionTemRequirePermission_ComAsKeysEsperadas()
    {
        var t = typeof(BiChs001ScheduleController);
        Assert.Equal("api/bi-chs001/schedule", t.GetCustomAttribute<RouteAttribute>()!.Template);
        foreach (var nome in new[] { "Status", "Tarefas", "TrabalhoDiario", "TrabalhoSemanal" })
            Assert.Equal(new[] { "reports.gestao.view", "reports.cronograma.view" }, t.GetMethod(nome)!.GetCustomAttribute<RequirePermissionAttribute>()!.Keys);
        Assert.Equal(new[] { "robot.run" }, t.GetMethod("Sincronizar")!.GetCustomAttribute<RequirePermissionAttribute>()!.Keys);
        Assert.All(new[] { "reports.gestao.view", "reports.cronograma.view", "robot.run" }, k => Assert.True(PermissionCatalog.Existe(k), k));
    }

    [Theory]
    [InlineData(PermissionCheck.Unauthenticated, PermissionCheck.Allowed, 401)]
    [InlineData(PermissionCheck.Denied, PermissionCheck.Allowed, 403)]
    [InlineData(PermissionCheck.Allowed, PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Allowed, PermissionCheck.Unauthenticated, 401)]
    [InlineData(PermissionCheck.Unavailable, PermissionCheck.Allowed, 503)]
    public async Task BloqueiosDeAcesso(PermissionCheck acesso, PermissionCheck perm, int esperado)
    {
        var (c, _) = Criar(acesso, perm);
        Assert.Equal(esperado, Status(await c.Tarefas(Ambiente.ProjetoChs001, null, default)));
        Assert.Equal(esperado, Status(await c.Sincronizar(Ambiente.ProjetoChs001, default)));
    }

    [Fact]
    public async Task IdInvalido400_OutroProjeto404_VersaoInexistente404()
    {
        var (c, _) = Criar();
        Assert.Equal(400, Status(await c.Tarefas("CHS001", null, default)));
        Assert.Equal(404, Status(await c.Tarefas("b.00000000-0000-0000-0000-000000000001", null, default)));
        Assert.Equal(404, Status(await c.Tarefas(Ambiente.ProjetoChs001, 999_999, default)));
    }

    [Fact]
    public async Task Sincronizar_ExigeRobotRun()
    {
        var (c, p) = Criar(perm: PermissionCheck.Denied);
        Assert.Equal(403, Status(await c.Sincronizar(Ambiente.ProjetoChs001, default)));
        Assert.Equal(new[] { "robot.run" }, p.Pedidas.Single());
    }

    [Fact]
    public async Task Tarefas_DaVersaoGravada_ComAliasECamposNativos()
    {
        var (c, _) = Criar();
        var corpo = Corpo(await c.Tarefas(Ambiente.ProjetoChs001, null, default));
        Assert.True(corpo.GetProperty("versaoId").GetInt32() > 0);
        var linhas = corpo.GetProperty("linhas").EnumerateArray().ToList();
        Assert.Equal(883, linhas.Count);
        var gate = linhas.Single(l => l.GetProperty("UID_Tarefa").GetInt32() == 3917);
        Assert.Equal("Engineering - 30%", gate.GetProperty("TASK_NAME").GetString());
        Assert.Equal("Emgineering - 30%", gate.GetProperty("TASK_NAME_Original").GetString());
        Assert.Equal(1, gate.GetProperty("Alias_Id").GetInt32());
        var folhas = linhas.Where(l => !l.GetProperty("Eh_Tarefa_Resumo").GetBoolean()).ToList();
        Assert.Equal(755, folhas.Count);
        Assert.All(folhas, f =>
        {
            Assert.NotEqual(JsonValueKind.Null, f.GetProperty("Inicio_Linha_Base").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, f.GetProperty("UID_Tarefa_Pai").ValueKind);
        });
        Assert.True(folhas.Count(f => f.GetProperty("Status1").ValueKind == JsonValueKind.String) > 700);
        Assert.True(folhas.Count(f => f.GetProperty("NA_Document_Number_Campo").ValueKind == JsonValueKind.String) > 0);
    }

    [Fact]
    public async Task SerieDiariaESemanal_DaMesmaVersao_SemanaNoDomingo()
    {
        var (c, _) = Criar();
        int versao = Corpo(await c.Tarefas(Ambiente.ProjetoChs001, null, default)).GetProperty("versaoId").GetInt32();
        var dia = Corpo(await c.TrabalhoDiario(Ambiente.ProjetoChs001, versao, default));
        var sem = Corpo(await c.TrabalhoSemanal(Ambiente.ProjetoChs001, versao, default));
        Assert.Equal(versao, dia.GetProperty("versaoId").GetInt32());
        Assert.Equal(16024, dia.GetProperty("totalLinhas").GetInt32());
        Assert.All(sem.GetProperty("linhas").EnumerateArray(), l =>
            Assert.Equal(DayOfWeek.Sunday, DateTime.Parse(l.GetProperty("Domingo").GetString()!).DayOfWeek));
        decimal somaDia = dia.GetProperty("linhas").EnumerateArray().Sum(l => l.GetProperty("Trabalho_Real_Horas").ValueKind == JsonValueKind.Number ? l.GetProperty("Trabalho_Real_Horas").GetDecimal() : 0);
        decimal somaSem = sem.GetProperty("linhas").EnumerateArray().Sum(l => l.GetProperty("Trabalho_Real_Horas").ValueKind == JsonValueKind.Number ? l.GetProperty("Trabalho_Real_Horas").GetDecimal() : 0);
        Assert.Equal(somaDia, somaSem);
    }

    [Fact]
    public async Task Status_InformaVersaoAliasEConferenciaComOLegado()
    {
        var (c, _) = Criar();
        var corpo = Corpo(await c.Status(Ambiente.ProjetoChs001, default));
        Assert.True(corpo.GetProperty("schemaInstalado").GetBoolean());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("indisponivel").ValueKind);
        Assert.Equal("Emgineering - 30%", corpo.GetProperty("alias")[0].GetProperty("Valor_Bruto").GetString());
        var leg = corpo.GetProperty("comparacaoLegado");
        Assert.Equal(883, leg.GetProperty("Tarefas_Legado").GetInt32());
        Assert.Equal(883, leg.GetProperty("Casadas_Por_UID").GetInt32());
    }
}
