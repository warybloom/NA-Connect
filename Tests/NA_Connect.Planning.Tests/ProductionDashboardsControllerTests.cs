using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace NA_Connect.Planning.Tests;

/// <summary>Fase 3 — segurança e parsing de filtros do endpoint api/reports/production/dashboard/{id}.</summary>
public class ProductionDashboardsControllerTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    private static (ProductionDashboardsController C, ProjectAccess A, Repo R) Criar(string query = "", bool autenticado = true,
        PermissionCheck decision = PermissionCheck.Allowed, bool throws = false, Perm? perm = null)
    {
        var ctx = new DefaultHttpContext { Session = new MemorySession() };
        ctx.Request.QueryString = new QueryString(query);
        var a = new ProjectAccess { Decision = autenticado ? decision : PermissionCheck.Unauthenticated }; var r = new Repo { Throws = throws };
        return (new ProductionDashboardsController(a, r, perm ?? new Perm(), NullLogger<ProductionDashboardsController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = ctx } }, a, r);
    }

    [Fact]
    public async Task SemSessao_NuncaChamaDependencias()
    {
        var (c, a, r) = Criar(autenticado: false);
        Assert.IsType<UnauthorizedObjectResult>(await c.Dashboard("ldp", Hub, Project));
        Assert.Equal(1, a.Calls); Assert.Equal(0, r.Calls);
    }

    [Fact]
    public async Task DashboardInexistente_404()
    {
        var (c, a, r) = Criar();
        Assert.IsType<NotFoundObjectResult>(await c.Dashboard("custos-secretos", Hub, Project));
        Assert.Equal(0, a.Calls); Assert.Equal(0, r.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("' OR 1=1 --")]
    public async Task ProjetoInvalido_400(string project)
    {
        var (c, a, r) = Criar();
        Assert.IsType<BadRequestObjectResult>(await c.Dashboard("ldp", Hub, project));
        Assert.Equal(0, a.Calls); Assert.Equal(0, r.Calls);
    }

    [Fact]
    public async Task FiltroEmDimensaoDesconhecida_400SemLerBanco()
    {
        var (c, a, r) = Criar("?f.custo=1");
        Assert.IsType<BadRequestObjectResult>(await c.Dashboard("ldp", Hub, Project));
        Assert.Equal(1, a.Calls); Assert.Equal(0, r.Calls);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task AcessoNegado_NuncaLeRepository(PermissionCheck decision, int status)
    {
        var (c, _, r) = Criar(decision: decision);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await c.Dashboard("ldp", Hub, Project)).StatusCode);
        Assert.Equal(0, r.Calls);
    }

    [Fact]
    public async Task DisciplinaMaisStatus_FiltrosAplicadosSobreOProjetoDaRequisicao()
    {
        var (c, _, r) = Criar("?f.disciplina=MECANICA&f.status=EM_ANALISE_CLIENTE");
        var ok = Assert.IsType<OkObjectResult>(await c.Dashboard("ldp", Hub, Project));
        var res = Assert.IsType<DashboardResult>(ok.Value);
        Assert.Equal(Project, r.Project);
        Assert.Equal("ldp", r.Dashboard);
        Assert.Equal(3, res.TotalRows);
        Assert.Equal(1, res.FilteredRows);                        // só MECÂNICA + Client Review
        Assert.Equal(2, res.ActiveFilters.Count);
    }

    [Fact]
    public async Task SessaoNaSemTokenEHubVazio_UsaSomenteGateInternoESql()
    {
        var (c, a, r) = Criar();
        Assert.IsType<OkObjectResult>(await c.Dashboard("ldp", "", Project));
        Assert.Equal(1, a.Calls); Assert.Equal(1, r.Calls);
    }

    [Fact]
    public async Task FalhaDoRepository_503SemDetalheInterno()
    {
        var (c, _, _) = Criar(throws: true);
        var result = Assert.IsType<ObjectResult>(await c.Dashboard("ldp", Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    private sealed class ProjectAccess : IProjectAccessService
    {
        public int Calls; public PermissionCheck Decision = PermissionCheck.Allowed;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Decision); }
    }

    private sealed class Repo : IProductionDashboardRepository
    {
        public int Calls; public bool Throws; public string? Project, Dashboard;
        public Task<IReadOnlyList<DashboardRow>> ReadRowsAsync(DashboardDefinition definition, string projectId, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId; Dashboard = definition.Id;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            var linhas = new List<Dictionary<string, object?>>
            {
                Row(1, "MECÂNICA", "CLIENT REVIEW"), Row(2, "MECÂNICA", "APPROVED"), Row(3, "CIVIL", "Em análise do cliente"),
            };
            // Mesmo comportamento do repositório real: aplica o complemento (Merge) antes da avaliação.
            definition.Merge?.Invoke(linhas, new List<Dictionary<string, object?>>());
            return Task.FromResult<IReadOnlyList<DashboardRow>>(linhas.Select(l => new DashboardRow(l)).ToList());
        }
        private static Dictionary<string, object?> Row(int id, string disciplina, string status) => new()
        {
            ["ID_Registro"] = id, ["Tipo_LDP"] = 1, ["Discipline"] = disciplina, ["Document_Status"] = status, ["NA_Document_Number"] = $"NA-{id}",
        };
    }

    private const string Project2 = "b.33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Multiprojeto_MesmaDefinicaoSobreAUniaoDasLinhas_AcessoEPermissaoPorProjeto()
    {
        var (c, a, r) = Criar();
        var ok = Assert.IsType<OkObjectResult>(await c.Dashboard("ldp", Hub, Project, projetos: new[] { Project2 }));
        Assert.Equal(2, r.Calls); Assert.Equal(2, a.Calls);                          // cada projeto lido e autorizado
        var res = Assert.IsType<DashboardResult>(ok.Value);
        Assert.Equal(6m, res.Kpis.Single(k => k.Key == "total").Value);                // 3 linhas de cada projeto, mesma regra
        // Um projeto (sem "projetos") = comportamento anterior.
        var (c1, _, r1) = Criar();
        Assert.Equal(3m, Assert.IsType<DashboardResult>(Assert.IsType<OkObjectResult>(await c1.Dashboard("ldp", Hub, Project)).Value).Kpis.Single(k => k.Key == "total").Value);
        Assert.Equal(1, r1.Calls);
    }

    [Fact]
    public async Task Multiprojeto_ProjetoExtraSemPermissao_403_SemLerDados()
    {
        var (c, a, r) = Criar(perm: new Perm { NegadoNoProjeto = Project2 });
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.Dashboard("ldp", Hub, Project, projetos: new[] { Project2 })).StatusCode);
        Assert.Equal(0, r.Calls); Assert.Equal(2, a.Calls);
    }

    [Fact]
    public async Task SemPermissaoDoDashboard_403_SemLerDados()
    {
        var perm = new Perm { Negadas = { "production.managerial.rnc.view", "production.operational.rnc.view" } };
        var (c, a, r) = Criar(perm: perm);
        var res = Assert.IsType<ObjectResult>(await c.Dashboard("rnc", Hub, Project));
        Assert.Equal(403, res.StatusCode);
        Assert.Equal(1, a.Calls); Assert.Equal(0, r.Calls);
        Assert.Equal(new[] { "production.view", "production.managerial.rnc.view", "production.operational.rnc.view" }, perm.Consultadas); // módulo + qualquer variante
        Assert.Equal(Project, perm.UltimoProjeto);
    }

    [Fact]
    public async Task PermissaoIndisponivel_503_FalhaFechada()
    {
        var (c, _, r) = Criar(perm: new Perm { Resultado = PermissionCheck.Unavailable });
        Assert.Equal(503, Assert.IsType<ObjectResult>(await c.Dashboard("ldp", Hub, Project)).StatusCode);
        Assert.Equal(0, r.Calls);
    }

    [Fact]
    public void TodoDashboardTemPermissionKeyNoCatalogo()
    {
        foreach (var id in ProductionDashboards.Todos.Keys)
        {
            Assert.True(PermissionCatalog.DashboardsProducao.TryGetValue(id, out var keys), $"Dashboard {id} sem key.");
            Assert.Equal(2, keys!.Count); // Gerencial + Operacional
            Assert.All(keys, key => Assert.True(PermissionCatalog.Existe(key), $"Key {key} fora do catálogo."));
        }
    }

    internal sealed class Perm : IPermissionService
    {
        public HashSet<string> Negadas { get; } = new();
        public PermissionCheck? Resultado { get; init; }
        public string? NegadoNoProjeto { get; init; }
        public List<string> Consultadas { get; } = new();
        public string? UltimoProjeto { get; private set; }
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false)
        {
            Consultadas.AddRange(anyOfKeys); UltimoProjeto = projectIdAcc;
            if (Resultado is { } r) return Task.FromResult(r);
            if (NegadoNoProjeto != null && projectIdAcc == NegadoNoProjeto) return Task.FromResult(PermissionCheck.Denied);
            return Task.FromResult(anyOfKeys.Any(k => !Negadas.Contains(k)) ? PermissionCheck.Allowed : PermissionCheck.Denied);
        }
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class MemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsAvailable => true; public string Id => "test"; public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear(); public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
