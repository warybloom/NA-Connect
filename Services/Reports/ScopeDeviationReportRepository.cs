using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record ScopeDeviationRow(int IdRegistro, string? Item, string? Descricao, string? Disciplina,
    string? NumeroDesvio, string? Data, string? QuantidadeHH, string? Status, string? DesvioInterno, string? ChangeOrder);
public sealed record ScopeDeviationReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize, IReadOnlyList<ScopeDeviationRow> Rows);

public interface IScopeDeviationReportRepository
{
    Task<ScopeDeviationReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Fonte: dbo.vw_Latest_ScopeDeviation ("última versão + projeto ativo", depende de dbo.Projetos +
/// tb_ScopeDeviation). Esta fonte NÃO tem colunas de custo/valor, causa, impacto ou responsável — só
/// ITEM, DESCRICAO, DISCIPLINA, N_DO_DESVIO, DATA (texto), QUANT_HH (texto), STATUS, DESVIO_INTERNO,
/// CHANGE_ORDER. Nenhum desses campos ausentes é inventado aqui; DATA/QUANT_HH permanecem texto como
/// armazenados, nunca convertidos/somados sem prova do formato real.
/// </summary>
public sealed class ScopeDeviationReportRepository(IConfiguration configuration) : IScopeDeviationReportRepository
{
    public const string Query = """
        WITH Source AS (
            SELECT Id_Registro, ITEM, DESCRICAO, DISCIPLINA, N_DO_DESVIO, DATA, QUANT_HH, STATUS, DESVIO_INTERNO, CHANGE_ORDER, Data_Sincronizacao
            FROM dbo.vw_Latest_ScopeDeviation
            WHERE Project_ID_ACC = @projectId
        ), Filtered AS (
            SELECT * FROM Source
            WHERE (@status IS NULL OR STATUS = @status)
              AND (@disciplina IS NULL OR DISCIPLINA = @disciplina)
              AND (@search IS NULL OR N_DO_DESVIO LIKE @search OR DESCRICAO LIKE @search)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Source) AS TotalRows,
               (SELECT COUNT_BIG(*) FROM Filtered) AS FilteredRows,
               (SELECT MAX(Data_Sincronizacao) FROM Source) AS LastSync,
               p.Id_Registro, p.ITEM, p.DESCRICAO, p.DISCIPLINA, p.N_DO_DESVIO, p.DATA, p.QUANT_HH, p.STATUS, p.DESVIO_INTERNO, p.CHANGE_ORDER
        FROM (SELECT 1 AS X) dummy
        OUTER APPLY (
            SELECT * FROM Filtered ORDER BY Id_Registro OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;

    public async Task<ScopeDeviationReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(Query, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 200).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@disciplina", SqlDbType.NVarChar, 200).Value = (object?)disciplina ?? DBNull.Value;
        command.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, filtered = 0;
        DateTime? lastSync = null;
        var rows = new List<ScopeDeviationRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); filtered = reader.GetInt64(1);
            lastSync = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            if (reader.IsDBNull(3)) continue;
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            rows.Add(new(reader.GetInt32(3), Text(4), Text(5), Text(6), Text(7), Text(8), Text(9), Text(10), Text(11), Text(12)));
        }
        return new(total, filtered, lastSync, page, pageSize, rows);
    }
}
