using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

public class ProductionDocumentsReportsTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task MissingSessionNeverCallsDependencies()
    {
        var (controller, access, repository) = Create(false);
        Assert.IsType<UnauthorizedObjectResult>(await controller.Documents(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unauthenticated, 401)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task FailedAccessNeverReadsReport(PermissionCheck decision, int status)
    {
        var (controller, access, repository) = Create(); access.Decision = decision;
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Documents(Hub, Project));
        Assert.Equal(status, result.StatusCode); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("../project", 1, 25)]
    [InlineData(Project, 0, 25)]
    [InlineData(Project, 1, 101)]
    [InlineData(Project, 1000001, 25)]
    public async Task InvalidScopeOrPaginationNeverCallsDependencies(string project, int page, int size)
    {
        var (controller, access, repository) = Create();
        Assert.IsType<BadRequestObjectResult>(await controller.Documents(Hub, project, page: page, pageSize: size));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task OversizedStatusIsRejected()
    {
        var (controller, access, repository) = Create();
        Assert.IsType<BadRequestObjectResult>(await controller.Documents(Hub, Project, new string('a', 101)));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("EM ANDAMENTO")]
    [InlineData("' OR 1=1--")]
    [InlineData("")]
    public async Task AuthorizedRequestPreservesProjectFilterAndNullGroup(string documentStatus)
    {
        var (controller, access, repository) = Create();
        var result = Assert.IsType<OkObjectResult>(await controller.Documents(Hub, Project, documentStatus, 2, 25));
        var report = Assert.IsType<ProductionDocumentsReport>(result.Value);
        Assert.Equal(Project, repository.Project);
        Assert.Equal(string.IsNullOrEmpty(documentStatus) ? null : documentStatus, repository.DocumentStatus);
        Assert.Equal(Project, access.Project); Assert.Equal(2, report.Page);
        Assert.Null(report.LastSync);
        var group = Assert.Single(report.Groups);
        Assert.Null(group.DocumentStatus); Assert.Null(group.Discipline); Assert.Equal(3, group.Count);
    }

    [Fact]
    public async Task RepositoryFailureIs503WithoutInternalDetails()
    {
        var (controller, _, repository) = Create(); repository.Throws = true;
        var result = Assert.IsType<ObjectResult>(await controller.Documents(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    private static (ProductionDocumentsReportsController, Access, Repository) Create(bool authenticated = true)
    {
        var access = new Access(); var repository = new Repository();
        var context = new DefaultHttpContext { Session = new MemorySession() };
        access.Decision = authenticated ? PermissionCheck.Allowed : PermissionCheck.Unauthenticated;
        return (new ProductionDocumentsReportsController(access, new Perm(), repository, NullLogger<ProductionDocumentsReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } }, access, repository);
    }
    private sealed class Access : IProjectAccessService
    {
        public int Calls; public string? Project;
        public PermissionCheck Decision = PermissionCheck.Allowed;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectId, CancellationToken cancellationToken)
        { Calls++; Project = projectId; return Task.FromResult(Decision); }
    }
    private sealed class Perm : IPermissionService
    {
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys, string? projectId, CancellationToken ct, bool anyProject = false) => Task.FromResult(PermissionCheck.Allowed);
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Repository : IProductionDocumentsReportRepository
    {
        public int Calls; public string? Project, DocumentStatus; public bool Throws;
        public Task<ProductionDocumentsReport> ReadAsync(string projectId, string? documentStatus, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId; DocumentStatus = documentStatus;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new ProductionDocumentsReport(3, 3, 1, 2, 0, 1, null, page, pageSize,
                new[] { new ProductionDocumentsReportGroup(null, null, 3) }));
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
