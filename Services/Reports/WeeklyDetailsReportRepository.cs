using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record WeeklyDetailRow(int IdRegistro, string? Atividade, string? Disciplina, string? Tarefa,
    string? PercentPrevistoSemana, string? PercentRealizado, string? Recurso, string? Fase, string? Status,
    DateTime? Baseline, DateTime? Reprogramado, string? Preparacao, string? Verificacao, string? Aprovacao,
    decimal? HhPrevisto, decimal? HhVerificacao, decimal? HhAprovacao, string? LiberadoExecucao,
    string? Observacao, string? TipoRegistro, string? StatusImportacao, string? DiagnosticoImportacao);
public sealed record WeeklyDetailsReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize, IReadOnlyList<WeeklyDetailRow> Rows);

public interface IWeeklyDetailsReportRepository
{
    Task<WeeklyDetailsReport> ReadAsync(string projectId, string? disciplina, string? status, string? responsavel,
        string? fase, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Fonte: dbo.tb_WeeklyEngineeringSchedule — MESMA tabela já usada por WeeklyReportRepository (endpoint
/// /api/reports/weekly, mantido intacto). Este é um endpoint ADICIONAL sobre a mesma tabela, só com os
/// campos ricos de engenharia semanal que o resumo de qualidade de importação não expõe (baseline,
/// reprogramado, preparação, verificação, aprovação, HH por etapa, liberação, observação). Nenhuma
/// tabela nova, nenhuma alteração na tabela operacional.
/// </summary>
public sealed class WeeklyDetailsReportRepository(IConfiguration configuration) : IWeeklyDetailsReportRepository
{
    public const string Query = """
        WITH Source AS (
            SELECT Id_Registro, Numero_NA_Atividade, Disciplina, Nome_Tarefa, Perc_Previsto_Semana, Perc_Realizado,
                   Recurso, Weekly_Phase, Weekly_Progress_Status, Weekly_Baseline_Date, Weekly_Replanned_Date,
                   Weekly_Preparation_Completed, Weekly_Verification, Weekly_Approval, Weekly_HH_Previsto,
                   Weekly_HH_Verificacao, Weekly_HH_Aprovacao, Weekly_Liberado_Execucao, Weekly_Observacao_Dia,
                   Tipo_Registro, Status_Importacao, Diagnostico_Importacao, Data_Sincronizacao
            FROM dbo.tb_WeeklyEngineeringSchedule
            WHERE Project_ID_ACC = @projectId
        ), Filtered AS (
            SELECT * FROM Source
            WHERE (@disciplina IS NULL OR Disciplina = @disciplina)
              AND (@status IS NULL OR Weekly_Progress_Status = @status)
              AND (@responsavel IS NULL OR Recurso = @responsavel)
              AND (@fase IS NULL OR Weekly_Phase = @fase)
              AND (@search IS NULL OR Numero_NA_Atividade LIKE @search OR Nome_Tarefa LIKE @search)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Source) AS TotalRows,
               (SELECT COUNT_BIG(*) FROM Filtered) AS FilteredRows,
               (SELECT MAX(Data_Sincronizacao) FROM Source) AS LastSync,
               p.Id_Registro, p.Numero_NA_Atividade, p.Disciplina, p.Nome_Tarefa, p.Perc_Previsto_Semana, p.Perc_Realizado,
               p.Recurso, p.Weekly_Phase, p.Weekly_Progress_Status, p.Weekly_Baseline_Date, p.Weekly_Replanned_Date,
               p.Weekly_Preparation_Completed, p.Weekly_Verification, p.Weekly_Approval, p.Weekly_HH_Previsto,
               p.Weekly_HH_Verificacao, p.Weekly_HH_Aprovacao, p.Weekly_Liberado_Execucao, p.Weekly_Observacao_Dia,
               p.Tipo_Registro, p.Status_Importacao, p.Diagnostico_Importacao
        FROM (SELECT 1 AS X) dummy
        OUTER APPLY (
            SELECT * FROM Filtered ORDER BY Id_Registro OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;

    public async Task<WeeklyDetailsReport> ReadAsync(string projectId, string? disciplina, string? status, string? responsavel,
        string? fase, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(Query, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@disciplina", SqlDbType.NVarChar, 200).Value = (object?)disciplina ?? DBNull.Value;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 200).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@responsavel", SqlDbType.NVarChar, 200).Value = (object?)responsavel ?? DBNull.Value;
        command.Parameters.Add("@fase", SqlDbType.NVarChar, 200).Value = (object?)fase ?? DBNull.Value;
        command.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, filtered = 0;
        DateTime? lastSync = null;
        var rows = new List<WeeklyDetailRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); filtered = reader.GetInt64(1);
            lastSync = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            if (reader.IsDBNull(3)) continue;
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            DateTime? DateVal(int i) => reader.IsDBNull(i) ? null : reader.GetDateTime(i);
            decimal? Dec(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            rows.Add(new(reader.GetInt32(3), Text(4), Text(5), Text(6), Text(7), Text(8), Text(9), Text(10), Text(11),
                DateVal(12), DateVal(13), Text(14), Text(15), Text(16), Dec(17), Dec(18), Dec(19), Text(20), Text(21), Text(22), Text(23), Text(24)));
        }
        return new(total, filtered, lastSync, page, pageSize, rows);
    }
}
