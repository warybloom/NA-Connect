using APS.WebApp.Controllers.WeeklyPlanning;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.WeeklyPlanning.Tests;

public sealed class ControllerTests
{
    private const string Id = "b.11111111-1111-1111-1111-111111111111";
    private static readonly DateOnly Start = new(2026, 9, 21);
    private sealed class Access(ProjectReportAccess decision) : IProjectReportAccess
    {
        public int Calls { get; private set; }
        public Task<ProjectReportAccess> CheckAsync(string h, string p, string token, CancellationToken ct)
        { Calls++; return Task.FromResult(decision); }
    }
    private sealed class Session : ISession
    {
        private readonly Dictionary<string, byte[]> data = [];
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => data.Keys;
        public void Clear() => data.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => data.Remove(key);
        public void Set(string key, byte[] value) => data[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => data.TryGetValue(key, out value);
    }
    private static WeeklyPlanningController Controller(Access access, bool authenticated)
    {
        var context = new DefaultHttpContext { Session = new Session() };
        if (authenticated) context.Session.SetString("AccessToken", "test-token");
        return new(new ConfigurationBuilder().Build(), access, NullLogger<WeeklyPlanningController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } };
    }
    [Fact] public async Task MissingSessionNeverReadsSource()
    {
        var access = new Access(ProjectReportAccess.Allowed);
        Assert.IsType<UnauthorizedObjectResult>(await Controller(access, false).Documents(Id, Id, Start, Start.AddDays(4)));
        Assert.Equal(0, access.Calls);
    }
    [Theory]
    [InlineData(ProjectReportAccess.Denied, 403)]
    [InlineData(ProjectReportAccess.Unavailable, 503)]
    [InlineData(ProjectReportAccess.Unauthenticated, 401)]
    public async Task ExistingProjectAccessFailsClosed(ProjectReportAccess decision, int status)
    {
        var result = await Controller(new Access(decision), true).Documents(Id, Id, Start, Start.AddDays(4));
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }
    [Fact] public async Task InvalidFiltersNeverCallProjectAccess()
    {
        var access = new Access(ProjectReportAccess.Allowed);
        var controller = Controller(access, true);
        Assert.IsType<BadRequestObjectResult>(await controller.Documents(Id, Id, Start, Start.AddDays(7)));
        Assert.IsType<BadRequestObjectResult>(await controller.Documents(Id, Id, Start, Start, pageSize: 101));
        Assert.IsType<BadRequestObjectResult>(await controller.Legacy(Id, "wrong", Start, Start));
        Assert.Equal(0, access.Calls);
    }
}
