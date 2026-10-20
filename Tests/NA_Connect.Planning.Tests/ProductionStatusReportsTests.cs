using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

public class ProductionStatusReportsTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task MissingSessionNeverCallsDependencies()
    {
        var (controller, access, repository) = Create(PermissionCheck.Unauthenticated);
        Assert.IsType<UnauthorizedObjectResult>(await controller.ProductionStatus(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unauthenticated, 401)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task FailedAccessNeverReadsReport(PermissionCheck decision, int status)
    {
        var (controller, access, repository) = Create(); access.Decision = decision;
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.ProductionStatus(Hub, Project));
        Assert.Equal(status, result.StatusCode); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("../project")]
    [InlineData("not-an-acc-id")]
    public async Task InvalidScopeNeverCallsDependencies(string project)
    {
        var (controller, access, repository) = Create();
        Assert.IsType<BadRequestObjectResult>(await controller.ProductionStatus(Hub, project));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task AuthorizedRequestPreservesProjectAndReturnsReport()
    {
        var (controller, access, repository) = Create();
        var result = Assert.IsType<OkObjectResult>(await controller.ProductionStatus(Hub, Project));
        var report = Assert.IsType<ProductionStatusReport>(result.Value);
        Assert.Equal(Project, repository.Project);
        Assert.Equal(Project, access.Project);
        Assert.Single(report.Documentos);
        Assert.Equal("FISICO_PARCIAL", report.Documentos[0].ClasseFisica);
    }

    [Fact]
    public async Task RepositoryFailureIs503WithoutInternalDetails()
    {
        var (controller, _, repository) = Create(); repository.Throws = true;
        var result = Assert.IsType<ObjectResult>(await controller.ProductionStatus(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    [Fact]
    public async Task SessaoNaSemTokenEHubVazio_UsaSomenteGateInternoESql()
    {
        var (controller, access, repository) = Create();
        Assert.IsType<OkObjectResult>(await controller.ProductionStatus("", Project));
        Assert.Equal(1, access.Calls); Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task GateInternoPassa_MasPermissaoFuncionalNegada_NaoLeRepository()
    {
        var access = new ProjectAccess(); var repository = new Repository(); var perm = new Perm { Decision = PermissionCheck.Denied };
        var controller = new ProductionStatusReportsController(access, perm, repository, NullLogger<ProductionStatusReportsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } }
        };
        var result = Assert.IsType<ObjectResult>(await controller.ProductionStatus("", Project));
        Assert.Equal(403, result.StatusCode); Assert.Equal(0, repository.Calls);
        Assert.Equal(new[] { "reports.producao.view" }, perm.Keys);
    }

    private static (ProductionStatusReportsController, ProjectAccess, Repository) Create(PermissionCheck decision = PermissionCheck.Allowed)
    {
        var access = new ProjectAccess { Decision = decision }; var repository = new Repository();
        var context = new DefaultHttpContext { Session = new MemorySession() };
        return (new ProductionStatusReportsController(access, new Perm(), repository, NullLogger<ProductionStatusReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } }, access, repository);
    }
    private sealed class ProjectAccess : IProjectAccessService
    {
        public int Calls; public string? Project;
        public PermissionCheck Decision = PermissionCheck.Allowed;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectId, CancellationToken cancellationToken)
        { Calls++; Project = projectId; return Task.FromResult(Decision); }
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
    private sealed class Repository : IProductionStatusReportRepository
    {
        public int Calls; public string? Project; public bool Throws;
        public Task<ProductionStatusReport> ReadAsync(string projectId, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new ProductionStatusReport(
                new[] { new MapaCalorDocumento(1, "DOC-1", "Título", "Tubulação", "EM ANDAMENTO", "APROVADO", "EM ANDAMENTO", 50m, "FISICO_PARCIAL") }));
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
