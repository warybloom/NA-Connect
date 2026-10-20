using APS.WebApp.Controllers;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Qualidade.Tests;

public class QualityReportsTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task MissingSessionNeverCallsDependencies()
    {
        var (controller, access, repository) = Create(false);
        Assert.IsType<UnauthorizedObjectResult>(await controller.Quality(Hub, Project));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(ProjectReportAccess.Denied, 403)]
    [InlineData(ProjectReportAccess.Unauthenticated, 401)]
    [InlineData(ProjectReportAccess.Unavailable, 503)]
    public async Task FailedAccessNeverReadsReport(ProjectReportAccess decision, int status)
    {
        var (controller, access, repository) = Create(); access.Decision = decision;
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Quality(Hub, Project));
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
        Assert.IsType<BadRequestObjectResult>(await controller.Quality(Hub, project, page: page, pageSize: size));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task OversizedStatusIsRejected()
    {
        var (controller, access, repository) = Create();
        Assert.IsType<BadRequestObjectResult>(await controller.Quality(Hub, Project, new string('a', 101)));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("Status sem classificação")]
    [InlineData("' OR 1=1--")]
    [InlineData("")]
    public async Task AuthorizedRequestPreservesProjectFilterAndNullGroup(string status)
    {
        var (controller, access, repository) = Create();
        var result = Assert.IsType<OkObjectResult>(await controller.Quality(Hub, Project, status, 2, 25));
        var report = Assert.IsType<QualityReport>(result.Value);
        Assert.Equal(Project, repository.Project);
        Assert.Equal(string.IsNullOrEmpty(status) ? null : status, repository.Status);
        Assert.Equal("session-only", access.Token); Assert.Equal(Hub, access.Hub);
        Assert.Equal(Project, access.Project); Assert.Equal(2, report.Page);
        Assert.Null(report.LastSync);
        var group = Assert.Single(report.Groups);
        Assert.Null(group.Status); Assert.Null(group.Discipline); Assert.Equal(3, group.Count);
    }

    [Fact]
    public async Task RepositoryFailureIs503WithoutInternalDetails()
    {
        var (controller, _, repository) = Create(); repository.Throws = true;
        var result = Assert.IsType<ObjectResult>(await controller.Quality(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    private static (QualityReportsController, Access, Repository) Create(bool authenticated = true)
    {
        var access = new Access(); var repository = new Repository();
        var context = new DefaultHttpContext { Session = new MemorySession() };
        if (authenticated) context.Session.SetString("AccessToken", "session-only");
        return (new QualityReportsController(access, repository, NullLogger<QualityReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } }, access, repository);
    }
    private sealed class Access : IProjectReportAccess
    {
        public int Calls; public string? Token, Hub, Project;
        public ProjectReportAccess Decision = ProjectReportAccess.Allowed;
        public Task<ProjectReportAccess> CheckAsync(string hubId, string projectId, string accessToken, CancellationToken cancellationToken)
        { Calls++; Token = accessToken; Hub = hubId; Project = projectId; return Task.FromResult(Decision); }
    }
    private sealed class Repository : IQualityReportRepository
    {
        public int Calls; public string? Project, Status; public bool Throws;
        public Task<QualityReport> ReadAsync(string projectId, string? status, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId; Status = status;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new QualityReport(3, 3, 1, null, page, pageSize,
                new[] { new QualityReportGroup(null, null, 3) }));
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
