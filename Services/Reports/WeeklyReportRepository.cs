using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed class WeeklyReportRepository(IConfiguration configuration) : IWeeklyReportRepository
{
    // Uma única instrução SELECT. Fonte atual, sem views latest legadas ou alterações no operacional.
    public const string Query = """
        WITH Source AS (
            SELECT Id_Registro, Tipo_Registro, Status_Importacao, Data_Sincronizacao,
                   Numero_NA_Atividade, Nome_Tarefa, Disciplina, Diagnostico_Importacao,
                   Arquivo_Origem, Aba_Origem, Linha_Excel
            FROM dbo.tb_WeeklyEngineeringSchedule
            WHERE Project_ID_ACC = @projectId
        ), Summary AS (
            SELECT COUNT_BIG(*) AS TotalRows,
                   COUNT_BIG(CASE WHEN Tipo_Registro = N'DADO' THEN 1 END) AS DataRows,
                   COUNT_BIG(CASE WHEN Tipo_Registro = N'DADO' AND Status_Importacao = N'COM_ALERTAS' THEN 1 END) AS WarningRows,
                   COUNT_BIG(CASE WHEN Tipo_Registro IN (N'SEM_EXCEL', N'PADRAO_INCORRETO', N'PADRAO_AMBIGUO') THEN 1 END) AS SentinelRows,
                   MAX(Data_Sincronizacao) AS LastSync,
                   COUNT_BIG(CASE WHEN @status IS NULL OR Status_Importacao = @status THEN 1 END) AS FilteredRows
            FROM Source
        )
        SELECT s.*, p.* FROM Summary s
        OUTER APPLY (
            SELECT Id_Registro, Numero_NA_Atividade, Nome_Tarefa, Disciplina, Tipo_Registro,
                   Status_Importacao, Diagnostico_Importacao, Arquivo_Origem, Aba_Origem, Linha_Excel
            FROM Source WHERE @status IS NULL OR Status_Importacao = @status
            ORDER BY Id_Registro OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p
        ORDER BY p.Id_Registro;
        """;

    public async Task<WeeklyReport> ReadAsync(string projectId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(Query, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 50).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, data = 0, warnings = 0, sentinels = 0, filtered = 0;
        DateTime? lastSync = null;
        var rows = new List<WeeklyReportRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); data = reader.GetInt64(1); warnings = reader.GetInt64(2);
            sentinels = reader.GetInt64(3); lastSync = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
            filtered = reader.GetInt64(5);
            if (reader.IsDBNull(6)) continue; // OUTER APPLY preserva resumo mesmo sem linhas na página.
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            rows.Add(new(reader.GetInt32(6), Text(7), Text(8), Text(9), Text(10), Text(11), Text(12), Text(13), Text(14), reader.IsDBNull(15) ? null : reader.GetInt32(15)));
        }
        return new(data, warnings, sentinels, total - data - sentinels, total, filtered, lastSync, page, pageSize, rows);
    }
}
