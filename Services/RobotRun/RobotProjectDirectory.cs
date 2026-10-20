using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.RobotRun;

public interface IRobotProjectDirectory
{
    Task<IReadOnlyList<RegisteredRobotProject>> ReadAsync(CancellationToken ct);
    async Task<IReadOnlyList<RegisteredRobotProject>> ReadActiveAsync(CancellationToken ct) =>
        (await ReadAsync(ct)).Where(p => p.Active).ToArray();
}
public sealed class RobotProjectDirectory(IConfiguration configuration) : IRobotProjectDirectory
{
    public Task<IReadOnlyList<RegisteredRobotProject>> ReadAsync(CancellationToken ct) => ReadAsync(false, ct);
    public Task<IReadOnlyList<RegisteredRobotProject>> ReadActiveAsync(CancellationToken ct) => ReadAsync(true, ct);

    private async Task<IReadOnlyList<RegisteredRobotProject>> ReadAsync(bool activeOnly, CancellationToken ct)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection"));
        await connection.OpenAsync(ct);
        using var command = new SqlCommand($"""
            SELECT P.Project_ID_ACC,P.Nome_Projeto,H.Account_ID_ACC,P.Is_Ativo,P.Tipo_LDP
            FROM dbo.Projetos P LEFT JOIN dbo.Hubs_Conta H ON P.HubContaID=H.HubContaID
            {(activeOnly ? "WHERE P.Is_Ativo = 1" : "")}
            ORDER BY P.Nome_Projeto,P.Project_ID_ACC
            """, connection) { CommandTimeout = 30 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<RegisteredRobotProject>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2), !reader.IsDBNull(3) && Convert.ToBoolean(reader.GetValue(3)), reader.IsDBNull(4) ? 1 : Convert.ToInt32(reader.GetValue(4))));
        return rows;
    }
}
