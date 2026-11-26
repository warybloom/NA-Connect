using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record HoldRow(int IdRegistro, string? Item, string? NumeroHold, string? NumeroNaDocumento,
    string? Disciplina, string? TituloDocumento, string? Revisao, string? MotivoHold, string? Status, string? Responsavel);
public sealed record HoldReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize, IReadOnlyList<HoldRow> Rows);

public interface IHoldReportRepository
{
    Task<HoldReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Fonte: dbo.vw_Latest_HoldControl — mesmo padrão "última versão + projeto ativo" já comprovado
/// (VW_Ldp_Unificada, vw_Latest_WeeklySchedule etc.: depende de dbo.Projetos + tb_HoldControl). Não há
/// nenhuma coluna de data de abertura/prazo/fechamento nesta fonte — apenas Data_Sincronizacao (técnica).
/// Por isso NÃO existe "dias em aberto"/aging aqui: calcular isso seria inventar uma data que a fonte
/// real não tem. Cards de "Abertos/Fechados/Vencidos" também não são inferidos a partir do texto de
/// STATUS sem um catálogo comprovado — o agrupamento por Status real (texto como armazenado) substitui
/// essa classificação sem assumir semântica de negócio.
/// </summary>
public sealed class HoldReportRepository(IConfiguration configuration) : IHoldReportRepository
{
    public const string Query = """
        WITH Source AS (
            SELECT Id_Registro, ITEM, N_HOLD, N_NA_DOCUMENTO, DISCIPLINA, TITULO_DOCUMENTO, REV, MOTIVO_HOLD, STATUS, RESPONSAVEL, Data_Sincronizacao
            FROM dbo.vw_Latest_HoldControl
            WHERE Project_ID_ACC = @projectId
        ), Filtered AS (
            SELECT * FROM Source
            WHERE (@status IS NULL OR STATUS = @status)
              AND (@disciplina IS NULL OR DISCIPLINA = @disciplina)
              AND (@search IS NULL OR N_HOLD LIKE @search OR N_NA_DOCUMENTO LIKE @search OR TITULO_DOCUMENTO LIKE @search OR MOTIVO_HOLD LIKE @search)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Source) AS TotalRows,
               (SELECT COUNT_BIG(*) FROM Filtered) AS FilteredRows,
               (SELECT MAX(Data_Sincronizacao) FROM Source) AS LastSync,
               p.Id_Registro, p.ITEM, p.N_HOLD, p.N_NA_DOCUMENTO, p.DISCIPLINA, p.TITULO_DOCUMENTO, p.REV, p.MOTIVO_HOLD, p.STATUS, p.RESPONSAVEL
        FROM (SELECT 1 AS X) dummy
        OUTER APPLY (
            SELECT * FROM Filtered ORDER BY Id_Registro OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        """;

    public async Task<HoldReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken)
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
        var rows = new List<HoldRow>();
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
