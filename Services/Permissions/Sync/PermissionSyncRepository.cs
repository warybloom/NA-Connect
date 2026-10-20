using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Permissions.Sync;

public sealed record ProjetoNa(int ProjetoId, string ProjectIdAcc, string Nome, bool Ativo);
public sealed record UsuarioUpsert(int UserId, bool Criado);
public sealed record MembroProjetoView(int UserId, string Email, string? Nome, string? AutodeskUserId, bool? AtivoNoProjeto,
    DateTime? UltimoAcessoEm, IReadOnlyList<string> PerfisDoProjeto, IReadOnlyList<string> PerfisGlobais, int Excecoes);

/// <summary>Persistência da sincronização ACC → perm.Users / perm.UserProjects / perfil padrão. Tudo auditado.</summary>
public interface IPermissionSyncRepository
{
    Task<IReadOnlyList<ProjetoNa>> ProjetosNaAsync(CancellationToken ct);
    /// <summary>UPSERT por AutodeskUserId, senão e-mail (case-insensitive). Nunca duplica. Audita USER_CREATED.</summary>
    Task<UsuarioUpsert> UpsertUsuarioAsync(string? autodeskUserId, string email, string? nome, bool registrarAcesso,
        int? porUserId, string? porEmail, string origem, CancellationToken ct);
    /// <summary>Perfil padrão por projeto (ativo e nunca MasterAdmin). null = não existe → nenhum perfil é atribuído.</summary>
    Task<int?> PerfilPadraoIdAsync(string nome, CancellationToken ct);
    /// <summary>Estado dos pares informados + todos os vínculos existentes do usuário OU do projeto indicados.</summary>
    Task<IReadOnlyList<EstadoVinculo>> EstadosAsync(IReadOnlyCollection<(int UserId, int ProjetoId)> pares, int? vinculosDoUsuario,
        int? vinculosDoProjeto, CancellationToken ct);
    Task AplicarAsync(IReadOnlyList<OperacaoPlanejada> ops, int? perfilPadraoId, int? porUserId, string? porEmail, string origem, CancellationToken ct);
    Task RegistrarResumoAsync(int projetoId, int? porUserId, string? porEmail, ResumoSincronizacao resumo, CancellationToken ct);
    Task<ResumoSincronizacao?> UltimoResumoAsync(int projetoId, CancellationToken ct);
    Task<IReadOnlyList<MembroProjetoView>> MembrosAsync(int projetoId, CancellationToken ct);
}

public static class PermissionAuditAlvos
{
    public const string UserCreated = "USER_CREATED", ProjectAdded = "USER_PROJECT_ADDED", ProjectReactivated = "USER_PROJECT_REACTIVATED",
        ProjectDeactivated = "USER_PROJECT_DEACTIVATED", DefaultProfile = "DEFAULT_PROFILE_ASSIGNED", ProfileChanged = "PROFILE_CHANGED",
        OverrideChanged = "OVERRIDE_CHANGED", ProjectSync = "ACC_PROJECT_SYNC";
    /// <summary>Referência dos eventos de status feitos manualmente pelo administrador (a sincronização não os desfaz).</summary>
    public const string OrigemAdmin = "ADMIN";
}

