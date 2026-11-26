using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;

namespace APS.WebApp.Services.BiBml011016;

public sealed record BiBml011016PbixRecurso(string Caminho, long Bytes);

public sealed record BiBml011016PbixEstado(string Estado, string? Motivo, string? Sha256)
{
    public const string Ok = "OK";
    public const string Pendente = "PENDENTE";
    public const string Divergente = "PBIX_DIVERGENTE";
}

/// <summary>
/// Serve a definição PBIR/assets do PBIX SOMENTE pelo endpoint protegido (nada em wwwroot/public). Caminho do arquivo vem só da
/// configuração do servidor (BiBml011016:PbixPath) e o SHA256 precisa ser o do PBIX entregue pelo usuário. Apenas entradas
/// "Report/**" são expostas — DataModel (dados embutidos no PBIX) NUNCA é servido.
/// </summary>
public sealed class BiBml011016Pbix(string? caminhoArquivo, string sha256Esperado = BiBml011016Catalogo.Sha256Pbix)
{
    public const string Prefixo = "Report/";
    private static readonly ConcurrentDictionary<(string, long, DateTime), string> Hashes = new();

    public BiBml011016PbixEstado Verificar()
    {
        if (string.IsNullOrWhiteSpace(caminhoArquivo))
            return new(BiBml011016PbixEstado.Pendente, "BiBml011016:PbixPath não configurado no servidor.", null);
        var info = new FileInfo(caminhoArquivo);
        if (!info.Exists)
            return new(BiBml011016PbixEstado.Pendente, "Arquivo PBIX configurado não encontrado no servidor.", null);
        var sha = Hashes.GetOrAdd((info.FullName, info.Length, info.LastWriteTimeUtc), _ =>
        {
            using var s = info.OpenRead();
            return Convert.ToHexString(SHA256.HashData(s));
        });
        return string.Equals(sha, sha256Esperado, StringComparison.OrdinalIgnoreCase)
            ? new(BiBml011016PbixEstado.Ok, null, sha)
            : new(BiBml011016PbixEstado.Divergente, "SHA256 do PBIX configurado difere do PBIX de referência do BI BML011.016.", sha);
    }

    public static bool CaminhoPermitido(string? caminho) =>
        !string.IsNullOrWhiteSpace(caminho)
        && caminho.StartsWith(Prefixo, StringComparison.Ordinal)
        && !caminho.Contains('\\') && !caminho.Contains("..", StringComparison.Ordinal) && !caminho.Contains(':')
        && !caminho.EndsWith('/');

    public IReadOnlyList<BiBml011016PbixRecurso> Manifesto()
    {
        using var zip = ZipFile.OpenRead(caminhoArquivo!);
        return zip.Entries.Where(e => CaminhoPermitido(e.FullName))
            .Select(e => new BiBml011016PbixRecurso(e.FullName, e.Length))
            .OrderBy(e => e.Caminho, StringComparer.Ordinal).ToArray();
    }

    /// <returns>null quando o caminho não é uma entrada permitida do PBIX.</returns>
    public byte[]? Ler(string caminho)
    {
        if (!CaminhoPermitido(caminho)) return null;
        using var zip = ZipFile.OpenRead(caminhoArquivo!);
        var entrada = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, caminho, StringComparison.Ordinal));
        if (entrada is null) return null;
        using var s = entrada.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static string ContentType(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}
