using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace NA_Connect.Planning.Tests;

public class WeeklyReportsTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task MissingSessionDoesNotCallAccOrSql()
    {
        var (controller, access, repository) = Create(false);
        Assert.IsType<UnauthorizedObjectResult>(await controller.Weekly(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unauthenticated, 401)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task FailedAccessNeverQueriesSql(PermissionCheck decision, int status)
    {
        var (controller, access, repository) = Create(); access.Decision = decision;
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Weekly(Hub, Project));
        Assert.Equal(status, result.StatusCode); Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("../project", 1, 25, null)]
    [InlineData(Project, 0, 25, null)]
    [InlineData(Project, 1, 101, null)]
    [InlineData(Project, 1000001, 25, null)]
    [InlineData(Project, 1, 25, "' OR 1=1--")]
    public async Task InvalidFiltersDoNotReachDependencies(string project, int page, int size, string? status)
    {
        var (controller, access, repository) = Create();
        Assert.IsType<BadRequestObjectResult>(await controller.Weekly(Hub, project, status, page, size));
        Assert.Equal(0, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task SuccessPreservesExactProjectAndNulls()
    {
        var (controller, access, repository) = Create();
        var result = Assert.IsType<OkObjectResult>(await controller.Weekly(Hub, Project, "COM_ALERTAS", 2, 25));
        var report = Assert.IsType<WeeklyReport>(result.Value);
        Assert.Equal(Project, repository.Project); Assert.Equal("COM_ALERTAS", repository.Status);
        Assert.Null(report.LastSync);
        Assert.Null(Assert.Single(report.Rows).Diagnostic);
        Assert.Equal(2, report.Page);
    }

    [Fact]
    public async Task DependencyExceptionIsNotExposed()
    {
        var (controller, access, repository) = Create(); access.Throws = true;
        var result = Assert.IsType<ObjectResult>(await controller.Weekly(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(200, "{\"data\":[]}", ProjectReportAccess.Allowed)]
    [InlineData(200, "{}", ProjectReportAccess.Unavailable)]
    [InlineData(200, "{\"data\":{}}", ProjectReportAccess.Unavailable)]
    [InlineData(401, "", ProjectReportAccess.Unauthenticated)]
    [InlineData(403, "", ProjectReportAccess.Denied)]
    [InlineData(404, "", ProjectReportAccess.Denied)]
    [InlineData(500, "", ProjectReportAccess.Unavailable)]
    public async Task AccGateUsesSessionBearerAndFailsClosed(int status, string body, ProjectReportAccess expected)
    {
        var handler = new StubHandler(status, body);
        var gate = new AccProjectReportAccess(new ClientFactory(handler));
        Assert.Equal(expected, await gate.CheckAsync(Hub, Project, "session-only", default));
        Assert.Equal("GET", handler.Method); Assert.Equal("Bearer session-only", handler.Authorization);
        Assert.EndsWith($"/hubs/{Hub}/projects/{Project}/topFolders", handler.Url);
    }

    private static (WeeklyReportsController, Access, Repository) Create(bool authenticated = true)
    {
        var access = new Access(); var repository = new Repository();
        var context = new DefaultHttpContext { Session = new MemorySession() };
        access.Decision = authenticated ? PermissionCheck.Allowed : PermissionCheck.Unauthenticated;
        var controller = new WeeklyReportsController(access, new Perm(), repository, NullLogger<WeeklyReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = context } };
        return (controller, access, repository);
    }
    private sealed class Access : IProjectAccessService
    {
        public int Calls; public bool Throws;
        public PermissionCheck Decision = PermissionCheck.Allowed;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectId, CancellationToken cancellationToken)
        { Calls++; if (Throws) throw new HttpRequestException("secret"); return Task.FromResult(Decision); }
    }
    private sealed class Perm : IPermissionService
    {
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys, string? projectId, CancellationToken ct, bool anyProject = false) => Task.FromResult(PermissionCheck.Allowed);
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Repository : IWeeklyReportRepository
    {
        public int Calls; public string? Project; public string? Status;
        public Task<WeeklyReport> ReadAsync(string projectId, string? status, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId; Status = status;
            return Task.FromResult(new WeeklyReport(1, 0, 0, 0, 1, 1, null, page, pageSize,
                new[] { new WeeklyReportRow(1, null, null, null, "DADO", "OK", null, null, null, null) }));
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
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class StubHandler(int status, string body) : HttpMessageHandler
    {
        public string? Method, Authorization, Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method.Method; Authorization = request.Headers.Authorization?.ToString(); Url = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }
}
