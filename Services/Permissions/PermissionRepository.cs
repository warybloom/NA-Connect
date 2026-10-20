using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Permissions;

public sealed record PermUser(int UserId, string? AccUserId, string Email, string? DisplayName, bool Ativo, DateTime? UltimoAcessoEm);
public sealed record PermProfile(int ProfileId, string Name, string? Description, bool Ativo, bool IsSystem, bool IsMasterAdmin, IReadOnlyList<string> Permissions);
public sealed record UserProjectView(PermUser User, bool AtivoNoProjeto, IReadOnlyList<int> ProfileIds, IReadOnlyList<PermissionOverride> Overrides);
public sealed record PermittedProject(int ProjetoId, string ProjectIdAcc, string NomeProjeto, byte? TipoLdp, bool IsAtivo);
public sealed record PermissionAuditEntry(long Id, DateTime DataHora, string? AlteradoPorEmail, int? UsuarioAfetadoId, string? UsuarioAfetadoEmail,
    int? ProjetoId, string Alvo, string? Referencia, string? ValorAnterior, string? ValorNovo);

public interface IPermissionRepository
{
    Task<bool> SchemaInstaladoAsync(CancellationToken ct);
    Task<int?> ResolverProjetoIdAsync(string projectIdAcc, CancellationToken ct);
    Task<bool> UsuarioPossuiAcessoAtivoProjetoAsync(int userId, string projectIdAcc, CancellationToken ct);
    Task<int> RegistrarAcessoAsync(string? accUserId, string email, string? nome, CancellationToken ct);
    Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct);
    Task<IReadOnlyList<PermittedProject>> ListarProjetosPermitidosAsync(int userId, CancellationToken ct);
    Task<UserPermissionData> CarregarDadosAsync(int userId, bool masterPorConfiguracao, CancellationToken ct);

    Task<IReadOnlyList<PermProfile>> ListarPerfisAsync(CancellationToken ct);
    Task<int> CriarPerfilAsync(string nome, string? descricao, IEnumerable<string> permissoes, CancellationToken ct);
    Task AtualizarPerfilAsync(int profileId, string nome, string? descricao, bool ativo, CancellationToken ct);
    Task ExcluirPerfilAsync(int profileId, CancellationToken ct);
    Task DefinirPermissoesPerfilAsync(int profileId, IEnumerable<string> permissoes, CancellationToken ct);

    Task<IReadOnlyList<PermUser>> ListarUsuariosAsync(CancellationToken ct);
    Task<int> CadastrarUsuarioAsync(string email, string? nome, CancellationToken ct);
    Task<UserProjectView> ObterUsuarioNoProjetoAsync(int userId, int? projetoId, CancellationToken ct);
    Task DefinirPerfisUsuarioAsync(int userId, int? projetoId, IEnumerable<int> profileIds, CancellationToken ct);
    Task DefinirOverridesUsuarioAsync(int userId, int? projetoId, IEnumerable<PermissionOverride> overrides, CancellationToken ct);
    Task DefinirAtivoNoProjetoAsync(int userId, int projetoId, bool ativo, CancellationToken ct);

    Task AuditarAsync(int? alteradoPor, string? alteradoPorEmail, int? usuarioAfetado, int? projetoId, string alvo, string? referencia,
        string? anterior, string? novo, CancellationToken ct);
    Task<IReadOnlyList<PermissionAuditEntry>> LerAuditoriaAsync(int top, CancellationToken ct);
}

