using System.Reflection;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests.Reports;

/// <summary>
/// Controle de Produção para usuário N&A sem Autodesk: relatórios SQL identificam o projeto só pelo Project_ID_ACC.
/// hubId é opcional e ignorado — e a autorização (UserProjects + permission key) continua igual, sem bypass.
/// </summary>
public sealed class SqlReportsSemHubIdTests
{
    private const string Projeto = "b.c3e20d9d-779e-423c-9381-abec38cf11cb";

    /// <summary>Controllers de relatório SQL (leem só o banco N&A). CDE real/upload/planejamento não entram aqui.</summary>
    private static readonly string[] ControllersSql =
    [
        "Avanco4DReportsController", "CapReportsController", "CdeReportsController", "CurvaSReportsController",
        "FinanceiroHorasReportsController", "GestaoVistaReportsController", "HoldReportsController", "LdpDetailsController",
        "ProductionDashboardsController", "ProductionDocumentsReportsController", "ProductionReportsController",
        "ProductionStatusReportsController", "QualityReportsController", "ScheduleReportsController",
        "ScopeDeviationReportsController", "WeeklyDetailsController", "WeeklyReportsController",
    ];

    private static ModelMetadataProvider MetadataMvc() =>
        new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider().GetRequiredService<IModelMetadataProvider>() as ModelMetadataProvider ?? throw new InvalidOperationException();

    private static IEnumerable<(string Acao, ParameterInfo Hub, ParameterInfo? Projeto)> ParametrosHub()
    {
        var tipos = typeof(GestaoVistaReportsController).Assembly.GetTypes().Where(t => ControllersSql.Contains(t.Name)).ToList();
        Assert.Equal(ControllersSql.Length, tipos.Count);
        foreach (var tipo in tipos)
        foreach (var metodo in tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var hub = metodo.GetParameters().FirstOrDefault(p => p.Name == "hubId");
            if (hub is not null) yield return ($"{tipo.Name}.{metodo.Name}", hub, metodo.GetParameters().FirstOrDefault(p => p.Name == "projectId"));
        }
    }

    // ---------- causa do 400: [Required] implícito do ASP.NET para string não anulável ----------

    [Fact]
    public void ModelBinding_HubIdNaoEhObrigatorio_EmNenhumRelatorioSql_ProjectIdContinua()
    {
        var metadata = MetadataMvc();
        var parametros = ParametrosHub().ToList();
        Assert.Equal(23, parametros.Count);

        foreach (var (acao, hub, projeto) in parametros)
        {
            Assert.False(metadata.GetMetadataForParameter(hub).IsRequired, $"{acao}: hubId vazio geraria 400.");
            // Controle: o mesmo mecanismo continua exigindo projectId (o identificador real).
            Assert.NotNull(projeto);
            Assert.True(metadata.GetMetadataForParameter(projeto!).IsRequired, $"{acao}: projectId deve continuar obrigatório.");
        }
    }

    // ---------- A / E / F: gestao-vista ----------

    private static GestaoVistaReportsController GestaoVista(PermissionCheck projeto, PermissionCheck permissao, GestaoRepo repo) =>
        new(new Acesso(projeto), new Permissoes(permissao), repo, NullLogger<GestaoVistaReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task A_GestaoVista_SemHubId_200()
    {
        var repo = new GestaoRepo();
        var result = await GestaoVista(PermissionCheck.Allowed, PermissionCheck.Allowed, repo).GestaoVista(null, Projeto);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Projeto, repo.Projeto);
    }

    [Fact]
    public async Task E_GestaoVista_SemUserProjects_403_SemLerBanco()
    {
        var repo = new GestaoRepo();
        var result = Assert.IsType<ObjectResult>(await GestaoVista(PermissionCheck.Denied, PermissionCheck.Allowed, repo).GestaoVista(null, Projeto));

        Assert.Equal(403, result.StatusCode);
        Assert.Null(repo.Projeto);
    }

    [Fact]
    public async Task F_GestaoVista_SemPermissionKey_403_SemLerBanco()
    {
        var repo = new GestaoRepo();
        var result = Assert.IsType<ObjectResult>(await GestaoVista(PermissionCheck.Allowed, PermissionCheck.Denied, repo).GestaoVista(null, Projeto));

        Assert.Equal(403, result.StatusCode);
        Assert.Null(repo.Projeto);
    }

    // ---------- B / E / F: dashboard/ldp ----------

    private static ProductionDashboardsController Dashboard(PermissionCheck projeto, PermissionCheck permissao, DashRepo repo) =>
        new(new Acesso(projeto), repo, new Permissoes(permissao), NullLogger<ProductionDashboardsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task B_DashboardLdp_SemHubId_200()
    {
        var repo = new DashRepo();
        var result = await Dashboard(PermissionCheck.Allowed, PermissionCheck.Allowed, repo).Dashboard("ldp", null, Projeto);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Projeto, repo.Projeto);
    }

    [Fact]
    public async Task E_DashboardLdp_SemUserProjects_403()
    {
        var repo = new DashRepo();
        var result = Assert.IsType<ObjectResult>(await Dashboard(PermissionCheck.Denied, PermissionCheck.Allowed, repo).Dashboard("ldp", null, Projeto));

        Assert.Equal(403, result.StatusCode);
        Assert.Null(repo.Projeto);
    }

    [Fact]
    public async Task F_DashboardLdp_SemPermissionKey_403()
    {
        var repo = new DashRepo();
        var result = Assert.IsType<ObjectResult>(await Dashboard(PermissionCheck.Allowed, PermissionCheck.Denied, repo).Dashboard("ldp", null, Projeto));

        Assert.Equal(403, result.StatusCode);
        Assert.Null(repo.Projeto);
    }

    // ---------- fakes ----------

    private sealed class Acesso(PermissionCheck decisao) : IProjectAccessService
    {
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult(decisao);
    }

    private sealed class Permissoes(PermissionCheck decisao) : IPermissionService
    {
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false) =>
            Task.FromResult(decisao);
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class GestaoRepo : IGestaoVistaReportRepository
    {
        public string? Projeto;
        public Task<GestaoVistaReport> ReadAsync(string projectId, CancellationToken cancellationToken)
        {
            Projeto = projectId;
            return Task.FromResult((GestaoVistaReport)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GestaoVistaReport)));
        }
    }

    private sealed class DashRepo : IProductionDashboardRepository
    {
        public string? Projeto;
        public Task<IReadOnlyList<DashboardRow>> ReadRowsAsync(DashboardDefinition definition, string projectId, CancellationToken cancellationToken)
        {
            Projeto = projectId;
            return Task.FromResult<IReadOnlyList<DashboardRow>>(Array.Empty<DashboardRow>());
        }
    }
}
