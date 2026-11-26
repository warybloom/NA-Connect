using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record ProductionDocumentsReportGroup(string? DocumentStatus, string? Discipline, long Count);
public sealed record ProductionDocumentsReport(long TotalRows, long FilteredRows, long TotalGroups,
    long TotalDisciplines, long DocumentsWithoutStatus, long DocumentsCompleted, DateTime? LastSync,
    int Page, int PageSize, IReadOnlyList<ProductionDocumentsReportGroup> Groups);

public interface IProductionDocumentsReportRepository
{
    Task<ProductionDocumentsReport> ReadAsync(string projectId, string? documentStatus, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class ProductionDocumentsReportRepository(IConfiguration configuration) : IProductionDocumentsReportRepository
{
    // Fonte: dbo.VW_Ldp_Unificada — já comprovada em produção por Services/LdpUpdateService.cs
    // (SyncAccAttributesFromSqlAsync): apesar do nome, é a view "última versão" (ROW_NUMBER PARTITION BY
    // Project_ID_ACC, NA_Document_Number ORDER BY Data_Sincronizacao DESC, ID_Registro DESC, rn = 1),
    // filtrada por Projetos.Is_Ativo = 1 — ou seja, só a sincronização mais recente de cada documento,
    // só de projetos ativos ("documentos ativos" do pedido). dbo.VW_LDP_DOCUMENTOS_ATIVOS foi considerada
    // e descartada: não tem nenhum uso comprovado em produção e expõe uma coluna rn não filtrada (exigiria
    // assumir sem prova que rn=1 é a mesma semântica) — preferida a fonte já validada e documentada.
    // Contagem de linhas do snapshot ativo, sem inventar regra de "documento obrigatório" ou "atraso":
    // DocumentsWithoutStatus é fato estrutural (Document_Status nulo/vazio), DocumentsCompleted é leitura
    // direta do percentual já publicado (Percent_Physical_Completed = 100), nunca um índice calculado.
    public static readonly string Query = $"""
        WITH Source AS (
            SELECT Document_Status, Discipline, Percent_Physical_Completed, Data_Sincronizacao
            FROM {APS.WebApp.Services.Planning.PmoLdpSnapshot.Tabela}
            WHERE Project_ID_ACC = @projectId
              AND {APS.WebApp.Services.Planning.LdpItemRules.SqlIsDocument("LdpAtual")}
        ), GroupedRows AS (
            SELECT Document_Status, Discipline, COUNT_BIG(*) AS RecordCount
            FROM Source WHERE @documentStatus IS NULL OR Document_Status = @documentStatus
            GROUP BY Document_Status, Discipline
        ), Summary AS (
            SELECT COUNT_BIG(*) AS TotalRows,
                   COUNT_BIG(CASE WHEN @documentStatus IS NULL OR Document_Status = @documentStatus THEN 1 END) AS FilteredRows,
                   COUNT_BIG(DISTINCT Discipline) AS TotalDisciplines,
                   COUNT_BIG(CASE WHEN Document_Status IS NULL OR LTRIM(RTRIM(Document_Status)) = '' THEN 1 END) AS DocumentsWithoutStatus,
                   COUNT_BIG(CASE WHEN Percent_Physical_Completed = 100 THEN 1 END) AS DocumentsCompleted,
                   MAX(Data_Sincronizacao) AS LastSync
            FROM Source
        )
        SELECT s.TotalRows, s.FilteredRows, s.TotalDisciplines, s.DocumentsWithoutStatus, s.DocumentsCompleted, s.LastSync,
               (SELECT COUNT_BIG(*) FROM GroupedRows) AS TotalGroups,
               p.Document_Status, p.Discipline, p.RecordCount
        FROM Summary s
        OUTER APPLY (
            SELECT Document_Status, Discipline, RecordCount FROM GroupedRows
            ORDER BY Document_Status, Discipline OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p
        ORDER BY p.Document_Status, p.Discipline
        {APS.WebApp.Services.Planning.PmoLdpSnapshot.OpcaoPlano};
        """;

    public async Task<ProductionDocumentsReport> ReadAsync(string projectId, string? documentStatus, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(Query, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@documentStatus", SqlDbType.NVarChar, 100).Value = (object?)documentStatus ?? DBNull.Value;
        command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long total = 0, filtered = 0, totalGroups = 0, totalDisciplines = 0, withoutStatus = 0, completed = 0;
        DateTime? lastSync = null;
        var groups = new List<ProductionDocumentsReportGroup>();
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(0); filtered = reader.GetInt64(1);
            totalDisciplines = reader.GetInt64(2); withoutStatus = reader.GetInt64(3); completed = reader.GetInt64(4);
            lastSync = reader.IsDBNull(5) ? null : reader.GetDateTime(5);
            totalGroups = reader.GetInt64(6);
            // Um grupo com Document_Status e Discipline NULL continua sendo um grupo real.
            if (!reader.IsDBNull(9)) groups.Add(new(reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetInt64(9)));
        }
        return new(total, filtered, totalGroups, totalDisciplines, withoutStatus, completed, lastSync, page, pageSize, groups);
    }
}
