using APS.WebApp.Controllers;
using APS.WebApp.Services.RobotRun;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.RobotConfiguration.Tests;

public sealed class ControllerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "robot-config-api-tests-" + Guid.NewGuid().ToString("N"));
    private sealed class DirectorySource : IRobotProjectDirectory
    {
        public Task<IReadOnlyList<RegisteredRobotProject>> ReadAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RegisteredRobotProject>>(
            [new(ConfigurationTests.Id(1), "Controlled fixture", ConfigurationTests.Id(10), true, 1)]);
    }
    private sealed class Access(ProjectReportAccess decision) : IProjectReportAccess
    {
        public Task<ProjectReportAccess> CheckAsync(string h, string p, string token, CancellationToken ct) => Task.FromResult(decision);
    }
    private sealed class Session : ISession
    {
        private readonly Dictionary<string, byte[]> values = [];
        public string Id => "unit-test"; public bool IsAvailable => true; public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear(); public void Remove(string key) => values.Remove(key); public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
    private (RobotConfigurationController Controller, RobotConfigurationStore Store, RobotQueueRunner Runner, RobotRunGate Gate) Setup(bool loggedIn, ProjectReportAccess access = ProjectReportAccess.Allowed)
    {
        var store = new RobotConfigurationStore(Path.Combine(root, "configuration.json")); var directory = new DirectorySource(); var gate = new RobotRunGate();
        var runner = new RobotQueueRunner(gate, directory, (_, _, _) => Task.FromResult(new RobotCycleOutcome(false, "Controlled fixture; no real worker")), CancellationToken.None);
        var context = new DefaultHttpContext { Session = new Session() };
        if (loggedIn) context.Session.SetString("AccessToken", "unit-test-only");
        var controller = new RobotConfigurationController(store, directory, new Access(access), runner, NullLogger<RobotConfigurationController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        return (controller, store, runner, gate);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Fact] public async Task NoSessionCannotReadSaveOrRun()
    {
        var s = Setup(false);
        Assert.IsType<UnauthorizedObjectResult>(await s.Controller.Get(default));
        Assert.IsType<UnauthorizedObjectResult>(await s.Controller.Save("main", new(0, ConfigurationTests.Robot(ConfigurationTests.Project(1, 1))), default));
        Assert.IsType<UnauthorizedObjectResult>(await s.Controller.Run(new("main", 0), default));
        Assert.False(s.Store.Exists); Assert.Equal(RobotRunStatus.Idle, s.Gate.Snapshot.State);
    }
    [Fact] public async Task UnknownAndUnauthorizedProjectNeverSavesOrRuns()
    {
        var s = Setup(true, ProjectReportAccess.Denied);
        Assert.IsType<BadRequestObjectResult>(await s.Controller.Save("main", new(0, ConfigurationTests.Robot(ConfigurationTests.Project(99, 1))), default));
        var denied = await s.Controller.Save("main", new(0, ConfigurationTests.Robot(ConfigurationTests.Project(1, 1))), default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(denied).StatusCode);
        Assert.False(s.Store.Exists); Assert.Equal(RobotRunStatus.Idle, s.Gate.Snapshot.State);
    }
    [Fact] public async Task SaveReloadAndRunConfiguredUsesSavedRevisionAndExistingGate()
    {
        var s = Setup(true);
        Assert.IsType<OkObjectResult>(await s.Controller.Save("main", new(0, ConfigurationTests.Robot(ConfigurationTests.Project(1, 1))), default));
        Assert.Equal(1, (await s.Store.ReadAsync()).Revision);
        Assert.IsType<ConflictObjectResult>(await s.Controller.Run(new("main", 0), default));
        s.Gate.TryBegin(false, null, null, DateTime.Now, out _);
        Assert.IsType<ConflictObjectResult>(await s.Controller.Run(new("main", 1), default));
        s.Gate.Finish(new(false, "finished"), DateTime.Now);
        Assert.IsType<AcceptedResult>(await s.Controller.Run(new("main", 1), default));
        await s.Runner.Completion;
        Assert.Equal(RobotRunStatus.Completed, s.Gate.Snapshot.State);
    }
    [Fact] public async Task CorruptedJsonCannotTriggerPipeline()
    {
        var s = Setup(true); Directory.CreateDirectory(root); await File.WriteAllTextAsync(s.Store.FilePath, "not-json");
        var result = await s.Controller.Run(new("main", 0), default);
        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(RobotRunStatus.Idle, s.Gate.Snapshot.State);
    }
}
