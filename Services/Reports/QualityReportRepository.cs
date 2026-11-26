using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record QualityReportGroup(string? Status, string? Discipline, long Count);
public sealed record QualityReport(long TotalRows, long FilteredRows, long TotalGroups,
    DateTime? LastSync, int Page, int PageSize, IReadOnlyList<QualityReportGroup> Groups);

public interface IQualityReportRepository
{
    Task<QualityReport> ReadAsync(string projectId, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class QualityReportRepository(IConfiguration configuration) : IQualityReportRepository
{
    // Contagem de linhas do snapshot RNC, sem deduplicação por formulário ou classificação de status.
    public const string Query = """
        WITH Source AS (
            SELECT Status, Disciplina, Data_Sincronizacao
            FROM dbo.[NCR NONCONFORMITY REPORT]
            WHERE Project_ID_ACC = @projectId
        ), GroupedRows AS (
            SELECT Status, Disciplina, COUNT_BIG(*) AS RecordCount
            FROM Source WHERE @status IS NULL OR Status = @status
            GROUP BY Status, Disciplina
        ), Summary AS (
            SELECT COUNT_BIG(*) AS TotalRows,
                   COUNT_BIG(CASE WHEN @status IS NULL OR Status = @status THEN 1 END) AS FilteredRows,
                   MAX(Data_Sincronizacao) AS LastSync
            FROM Source
        )
        SELECT s.TotalRows, s.FilteredRows, s.LastSync,
               (SELECT COUNT_BIG(*) FROM GroupedRows) AS TotalGroups,
               p.Status, p.Disciplina, p.RecordCount
        FROM Summary s
        OUTER APPLY (
            SELECT Status, Disciplina, RecordCount FROM GroupedRows
            ORDER BY Status, Disciplina OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p
        ORDER BY p.Status, p.Disciplina;
        """;

    public async Task<QualityReport> ReadAsync(string projectId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(Query, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 100).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, filtered = 0, totalGroups = 0;
        DateTime? lastSync = null;
        var groups = new List<QualityReportGroup>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); filtered = reader.GetInt64(1);
            lastSync = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            totalGroups = reader.GetInt64(3);
            // Um grupo com Status e Disciplina NULL continua sendo um grupo real.
            if (!reader.IsDBNull(6)) groups.Add(new(reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6)));
        }
        return new(total, filtered, totalGroups, lastSync, page, pageSize, groups);
    }
}
