using System.Text.Json;

namespace APS.WebApp.Services.RobotRun;

public sealed class RobotConfigurationStore(string filePath)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string FilePath { get; } = Path.GetFullPath(filePath);
    public bool Exists => File.Exists(FilePath);
    public async Task<RobotConfiguration> ReadAsync(CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try { return await ReadCore(ct); }
        finally { mutex.Release(); }
    }
    private async Task<RobotConfiguration> ReadCore(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return RobotConfiguration.Empty;
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Length > 2_000_000) throw new ArgumentException("Arquivo excessivo.");
            var config = JsonSerializer.Deserialize<RobotConfiguration>(await File.ReadAllTextAsync(FilePath, ct), Json);
            return RobotConfigurationRules.Validate(config!);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or IOException)
        { throw new RobotConfigurationException("Configuração ausente de conteúdo válido/corrompida. Execução bloqueada; restaure o backup válido com o administrador."); }
    }
    public async Task<RobotConfiguration> SaveAsync(string robotId, RobotDefinition robot, long expectedRevision, CancellationToken ct = default)
    {
        if (robotId != robot.Id) throw new ArgumentException("Robô divergente.");
        RobotConfigurationRules.Validate(new(1, 0, null, [robot]));
        await mutex.WaitAsync(ct);
        string? temp = null;
        try
        {
            var current = await ReadCore(ct); // Corruption never overwrites the last good backup.
            if (current.Revision != expectedRevision) throw new RobotConfigurationConflictException();
            if (!current.Robots.Any(r => r.Id == robotId)) throw new ArgumentException("Robô não cadastrado. Criação de robôs adicionais indisponível nesta versão.");
            var next = RobotConfigurationRules.Validate(new(1, checked(current.Revision + 1), DateTimeOffset.UtcNow,
                current.Robots.Select(r => r.Id == robotId ? robot : r).ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                stream.Flush(flushToDisk: true);
            }
            RobotConfigurationRules.Validate(JsonSerializer.Deserialize<RobotConfiguration>(await File.ReadAllTextAsync(temp, ct), Json)!);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
            return next;
        }
        finally { if (temp != null && File.Exists(temp)) File.Delete(temp); mutex.Release(); }
    }
}
