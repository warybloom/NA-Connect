using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Tv.Portal;

public interface ITvPortalRoleDesignation { Task<bool> IsTvAsync(int userId, CancellationToken ct); }
public interface ITvPortalProjectRegistry { Task<bool> ExistsActiveAsync(string projectIdAcc, CancellationToken ct); }

/// <summary>Confere apenas que o projeto configurado pelo admin existe e segue ativo.
/// Não concede nem exige perm.UserProjects.</summary>
public sealed class SqlTvPortalProjectRegistry(IConfiguration configuration) : ITvPortalProjectRegistry
{
    public async Task<bool> ExistsActiveAsync(string projectIdAcc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectIdAcc)) return false;
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await connection.OpenAsync(ct);
        using var command = new SqlCommand("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Projetos WHERE Project_ID_ACC=@id AND Is_Ativo=1) THEN 1 ELSE 0 END", connection) { CommandTimeout = 15 };
        command.Parameters.Add("@id", SqlDbType.NVarChar, 255).Value =
            projectIdAcc.StartsWith("b.", StringComparison.Ordinal) ? projectIdAcc : "b." + projectIdAcc;
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }
}

/// <summary>Inactive TV profile/assignment must not turn compatibility mode into an administrative escape.</summary>
public sealed class SqlTvPortalRoleDesignation(IConfiguration configuration) : ITvPortalRoleDesignation
{
    public async Task<bool> IsTvAsync(int userId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await connection.OpenAsync(ct);
        using var command = new SqlCommand("SELECT CASE WHEN EXISTS(SELECT 1 FROM perm.UserProjectProfiles WHERE UserId=@userId AND ProfileId=7) THEN 1 ELSE 0 END", connection) { CommandTimeout = 15 };
        command.Parameters.Add("@userId", SqlDbType.Int).Value = userId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }
}
