using System.Data;
using APS.WebApp.Models.WeeklyPlanning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.WeeklyPlanning;

/// <summary>Independent, parameterized read adapter. Snapshot references never become canonical document keys.</summary>
public sealed class PlanningSourceReader(IConfiguration configuration)
{
    public const string DocumentsSql = """
        WITH Candidates AS (
          SELECT ID_Registro, NA_Document_Number, Document_Title, Discipline,
                 COALESCE(Replanned_Date, Planned_Date_Baseline) AS PlannedDate, Planned_HH
          FROM dbo.VW_Ldp_Unificada
          WHERE Project_ID_ACC = @projectId
            AND (@discipline IS NULL OR Discipline = @discipline)
            AND COALESCE(Replanned_Date, Planned_Date_Baseline) >= @start
            AND COALESCE(Replanned_Date, Planned_Date_Baseline) < DATEADD(day, 1, @end)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Candidates) AS TotalRows, p.*
        FROM (SELECT 1 AS X) seed OUTER APPLY (
          SELECT * FROM Candidates ORDER BY ID_Registro
          OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;
    public const string LegacySql = """
        WITH Candidates AS (
          SELECT Id_Registro, Numero_NA_Atividade, Nome_Tarefa, Disciplina,
                 COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date) AS PlannedDate,
                 Recurso, Weekly_HH_Previsto, Weekly_Observacao_Dia, Data_Sincronizacao
          FROM dbo.tb_WeeklyEngineeringSchedule
          WHERE Project_ID_ACC = @projectId AND Tipo_Registro = 'DADO'
            AND (@discipline IS NULL OR Disciplina = @discipline)
            AND COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date) >= @start
            AND COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date) < DATEADD(day, 1, @end)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Candidates) AS TotalRows, p.*
        FROM (SELECT 1 AS X) seed OUTER APPLY (
          SELECT * FROM Candidates ORDER BY Id_Registro
          OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;
    public async Task<PlanningDocumentPage> Documents(string projectId, DateOnly start, DateOnly end,
        string? discipline, int page, int pageSize, CancellationToken ct)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        using var command = Command(connection, DocumentsSql, projectId, start, end, discipline, page, pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<PlanningDocumentCandidate>(); long total = 0;
        while (await reader.ReadAsync(ct))
        {
            total = reader.GetInt64(0);
            if (!reader.IsDBNull(1)) rows.Add(new(reader.GetInt32(1), Text(reader, 2), Text(reader, 3),
                Text(reader, 4), Date(reader, 5), reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
        }
        return new("dbo.VW_Ldp_Unificada", "SNAPSHOT_REFERENCE_ONLY", total, page, pageSize, rows,
            "Consulta por data da LDP; não é programação aprovada. ID identifica snapshot. HH total não é HH semanal. Documentos sem data ficam fora do período.");
    }
    public async Task<LegacyWeeklyPage> Legacy(string projectId, DateOnly start, DateOnly end,
        string? discipline, int page, int pageSize, CancellationToken ct)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        using var command = Command(connection, LegacySql, projectId, start, end, discipline, page, pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<LegacyWeeklyRow>(); long total = 0;
        while (await reader.ReadAsync(ct))
        {
            total = reader.GetInt64(0);
            if (!reader.IsDBNull(1)) rows.Add(new(reader.GetInt32(1), Text(reader, 2), Text(reader, 3), Text(reader, 4),
                Date(reader, 5), Text(reader, 6), reader.IsDBNull(7) ? null : reader.GetDecimal(7), Text(reader, 8), Date(reader, 9)));
        }
        return new("dbo.tb_WeeklyEngineeringSchedule", total, page, pageSize, rows,
            "Snapshot legado substituído a cada importação. Horas importadas sem equivalência confirmada com HH semanal; sem workflow ou histórico de aprovação.");
    }
    private SqlConnection Connection() => new(configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("Conexão não configurada."));
    private static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? Date(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static SqlCommand Command(SqlConnection connection, string sql, string projectId,
        DateOnly start, DateOnly end, string? discipline, int page, int pageSize)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@start", SqlDbType.Date).Value = start.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@end", SqlDbType.Date).Value = end.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@discipline", SqlDbType.NVarChar, 200).Value = (object?)discipline ?? DBNull.Value;
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        return command;
    }
}
