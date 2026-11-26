using System.Text.Json;

namespace APS.WebApp.Services.Tv.Portal;

/// <summary>Separate allowlist; no saved presentation is rewritten. Missing/corrupt file never grants access.</summary>
public sealed class TvPortalAssignmentStore(string path)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string FilePath { get; } = Path.GetFullPath(path);
    public async Task<TvPortalAssignments> ReadAsync(CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try { return await ReadCore(ct); } finally { mutex.Release(); }
    }
    private async Task<TvPortalAssignments> ReadCore(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return new(0, []);
        if (new FileInfo(FilePath).Length > 1_000_000) throw new IOException("Allowlist excessiva.");
        var value = JsonSerializer.Deserialize<TvPortalAssignments>(await File.ReadAllTextAsync(FilePath, ct), Json)
            ?? throw new IOException("Allowlist inválida.");
        Validate(value);
        return value;
    }
    private static void Validate(TvPortalAssignments value)
    {
        if (value.Grants is null || value.Grants.Length > 20 || value.Grants.Any(g => g is null
            || string.IsNullOrWhiteSpace(g.PresentationId) || g.UserIds is null || g.UserIds.Length > 2000 || g.UserIds.Any(id => id <= 0))
            || value.Grants.Select(g => g.PresentationId).Distinct(StringComparer.Ordinal).Count() != value.Grants.Length)
            throw new ArgumentException("Atribuições inválidas.");
    }
    public async Task<TvPortalAssignments> SaveAsync(TvPortalAssignments request, CancellationToken ct)
    {
        Validate(request);
        await mutex.WaitAsync(ct);
        string? temp = null;
        try
        {
            var previous = await ReadCore(ct);
            if (previous.Revision != request.Revision) throw new TvConfigurationConflictException();
            var next = request with { Revision = checked(previous.Revision + 1) };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next, Json), ct);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
            return next;
        }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); mutex.Release(); }
    }
}
