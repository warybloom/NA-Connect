using APS.WebApp.Services.RobotRun;
using System.Text.Json;

namespace NA_Connect.RobotConfiguration.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "robot-config-tests-" + Guid.NewGuid().ToString("N"));
    private RobotConfigurationStore Store => new(Path.Combine(root, "robot-configuration.json"));
    internal static string Id(int n) => $"b.00000000-0000-0000-0000-{n:D12}";
    internal static RobotDefinition Robot(params RobotProjectSelection[] projects) => new("main", "Robô Principal", true, projects);
    internal static RobotProjectSelection Project(int n, int order, params string[] stages) => new(Id(n), order, true, stages.Length == 0 ? ["07"] : stages);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); } // Only this test's generated directory.

    [Fact] public async Task SaveReloadOrderAndStagesPersistAndBackupIsPreviousValidVersion()
    {
        var store = Store;
        var saved = await store.SaveAsync("main", Robot(Project(3, 3, "07", "01", "05"), Project(1, 1), Project(2, 2)), 0);
        var loaded = await Store.ReadAsync();
        Assert.Equal(new[] { 1, 2, 3 }, loaded.Robots[0].Projects.Select(p => p.Order));
        Assert.Equal(new[] { "01", "05", "07" }, loaded.Robots[0].Projects[2].Stages);
        Assert.Equal(saved.SavedAt, loaded.SavedAt);
        await store.SaveAsync("main", loaded.Robots[0] with { Enabled = false }, 1);
        var backup = JsonSerializer.Deserialize<APS.WebApp.Services.RobotRun.RobotConfiguration>(await File.ReadAllTextAsync(store.FilePath + ".bak"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1, backup!.Revision); Assert.True(backup.Robots[0].Enabled);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact] public async Task MissingAndCorruptedConfigurationNeverBuildQueueOrOverwriteBackup()
    {
        var empty = await Store.ReadAsync();
        Assert.Throws<ArgumentException>(() => RobotConfigurationRules.BuildQueue(empty.Robots[0], []));
        Directory.CreateDirectory(root); await File.WriteAllTextAsync(Store.FilePath, "{broken");
        await Assert.ThrowsAsync<RobotConfigurationException>(() => Store.ReadAsync());
        await Assert.ThrowsAsync<RobotConfigurationException>(() => Store.SaveAsync("main", Robot(Project(1, 1)), 0));
        Assert.Equal("{broken", await File.ReadAllTextAsync(Store.FilePath));
    }
    [Fact] public async Task AtomicApiReadersNeverObservePartialConfigurationDuringConcurrentSaves()
    {
        var store = Store; await store.SaveAsync("main", Robot(Project(1, 1)), 0);
        var writer = Task.Run(async () => { for (int i = 1; i <= 20; i++) await store.SaveAsync("main", Robot(Project(1, 1)), i); });
        try { for (int i = 0; i < 100; i++)
        {
            var snapshot = await store.ReadAsync();
            Assert.Equal(1, snapshot.Version);
            Assert.Single(snapshot.Robots[0].Projects);
        } } finally { await writer; }
        Assert.Equal(21, (await store.ReadAsync()).Revision);
    }
    [Fact] public async Task InvalidStagesStaleWritesAndCancellationPreserveConfiguration()
    {
        var store = Store;
        await store.SaveAsync("main", Robot(Project(1, 1)), 0);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("main", Robot(Project(1, 1, "99")), 1));
        await Assert.ThrowsAsync<RobotConfigurationConflictException>(() => store.SaveAsync("main", Robot(Project(1, 1)), 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync("main", Robot(Project(1, 1)), 1, new CancellationToken(true)));
        Assert.Equal(1, (await store.ReadAsync()).Revision);
    }
    [Fact] public void DependenciesAndDisabledStagesAreNotSilentlyExpanded()
    {
        foreach (var stages in new[] { new[] { "02" }, new[] { "08" }, new[] { "03" }, new[] { "5B" } }) Assert.Throws<ArgumentException>(() => RobotStageCatalog.Validate(stages));
        Assert.Equal(new[] { "09" }, RobotStageCatalog.Validate(["09"], 2));
        Assert.Equal(new[] { "10" }, RobotStageCatalog.Validate(["10"], 2));
        Assert.Equal(new[] { "06" }, RobotStageCatalog.Validate(["06"], 2));
        Assert.Equal(new[] { "00", "09", "11" }, RobotStageCatalog.Validate(["11", "00", "09"], 2));
        Assert.Equal(new[] { "07" }, RobotStageCatalog.Validate(["07"], 2));
    }
    [Fact] public void InactiveProjectsAreBlockedAndDisabledSelectionsAreExcluded()
    {
        RegisteredRobotProject[] registry = [new(Id(1), "A", "hub", false, 1), new(Id(2), "B", "hub", true, 1)];
        Assert.Throws<ArgumentException>(() => RobotConfigurationRules.BuildQueue(Robot(Project(1, 1)), registry));
        var queue = RobotConfigurationRules.BuildQueue(Robot(Project(1, 1) with { Enabled = false }, Project(2, 2)), registry);
        Assert.Equal(Id(2), Assert.Single(queue).ProjectId);
    }
}
