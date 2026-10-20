using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports.Dashboards;

public interface IProductionDashboardRepository
{
    /// <summary>Linhas do PROJETO para a dashboard (um único SELECT parametrizado por @projectId).</summary>
    Task<IReadOnlyList<DashboardRow>> ReadRowsAsync(DashboardDefinition definition, string projectId, CancellationToken cancellationToken);
}

public sealed class ProductionDashboardRepository(IConfiguration configuration) : IProductionDashboardRepository
{
    public async Task<IReadOnlyList<DashboardRow>> ReadRowsAsync(DashboardDefinition definition, string projectId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        var linhas = await LerAsync(connection, definition.Sql, projectId, cancellationToken);
        if (definition.ComplementSql != null && definition.Merge != null)
            definition.Merge(linhas, await LerAsync(connection, definition.ComplementSql, projectId, cancellationToken));
        return linhas.Select(v => new DashboardRow(v)).ToList();
    }

    private static async Task<List<Dictionary<string, object?>>> LerAsync(SqlConnection connection, string sql, string projectId, CancellationToken ct)
    {
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var colunas = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var linhas = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var valores = new Dictionary<string, object?>(colunas.Length, StringComparer.Ordinal);
            for (int i = 0; i < colunas.Length; i++) valores[colunas[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            linhas.Add(valores);
        }
        return linhas;
    }
}