public sealed class SqlPermissionSyncRepository(IConfiguration configuration) : IPermissionSyncRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var c = new SqlConnection(configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("Conexão indisponível."));
        await c.OpenAsync(ct);
        return c;
    }

    private static SqlCommand Cmd(SqlConnection c, string sql, SqlTransaction? tx = null) => new(sql, c, tx) { CommandTimeout = 60 };

    public async Task<IReadOnlyList<ProjetoNa>> ProjetosNaAsync(CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT ProjetoID, Project_ID_ACC, ISNULL(Nome_Projeto, N''), ISNULL(Is_Ativo, 0) FROM dbo.Projetos WHERE Project_ID_ACC IS NOT NULL AND Project_ID_ACC <> N'';");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<ProjetoNa>();
        while (await r.ReadAsync(ct)) lista.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3)));
        return lista;
    }

    /// <summary>SQL do UPSERT de usuário — compartilhado com SqlPermissionRepository.RegistrarAcessoAsync (uma única regra).</summary>
    internal const string UpsertUsuarioSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @id INT = (SELECT TOP 1 UserId FROM perm.Users WITH (UPDLOCK, HOLDLOCK) WHERE @acc IS NOT NULL AND AccUserId = @acc);
        -- Coluna Email usa collation case-insensitive: "Fulano@NA.com" e "fulano@na.com" são o mesmo usuário.
        IF @id IS NULL SET @id = (SELECT TOP 1 UserId FROM perm.Users WITH (UPDLOCK, HOLDLOCK) WHERE Email = @email);
        DECLARE @criado BIT = 0;
        IF @id IS NULL
        BEGIN
            INSERT INTO perm.Users (AccUserId, Email, DisplayName, Ativo, UltimoAcessoEm)
            VALUES (@acc, @email, NULLIF(@nome, N''), 1, CASE WHEN @acesso = 1 THEN SYSUTCDATETIME() END);
            SET @id = SCOPE_IDENTITY();
            SET @criado = 1;
            INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
            VALUES (@porId, @porEmail, @id, NULL, N'USER_CREATED', @origem, NULL,
                    (SELECT @email AS email, @acc AS autodeskUserId, NULLIF(@nome, N'') AS nome FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
        END
        ELSE
            UPDATE perm.Users SET
                AccUserId = COALESCE(AccUserId, @acc),
                Email = CASE WHEN NOT EXISTS (SELECT 1 FROM perm.Users o WHERE o.Email = @email AND o.UserId <> @id) THEN @email ELSE Email END,
                DisplayName = COALESCE(NULLIF(@nome, N''), DisplayName),
                UltimoAcessoEm = CASE WHEN @acesso = 1 THEN SYSUTCDATETIME() ELSE UltimoAcessoEm END
            WHERE UserId = @id;
        COMMIT TRANSACTION;
        SELECT @id, @criado;
        """;

    internal static void ParametrosUpsert(SqlCommand cmd, string? acc, string email, string? nome, bool acesso, int? porId, string? porEmail, string origem)
    {
        cmd.Parameters.Add("@acc", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(acc) ? DBNull.Value : acc.Trim();
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email.Trim();
        cmd.Parameters.Add("@nome", SqlDbType.NVarChar, 200).Value = (object?)nome?.Trim() ?? DBNull.Value;
        cmd.Parameters.Add("@acesso", SqlDbType.Bit).Value = acesso;
        cmd.Parameters.Add("@porId", SqlDbType.Int).Value = (object?)porId ?? DBNull.Value;
        cmd.Parameters.Add("@porEmail", SqlDbType.NVarChar, 256).Value = (object?)porEmail ?? DBNull.Value;
        cmd.Parameters.Add("@origem", SqlDbType.NVarChar, 200).Value = origem;
    }

    public async Task<UsuarioUpsert> UpsertUsuarioAsync(string? autodeskUserId, string email, string? nome, bool registrarAcesso,
        int? porUserId, string? porEmail, string origem, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, UpsertUsuarioSql);
        ParametrosUpsert(cmd, autodeskUserId, email, nome, registrarAcesso, porUserId, porEmail, origem);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new UsuarioUpsert(r.GetInt32(0), r.GetBoolean(1));
    }

    public async Task<int?> PerfilPadraoIdAsync(string nome, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT TOP 1 ProfileId FROM perm.PermissionProfiles WHERE Name = @n AND Ativo = 1 AND IsMasterAdmin = 0;");
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = nome;
        return await cmd.ExecuteScalarAsync(ct) is int id ? id : null;
    }

    public async Task<IReadOnlyList<EstadoVinculo>> EstadosAsync(IReadOnlyCollection<(int UserId, int ProjetoId)> pares, int? vinculosDoUsuario,
        int? vinculosDoProjeto, CancellationToken ct)
    {
        const string sql = """
            DECLARE @pares TABLE (UserId INT NOT NULL, ProjetoId INT NOT NULL, PRIMARY KEY (UserId, ProjetoId));
            INSERT INTO @pares (UserId, ProjetoId)
                SELECT DISTINCT j.u, j.p FROM OPENJSON(@json) WITH (u INT '$.u', p INT '$.p') j;
            INSERT INTO @pares (UserId, ProjetoId)
                SELECT up.UserId, up.ProjetoId FROM perm.UserProjects up
                WHERE ((@usr IS NOT NULL AND up.UserId = @usr) OR (@proj IS NOT NULL AND up.ProjetoId = @proj))
                  AND NOT EXISTS (SELECT 1 FROM @pares x WHERE x.UserId = up.UserId AND x.ProjetoId = up.ProjetoId);
            SELECT x.UserId, x.ProjetoId, up.Ativo,
                CASE WHEN EXISTS (SELECT 1 FROM perm.UserProjectProfiles upp WHERE upp.UserId = x.UserId AND upp.ProjetoId = x.ProjetoId) THEN 1 ELSE 0 END,
                CASE WHEN EXISTS (SELECT 1 FROM perm.PermissionAuditLog a WHERE a.UsuarioAfetadoId = x.UserId AND a.ProjetoId = x.ProjetoId
                                  AND a.Alvo IN (N'PROFILE_CHANGED', N'DEFAULT_PROFILE_ASSIGNED', N'USER_PROFILE')) THEN 1 ELSE 0 END,
                CASE WHEN (SELECT TOP 1 CASE WHEN a.Alvo = N'USER_PROJECT_DEACTIVATED' AND a.Referencia = N'ADMIN' THEN 1 ELSE 0 END
                           FROM perm.PermissionAuditLog a
                           WHERE a.UsuarioAfetadoId = x.UserId AND a.ProjetoId = x.ProjetoId
                             AND a.Alvo IN (N'USER_PROJECT_ADDED', N'USER_PROJECT_REACTIVATED', N'USER_PROJECT_DEACTIVATED')
                           ORDER BY a.Id DESC) = 1 THEN 1 ELSE 0 END
            FROM @pares x
            LEFT JOIN perm.UserProjects up ON up.UserId = x.UserId AND up.ProjetoId = x.ProjetoId;
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(pares.Select(p => new { u = p.UserId, p = p.ProjetoId }));
        cmd.Parameters.Add("@usr", SqlDbType.Int).Value = (object?)vinculosDoUsuario ?? DBNull.Value;
        cmd.Parameters.Add("@proj", SqlDbType.Int).Value = (object?)vinculosDoProjeto ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<EstadoVinculo>();
        while (await r.ReadAsync(ct))
            lista.Add(new(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetBoolean(2), r.GetInt32(3) == 1, r.GetInt32(4) == 1, r.GetInt32(5) == 1));
        return lista;
    }

    public async Task AplicarAsync(IReadOnlyList<OperacaoPlanejada> ops, int? perfilPadraoId, int? porUserId, string? porEmail, string origem, CancellationToken ct)
    {
        if (ops.Count == 0) return;
        // Cada operação é condicional ao estado atual (reexecução/concorrência não duplica nem sobrescreve) e só audita se mudou algo.
        const string adicionar = """
            IF NOT EXISTS (SELECT 1 FROM perm.UserProjects WHERE UserId = @u AND ProjetoId = @p)
            BEGIN
                INSERT INTO perm.UserProjects (UserId, ProjetoId, Ativo) VALUES (@u, @p, 1);
                INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
                VALUES (@porId, @porEmail, @u, @p, N'USER_PROJECT_ADDED', @origem, NULL, N'{"ativo":true}');
            END
            """;
        const string reativar = """
            UPDATE perm.UserProjects SET Ativo = 1, AtualizadoEm = SYSUTCDATETIME() WHERE UserId = @u AND ProjetoId = @p AND Ativo = 0;
            IF @@ROWCOUNT > 0
                INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
                VALUES (@porId, @porEmail, @u, @p, N'USER_PROJECT_REACTIVATED', @origem, N'{"ativo":false}', N'{"ativo":true}');
            """;
        const string desativar = """
            UPDATE perm.UserProjects SET Ativo = 0, AtualizadoEm = SYSUTCDATETIME() WHERE UserId = @u AND ProjetoId = @p AND Ativo = 1;
            IF @@ROWCOUNT > 0
                INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
                VALUES (@porId, @porEmail, @u, @p, N'USER_PROJECT_DEACTIVATED', @origem, N'{"ativo":true}', N'{"ativo":false}');
            """;
        const string perfilPadrao = """
            IF NOT EXISTS (SELECT 1 FROM perm.UserProjectProfiles WHERE UserId = @u AND ProjetoId = @p)
            BEGIN
                INSERT INTO perm.UserProjectProfiles (UserId, ProjetoId, ProfileId, Ativo) VALUES (@u, @p, @perfil, 1);
                INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
                VALUES (@porId, @porEmail, @u, @p, N'DEFAULT_PROFILE_ASSIGNED', @origem, N'[]',
                        (SELECT @perfil AS profileId, (SELECT Name FROM perm.PermissionProfiles WHERE ProfileId = @perfil) AS nome FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
            END
            """;
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        foreach (var op in ops)
        {
            if (op.Operacao == OperacaoVinculo.AtribuirPerfilPadrao && perfilPadraoId is null) continue;
            var sql = op.Operacao switch
            {
                OperacaoVinculo.Adicionar => adicionar,
                OperacaoVinculo.Reativar => reativar,
                OperacaoVinculo.Desativar => desativar,
                _ => perfilPadrao,
            };
            using var cmd = Cmd(c, sql, tx);
            cmd.Parameters.Add("@u", SqlDbType.Int).Value = op.UserId;
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = op.ProjetoId;
            cmd.Parameters.Add("@perfil", SqlDbType.Int).Value = (object?)perfilPadraoId ?? DBNull.Value;
            cmd.Parameters.Add("@porId", SqlDbType.Int).Value = (object?)porUserId ?? DBNull.Value;
            cmd.Parameters.Add("@porEmail", SqlDbType.NVarChar, 256).Value = (object?)porEmail ?? DBNull.Value;
            cmd.Parameters.Add("@origem", SqlDbType.NVarChar, 200).Value = origem;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task RegistrarResumoAsync(int projetoId, int? porUserId, string? porEmail, ResumoSincronizacao resumo, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, """
            INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
            VALUES (@porId, @porEmail, NULL, @p, N'ACC_PROJECT_SYNC', @origem, NULL, @json);
            """);
        cmd.Parameters.Add("@porId", SqlDbType.Int).Value = (object?)porUserId ?? DBNull.Value;
        cmd.Parameters.Add("@porEmail", SqlDbType.NVarChar, 256).Value = (object?)porEmail ?? DBNull.Value;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = projetoId;
        cmd.Parameters.Add("@origem", SqlDbType.NVarChar, 200).Value = resumo.Origem;
        cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(resumo, Json);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ResumoSincronizacao?> UltimoResumoAsync(int projetoId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, "SELECT TOP 1 ValorNovo FROM perm.PermissionAuditLog WHERE Alvo = N'ACC_PROJECT_SYNC' AND ProjetoId = @p ORDER BY Id DESC;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = projetoId;
        return await cmd.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<ResumoSincronizacao>(json, Json) : null;
    }

    public async Task<IReadOnlyList<MembroProjetoView>> MembrosAsync(int projetoId, CancellationToken ct)
    {
        const string sql = """
            WITH ids AS (
                SELECT UserId FROM perm.UserProjects WHERE ProjetoId = @p
                UNION SELECT UserId FROM perm.UserProjectProfiles WHERE ProjetoId = @p
                UNION SELECT UserId FROM perm.UserPermissionOverrides WHERE ProjetoId = @p)
            SELECT u.UserId, u.Email, u.DisplayName, u.AccUserId, up.Ativo, u.UltimoAcessoEm,
                (SELECT STRING_AGG(p.Name, N'|') WITHIN GROUP (ORDER BY p.Name) FROM perm.UserProjectProfiles upp
                   JOIN perm.PermissionProfiles p ON p.ProfileId = upp.ProfileId
                   WHERE upp.UserId = u.UserId AND upp.ProjetoId = @p AND upp.Ativo = 1),
                (SELECT STRING_AGG(p.Name, N'|') WITHIN GROUP (ORDER BY p.Name) FROM perm.UserProjectProfiles upp
                   JOIN perm.PermissionProfiles p ON p.ProfileId = upp.ProfileId
                   WHERE upp.UserId = u.UserId AND upp.ProjetoId IS NULL AND upp.Ativo = 1),
                (SELECT COUNT(*) FROM perm.UserPermissionOverrides o WHERE o.UserId = u.UserId AND o.ProjetoId = @p)
            FROM ids
            JOIN perm.Users u ON u.UserId = ids.UserId
            LEFT JOIN perm.UserProjects up ON up.UserId = u.UserId AND up.ProjetoId = @p
            ORDER BY COALESCE(u.DisplayName, u.Email);
            """;
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, sql);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = projetoId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        static IReadOnlyList<string> Lista(SqlDataReader r, int i) => r.IsDBNull(i) ? Array.Empty<string>() : r.GetString(i).Split('|');
        var lista = new List<MembroProjetoView>();
        while (await r.ReadAsync(ct))
            lista.Add(new(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetBoolean(4), r.IsDBNull(5) ? null : r.GetDateTime(5), Lista(r, 6), Lista(r, 7), r.GetInt32(8)));
        return lista;
    }
}
