using System.Text.Json;

namespace APS.WebApp.Services.DocumentQuality;

/// <summary>
/// Histórico persistente da auditoria LDP x ACC. Cada execução gera um snapshot JSON imutável por projeto;
/// o latest é carregado automaticamente pela UI, então trocar de aba/recarregar a página não exige nova varredura do ACC.
/// </summary>
public sealed class DocumentPresenceAuditStore(string rootDirectory)
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    public async Task SaveAsync(DocumentPresenceAuditResult result, CancellationToken ct)
    {
        var dir = ProjectDirectory(result.ProjectId);
        Directory.CreateDirectory(dir);
        var stamp = result.ExecutedAt.UtcDateTime.ToString("yyyyMMdd_HHmmss_fffffff");
        var final = Path.Combine(dir, $"Auditoria_LDP_ACC_{stamp}.json");
        var temp = final + "." + Guid.NewGuid().ToString("N") + ".tmp";

        await _mutex.WaitAsync(ct);
        try
        {
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(result, Json), ct);
            File.Move(temp, final, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            _mutex.Release();
        }
    }

    public async Task<DocumentPresenceAuditResult?> GetLatestAsync(string projectId, CancellationToken ct)
    {
        var dir = ProjectDirectory(projectId);
        if (!Directory.Exists(dir)) return null;
        var file = Directory.EnumerateFiles(dir, "Auditoria_LDP_ACC_*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
        return file is null ? null : await ReadAsync(file, ct);
    }

    private async Task<DocumentPresenceAuditResult?> ReadAsync(string file, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync<DocumentPresenceAuditResult>(stream, Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string ProjectDirectory(string projectId)
    {
        var safe = string.Concat(projectId.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(RootDirectory, safe);
    }
}
