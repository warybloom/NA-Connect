using System.Data;
using APS.WebApp.Services.Reports;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Planning;

/// <summary>Documento da LDP atual do projeto, agrupado pela identidade canônica (NA_Document_Number normalizado).</summary>
public sealed record ManagementDocumentCandidate(string DocumentKey, string Codigo, string? Titulo, string? Disciplina,
    string? Status, string? Situacao, bool Atividade, int Linhas, bool Gestao);

public sealed record ManagementDocumentCandidates(IReadOnlyList<ManagementDocumentCandidate> Documentos, int LinhasSemCodigo,
    IReadOnlyList<string> ClassificadosForaDaLdp);

public sealed record ManagementDocumentBatchResult(string Target, int Solicitados, int Alterados, int JaEstavam,
    IReadOnlyList<string> ForaDaLdp, int Invalidos);

public sealed class ManagementDocumentsSchemaMissingException() : Exception(
    "Persistência dos Documentos de Gestão não instalada (migração Migrations/Planning/20261029_ManagementDocuments.sql não aplicada).");

/// <summary>
/// Regras puras da classificação Documentos de Gestão × Engenharia. A identidade é Project_ID_ACC + chave canônica do
/// número N&amp;A (<see cref="PmoExecutiveRules.Chave"/>), estável entre cargas da LDP. Sem heurística de título/disciplina/pessoa.
/// </summary>
public static class ManagementDocumentRules
{
    public const string Gestao = "GESTAO", Engenharia = "ENGENHARIA";
    public const int MaxLote = 5000;

    public static string Key(string? codigo) => PmoExecutiveRules.Chave(codigo);

    public static string? NormalizeTarget(string? target) => (target ?? "").Trim().ToUpperInvariant() switch
    {
        Gestao => Gestao, Engenharia => Engenharia, _ => null,
    };

    /// <summary>Separa o lote em chaves válidas da LDP atual do projeto, chaves fora da LDP e entradas vazias. Duplicatas contam uma vez.</summary>
    public static (IReadOnlyList<string> Aceitas, IReadOnlyList<string> ForaDaLdp, int Invalidos) Validate(
        IEnumerable<string?> solicitadas, IReadOnlySet<string> chavesDaLdp)
    {
        var aceitas = new List<string>(); var fora = new List<string>(); var vistos = new HashSet<string>(StringComparer.Ordinal); var invalidos = 0;
        foreach (var raw in solicitadas)
        {
            var key = Key(raw);
            if (key.Length == 0) { invalidos++; continue; }
            if (!vistos.Add(key)) continue;
            if (chavesDaLdp.Contains(key)) aceitas.Add(key); else fora.Add(key);
        }
        return (aceitas, fora, invalidos);
    }
}

public sealed class ManagementDocumentsStore(IConfiguration config)
{
    private readonly string cs = config.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("LocalConnection ausente.");
    private async Task<SqlConnection> OpenAsync(CancellationToken ct) { var c = new SqlConnection(cs); await c.OpenAsync(ct); return c; }

