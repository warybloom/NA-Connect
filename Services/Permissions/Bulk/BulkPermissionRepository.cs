using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Permissions.Bulk;

public sealed record UsuarioResumo(int UserId, string Email, string? Nome, bool Ativo = true);

public sealed record EstadoLote(IReadOnlyList<EstadoPar> Pares, IReadOnlyDictionary<int, UsuarioResumo> Usuarios);

/// <summary>Auditoria do lote: executor, operação (BULK_*), referência única da operação e o detalhe completo em JSON.</summary>
public sealed record AuditoriaLote(int? PorUserId, string? PorEmail, string Alvo, string Referencia, object Detalhe);

/// <summary>Leitura/gravação das operações em lote sobre as MESMAS tabelas do sistema de permissões.</summary>
public interface IBulkPermissionRepository
{
    /// <summary>Estado de todos os pares usuários × projetos (vínculo ativo, perfis e exceções do projeto).</summary>
    Task<EstadoLote> CarregarEstadoAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<int> projetoIds, CancellationToken ct);

    /// <summary>
    /// Aplica TODOS os planos numa única transação (tudo ou nada) + auditoria individual (PROFILE_CHANGED/OVERRIDE_CHANGED)
    /// e o registro do lote. Qualquer falha desfaz tudo e lança.
    /// </summary>
    Task AplicarAsync(IReadOnlyList<(PlanoPar Plano, EstadoPar Antes)> planos, AuditoriaLote auditoria, CancellationToken ct);
}

