using System.Data;
using APS.WebApp.Services.Permissions;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.LocalAuth;

/// <summary>
/// auth.UserCredentials (Migrations/Auth/20261005_001_LocalCredentials.sql) + leitura de perm.Users.
/// Nunca cria usuário em perm.Users e nunca devolve o hash para fora do serviço de autenticação.
/// </summary>
public sealed class SqlNaLocalCredentialRepository(IConfiguration configuration) : INaLocalCredentialRepository
{
    private const string ColunasUsuario = "UserId, AccUserId, Email, DisplayName, Ativo, UltimoAcessoEm";

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var c = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await c.OpenAsync(ct);
        return c;
    }

    private static SqlCommand Cmd(SqlConnection c, string sql) => new(sql, c) { CommandTimeout = 30 };

    public async Task<PermUser?> ObterUsuarioPorEmailAsync(string email, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, $"SELECT TOP (1) {ColunasUsuario} FROM perm.Users WHERE Email = @email;");
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;
        return await LerUsuarioAsync(cmd, ct);
    }

    public async Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, $"SELECT {ColunasUsuario} FROM perm.Users WHERE UserId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        return await LerUsuarioAsync(cmd, ct);
    }

    public async Task<NaLocalCredential?> ObterCredencialAsync(int userId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, """
            SELECT UserId, PasswordHash, FailedAttempts, LockoutUntil, PasswordUpdatedAt, MustChangePassword
            FROM auth.UserCredentials WHERE UserId = @id;
            """);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new NaLocalCredential(r.GetInt32(0), r.GetString(1), r.GetInt32(2),
            r.IsDBNull(3) ? null : DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc),
            DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc), r.GetBoolean(5));
    }

    public async Task<(int FailedAttempts, DateTime? LockoutUntil)> RegistrarFalhaAsync(int userId, DateTime agoraUtc, CancellationToken ct)
    {
        // Mesma regra de NaLocalAuthPolicy.AposFalha, atômica (as colunas à direita são os valores ANTES do UPDATE).
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, """
            UPDATE auth.UserCredentials SET
                FailedAttempts = CASE WHEN LockoutUntil IS NOT NULL AND LockoutUntil <= @agora THEN 1 ELSE FailedAttempts + 1 END,
                LockoutUntil = CASE
                    WHEN (CASE WHEN LockoutUntil IS NOT NULL AND LockoutUntil <= @agora THEN 1 ELSE FailedAttempts + 1 END) >= @max THEN @bloqueioAte
                    WHEN LockoutUntil IS NOT NULL AND LockoutUntil <= @agora THEN NULL
                    ELSE LockoutUntil END
            OUTPUT inserted.FailedAttempts, inserted.LockoutUntil
            WHERE UserId = @id;
            """);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@agora", SqlDbType.DateTime2).Value = agoraUtc;
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = NaLocalAuthPolicy.MaxFailedAttempts;
        cmd.Parameters.Add("@bloqueioAte", SqlDbType.DateTime2).Value = agoraUtc + NaLocalAuthPolicy.LockoutDuration;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return (0, null);
        return (r.GetInt32(0), r.IsDBNull(1) ? null : DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc));
    }

    public async Task RegistrarSucessoAsync(int userId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, """
            UPDATE auth.UserCredentials SET FailedAttempts = 0, LockoutUntil = NULL
            WHERE UserId = @id AND (FailedAttempts <> 0 OR LockoutUntil IS NOT NULL);
            """);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DefinirSenhaAsync(int userId, string passwordHash, bool mustChangePassword, DateTime agoraUtc, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        using var cmd = Cmd(c, """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            UPDATE auth.UserCredentials WITH (UPDLOCK, SERIALIZABLE)
               SET PasswordHash = @hash, FailedAttempts = 0, LockoutUntil = NULL,
                   PasswordUpdatedAt = @agora, MustChangePassword = @must
             WHERE UserId = @id;
            IF @@ROWCOUNT = 0
                INSERT INTO auth.UserCredentials (UserId, PasswordHash, FailedAttempts, LockoutUntil, PasswordUpdatedAt, MustChangePassword)
                VALUES (@id, @hash, 0, NULL, @agora, @must);
            COMMIT;
            """);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@hash", SqlDbType.NVarChar, 512).Value = passwordHash;
        cmd.Parameters.Add("@agora", SqlDbType.DateTime2).Value = agoraUtc;
        cmd.Parameters.Add("@must", SqlDbType.Bit).Value = mustChangePassword;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<PermUser?> LerUsuarioAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new PermUser(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.IsDBNull(5) ? null : r.GetDateTime(5));
    }
}