/// <summary>
/// Acesso SQL das permissões (schema perm — ver Migrations/Permissions). Conexão via IConfiguration
/// ("LocalConnection"), nunca connection string fixa. Todo SQL parametrizado.
/// </summary>
public sealed class SqlPermissionRepository(IConfiguration configuration) : IPermissionRepository
{
    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var c = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await c.OpenAsync(ct);
        return c;
    }

    private static SqlCommand Cmd(SqlConnection c, string sql, SqlTransaction? tx = null) => new(sql, c, tx) { CommandTimeout = 30 };

    public async Task<bool> SchemaInstaladoAsync(CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT CASE WHEN OBJECT_ID(N'perm.PermissionProfiles', N'U') IS NOT NULL AND OBJECT_ID(N'perm.UserPermissionOverrides', N'U') IS NOT NULL THEN 1 ELSE 0 END;");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<int?> ResolverProjetoIdAsync(string projectIdAcc, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT TOP 1 ProjetoID FROM dbo.Projetos WHERE Project_ID_ACC = @acc;");
        // dbo.Projetos guarda o id ACC com prefixo "b." (padrão de toda a base); aceita também sem prefixo.
        cmd.Parameters.Add("@acc", SqlDbType.NVarChar, 255).Value = projectIdAcc.StartsWith("b.", StringComparison.Ordinal) ? projectIdAcc : "b." + projectIdAcc;
        return await cmd.ExecuteScalarAsync(ct) is int id ? id : null;
    }

    public async Task<bool> UsuarioPossuiAcessoAtivoProjetoAsync(int userId, string projectIdAcc, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.Projetos p
                JOIN perm.UserProjects up ON up.ProjetoId = p.ProjetoID AND up.Ativo = 1
                JOIN perm.Users u ON u.UserId = up.UserId AND u.Ativo = 1
                WHERE up.UserId = @userId AND p.Project_ID_ACC = @projectIdAcc AND p.Is_Ativo = 1
            ) THEN 1 ELSE 0 END;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@userId", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@projectIdAcc", SqlDbType.NVarChar, 255).Value =
            projectIdAcc.StartsWith("b.", StringComparison.Ordinal) ? projectIdAcc : "b." + projectIdAcc;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<int> RegistrarAcessoAsync(string? accUserId, string email, string? nome, CancellationToken ct)
    {
        // Identidade = usuário Autodesk: mesmo UPSERT da sincronização ACC (AutodeskUserId, senão e-mail case-insensitive;
        // nunca duplica; audita USER_CREATED). Aqui é o próprio usuário entrando: registra o último acesso.
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, Sync.SqlPermissionSyncRepository.UpsertUsuarioSql);
        Sync.SqlPermissionSyncRepository.ParametrosUpsert(cmd, accUserId, email, nome, acesso: true, porId: null, porEmail: email, origem: "LOGIN");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    public async Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT UserId, AccUserId, Email, DisplayName, Ativo, UltimoAcessoEm FROM perm.Users WHERE UserId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerUsuario(r) : null;
    }

    public async Task<IReadOnlyList<PermittedProject>> ListarProjetosPermitidosAsync(int userId, CancellationToken ct)
    {
        const string sql = """
            SELECT p.ProjetoID, p.Project_ID_ACC, COALESCE(p.Nome_Projeto, N''), p.Tipo_LDP, p.Is_Ativo
            FROM perm.UserProjects up
            JOIN perm.Users u ON u.UserId = up.UserId AND u.Ativo = 1
            JOIN dbo.Projetos p ON p.ProjetoID = up.ProjetoId AND p.Is_Ativo = 1
            WHERE up.UserId = @userId AND up.Ativo = 1
              AND p.Project_ID_ACC IS NOT NULL AND p.Project_ID_ACC <> N''
            ORDER BY COALESCE(p.Nome_Projeto, N''), p.ProjetoID;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@userId", SqlDbType.Int).Value = userId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<PermittedProject>();
        while (await r.ReadAsync(ct))
        {
            lista.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetByte(3), r.GetBoolean(4)));
        }
        return lista;
    }

    private static PermUser LerUsuario(SqlDataReader r) => new(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.IsDBNull(5) ? null : r.GetDateTime(5));

    public async Task<UserPermissionData> CarregarDadosAsync(int userId, bool masterPorConfiguracao, CancellationToken ct)
    {
        // Uma ida ao banco: perfis ativos atribuídos (com permissões), overrides e projetos em que o usuário está inativo.
        const string sql = """
            SELECT upp.ProfileId, p.Name, upp.ProjetoId, p.IsMasterAdmin, pp.PermissionKey
            FROM perm.UserProjectProfiles upp
            JOIN perm.PermissionProfiles p ON p.ProfileId = upp.ProfileId AND p.Ativo = 1
            JOIN perm.Users u ON u.UserId = upp.UserId AND u.Ativo = 1
            LEFT JOIN perm.ProfilePermissions pp ON pp.ProfileId = p.ProfileId
            WHERE upp.UserId = @id AND upp.Ativo = 1;
            SELECT o.ProjetoId, o.PermissionKey, o.Effect FROM perm.UserPermissionOverrides o
            JOIN perm.Users u ON u.UserId = o.UserId AND u.Ativo = 1 WHERE o.UserId = @id;
            SELECT ProjetoId, Ativo FROM perm.UserProjects WHERE UserId = @id;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var perfis = new Dictionary<(int, int?), (string Nome, bool Master, HashSet<string> Keys)>();
        while (await r.ReadAsync(ct))
        {
            var chave = (r.GetInt32(0), r.IsDBNull(2) ? (int?)null : r.GetInt32(2));
            if (!perfis.TryGetValue(chave, out var p)) perfis[chave] = p = (r.GetString(1), r.GetBoolean(3), new HashSet<string>(StringComparer.Ordinal));
            if (!r.IsDBNull(4)) p.Keys.Add(r.GetString(4));
        }
        await r.NextResultAsync(ct);
        var overrides = new List<PermissionOverride>();
        while (await r.ReadAsync(ct)) overrides.Add(new(r.IsDBNull(0) ? null : r.GetInt32(0), r.GetString(1), r.GetString(2)));
        await r.NextResultAsync(ct);
        var inativos = new HashSet<int>();
        var ativos = new HashSet<int>();
        while (await r.ReadAsync(ct))
        {
            int projeto = r.GetInt32(0);
            if (r.GetBoolean(1)) ativos.Add(projeto);
            else inativos.Add(projeto);
        }
        return new(masterPorConfiguracao,
            perfis.Select(kv => new ProfileAssignment(kv.Key.Item1, kv.Value.Nome, kv.Key.Item2, kv.Value.Master, kv.Value.Keys)).ToList(),
            overrides, inativos, ativos);
    }

    public async Task<IReadOnlyList<PermProfile>> ListarPerfisAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT p.ProfileId, p.Name, p.Description, p.Ativo, p.IsSystem, p.IsMasterAdmin, pp.PermissionKey
            FROM perm.PermissionProfiles p LEFT JOIN perm.ProfilePermissions pp ON pp.ProfileId = p.ProfileId
            ORDER BY p.IsSystem DESC, p.Name;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var perfis = new Dictionary<int, (PermProfile P, List<string> K)>();
        var ordem = new List<int>();
        while (await r.ReadAsync(ct))
        {
            int id = r.GetInt32(0);
            if (!perfis.ContainsKey(id))
            {
                var k = new List<string>();
                perfis[id] = (new PermProfile(id, r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3), r.GetBoolean(4), r.GetBoolean(5), k), k);
                ordem.Add(id);
            }
            if (!r.IsDBNull(6)) perfis[id].K.Add(r.GetString(6));
        }
        return ordem.Select(id => perfis[id].P).ToList();
    }

    public async Task<int> CriarPerfilAsync(string nome, string? descricao, IEnumerable<string> permissoes, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        using var cmd = Cmd(c, "INSERT INTO perm.PermissionProfiles (Name, Description, Ativo, IsSystem, IsMasterAdmin) VALUES (@n, @d, 1, 0, 0); SELECT CAST(SCOPE_IDENTITY() AS INT);", tx);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = nome;
        cmd.Parameters.Add("@d", SqlDbType.NVarChar, 500).Value = (object?)descricao ?? DBNull.Value;
        int id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        await GravarPermissoesPerfilAsync(c, tx, id, permissoes, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task AtualizarPerfilAsync(int profileId, string nome, string? descricao, bool ativo, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "UPDATE perm.PermissionProfiles SET Name = @n, Description = @d, Ativo = @a, AtualizadoEm = SYSUTCDATETIME() WHERE ProfileId = @id AND IsSystem = 0;");
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = profileId;
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = nome;
        cmd.Parameters.Add("@d", SqlDbType.NVarChar, 500).Value = (object?)descricao ?? DBNull.Value;
        cmd.Parameters.Add("@a", SqlDbType.Bit).Value = ativo;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExcluirPerfilAsync(int profileId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        using (var delAssignments = Cmd(c, """
            DELETE upp
            FROM perm.UserProjectProfiles upp
            INNER JOIN perm.PermissionProfiles p ON p.ProfileId = upp.ProfileId
            WHERE upp.ProfileId = @id AND p.IsSystem = 0;
            """, tx))
        {
            delAssignments.Parameters.Add("@id", SqlDbType.Int).Value = profileId;
            await delAssignments.ExecuteNonQueryAsync(ct);
        }
        using (var delPermissions = Cmd(c, """
            DELETE pp
            FROM perm.ProfilePermissions pp
            INNER JOIN perm.PermissionProfiles p ON p.ProfileId = pp.ProfileId
            WHERE pp.ProfileId = @id AND p.IsSystem = 0;
            """, tx))
        {
            delPermissions.Parameters.Add("@id", SqlDbType.Int).Value = profileId;
            await delPermissions.ExecuteNonQueryAsync(ct);
        }
        using (var delProfile = Cmd(c, "DELETE FROM perm.PermissionProfiles WHERE ProfileId = @id AND IsSystem = 0;", tx))
        {
            delProfile.Parameters.Add("@id", SqlDbType.Int).Value = profileId;
            if (await delProfile.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("Perfil inexistente ou protegido.");
        }
        await tx.CommitAsync(ct);
    }
    public async Task DefinirPermissoesPerfilAsync(int profileId, IEnumerable<string> permissoes, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        await GravarPermissoesPerfilAsync(c, tx, profileId, permissoes, ct);
        await tx.CommitAsync(ct);
    }

    private static async Task GravarPermissoesPerfilAsync(SqlConnection c, SqlTransaction tx, int profileId, IEnumerable<string> permissoes, CancellationToken ct)
    {
        using (var del = Cmd(c, "DELETE FROM perm.ProfilePermissions WHERE ProfileId = @id;", tx))
        {
            del.Parameters.Add("@id", SqlDbType.Int).Value = profileId;
            await del.ExecuteNonQueryAsync(ct);
        }
        foreach (var key in permissoes.Distinct(StringComparer.Ordinal))
        {
            using var ins = Cmd(c, "INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey) VALUES (@id, @k);", tx);
            ins.Parameters.Add("@id", SqlDbType.Int).Value = profileId;
            ins.Parameters.Add("@k", SqlDbType.NVarChar, 150).Value = key;
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyList<PermUser>> ListarUsuariosAsync(CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT UserId, AccUserId, Email, DisplayName, Ativo, UltimoAcessoEm FROM perm.Users ORDER BY COALESCE(DisplayName, Email);");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<PermUser>();
        while (await r.ReadAsync(ct)) lista.Add(LerUsuario(r));
        return lista;
    }

    public async Task<int> CadastrarUsuarioAsync(string email, string? nome, CancellationToken ct) =>
        await RegistrarAcessoSemLoginAsync(email, nome, ct);

    private async Task<int> RegistrarAcessoSemLoginAsync(string email, string? nome, CancellationToken ct)
    {
        const string sql = """
            DECLARE @id INT = (SELECT TOP 1 UserId FROM perm.Users WHERE Email = @email);
            IF @id IS NULL BEGIN INSERT INTO perm.Users (Email, DisplayName, Ativo) VALUES (@email, @nome, 1); SET @id = SCOPE_IDENTITY(); END
            SELECT @id;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;
        cmd.Parameters.Add("@nome", SqlDbType.NVarChar, 200).Value = (object?)nome ?? DBNull.Value;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<UserProjectView> ObterUsuarioNoProjetoAsync(int userId, int? projetoId, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAsync(userId, ct) ?? throw new KeyNotFoundException("Usuário inexistente.");
        const string sql = """
            SELECT ProfileId FROM perm.UserProjectProfiles WHERE UserId = @u AND Ativo = 1
              AND ((@p IS NULL AND ProjetoId IS NULL) OR ProjetoId = @p);
            SELECT ProjetoId, PermissionKey, Effect FROM perm.UserPermissionOverrides WHERE UserId = @u
              AND ((@p IS NULL AND ProjetoId IS NULL) OR ProjetoId = @p);
            SELECT Ativo FROM perm.UserProjects WHERE UserId = @u AND ProjetoId = @p;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = (object?)projetoId ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var perfis = new List<int>();
        while (await r.ReadAsync(ct)) perfis.Add(r.GetInt32(0));
        await r.NextResultAsync(ct);
        var ov = new List<PermissionOverride>();
        while (await r.ReadAsync(ct)) ov.Add(new(r.IsDBNull(0) ? null : r.GetInt32(0), r.GetString(1), r.GetString(2)));
        await r.NextResultAsync(ct);
        // Para escopo de projeto, ausência de UserProjects não equivale a vínculo ativo.
        bool ativo = projetoId is null || (await r.ReadAsync(ct) && r.GetBoolean(0));
        return new(usuario, ativo, perfis, ov);
    }

    public async Task DefinirPerfisUsuarioAsync(int userId, int? projetoId, IEnumerable<int> profileIds, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        using (var del = Cmd(c, "DELETE FROM perm.UserProjectProfiles WHERE UserId = @u AND ((@p IS NULL AND ProjetoId IS NULL) OR ProjetoId = @p);", tx))
        {
            del.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            del.Parameters.Add("@p", SqlDbType.Int).Value = (object?)projetoId ?? DBNull.Value;
            await del.ExecuteNonQueryAsync(ct);
        }
        foreach (var pid in profileIds.Distinct())
        {
            using var ins = Cmd(c, "INSERT INTO perm.UserProjectProfiles (UserId, ProjetoId, ProfileId, Ativo) VALUES (@u, @p, @pr, 1);", tx);
            ins.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            ins.Parameters.Add("@p", SqlDbType.Int).Value = (object?)projetoId ?? DBNull.Value;
            ins.Parameters.Add("@pr", SqlDbType.Int).Value = pid;
            await ins.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task DefinirOverridesUsuarioAsync(int userId, int? projetoId, IEnumerable<PermissionOverride> overrides, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        using (var del = Cmd(c, "DELETE FROM perm.UserPermissionOverrides WHERE UserId = @u AND ((@p IS NULL AND ProjetoId IS NULL) OR ProjetoId = @p);", tx))
        {
            del.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            del.Parameters.Add("@p", SqlDbType.Int).Value = (object?)projetoId ?? DBNull.Value;
            await del.ExecuteNonQueryAsync(ct);
        }
        foreach (var o in overrides.GroupBy(o => o.PermissionKey).Select(g => g.Last()))
        {
            using var ins = Cmd(c, "INSERT INTO perm.UserPermissionOverrides (UserId, ProjetoId, PermissionKey, Effect) VALUES (@u, @p, @k, @e);", tx);
            ins.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            ins.Parameters.Add("@p", SqlDbType.Int).Value = (object?)projetoId ?? DBNull.Value;
            ins.Parameters.Add("@k", SqlDbType.NVarChar, 150).Value = o.PermissionKey;
            ins.Parameters.Add("@e", SqlDbType.NVarChar, 5).Value = o.Effect;
            await ins.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task DefinirAtivoNoProjetoAsync(int userId, int projetoId, bool ativo, CancellationToken ct)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM perm.UserProjects WHERE UserId = @u AND ProjetoId = @p)
                UPDATE perm.UserProjects SET Ativo = @a WHERE UserId = @u AND ProjetoId = @p;
            ELSE INSERT INTO perm.UserProjects (UserId, ProjetoId, Ativo) VALUES (@u, @p, @a);
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = projetoId;
        cmd.Parameters.Add("@a", SqlDbType.Bit).Value = ativo;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AuditarAsync(int? alteradoPor, string? alteradoPorEmail, int? usuarioAfetado, int? projetoId, string alvo, string? referencia,
        string? anterior, string? novo, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
            VALUES (@por, @email, @afetado, @p, @alvo, @ref, @ant, @novo);
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@por", SqlDbType.Int).Value = (object?)alteradoPor ?? DBNull.Value;
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = (object?)alteradoPorEmail ?? DBNull.Value;
        cmd.Parameters.Add("@afetado", SqlDbType.Int).Value = (object?)usuarioAfetado ?? DBNull.Value;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = (object?)projetoId ?? DBNull.Value;
        cmd.Parameters.Add("@alvo", SqlDbType.NVarChar, 40).Value = alvo;
        cmd.Parameters.Add("@ref", SqlDbType.NVarChar, 200).Value = (object?)referencia ?? DBNull.Value;
        cmd.Parameters.Add("@ant", SqlDbType.NVarChar, -1).Value = (object?)anterior ?? DBNull.Value;
        cmd.Parameters.Add("@novo", SqlDbType.NVarChar, -1).Value = (object?)novo ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PermissionAuditEntry>> LerAuditoriaAsync(int top, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (@top) a.Id, a.DataHora, a.AlteradoPorEmail, a.UsuarioAfetadoId, u.Email, a.ProjetoId, a.Alvo, a.Referencia, a.ValorAnterior, a.ValorNovo
            FROM perm.PermissionAuditLog a LEFT JOIN perm.Users u ON u.UserId = a.UsuarioAfetadoId ORDER BY a.Id DESC;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@top", SqlDbType.Int).Value = top;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<PermissionAuditEntry>();
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (await r.ReadAsync(ct))
            lista.Add(new(r.GetInt64(0), r.GetDateTime(1), S(2), r.IsDBNull(3) ? null : r.GetInt32(3), S(4), r.IsDBNull(5) ? null : r.GetInt32(5), r.GetString(6), S(7), S(8), S(9)));
        return lista;
    }
}