public sealed class SqlBulkPermissionRepository(IConfiguration configuration) : IBulkPermissionRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var c = new SqlConnection(configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("Conexão indisponível."));
        await c.OpenAsync(ct);
        return c;
    }

    private static SqlCommand Cmd(SqlConnection c, string sql, SqlTransaction? tx = null) => new(sql, c, tx) { CommandTimeout = 120 };

    public async Task<EstadoLote> CarregarEstadoAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<int> projetoIds, CancellationToken ct)
    {
        const string sql = """
            DECLARE @u TABLE (UserId INT PRIMARY KEY);
            INSERT INTO @u SELECT DISTINCT CAST(value AS INT) FROM OPENJSON(@users);
            DECLARE @p TABLE (ProjetoId INT PRIMARY KEY);
            INSERT INTO @p SELECT DISTINCT CAST(value AS INT) FROM OPENJSON(@projetos);
            SELECT u.UserId, u.Email, u.DisplayName, u.Ativo FROM perm.Users u JOIN @u x ON x.UserId = u.UserId;
            SELECT up.UserId, up.ProjetoId, up.Ativo FROM perm.UserProjects up
                JOIN @u a ON a.UserId = up.UserId JOIN @p b ON b.ProjetoId = up.ProjetoId;
            SELECT upp.UserId, upp.ProjetoId, upp.ProfileId FROM perm.UserProjectProfiles upp
                JOIN @u a ON a.UserId = upp.UserId JOIN @p b ON b.ProjetoId = upp.ProjetoId WHERE upp.Ativo = 1;
            SELECT o.UserId, o.ProjetoId, o.PermissionKey, o.Effect FROM perm.UserPermissionOverrides o
                JOIN @u a ON a.UserId = o.UserId JOIN @p b ON b.ProjetoId = o.ProjetoId;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@users", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(userIds);
        cmd.Parameters.Add("@projetos", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(projetoIds);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var usuarios = new Dictionary<int, UsuarioResumo>();
        while (await r.ReadAsync(ct)) usuarios[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3));
        await r.NextResultAsync(ct);
        var ativos = new HashSet<(int, int)>();
        var existentes = new HashSet<(int, int)>();
        while (await r.ReadAsync(ct))
        {
            existentes.Add((r.GetInt32(0), r.GetInt32(1)));
            if (r.GetBoolean(2)) ativos.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        await r.NextResultAsync(ct);
        var perfis = new Dictionary<(int, int), HashSet<int>>();
        while (await r.ReadAsync(ct))
        {
            var k = (r.GetInt32(0), r.GetInt32(1));
            if (!perfis.TryGetValue(k, out var s)) perfis[k] = s = new HashSet<int>();
            s.Add(r.GetInt32(2));
        }
        await r.NextResultAsync(ct);
        var overrides = new Dictionary<(int, int), Dictionary<string, string>>();
        while (await r.ReadAsync(ct))
        {
            var k = (r.GetInt32(0), r.GetInt32(1));
            if (!overrides.TryGetValue(k, out var d)) overrides[k] = d = new Dictionary<string, string>(StringComparer.Ordinal);
            d[r.GetString(2)] = r.GetString(3);
        }

        var pares = (from u in userIds.Distinct() from p in projetoIds.Distinct()
                     select new EstadoPar(u, p, ativos.Contains((u, p)),
                         perfis.TryGetValue((u, p), out var ps) ? ps : new HashSet<int>(),
                         overrides.TryGetValue((u, p), out var os) ? os : new Dictionary<string, string>(StringComparer.Ordinal),
                         existentes.Contains((u, p)), !usuarios.TryGetValue(u, out var us) || us.Ativo)).ToList();
        return new EstadoLote(pares, usuarios);
    }

    public async Task AplicarAsync(IReadOnlyList<(PlanoPar Plano, EstadoPar Antes)> planos, AuditoriaLote auditoria, CancellationToken ct)
    {
        // Vínculo interno (cadastro administrativo): reativa a linha existente ou insere; nunca duplica (PK UserId+ProjetoId).
        const string garantirVinculo = """
            UPDATE perm.UserProjects SET Ativo = 1, AtualizadoEm = SYSUTCDATETIME() WHERE UserId = @u AND ProjetoId = @p AND Ativo = 0;
            IF NOT EXISTS (SELECT 1 FROM perm.UserProjects WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @u AND ProjetoId = @p)
                INSERT INTO perm.UserProjects (UserId, ProjetoId, Ativo, AtualizadoEm) VALUES (@u, @p, 1, SYSUTCDATETIME());
            """;
        const string removerPerfis = """
            DELETE FROM perm.UserProjectProfiles
            WHERE UserId = @u AND ProjetoId = @p AND ProfileId IN (SELECT CAST(value AS INT) FROM OPENJSON(@ids));
            """;
        // Idempotente: reativa linha existente (Ativo=0) ou insere; nunca duplica (índice único UserId+ProjetoId+ProfileId).
        const string adicionarPerfis = """
            UPDATE upp SET Ativo = 1 FROM perm.UserProjectProfiles upp
            WHERE upp.UserId = @u AND upp.ProjetoId = @p AND upp.ProfileId IN (SELECT CAST(value AS INT) FROM OPENJSON(@ids)) AND upp.Ativo = 0;
            INSERT INTO perm.UserProjectProfiles (UserId, ProjetoId, ProfileId, Ativo)
            SELECT @u, @p, CAST(j.value AS INT), 1 FROM OPENJSON(@ids) j
            WHERE NOT EXISTS (SELECT 1 FROM perm.UserProjectProfiles x WHERE x.UserId = @u AND x.ProjetoId = @p AND x.ProfileId = CAST(j.value AS INT));
            """;
        const string removerOverrides = """
            DELETE FROM perm.UserPermissionOverrides
            WHERE UserId = @u AND ProjetoId = @p AND PermissionKey IN (SELECT value FROM OPENJSON(@keys));
            """;
        const string gravarOverrides = """
            UPDATE o SET Effect = j.Effect FROM perm.UserPermissionOverrides o
            JOIN OPENJSON(@ovs) WITH (PermissionKey NVARCHAR(120) '$.k', Effect NVARCHAR(5) '$.e') j ON j.PermissionKey = o.PermissionKey
            WHERE o.UserId = @u AND o.ProjetoId = @p AND o.Effect <> j.Effect;
            INSERT INTO perm.UserPermissionOverrides (UserId, ProjetoId, PermissionKey, Effect)
            SELECT @u, @p, j.PermissionKey, j.Effect FROM OPENJSON(@ovs) WITH (PermissionKey NVARCHAR(120) '$.k', Effect NVARCHAR(5) '$.e') j
            WHERE NOT EXISTS (SELECT 1 FROM perm.UserPermissionOverrides x WHERE x.UserId = @u AND x.ProjetoId = @p AND x.PermissionKey = j.PermissionKey);
            """;
        const string auditar = """
            INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
            VALUES (@porId, @porEmail, @u, @p, @alvo, @ref, @antes, @depois);
            """;

        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        try
        {
            async Task Exec(string sql, int u, int p, Action<SqlParameterCollection> extra)
            {
                using var cmd = Cmd(c, sql, tx);
                cmd.Parameters.Add("@u", SqlDbType.Int).Value = u;
                cmd.Parameters.Add("@p", SqlDbType.Int).Value = p;
                extra(cmd.Parameters);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            Task Auditar(int? u, int? p, string alvo, string? antes, string depois) =>
                Exec(auditar, u ?? 0, p ?? 0, ps =>
                {
                    ps["@u"].Value = (object?)u ?? DBNull.Value;
                    ps["@p"].Value = (object?)p ?? DBNull.Value;
                    ps.Add("@porId", SqlDbType.Int).Value = (object?)auditoria.PorUserId ?? DBNull.Value;
                    ps.Add("@porEmail", SqlDbType.NVarChar, 256).Value = (object?)auditoria.PorEmail ?? DBNull.Value;
                    ps.Add("@alvo", SqlDbType.NVarChar, 50).Value = alvo;
                    ps.Add("@ref", SqlDbType.NVarChar, 200).Value = auditoria.Referencia;
                    ps.Add("@antes", SqlDbType.NVarChar, -1).Value = (object?)antes ?? DBNull.Value;
                    ps.Add("@depois", SqlDbType.NVarChar, -1).Value = depois;
                });

            foreach (var (plano, antes) in planos)
            {
                if (plano.Status != BulkStatus.Alterar || !plano.TemMudanca) continue;
                if (plano.Vinculo is not null)
                {
                    await Exec(garantirVinculo, plano.UserId, plano.ProjetoId, _ => { });
                    // Referência ADMIN: a sincronização ACC não desfaz decisão de status tomada pelo administrador.
                    await Auditar(plano.UserId, plano.ProjetoId,
                        plano.Vinculo == BulkVinculo.Reativar ? Sync.PermissionAuditAlvos.ProjectReactivated : Sync.PermissionAuditAlvos.ProjectAdded,
                        JsonSerializer.Serialize(new { ativo = plano.Vinculo == BulkVinculo.Reativar ? false : (bool?)null }),
                        JsonSerializer.Serialize(new { ativo = true, origem = Sync.PermissionAuditAlvos.OrigemAdmin }));
                }
                if (plano.PerfisRemover.Count > 0)
                    await Exec(removerPerfis, plano.UserId, plano.ProjetoId, ps => ps.Add("@ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(plano.PerfisRemover));
                if (plano.PerfisAdicionar.Count > 0)
                    await Exec(adicionarPerfis, plano.UserId, plano.ProjetoId, ps => ps.Add("@ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(plano.PerfisAdicionar));
                if (plano.PerfisAdicionar.Count + plano.PerfisRemover.Count > 0)
                {
                    // Auditoria individual (reconstrói o acesso e impede a sincronização ACC de reatribuir o perfil padrão).
                    var depois = antes.Perfis.Except(plano.PerfisRemover).Union(plano.PerfisAdicionar).OrderBy(x => x).ToList();
                    await Auditar(plano.UserId, plano.ProjetoId, Sync.PermissionAuditAlvos.ProfileChanged,
                        JsonSerializer.Serialize(antes.Perfis.OrderBy(x => x)), JsonSerializer.Serialize(depois));
                }
                if (plano.OverridesRemover.Count > 0)
                    await Exec(removerOverrides, plano.UserId, plano.ProjetoId, ps => ps.Add("@keys", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(plano.OverridesRemover));
                if (plano.OverridesGravar.Count > 0)
                    await Exec(gravarOverrides, plano.UserId, plano.ProjetoId, ps => ps.Add("@ovs", SqlDbType.NVarChar, -1).Value =
                        JsonSerializer.Serialize(plano.OverridesGravar.Select(kv => new { k = kv.Key, e = kv.Value })));
                if (plano.OverridesGravar.Count + plano.OverridesRemover.Count > 0)
                {
                    var depois = new Dictionary<string, string>(antes.Overrides, StringComparer.Ordinal);
                    foreach (var k in plano.OverridesRemover) depois.Remove(k);
                    foreach (var (k, e) in plano.OverridesGravar) depois[k] = e;
                    await Auditar(plano.UserId, plano.ProjetoId, Sync.PermissionAuditAlvos.OverrideChanged,
                        JsonSerializer.Serialize(antes.Overrides.OrderBy(kv => kv.Key)), JsonSerializer.Serialize(depois.OrderBy(kv => kv.Key)));
                }
            }
            await Auditar(null, null, auditoria.Alvo, null, JsonSerializer.Serialize(auditoria.Detalhe, Json));
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
