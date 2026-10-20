using APS.WebApp.Services.RobotRun;

namespace NA_Connect.RobotConfiguration.Tests;

public sealed class QueueTests
{
    private sealed class Directory : IRobotProjectDirectory
    {
        public HashSet<string> Inactive { get; } = [];
        public Task<IReadOnlyList<RegisteredRobotProject>> ReadAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RegisteredRobotProject>>(
            Enumerable.Range(1, 3).Select(n => new RegisteredRobotProject(ConfigurationTests.Id(n), "P" + n, "hub", !Inactive.Contains(ConfigurationTests.Id(n)), 1)).ToArray());
    }
    private static RobotQueueItem Item(int n, params string[] stages) => new(ConfigurationTests.Id(n), "P" + n, n, stages.Length == 0 ? ["07"] : stages);

    [Fact] public async Task QueuePreservesOrderAndStagesContinuesAfterFailureAndPublishesStatus()
    {
        var gate = new RobotRunGate(); var calls = new List<(string, string[])>(); var hold = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var runner = new RobotQueueRunner(gate, new Directory(), async (id, stages, ct) => {
            calls.Add((id, stages.ToArray()));
            if (id == ConfigurationTests.Id(1)) { entered.SetResult(); await hold.Task; throw new InvalidOperationException("Falha A"); }
            return new(false, "OK");
        }, CancellationToken.None);
        Assert.True(runner.TryStart("main", [Item(3), Item(1, "07", "05"), Item(2, "01")]).Started);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var status = gate.Snapshot;
        Assert.Equal(1, status.QueuePosition); Assert.Equal(3, status.QueueTotal); Assert.Equal("P2", status.NextProject); Assert.NotNull(status.StartedAt);
        Assert.False(gate.TryBegin(false, null, null, DateTime.Now, out _));
        Assert.False(new ManualRobotRunner(gate, (_, _) => Task.FromResult(new RobotCycleOutcome(false, "")), CancellationToken.None).TryStart(null, null).Started);
        Assert.False(runner.TryStart("main", [Item(2)]).Started);
        hold.SetResult(); await runner.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { ConfigurationTests.Id(1), ConfigurationTests.Id(2), ConfigurationTests.Id(3) }, calls.Select(c => c.Item1));
        Assert.Equal(new[] { "05", "07" }, calls[0].Item2); Assert.Equal(new[] { "01" }, calls[1].Item2);
        Assert.Equal(RobotRunStatus.Error, gate.Snapshot.State);
        Assert.Equal(1, gate.Snapshot.Projects.Count(p => p.State == "ERROR")); Assert.Equal(2, gate.Snapshot.Projects.Count(p => p.State == "COMPLETED"));
        Assert.True(gate.TryBegin(false, null, null, DateTime.Now, out _));
    }
    [Fact] public async Task ProjectDisabledAfterAdmissionIsNotExecuted()
    {
        var directory = new Directory(); directory.Inactive.Add(ConfigurationTests.Id(1));
        var calls = new List<string>(); var gate = new RobotRunGate();
        var runner = new RobotQueueRunner(gate, directory, (id, _, _) => { calls.Add(id); return Task.FromResult(new RobotCycleOutcome(false, "ok")); }, CancellationToken.None);
        runner.TryStart("main", [Item(1), Item(2)]); await runner.Completion;
        Assert.Equal(ConfigurationTests.Id(2), Assert.Single(calls));
        Assert.Equal("ERROR", gate.Snapshot.Projects[0].State);
    }
    [Fact] public async Task CallerCannotChangeAdmittedQueueAndAutomaticGateBlocksQueue()
    {
        var gate = new RobotRunGate(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource(); string[]? received = null;
        var runner = new RobotQueueRunner(gate, new Directory(), async (_, stages, _) => { entered.SetResult(); await release.Task; received = stages.ToArray(); return new(false, "ok"); }, CancellationToken.None);
        gate.TryBegin(false, null, null, DateTime.Now, out _); Assert.False(runner.TryStart("main", [Item(1)]).Started);
        gate.Finish(new(false, "ok"), DateTime.Now);
        var item = Item(1); runner.TryStart("main", [item]); await entered.Task;
        item.Stages[0] = "17"; release.SetResult(); await runner.Completion;
        Assert.Equal("07", Assert.Single(received!)); Assert.Equal(RobotRunStatus.Completed, gate.Snapshot.State);
    }
}
