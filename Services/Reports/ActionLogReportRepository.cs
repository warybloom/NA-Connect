using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record ActionLogRow(int IdRegistro, string? Id, string? Date, string? Name, string? Company,
    string? Cycle, string? FrontOfServices, string? Afe, string? Description, string? Name2, string? Company3,
    string? Schedule, string? Reschedule, string? Concl, string? Critical, string? Status, string? Comments);
public sealed record ActionLogReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize, IReadOnlyList<ActionLogRow> Rows);

public interface IInternalActionReportRepository
{
    Task<ActionLogReport> ReadAsync(string projectId, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken);
}
public interface IExternalActionReportRepository
{
    Task<ActionLogReport> ReadAsync(string projectId, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// CAP Interno e CAP Externo têm o MESMO formato de coluna (ID/Date/Name/Company/CYCLE/
/// FRONT_OF_SERVICES/AFE/Description/Name2/Company3/Schedule/Reschedule/Concl/Critical/Status/COMMENTS),
/// mas são fontes FISICAMENTE separadas — dbo.vw_Latest_InternalAction (tb_InternalActionLog) e
/// dbo.vw_Latest_ExternalAction (tb_ExternalActionLog), cada uma com seu próprio "última versão + projeto
/// ativo". Compartilhar aqui só o SQL/DTO idênticos (nunca misturar os dados: cada repository consulta a
/// sua própria tabela, cada um com sua própria contagem/paginação). Não há coluna de disciplina nesta
/// fonte — filtro por disciplina não se aplica a CAP Interno/Externo.
/// </summary>
internal static class ActionLogQuery
{
    public static string For(string viewName) => $"""
        WITH Source AS (
            SELECT Id_Registro, ID, [Date], Name, Company, CYCLE, FRONT_OF_SERVICES, AFE, Description, Name2, Company3, Schedule, Reschedule, Concl, Critical, Status, COMMENTS, Data_Sincronizacao
            FROM dbo.{viewName}
            WHERE Project_ID_ACC = @projectId
        ), Filtered AS (
            SELECT * FROM Source
            WHERE (@status IS NULL OR Status = @status)
              AND (@search IS NULL OR Name LIKE @search OR Company LIKE @search OR Description LIKE @search OR COMMENTS LIKE @search)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Source) AS TotalRows,
               (SELECT COUNT_BIG(*) FROM Filtered) AS FilteredRows,
               (SELECT MAX(Data_Sincronizacao) FROM Source) AS LastSync,
               p.Id_Registro, p.ID, p.[Date], p.Name, p.Company, p.CYCLE, p.FRONT_OF_SERVICES, p.AFE, p.Description, p.Name2, p.Company3, p.Schedule, p.Reschedule, p.Concl, p.Critical, p.Status, p.COMMENTS
        FROM (SELECT 1 AS X) dummy
        OUTER APPLY (
            SELECT * FROM Filtered ORDER BY Id_Registro OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;

    public static async Task<ActionLogReport> ReadAsync(IConfiguration configuration, string viewName, string projectId, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(For(viewName), connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 200).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, filtered = 0;
        DateTime? lastSync = null;
        var rows = new List<ActionLogRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); filtered = reader.GetInt64(1);
            lastSync = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            if (reader.IsDBNull(3)) continue;
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            rows.Add(new(reader.GetInt32(3), Text(4), Text(5), Text(6), Text(7), Text(8), Text(9), Text(10), Text(11), Text(12), Text(13), Text(14), Text(15), Text(16), Text(17), Text(18), Text(19)));
        }
        return new(total, filtered, lastSync, page, pageSize, rows);
    }
}

public sealed class InternalActionReportRepository(IConfiguration configuration) : IInternalActionReportRepository
{
    public Task<ActionLogReport> ReadAsync(string projectId, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken)
        => ActionLogQuery.ReadAsync(configuration, "vw_Latest_InternalAction", projectId, status, search, page, pageSize, cancellationToken);
}

public sealed class ExternalActionReportRepository(IConfiguration configuration) : IExternalActionReportRepository
{
    public Task<ActionLogReport> ReadAsync(string projectId, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken)
        => ActionLogQuery.ReadAsync(configuration, "vw_Latest_ExternalAction", projectId, status, search, page, pageSize, cancellationToken);
}
