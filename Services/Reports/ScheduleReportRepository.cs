using System.Data;
using Microsoft.Data.SqlClient;
using APS.WebApp.Services.CronogramaTipo2;

namespace APS.WebApp.Services.Reports;

public sealed record ScheduleRow(int IdCronograma, string? NaDocumentNumber, string? TaskName, string? EdtWbs,
    string? Disciplina, string? Fase, byte? GatePercentual, DateTime? TerminoLinhaBase, DateTime? Termino,
    decimal? PercentPrevistoReprogramado, decimal? PercentRealizado, decimal? Trabalho, decimal? WbsPercentPorWork,
    string? StatusValidacao, string? StatusDocumentoEngenharia, string? CrOs);
public sealed record ScheduleReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize, IReadOnlyList<ScheduleRow> Rows);

public interface IScheduleReportRepository
{
    Task<ScheduleReport> ReadAsync(string projectId, string? disciplina, string? fase, string? status,
        string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Visão operacional SOMENTE LEITURA do Cronograma Tipo 2 — fonte dbo.VW_Cronograma_Tipo2_Ultima_Versao
/// (Tipo_LDP = 2), a MESMA view já usada por CronogramaTipo2LdpSyncRepository para o matching real.
/// Nenhuma alteração no parser, na sincronização, no cálculo de WBS, em datas ou em Physical Completed —
/// hotfix aprovado permanece congelado. O "WBS calculado" exibido aqui é lido do MESMO snapshot já
/// persistido em Campos_Customizados_JSON via CronogramaTipo2Work.LerPercentualSnapshot (helper somente
/// leitura já existente, reaproveitado — nunca recalculado no frontend nem duplicado aqui).
/// </summary>
public sealed class ScheduleReportRepository(IConfiguration configuration) : IScheduleReportRepository
{
    public const string Query = """
        WITH Source AS (
            SELECT ID_Cronograma, NA_DOCUMENT_NUMBER, TASK_NAME, EDT_WBS, Disciplina_Cronograma, Fase, Gate_Percentual,
                   Termino_Linha_Base, Termino, Percent_Previsto_Reprogramado, Percent_Realizado, Trabalho,
                   Campos_Customizados_JSON, Status_Validacao_Documento_Gate, Status_Documento_Engenharia, CR_OS, Data_Sincronizacao
            FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao
            WHERE Project_ID_ACC = @projectId AND Tipo_LDP = 2
        ), Filtered AS (
            SELECT * FROM Source
            WHERE (@disciplina IS NULL OR Disciplina_Cronograma = @disciplina)
              AND (@fase IS NULL OR Fase = @fase)
              AND (@status IS NULL OR Status_Validacao_Documento_Gate = @status)
              AND (@search IS NULL OR NA_DOCUMENT_NUMBER LIKE @search OR TASK_NAME LIKE @search)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Source) AS TotalRows,
               (SELECT COUNT_BIG(*) FROM Filtered) AS FilteredRows,
               (SELECT MAX(Data_Sincronizacao) FROM Source) AS LastSync,
               p.ID_Cronograma, p.NA_DOCUMENT_NUMBER, p.TASK_NAME, p.EDT_WBS, p.Disciplina_Cronograma, p.Fase, p.Gate_Percentual,
               p.Termino_Linha_Base, p.Termino, p.Percent_Previsto_Reprogramado, p.Percent_Realizado, p.Trabalho,
               p.Campos_Customizados_JSON, p.Status_Validacao_Documento_Gate, p.Status_Documento_Engenharia, p.CR_OS
        FROM (SELECT 1 AS X) dummy
        OUTER APPLY (
            SELECT * FROM Filtered ORDER BY ID_Cronograma OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;

    public async Task<ScheduleReport> ReadAsync(string projectId, string? disciplina, string? fase, string? status,
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(Query, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@disciplina", SqlDbType.NVarChar, 200).Value = (object?)disciplina ?? DBNull.Value;
        command.Parameters.Add("@fase", SqlDbType.NVarChar, 50).Value = (object?)fase ?? DBNull.Value;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 100).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, filtered = 0;
        DateTime? lastSync = null;
        var rows = new List<ScheduleRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); filtered = reader.GetInt64(1);
            lastSync = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            if (reader.IsDBNull(3)) continue;
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            DateTime? DateVal(int i) => reader.IsDBNull(i) ? null : reader.GetDateTime(i);
            decimal? Dec(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            byte? ByteVal(int i) => reader.IsDBNull(i) ? null : reader.GetByte(i);
            string? json = Text(15);
            decimal? wbsPorWork = string.IsNullOrEmpty(json) ? null : CronogramaTipo2Work.LerPercentualSnapshot(json);
            rows.Add(new(reader.GetInt32(3), Text(4), Text(5), Text(6), Text(7), Text(8), ByteVal(9),
                DateVal(10), DateVal(11), Dec(12), Dec(13), Dec(14), wbsPorWork,
                Text(16), Text(17), Text(18)));
        }
        return new(total, filtered, lastSync, page, pageSize, rows);
    }
}
