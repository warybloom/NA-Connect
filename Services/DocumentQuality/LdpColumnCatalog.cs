using System.Text.RegularExpressions;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

/// <summary>Coluna REAL da LDP canônica (dbo.Ldp_Unificada), lida dos metadados do SQL Server.</summary>
public sealed record LdpColumn(string Name, string DataType, int Ordinal, string Label, bool Recommended, bool Available);

/// <summary>
/// CATÁLOGO DINÂMICO de campos auditáveis (01/10/2026): TODAS as colunas reais de dbo.Ldp_Unificada (sys.columns/sys.types) — sem
/// lista cravada no código. Rótulo amigável e "recomendado" são só apresentação; coluna sem rótulo aparece pelo próprio nome.
/// "Available" = a coluna é exposta pela carga da LDP ATUAL (PmoLdpSnapshot.Tabela) e pode ser comparada.
/// </summary>
public interface ILdpColumnCatalog
{
    Task<IReadOnlyList<LdpColumn>> GetColumnsAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlLdpColumnCatalog(IConfiguration configuration) : ILdpColumnCatalog
{
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim Mutex = new(1, 1);
    private static (DateTime Em, IReadOnlyList<LdpColumn> Colunas)? cache;

    public const string Query = """
        SELECT c.name, t.name, c.column_id
        FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.Ldp_Unificada')
        ORDER BY c.column_id;
        """;

    public async Task<IReadOnlyList<LdpColumn>> GetColumnsAsync(CancellationToken cancellationToken = default)
    {
        if (cache is { } c && DateTime.UtcNow - c.Em < Validade) return c.Colunas;
        await Mutex.WaitAsync(cancellationToken);
        try
        {
            if (cache is { } c2 && DateTime.UtcNow - c2.Em < Validade) return c2.Colunas;
            await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
                ?? throw new InvalidOperationException("Conexão indisponível."));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(Query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var lista = new List<(string, string, int)>();
            while (await reader.ReadAsync(cancellationToken)) lista.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
            var colunas = LdpColumnCatalogRules.Build(lista);
            cache = (DateTime.UtcNow, colunas);
            return colunas;
        }
        finally { Mutex.Release(); }
    }
}

public static class LdpColumnCatalogRules
{
    /// <summary>Campos documentais comuns (só destaque visual "RECOMENDADO"; nenhum outro campo é escondido).</summary>
    public static readonly IReadOnlySet<string> Recomendados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        DocumentQualityFields.NaDocumentNumber, DocumentQualityFields.ClientDocumentNumber, DocumentQualityFields.DocumentTitle,
        DocumentQualityFields.Revision, DocumentQualityFields.Discipline, DocumentQualityFields.Format,
    };

    /// <summary>Colunas expostas pela carga da LDP ATUAL (derivada de PmoLdpSnapshot.Tabela — "L.Coluna" e "AS Coluna").</summary>
    public static readonly IReadOnlySet<string> ColunasDaCarga = new HashSet<string>(
        Regex.Matches(PmoLdpSnapshot.Tabela, @"\bL\.([A-Za-z_][A-Za-z0-9_]*)|\bAS\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<LdpColumn> Build(IEnumerable<(string Name, string Type, int Ordinal)> columns) =>
        columns.OrderBy(c => c.Ordinal)
            .Select(c => new LdpColumn(c.Name, c.Type, c.Ordinal, DocumentQualityFields.Label(c.Name), Recomendados.Contains(c.Name), ColunasDaCarga.Contains(c.Name)))
            .ToArray();

    /// <summary>
    /// Nome de campo vindo do frontend/configuração → nome CANÔNICO da coluna real. Só aceita colunas que existem no schema da LDP e
    /// são expostas pela carga; qualquer outra coisa é recusada (nunca chega a uma query).
    /// </summary>
    public static string Canonical(string? field, IReadOnlyList<LdpColumn> catalog)
    {
        var coluna = catalog.FirstOrDefault(c => string.Equals(c.Name, field?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (coluna == null) throw new ArgumentException($"O campo \"{field}\" não existe na LDP (dbo.Ldp_Unificada).");
        if (!coluna.Available) throw new ArgumentException($"O campo \"{coluna.Name}\" existe na LDP mas não é carregado pela LDP atual.");
        return coluna.Name;
    }

    /// <summary>Identificador SQL entre colchetes (escape de "]") — usado SÓ depois de Canonical() validar contra o schema.</summary>
    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}
