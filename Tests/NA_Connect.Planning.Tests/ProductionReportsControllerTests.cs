using System.Text.RegularExpressions;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Relatório de Produção (api/reports/production/*): mesmo contrato de segurança dos demais relatórios —
/// sessão ausente/escopo inválido nunca chamam dependências, 403/503 do gate nunca leem o repository,
/// o projeto chega intacto ao repository, falha nunca expõe detalhe interno. Mais testes de CONTRATO SQL:
/// toda instrução filtra por Project_ID_ACC = @projectId, é somente leitura e usa parâmetros.
/// </summary>
public class ProductionReportsControllerTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";
    private const string OutroProjeto = "b.33333333-3333-3333-3333-333333333333";

    private static DefaultHttpContext AuthenticatedContext()
    {
        var context = new DefaultHttpContext { Session = new MemorySession() };
        return context;
    }

    private static (ProductionReportsController Controller, ProjectAccess Access, Repos Repos) Criar(HttpContext context,
        PermissionCheck decision = PermissionCheck.Allowed, bool throws = false, Perm? perm = null)
    {
        var access = new ProjectAccess { Decision = decision };
        var repos = new Repos { Throws = throws };
        var controller = new ProductionReportsController(access, perm ?? new Perm(), repos, repos, repos, repos, repos, repos,
            NullLogger<ProductionReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } };
        return (controller, access, repos);
    }

    /// <summary>As 6 ações do controller, chamadas com hub/projeto e parâmetros padrão.</summary>
    private static readonly string[] NomesAcoes = { "change-orders", "quality-deviations", "rnc", "document-control", "schedule-leveling", "schedule-control" };
    public static TheoryData<string> Acoes => new(NomesAcoes);

    private static Task<IActionResult> Chamar(ProductionReportsController c, string acao, string hub, string project, int page = 1, int pageSize = 25) => acao switch
    {
        "change-orders" => c.ChangeOrders(hub, project, page: page, pageSize: pageSize),
        "quality-deviations" => c.QualityDeviations(hub, project, page: page, pageSize: pageSize),
        "rnc" => c.Rnc(hub, project, page: page, pageSize: pageSize),
        "document-control" => c.DocumentControl(hub, project),
        "schedule-leveling" => c.ScheduleLeveling(hub, project),
        "schedule-control" => c.ScheduleControl(hub, project),
        _ => throw new ArgumentOutOfRangeException(nameof(acao))
    };

    [Theory, MemberData(nameof(Acoes))]
    public async Task SemSessao_NuncaChamaDependencias(string acao)
    {
        var (c, access, repos) = Criar(new DefaultHttpContext { Session = new MemorySession() }, PermissionCheck.Unauthenticated);
        Assert.IsType<UnauthorizedObjectResult>(await Chamar(c, acao, Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repos.Calls);
    }

    [Theory, MemberData(nameof(Acoes))]
    public async Task ProjetoInvalido_400SemChamarDependencias(string acao)
    {
        var (c, access, repos) = Criar(AuthenticatedContext());
        Assert.IsType<BadRequestObjectResult>(await Chamar(c, acao, Hub, "' OR 1=1 --"));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repos.Calls);
    }

    [Theory]
    [InlineData("change-orders")]
    [InlineData("quality-deviations")]
    [InlineData("rnc")]
    public async Task PaginacaoForaDoLimite_400(string acao)
    {
        var (c, _, repos) = Criar(AuthenticatedContext());
        Assert.IsType<BadRequestObjectResult>(await Chamar(c, acao, Hub, Project, page: 0));
        Assert.IsType<BadRequestObjectResult>(await Chamar(c, acao, Hub, Project, pageSize: 101));
        Assert.Equal(0, repos.Calls);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task AcessoNegado_NuncaLeRepository(PermissionCheck decision, int status)
    {
        foreach (var acao in NomesAcoes)
        {
            var (c, _, repos) = Criar(AuthenticatedContext(), decision);
            var result = Assert.IsAssignableFrom<ObjectResult>(await Chamar(c, acao, Hub, Project));
            Assert.Equal(status, result.StatusCode); Assert.Equal(0, repos.Calls);
        }
    }

    [Theory, MemberData(nameof(Acoes))]
    public async Task Autorizado_RepositoryRecebeSomenteOProjetoDaRequisicao(string acao)
    {
        var (c, _, repos) = Criar(AuthenticatedContext());
        Assert.IsType<OkObjectResult>(await Chamar(c, acao, Hub, Project));
        Assert.Equal(1, repos.Calls); Assert.Equal(Project, repos.Project);

        var (c2, _, repos2) = Criar(AuthenticatedContext());
        await Chamar(c2, acao, Hub, OutroProjeto);
        Assert.Equal(OutroProjeto, repos2.Project);
    }

    [Fact]
    public async Task Filtros_ChegamIntactosEVaziosViramNull()
    {
        var (c, _, repos) = Criar(AuthenticatedContext());
        await c.Rnc(Hub, Project, "ABERTO", "Mecânica", "RNC-1", 2, 50);
        Assert.Equal(new[] { "ABERTO", "Mecânica", "RNC-1" }, repos.Filtros);
        Assert.Equal((2, 50), repos.Pagina);

        await c.QualityDeviations(Hub, Project, "", "Carimbo", null);
        Assert.Equal(new string?[] { null, "Carimbo", null }, repos.Filtros);
    }

    [Theory, MemberData(nameof(Acoes))]
    public async Task FalhaDoRepository_503SemDetalheInterno(string acao)
    {
        var (c, _, _) = Criar(AuthenticatedContext(), throws: true);
        var result = Assert.IsType<ObjectResult>(await Chamar(c, acao, Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    // ---------------------------------------------------------------- contrato SQL ----

    private static readonly Dictionary<string, string> TodasQueries = new()
    {
        ["ChangeOrder"] = ChangeOrderReportRepository.Query,
        ["QualityDeviation"] = QualityDeviationReportRepository.Query,
        ["Rnc"] = RncReportRepository.Query,
        ["DocumentControl"] = DocumentControlReportRepository.Query,
        ["ScheduleLeveling"] = ScheduleLevelingReportRepository.Query,
        ["ScheduleControl"] = ScheduleControlReportRepository.Query,
    };

    public static TheoryData<string, string> Queries
    {
        get { var d = new TheoryData<string, string>(); foreach (var (k, v) in TodasQueries) d.Add(k, v); return d; }
    }

    [Theory, MemberData(nameof(Queries))]
    public void Sql_SomenteLeitura(string nome, string sql)
    {
        Assert.False(Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE)\b", RegexOptions.IgnoreCase),
            $"{nome}: instrução de escrita/DDL encontrada.");
    }

    [Theory, MemberData(nameof(Queries))]
    public void Sql_TodaInstrucaoFiltraPeloProjetoParametrizado(string nome, string sql)
    {
        // Cada instrução (separada por ';') precisa ter o filtro por Project_ID_ACC = @projectId — nenhuma
        // leitura sem escopo de projeto, nenhum projeto concatenado no texto.
        var instrucoes = sql.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(s => !string.IsNullOrWhiteSpace(s));
        foreach (var instrucao in instrucoes)
            Assert.True(instrucao.Contains("Project_ID_ACC = @projectId", StringComparison.Ordinal), $"{nome}: instrução sem filtro de projeto:\n{instrucao}");
    }

    [Theory, MemberData(nameof(Queries))]
    public void Sql_FiltrosDoUsuarioSaoParametros(string nome, string sql)
    {
        // Nenhum placeholder de interpolação sobrou e os filtros de texto aparecem como @parâmetros.
        Assert.DoesNotContain("{", sql);
        foreach (Match m in Regex.Matches(sql, @"LIKE\s+(\S+)")) Assert.StartsWith("@", m.Groups[1].Value);
        Assert.Contains("@projectId", sql);
    }

    [Theory]
    [InlineData("ChangeOrder")]
    [InlineData("QualityDeviation")]
    [InlineData("Rnc")]
    public void Sql_PaginacaoNoServidor_ContadoresSobreConjuntoInteiro(string nome)
    {
        var sql = TodasQueries[nome];
        Assert.Contains("OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY", sql);
        // Contadores/agrupamentos leem "Source" (projeto inteiro), nunca a página.
        var primeira = sql.Split(';')[0];
        Assert.DoesNotContain("OFFSET", primeira);
    }

    // ------------------------------------------------------------------------ fakes ----
    [Fact]
    public async Task SessaoNaSemTokenEHubVazio_UsaSomenteGateInternoESql()
    {
        var (c, access, repos) = Criar(AuthenticatedContext());
        Assert.IsType<OkObjectResult>(await Chamar(c, "rnc", "", Project));
        Assert.Equal(1, access.Calls); Assert.Equal(1, repos.Calls);
    }

    [Fact]
    public async Task GateInternoPassa_MasPermissaoFuncionalNegada_NaoLeRepository()
    {
        var perm = new Perm { Decision = PermissionCheck.Denied };
        var (c, _, repos) = Criar(AuthenticatedContext(), perm: perm);
        var result = Assert.IsType<ObjectResult>(await c.ChangeOrders("", Project));
        Assert.Equal(403, result.StatusCode); Assert.Equal(0, repos.Calls);
        Assert.Equal(new[] { "production.managerial.change-order.view", "production.operational.change-order.view" }, perm.Keys);
    }

    private sealed class ProjectAccess : IProjectAccessService
    {
        public int Calls; public PermissionCheck Decision = PermissionCheck.Allowed;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Decision); }
    }

    private sealed class Perm : IPermissionService
    {
        public PermissionCheck Decision { get; init; } = PermissionCheck.Allowed;
        public List<string> Keys { get; } = new();
        public bool Enforced => false;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false)
        {
            Keys.AddRange(anyOfKeys);
            return Task.FromResult(Decision);
        }
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Repos : IChangeOrderReportRepository, IQualityDeviationReportRepository, IRncReportRepository,
        IDocumentControlReportRepository, IScheduleLevelingReportRepository, IScheduleControlReportRepository
    {
        public int Calls; public string? Project; public bool Throws; public string?[] Filtros = Array.Empty<string?>(); public (int, int) Pagina;
        private void Registrar(string projectId, params string?[] filtros)
        {
            Calls++; Project = projectId; Filtros = filtros;
            if (Throws) throw new InvalidOperationException("secret SQL details");
        }

        public Task<ChangeOrderReport> ReadAsync(string projectId, string? changeOrder, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Registrar(projectId, changeOrder, search); Pagina = (page, pageSize);
            return Task.FromResult(new ChangeOrderReport(0, 0, null, page, pageSize, [], [], []));
        }

        Task<QualityDeviationReport> IQualityDeviationReportRepository.ReadAsync(string projectId, string? disciplina, string? tipo, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Registrar(projectId, disciplina, tipo, search); Pagina = (page, pageSize);
            return Task.FromResult(new QualityDeviationReport(0, 0, 0, 0, null, null, page, pageSize, [], [], [], []));
        }

        Task<RncReport> IRncReportRepository.ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Registrar(projectId, status, disciplina, search); Pagina = (page, pageSize);
            return Task.FromResult(new RncReport(0, 0, null, page, pageSize, [], [], [], [], []));
        }

        Task<DocumentControlReport> IDocumentControlReportRepository.ReadAsync(string projectId, CancellationToken cancellationToken)
        {
            Registrar(projectId);
            return Task.FromResult(new DocumentControlReport(0, 0, 0, 0, 0, null, [], [], [], [], []));
        }

        Task<ScheduleLevelingReport> IScheduleLevelingReportRepository.ReadAsync(string projectId, CancellationToken cancellationToken)
        {
            Registrar(projectId);
            return Task.FromResult(new ScheduleLevelingReport(0, 0, 0, null, 0, 0, null, [], []));
        }

        Task<ScheduleControlReport> IScheduleControlReportRepository.ReadAsync(string projectId, CancellationToken cancellationToken)
        {
            Registrar(projectId);
            return Task.FromResult(new ScheduleControlReport(0, 0, 0, 0, null, [], [], []));
        }
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
