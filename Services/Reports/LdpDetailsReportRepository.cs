using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record LdpGateInfo(byte Gate, string? GateDescricao, bool GateAtivo, string? Fase, string? DocumentStatus,
    string? Situation, DateTime? PlannedBaseline, DateTime? ReplannedDate, decimal? PlannedPhysical, decimal? PhysicalCompleted);
public sealed record LdpDetailRow(int IdRegistro, string? NaDocumentNumber, string? ClientDocumentNumber, string? DocumentTitle,
    string? Discipline, string? DocumentStatus, string? StatusInFlow, string? Situation, decimal? WbsPercent,
    decimal? PercentPlannedPhysical, decimal? PercentPhysicalCompleted, DateTime? PlannedDateBaseline, DateTime? ReplannedDate,
    DateTime? EmissionDate, string? CommentStatus, decimal? PlannedHh, decimal? ActualHh, decimal? PlannedCost, decimal? ActualCost,
    IReadOnlyList<LdpGateInfo> Gates);
public sealed record LdpDetailsReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize, IReadOnlyList<LdpDetailRow> Rows);

public interface ILdpDetailsReportRepository
{
    Task<LdpDetailsReport> ReadAsync(string projectId, string? discipline, string? documentStatus, string? gateFase,
        string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Visão operacional SOMENTE LEITURA da LDP: base de documento em dbo.VW_Ldp_Unificada (mesma fonte já
/// comprovada por Produção/Documentos e Mapa de Calor) + gates 30/60/90 em
/// dbo.VW_Ldp_Documento_Gates_Ultima_Versao (mesma view já usada por CronogramaTipo2LdpSyncRepository
/// para o matching real), unidos em memória por Ldp_Registro_Id = ID_Registro. Nenhuma tabela nova,
/// nenhuma alteração no hotfix de Cronograma/LDP aprovado — só leitura.
/// </summary>
public sealed class LdpDetailsReportRepository(IConfiguration configuration) : ILdpDetailsReportRepository
{
    // DESEMPENHO (hotfix 29/09/2026): a LDP atual é lida UMA vez (com PmoLdpSnapshot.OpcaoPlano) para #LdpAtual da sessão.
    // Antes, a CTE Source era referenciada 4 vezes (2 contagens, MAX e página) e o snapshot era recalculado em cada uma
    // (35–49 s, timeout mesmo no CHS001). O statement final é o MESMO de antes, só lendo de #LdpAtual.
    public static readonly string BaseQuery = $"""
        SET NOCOUNT ON;
        IF OBJECT_ID('tempdb..#LdpAtual') IS NOT NULL DROP TABLE #LdpAtual;
        SELECT ID_Registro, NA_Document_Number, Client_Document_Number, Document_Title, Discipline, Document_Status,
               Status_In_Flow, Situation, WBS_Percent, Percent_Planned_Physical, Percent_Physical_Completed,
               Planned_Date_Baseline, Replanned_Date, Emission_Date, Comment_Status, Planned_HH, Actual_HH,
               Planned_Cost, Actual_Cost, Data_Sincronizacao
        INTO #LdpAtual
        FROM {APS.WebApp.Services.Planning.PmoLdpSnapshot.Tabela}
        WHERE Project_ID_ACC = @projectId
          AND {APS.WebApp.Services.Planning.LdpItemRules.SqlIsDocument("LdpAtual")}
        {APS.WebApp.Services.Planning.PmoLdpSnapshot.OpcaoPlano};
        WITH Source AS (
            SELECT * FROM #LdpAtual
        ), Filtered AS (
            SELECT * FROM Source
            WHERE (@discipline IS NULL OR Discipline = @discipline)
              AND (@documentStatus IS NULL OR Document_Status = @documentStatus)
              AND (@search IS NULL OR NA_Document_Number LIKE @search OR Client_Document_Number LIKE @search OR Document_Title LIKE @search)
        )
        SELECT (SELECT COUNT_BIG(*) FROM Source) AS TotalRows,
               (SELECT COUNT_BIG(*) FROM Filtered) AS FilteredRows,
               (SELECT MAX(Data_Sincronizacao) FROM Source) AS LastSync,
               p.ID_Registro, p.NA_Document_Number, p.Client_Document_Number, p.Document_Title, p.Discipline, p.Document_Status,
               p.Status_In_Flow, p.Situation, p.WBS_Percent, p.Percent_Planned_Physical, p.Percent_Physical_Completed,
               p.Planned_Date_Baseline, p.Replanned_Date, p.Emission_Date, p.Comment_Status, p.Planned_HH, p.Actual_HH,
               p.Planned_Cost, p.Actual_Cost
        FROM (SELECT 1 AS X) dummy
        OUTER APPLY (
            SELECT * FROM Filtered ORDER BY NA_Document_Number OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
        ) p;
        DROP TABLE #LdpAtual;
        """;

    // Gates de TODOS os documentos retornados na página, buscados de uma vez (nunca N+1) — filtro
    // opcional de Fase/gate é aplicado depois de agrupar por documento, para não perder o documento caso
    // outro gate dele não bata o filtro.
    public const string GatesQuery = """
        SELECT Ldp_Registro_Id, Gate_Percentual, Gate_Descricao, Gate_Ativo, Fase, Document_Status, Situation,
               Planned_Baseline, Replanned_Date, Planned_Physical, Physical_Completed
        FROM dbo.VW_Ldp_Documento_Gates_Ultima_Versao
        WHERE Project_ID_ACC = @projectId AND Ldp_Registro_Id IN (SELECT value FROM STRING_SPLIT(@ids, ','));
        """;

    public async Task<LdpDetailsReport> ReadAsync(string projectId, string? discipline, string? documentStatus, string? gateFase,
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);

        long total = 0, filtered = 0;
        DateTime? lastSync = null;
        var baseRows = new List<(int Id, LdpDetailRow Row)>();

        using (var command = new SqlCommand(BaseQuery, connection) { CommandTimeout = 30 })
        {
            command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            command.Parameters.Add("@discipline", SqlDbType.NVarChar, 200).Value = (object?)discipline ?? DBNull.Value;
            command.Parameters.Add("@documentStatus", SqlDbType.NVarChar, 200).Value = (object?)documentStatus ?? DBNull.Value;
            command.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";
            command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
            command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                total = reader.GetInt64(0); filtered = reader.GetInt64(1);
                lastSync = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                if (reader.IsDBNull(3)) continue;
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                DateTime? DateVal(int i) => reader.IsDBNull(i) ? null : reader.GetDateTime(i);
                decimal? Dec(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
                int id = reader.GetInt32(3);
                baseRows.Add((id, new LdpDetailRow(id, Text(4), Text(5), Text(6), Text(7), Text(8), Text(9), Text(10),
                    Dec(11), Dec(12), Dec(13), DateVal(14), DateVal(15), DateVal(16), Text(17), Dec(18), Dec(19), Dec(20), Dec(21),
                    Array.Empty<LdpGateInfo>())));
            }
        }

        var gatesByDoc = new Dictionary<int, List<LdpGateInfo>>();
        if (baseRows.Count > 0)
        {
            using var gatesCommand = new SqlCommand(GatesQuery, connection) { CommandTimeout = 30 };
            gatesCommand.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            gatesCommand.Parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = string.Join(',', baseRows.Select(r => r.Id));
            await using var gatesReader = await gatesCommand.ExecuteReaderAsync(cancellationToken);
            while (await gatesReader.ReadAsync(cancellationToken))
            {
                string? Text(int i) => gatesReader.IsDBNull(i) ? null : gatesReader.GetString(i);
                DateTime? DateVal(int i) => gatesReader.IsDBNull(i) ? null : gatesReader.GetDateTime(i);
                decimal? Dec(int i) => gatesReader.IsDBNull(i) ? null : gatesReader.GetDecimal(i);
                int ldpRegistroId = checked((int)gatesReader.GetInt64(0));
                var gate = new LdpGateInfo(gatesReader.GetByte(1), Text(2), gatesReader.GetBoolean(3), Text(4), Text(5), Text(6),
                    DateVal(7), DateVal(8), Dec(9), Dec(10));
                if (!gatesByDoc.TryGetValue(ldpRegistroId, out var list)) gatesByDoc[ldpRegistroId] = list = new List<LdpGateInfo>();
                list.Add(gate);
            }
        }

        var rows = baseRows.Select(r =>
        {
            var gates = gatesByDoc.TryGetValue(r.Id, out var list) ? list : new List<LdpGateInfo>();
            if (gateFase != null) gates = gates.Where(g => g.Fase == gateFase).ToList();
            return r.Row with { Gates = gates };
        })
        .Where(r => gateFase == null || r.Gates.Count > 0)
        .ToList();

        return new(total, filtered, lastSync, page, pageSize, rows);
    }
}
