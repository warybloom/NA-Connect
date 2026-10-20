using System.Text.Json;

namespace APS.WebApp.Services.ProjectIdentity;

/// <summary>Logo do Projeto (Identidade Visual): uma única configuração por Project_ID_ACC, consumida por todas as telas.</summary>
public sealed record ProjectLogoInfo(string ProjectId, string FileName, string ContentType, long Version, DateTimeOffset UpdatedAt, string? UpdatedBy);

public sealed class ProjectIdentityException(string message) : Exception(message);

/// <summary>
/// Auditoria (30/09/2026): dbo.Projetos NÃO tem coluna de logo/imagem e não há mecanismo de arquivos reutilizável no
/// backend. Sem DDL: logos ficam em App_Data/project-identity/ (um arquivo por projeto) + índice JSON, com escrita
/// atômica — mesmo padrão de RobotConfigurationStore/TvConfigurationStore. Só PNG/JPEG/WebP (conferidos pelos bytes
/// iniciais, não pela extensão); SVG é recusado (pode conter script).
/// </summary>
public sealed class ProjectIdentityStore
{
    public const int TamanhoMaximo = 1_048_576; // 1 MB
    private readonly SemaphoreSlim mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string pasta;
    private string Indice => Path.Combine(pasta, "index.json");

    public ProjectIdentityStore(string directory) => pasta = Path.GetFullPath(directory);

    public static bool EhAccId(string? v) =>
        v is { Length: 38 } && v.StartsWith("b.", StringComparison.Ordinal) && Guid.TryParseExact(v[2..], "D", out _);

    /// <summary>Tipo real da imagem pelos bytes iniciais (null = não suportado).</summary>
    public static (string ContentType, string Ext)? DetectarImagem(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("image/png", "png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", "jpg");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", "webp");
        return null;
    }

    public async Task<IReadOnlyList<ProjectLogoInfo>> ListAsync(CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try { return (await LerIndice(ct)).Values.OrderBy(i => i.ProjectId, StringComparer.Ordinal).ToArray(); }
        finally { mutex.Release(); }
    }

    public async Task<(ProjectLogoInfo Info, byte[] Bytes)?> ReadAsync(string projectId, CancellationToken ct = default)
    {
        if (!EhAccId(projectId)) return null;
        await mutex.WaitAsync(ct);
        try
        {
            if (!(await LerIndice(ct)).TryGetValue(projectId, out var info)) return null;
            var caminho = Path.Combine(pasta, info.FileName);
            return File.Exists(caminho) ? (info, await File.ReadAllBytesAsync(caminho, ct)) : null;
        }
        finally { mutex.Release(); }
    }

    public async Task<ProjectLogoInfo> SaveAsync(string projectId, byte[] bytes, string? updatedBy, CancellationToken ct = default)
    {
        if (!EhAccId(projectId)) throw new ArgumentException("Projeto inválido.");
        if (bytes.Length == 0) throw new ArgumentException("Arquivo vazio.");
        if (bytes.Length > TamanhoMaximo) throw new ArgumentException("A logo deve ter no máximo 1 MB.");
        var tipo = DetectarImagem(bytes) ?? throw new ArgumentException("Formato não suportado. Use PNG, JPG ou WebP.");
        await mutex.WaitAsync(ct);
        try
        {
            var indice = await LerIndice(ct);
            Directory.CreateDirectory(pasta);
            var nome = $"{projectId}.{tipo.Ext}";
            await EscreverAtomico(Path.Combine(pasta, nome), bytes, ct);
            // Formato mudou (ex.: png → jpg): o arquivo antigo sai para não sobrar lixo.
            if (indice.TryGetValue(projectId, out var anterior) && anterior.FileName != nome) TryDelete(Path.Combine(pasta, anterior.FileName));
            var info = new ProjectLogoInfo(projectId, nome, tipo.ContentType, (anterior?.Version ?? 0) + 1, DateTimeOffset.UtcNow, updatedBy);
            indice[projectId] = info;
            await GravarIndice(indice, ct);
            return info;
        }
        finally { mutex.Release(); }
    }

    public async Task<bool> DeleteAsync(string projectId, CancellationToken ct = default)
    {
        if (!EhAccId(projectId)) return false;
        await mutex.WaitAsync(ct);
        try
        {
            var indice = await LerIndice(ct);
            if (!indice.Remove(projectId, out var info)) return false;
            await GravarIndice(indice, ct);
            TryDelete(Path.Combine(pasta, info.FileName));
            return true;
        }
        finally { mutex.Release(); }
    }

    private async Task<Dictionary<string, ProjectLogoInfo>> LerIndice(CancellationToken ct)
    {
        if (!File.Exists(Indice)) return new(StringComparer.Ordinal);
        try
        {
            var lista = JsonSerializer.Deserialize<List<ProjectLogoInfo>>(await File.ReadAllTextAsync(Indice, ct), Json) ?? [];
            return lista.Where(i => EhAccId(i.ProjectId) && !i.FileName.Contains("..") && Path.GetFileName(i.FileName) == i.FileName)
                .ToDictionary(i => i.ProjectId, StringComparer.Ordinal);
        }
        catch (JsonException) { throw new ProjectIdentityException("Índice de logos corrompido. Restaure App_Data/project-identity/index.json.bak."); }
    }

    private async Task GravarIndice(Dictionary<string, ProjectLogoInfo> indice, CancellationToken ct) =>
        await EscreverAtomico(Indice, JsonSerializer.SerializeToUtf8Bytes(indice.Values.OrderBy(i => i.ProjectId, StringComparer.Ordinal).ToArray(), Json), ct);

    private static async Task EscreverAtomico(string destino, byte[] bytes, CancellationToken ct)
    {
        var temp = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await s.WriteAsync(bytes, ct);
                s.Flush(flushToDisk: true);
            }
            if (File.Exists(destino)) File.Replace(temp, destino, destino + ".bak");
            else File.Move(temp, destino);
        }
        finally { TryDelete(temp); }
    }

    private static void TryDelete(string caminho) { try { if (File.Exists(caminho)) File.Delete(caminho); } catch (IOException) { /* best-effort */ } }
}