    private static async Task<bool> SchemaAsync(SqlConnection c, SqlTransaction? tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'planning.ManagementDocument', N'U') IS NULL OR OBJECT_ID(N'planning.ManagementDocumentAudit', N'U') IS NULL THEN 0 ELSE 1 END", c, tx);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    /// <summary>Chaves marcadas como Gestão. Null = persistência ainda não instalada (estado pendente honesto).</summary>
    public async Task<IReadOnlyList<string>?> ReadKeysAsync(string projectId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        if (!await SchemaAsync(c, null, ct)) return null;
        await using var cmd = new SqlCommand("SELECT DocumentKey FROM planning.ManagementDocument WHERE ProjectIdAcc=@p ORDER BY DocumentKey", c);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        var keys = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) keys.Add(r.GetString(0));
        return keys;
    }

    private static async Task<List<ManagementDocumentCandidate>> LdpAsync(SqlConnection c, SqlTransaction? tx, string projectId, CancellationToken ct)
    {
        var activity = LdpItemRules.SqlIsActivity("LdpAtual");
        var sql = $"""
            SELECT UPPER(LTRIM(RTRIM(LdpAtual.NA_Document_Number))) K, MAX(LTRIM(RTRIM(LdpAtual.NA_Document_Number))) Codigo,
                   MAX(NULLIF(LTRIM(RTRIM(LdpAtual.Document_Title)),'')) Titulo, MAX(NULLIF(LTRIM(RTRIM(LdpAtual.Discipline)),'')) Disciplina,
                   MAX(NULLIF(LTRIM(RTRIM(LdpAtual.Document_Status)),'')) Status, MAX(NULLIF(LTRIM(RTRIM(LdpAtual.Situation)),'')) Situacao,
                   MAX(CASE WHEN {activity} THEN 1 ELSE 0 END) Atividade, COUNT(1) Linhas
            FROM {PmoLdpSnapshot.Tabela}
            WHERE NULLIF(LTRIM(RTRIM(LdpAtual.NA_Document_Number)),'') IS NOT NULL
            GROUP BY UPPER(LTRIM(RTRIM(LdpAtual.NA_Document_Number)))
            ORDER BY K {PmoLdpSnapshot.OpcaoPlano}
            """;
        await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 120 };
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        var list = new List<ManagementDocumentCandidate>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetString(0), r.GetString(1), T(2), T(3), T(4), T(5), r.GetInt32(6) == 1, r.GetInt32(7), false));
        return list;
    }

    public async Task<(bool SchemaAvailable, ManagementDocumentCandidates Candidates)> CandidatesAsync(string projectId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var docs = await LdpAsync(c, null, projectId, ct);
        int semCodigo;
        await using (var cmd = new SqlCommand($"SELECT COUNT(1) FROM {PmoLdpSnapshot.Tabela} WHERE NULLIF(LTRIM(RTRIM(LdpAtual.NA_Document_Number)),'') IS NULL {PmoLdpSnapshot.OpcaoPlano}", c) { CommandTimeout = 120 })
        {
            cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            semCodigo = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
        var available = await SchemaAsync(c, null, ct);
        var marked = new HashSet<string>(StringComparer.Ordinal);
        if (available)
        {
            await using var cmd = new SqlCommand("SELECT DocumentKey FROM planning.ManagementDocument WHERE ProjectIdAcc=@p", c);
            cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) marked.Add(r.GetString(0));
        }
        var ldpKeys = docs.Select(d => d.DocumentKey).ToHashSet(StringComparer.Ordinal);
        return (available, new(docs.Select(d => d with { Gestao = marked.Contains(d.DocumentKey) }).ToList(), semCodigo,
            marked.Where(k => !ldpKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList()));
    }

    /// <summary>
    /// Aplica o lote numa transação. Só aceita chaves presentes na LDP atual DESTE projeto (nunca mistura projetos).
    /// GESTAO insere o que ainda não está marcado; ENGENHARIA remove a marcação. Cada mudança efetiva vai para a auditoria.
    /// </summary>
    public async Task<ManagementDocumentBatchResult> ApplyAsync(string projectId, IReadOnlyCollection<string?> documentKeys, string target,
        string? changedBy, CancellationToken ct)
    {
        if (documentKeys.Count == 0) throw new ArgumentException("Selecione ao menos um documento.");
        if (documentKeys.Count > ManagementDocumentRules.MaxLote) throw new ArgumentException($"Lote acima do limite de {ManagementDocumentRules.MaxLote} documentos.");
        await using var c = await OpenAsync(ct);
        if (!await SchemaAsync(c, null, ct)) throw new ManagementDocumentsSchemaMissingException();
        var ldp = await LdpAsync(c, null, projectId, ct);
        var originals = ldp.ToDictionary(d => d.DocumentKey, d => d.Codigo, StringComparer.Ordinal);
        var (aceitas, fora, invalidos) = ManagementDocumentRules.Validate(documentKeys, originals.Keys.ToHashSet(StringComparer.Ordinal));
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var alterados = 0;
            foreach (var key in aceitas)
            {
                var sql = target == ManagementDocumentRules.Gestao
                    ? """
                      IF NOT EXISTS (SELECT 1 FROM planning.ManagementDocument WITH (UPDLOCK, HOLDLOCK) WHERE ProjectIdAcc=@p AND DocumentKey=@k)
                      BEGIN
                        INSERT INTO planning.ManagementDocument(ProjectIdAcc, DocumentKey, DocumentCodeOriginal, ClassifiedBy) VALUES(@p, @k, @o, @u);
                        INSERT INTO planning.ManagementDocumentAudit(ProjectIdAcc, DocumentKey, Target, ChangedBy) VALUES(@p, @k, 'GESTAO', @u);
                        SELECT 1;
                      END ELSE SELECT 0;
                      """
                    : """
                      DELETE FROM planning.ManagementDocument WHERE ProjectIdAcc=@p AND DocumentKey=@k;
                      IF @@ROWCOUNT > 0
                      BEGIN
                        INSERT INTO planning.ManagementDocumentAudit(ProjectIdAcc, DocumentKey, Target, ChangedBy) VALUES(@p, @k, 'ENGENHARIA', @u);
                        SELECT 1;
                      END ELSE SELECT 0;
                      """;
                await using var cmd = new SqlCommand(sql, c, tx);
                cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
                cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = key;
                cmd.Parameters.Add("@o", SqlDbType.NVarChar, 200).Value = originals[key];
                cmd.Parameters.Add("@u", SqlDbType.NVarChar, 256).Value = (object?)changedBy ?? DBNull.Value;
                alterados += Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            await tx.CommitAsync(ct);
            return new(target, documentKeys.Count, alterados, aceitas.Count - alterados, fora, invalidos);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }
}
