using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.RobotRun;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace NA_Connect.RobotConfiguration.Tests;

public sealed class ManualMultiProjectTests
{
    private static string Id(int n) => $"b.00000000-0000-0000-0000-{n:000000000000}";
    private sealed class DirectorySource : IRobotProjectDirectory
    {
        public IReadOnlyList<RegisteredRobotProject> Rows = [new(Id(1), "One", Id(10), true, 1), new(Id(2), "Two", Id(10), true, 2), new(Id(3), "Inactive", Id(10), false, 1)];
        public Task<IReadOnlyList<RegisteredRobotProject>> ReadAsync(CancellationToken ct) => Task.FromResult(Rows);
    }
    /// <summary>Gate positivo N&amp;A: UserProjects ativos do usuário.</summary>
    private sealed class Access : IProjectAccessService
    {
        public HashSet<string>? UserProjects; // null = todos
        public PermissionCheck? Forced;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult(Forced ??
            (UserProjects == null || UserProjects.Contains(projectIdAcc!) ? PermissionCheck.Allowed : PermissionCheck.Denied));
    }
    private sealed class Perm : IPermissionService
    {
        public bool LoggedIn = true;
        public HashSet<string>? RunProjects; // null = robot.run em todos
        public List<(string Key, string? Project)> Checks = [];
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys, string? project, CancellationToken ct, bool anyProject = false)
        {
            foreach (var k in keys) Checks.Add((k, project));
            return Task.FromResult(!LoggedIn ? PermissionCheck.Unauthenticated : RunProjects == null || RunProjects.Contains(project!) ? PermissionCheck.Allowed : PermissionCheck.Denied);
        }
        public Task<EffectivePermissions> GetEffectiveAsync(string? p, CancellationToken ct) => throw new NotImplementedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) =>
            Task.FromResult(LoggedIn ? new CurrentPermissionUser(7, "", "user@na.com", "User", false) : null);
        public Task<int?> ResolverProjetoIdAsync(string? p, CancellationToken ct) => throw new NotImplementedException();
    }
    private static (RobotRunController Controller, ManualRobotRunner Runner, List<string?> Calls, DirectorySource Directory, Access Access, Perm Perm, RobotRunGate Gate) Setup()
    {
        var gate = new RobotRunGate(); var calls = new List<string?>(); var directory = new DirectorySource(); var access = new Access(); var perm = new Perm();
        var runner = new ManualRobotRunner(gate, (id, _) => { calls.Add(id); return Task.FromResult(new RobotCycleOutcome(false, "fixture")); }, CancellationToken.None);
        // Sessão N&A sem AccessToken Autodesk: o controller não depende mais dele.
        var controller = new RobotRunController(access, perm, runner, directory, NullLogger<RobotRunController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (controller, runner, calls, directory, access, perm, gate);
    }
    [Fact] public async Task ProjectIdsSemHubIdExecutamSomenteSelecionados()
    {
        var s = Setup();
        Assert.IsType<AcceptedResult>(await s.Controller.Run(new(null, Id(1)), default));
        await s.Runner.Completion; Assert.Equal(new[] { Id(1) }, s.Calls);
        s.Calls.Clear();
        Assert.IsType<AcceptedResult>(await s.Controller.Run(new(null, null, false, [Id(2), Id(1), Id(2)]), default));
        await s.Runner.Completion; Assert.Equal(new[] { Id(2), Id(1) }, s.Calls);
    }
    [Fact] public async Task HubIdLegadoEhIgnorado_HubResolvidoNoCadastro()
    {
        var s = Setup();
        // hubId errado no corpo não muda nada: o backend usa o hub do cadastro.
        Assert.IsType<AcceptedResult>(await s.Controller.Run(new("qualquer-coisa", Id(1)), default));
        await s.Runner.Completion; Assert.Equal(new[] { Id(1) }, s.Calls);
        // Projeto sem hub válido no cadastro: bloqueado no backend.
        s = Setup(); s.Directory.Rows = [new(Id(1), "Sem hub", "", true, 1)];
        var r = Assert.IsType<BadRequestObjectResult>(await s.Controller.Run(new(null, Id(1)), default));
        Assert.Contains("hub", JsonSerializer.Serialize(r.Value), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(s.Calls);
    }
    [Fact] public async Task SemUserProjectNaoExecuta()
    {
        var s = Setup(); s.Access.UserProjects = [Id(2)];
        Assert.Equal(403, Assert.IsType<ObjectResult>(await s.Controller.Run(new(null, null, false, [Id(2), Id(1)]), default)).StatusCode);
        Assert.Empty(s.Calls);
        // robot.run é checado NO PROJETO.
        s = Setup(); s.Perm.RunProjects = [Id(2)];
        Assert.Equal(403, Assert.IsType<ObjectResult>(await s.Controller.Run(new(null, Id(1)), default)).StatusCode);
        Assert.Contains(("robot.run", (string?)Id(1)), s.Perm.Checks);
        Assert.Empty(s.Calls);
    }
    [Fact] public async Task TodosPermitidosExecutaSomenteAtivosComUserProjectERobotRun()
    {
        var s = Setup();
        s.Directory.Rows = [new(Id(1), "One", Id(10), true, 1), new(Id(2), "Two", Id(10), true, 1), new(Id(3), "Three", Id(10), true, 1), new(Id(4), "Inactive", Id(10), false, 1)];
        s.Access.UserProjects = [Id(1), Id(3), Id(4)]; s.Perm.RunProjects = [Id(1), Id(2), Id(4)];
        Assert.IsType<AcceptedResult>(await s.Controller.Run(new(null, null, true), default));
        await s.Runner.Completion;
        Assert.Equal(new[] { Id(1) }, s.Calls);
        s = Setup(); s.Access.UserProjects = [];
        Assert.IsType<BadRequestObjectResult>(await s.Controller.Run(new(null, null, true), default)); Assert.Empty(s.Calls);
    }
    [Fact] public async Task InvalidMixedEmptyInactiveUnknownAndUnauthenticatedDoNotRun()
    {
        var s = Setup();
        foreach (var request in new RobotRunRequest[] { new(null, null), new(null, null, false, []),
            new(null, Id(1), true), new(null, Id(1), false, [Id(2)]), new(null, null, false, ["bad"]), new(null, Id(3)) })
            Assert.IsType<BadRequestObjectResult>(await s.Controller.Run(request, default));
        Assert.IsType<NotFoundObjectResult>(await s.Controller.Run(new(null, Id(99)), default));
        s.Access.Forced = PermissionCheck.Unavailable;
        Assert.Equal(503, Assert.IsType<ObjectResult>(await s.Controller.Run(new(null, Id(1)), default)).StatusCode);
        s.Perm.LoggedIn = false;
        Assert.IsType<UnauthorizedObjectResult>(await s.Controller.Run(new(null, null, true), default));
        Assert.IsType<UnauthorizedObjectResult>(await s.Controller.Status(default));
        Assert.Empty(s.Calls);
    }
    [Fact] public async Task StatusFuncionaComSessaoNaSemAccessToken()
    {
        var s = Setup();
        var ok = Assert.IsType<OkObjectResult>(await s.Controller.Status(default));
        Assert.Equal(RobotRunStatus.Idle, Assert.IsType<RobotRunStatus>(ok.Value).State);
    }
    [Fact] public async Task EmptyActiveSetAndBusyGateDoNotRun_409DevolveExecucaoAtiva()
    {
        var s = Setup(); s.Directory.Rows = [];
        Assert.IsType<BadRequestObjectResult>(await s.Controller.Run(new(null, null, true), default));
        s = Setup(); s.Gate.TryBegin(false, null, null, DateTime.Now, out _);
        var conflict = Assert.IsType<ConflictObjectResult>(await s.Controller.Run(new(null, null, true), default)); Assert.Empty(s.Calls);
        var json = JsonSerializer.Serialize(conflict.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"status\":{\"state\":\"RUNNING\"", json);
    }
    [Fact] public async Task BatchHoldsGateAcrossProjectsAndRunsSequentially()
    {
        var gate = new RobotRunGate(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = new List<string?>();
        var runner = new ManualRobotRunner(gate, async (id, ct) => {
            calls.Add(id); Assert.Equal(RobotRunStatus.Running, gate.Snapshot.State);
            Assert.False(gate.TryBegin(false, null, null, DateTime.Now, out _));
            if (calls.Count == 1) { entered.SetResult(); await release.Task; }
            return new(false, "fixture");
        }, CancellationToken.None);
        Assert.True(runner.TryStartProjects([new(Id(1), "One"), new(Id(2), "Two")]).Started);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { Id(1) }, calls);
        Assert.Equal(1, gate.Snapshot.QueuePosition); Assert.Equal(2, gate.Snapshot.QueueTotal); Assert.Equal("Two", gate.Snapshot.NextProject);
        Assert.False(runner.TryStart(Id(3), "Other").Started);
        release.SetResult(); await runner.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { Id(1), Id(2) }, calls); Assert.Equal(RobotRunStatus.Completed, runner.Status.State);
    }
    [Fact] public async Task FailureDoesNotReleaseGateBeforeRemainingProjects()
    {
        var calls = new List<string?>(); var gate = new RobotRunGate();
        var runner = new ManualRobotRunner(gate, (id, _) => { calls.Add(id); if (id == Id(1)) throw new InvalidOperationException("fixture failure"); return Task.FromResult(new RobotCycleOutcome(false, "ok")); }, CancellationToken.None);
        runner.TryStartProjects([new(Id(1), "One"), new(Id(2), "Two")]); await runner.Completion;
        Assert.Equal(2, calls.Count); Assert.Equal(RobotRunStatus.Error, runner.Status.State);
    }
    [Fact] public void JsonAcceptsLegacyAndNewContract()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(Id(1), JsonSerializer.Deserialize<RobotRunRequest>($"{{\"hubId\":\"{Id(10)}\",\"projectId\":\"{Id(1)}\"}}", options)!.ProjectId);
        Assert.Single(JsonSerializer.Deserialize<RobotRunRequest>($"{{\"projectIds\":[\"{Id(1)}\"]}}", options)!.ProjectIds!);
        Assert.True(JsonSerializer.Deserialize<RobotRunRequest>("{\"allProjects\":true}", options)!.AllProjects);
    }
}
