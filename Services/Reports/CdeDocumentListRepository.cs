using System.Data;
using APS.WebApp.Models;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

// =============================================================================================
// CDE / LISTA DE DOCUMENTOS — visão de dados da LDP ESPELHO do projeto, lida do BANCO (nunca baixa o
// Excel espelho do ACC). As colunas seguem EXATAMENTE o que cada gerador de espelho publica como
// visível — a definição de colunas mora só aqui (o frontend não tem colunas fixas):
//   Tipo 1: LdpUpdateService.MirrorLdpControlToProjectAsync mantém as colunas 1..15 (A:O) da aba LDP e
//           apaga da 16 em diante → os 15 primeiros campos da LDP Tipo 1 (mesma ordem de Ldp_Unificada).
//   Tipo 2: LdpTipo2EspelhoService.Preencher grava A:W e OCULTA as colunas 10..17 → visíveis: 1..9
//           (item, schedule id, disciplina, N&A, cliente, título, formato, páginas, baseline do gate
//           atual) + 18..20 (status dos gates 30/60/90) + 21..23 (aplicabilidade "X").
//           O gate atual usa a MESMA regra do gerador (LdpTipo2EspelhoService.EscolherGate).
// A configuração do novo motor (ldp.ExcelEspelho*) está vazia hoje; os geradores reais não a usam para
// colunas. Contratos em Tests/.../CdeDocumentListContractTests.cs amarram estas definições aos geradores.
// Somente SELECT, sempre filtrado por Project_ID_ACC = @projectId.
// =============================================================================================

public sealed record CdeDocumentColumn(string Key, string Label, string Type, bool Sortable = true);
public sealed record CdeDocumentListReport(int TipoLdp, IReadOnlyList<CdeDocumentColumn> Columns, long TotalRows, long FilteredRows,
    DateTime? LastSync, int Page, int PageSize, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

public interface ICdeDocumentListRepository
{
    Task<CdeDocumentListReport> ReadAsync(string projectId, string? disciplina, string? search, string? sortKey, bool sortDesc,
        int page, int pageSize, CancellationToken cancellationToken);
}

public static class CdeDocumentListDefinitions
{
    public const string Texto = "texto", Data = "data", Numero = "numero", Marcador = "marcador";

    /// <summary>Espelho Tipo 1 = colunas 1..15 da LDP Tipo 1 (A:O).</summary>
    public static readonly IReadOnlyList<CdeDocumentColumn> Tipo1 = new CdeDocumentColumn[]
    {
        new("item", "Item", Texto),
        new("scheduleId", "Schedule ID", Texto),
        new("discipline", "Disciplina", Texto),
        new("naDocumentNumber", "N&A Document Number", Texto),
        new("clientDocumentNumber", "Client Document Number", Texto),
        new("documentTitle", "Título", Texto),
        new("format", "Formato", Texto),
        new("pages", "Nº Páginas", Numero),
        new("plannedDateBaseline", "Data Planejada (Baseline)", Data),
        new("replannedDate", "Data Reprogramada", Data),
        new("emissionDate", "Data de Emissão", Data),
        new("transmittalNumber", "Transmittal", Texto),
        new("issuedPurpose", "Propósito de Emissão", Texto),
        new("revision", "Revisão", Texto),
        new("documentStatus", "Status do Documento", Texto),
    };

    /// <summary>Espelho Tipo 2 = colunas VISÍVEIS do A:W (1..9 e 18..23; 10..17 ocultas pelo gerador).</summary>
    public static readonly IReadOnlyList<CdeDocumentColumn> Tipo2 = new CdeDocumentColumn[]
    {
        new("item", "Item", Numero),
        new("scheduleId", "Schedule ID", Texto),
        new("discipline", "Disciplina", Texto),
        new("naDocumentNumber", "N&A Document Number", Texto),
        new("clientDocumentNumber", "Client Document Number", Texto),
        new("documentTitle", "Título", Texto),
        new("format", "Formato", Texto),
        new("pages", "Nº Páginas", Numero),
        new("gatePlannedBaseline", "Baseline (gate atual)", Data, false),
        new("gate30Status", "Status 30%", Texto, false),
        new("gate60Status", "Status 60%", Texto, false),
        new("gate90Status", "Status 90%", Texto, false),
        new("gate30Aplica", "Aplica 30%", Marcador, false),
        new("gate60Aplica", "Aplica 60%", Marcador, false),
        new("gate90Aplica", "Aplica 90%", Marcador, false),
    };

    /// <summary>Chaves ordenáveis no servidor → coluna SQL (whitelist; nunca texto do usuário no SQL).</summary>
    public static readonly IReadOnlyDictionary<string, string> Ordenacao = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["item"] = "Sequencia", ["scheduleId"] = "Schedule_ID", ["discipline"] = "Discipline",
        ["naDocumentNumber"] = "NA_Document_Number", ["clientDocumentNumber"] = "Client_Document_Number",
        ["documentTitle"] = "Document_Title", ["format"] = "Format", ["pages"] = "No_Pages",
        ["plannedDateBaseline"] = "Planned_Date_Baseline", ["replannedDate"] = "Replanned_Date",
        ["emissionDate"] = "Emission_Date", ["transmittalNumber"] = "Transmittal_Number",
        ["issuedPurpose"] = "Issued_Purpose", ["revision"] = "Revision", ["documentStatus"] = "Document_Status",
    };

    public static IReadOnlyList<CdeDocumentColumn> Colunas(int tipoLdp) => tipoLdp == 2 ? Tipo2 : Tipo1;

    /// <summary>ORDER BY seguro: só chaves da whitelist que pertencem ao espelho do tipo; senão ordem do espelho.</summary>
    public static string OrderBy(int tipoLdp, string? sortKey, bool desc)
    {
        bool valida = sortKey != null && Ordenacao.TryGetValue(sortKey, out _) && Colunas(tipoLdp).Any(c => c.Key == sortKey);
        string coluna = valida ? Ordenacao[sortKey!] : "Sequencia";
        string direcao = desc && valida ? "DESC" : "ASC";
        // Desempate pela ordem da LDP — nunca repetir Sequencia (SQL Server rejeita coluna duplicada no ORDER BY).
        return coluna == "Sequencia" ? $"Sequencia {direcao}" : $"{coluna} {direcao}, Sequencia ASC";
    }
}

public sealed class CdeDocumentListRepository(IConfiguration configuration) : ICdeDocumentListRepository
{
    public const string TipoQuery = "SELECT TOP 1 Tipo_LDP FROM dbo.Projetos WHERE Project_ID_ACC = @projectId;";

    // Universo = LDP atual do projeto (PmoLdpSnapshot, mesma fonte canônica dos relatórios). Sequencia =
    // ordem da LDP (ID_Registro de inserção), calculada ANTES do filtro — o "Item" do Tipo 2 é essa posição,
    // como o gerador numera (index + 1).
    // DESEMPENHO: PmoLdpSnapshot.Tabela é caro em projetos Tipo 1 grandes (a subconsulta de corte é
    // reavaliada por linha; Ldp_Unificada só tem índice na PK — ~15 s no BML011.016). Por isso a tabela é
    // referenciada UMA única vez: totais, filtrados, última sincronização e paginação saem de funções de
    // janela na mesma varredura. A linha de Sequencia = 1 volta sempre (NaPagina = 0) só para carregar os
    // totais quando o filtro/página não retorna documentos.
    public static string Query(string orderBy) => $"""
        WITH S AS (
            SELECT ID_Registro, Item, Schedule_ID, Discipline, NA_Document_Number, Client_Document_Number, Document_Title, Format,
                   No_Pages, Planned_Date_Baseline, Replanned_Date, Emission_Date, Transmittal_Number, Issued_Purpose, Revision,
                   Document_Status, ROW_NUMBER() OVER (ORDER BY ID_Registro) AS Sequencia,
                   COUNT_BIG(*) OVER () AS TotalRows, MAX(Data_Sincronizacao) OVER () AS LastSync
            FROM {APS.WebApp.Services.Planning.PmoLdpSnapshot.Tabela}
            WHERE {APS.WebApp.Services.Planning.LdpItemRules.SqlIsDocument("LdpAtual")}
        ), F AS (
            SELECT S.*, CASE WHEN (@disciplina IS NULL OR Discipline = @disciplina)
                              AND (@search IS NULL OR NA_Document_Number LIKE @search OR Client_Document_Number LIKE @search OR Document_Title LIKE @search)
                             THEN 1 ELSE 0 END AS Passa
            FROM S
        ), G AS (
            SELECT F.*, CAST(SUM(Passa) OVER () AS BIGINT) AS FilteredRows,
                   ROW_NUMBER() OVER (PARTITION BY Passa ORDER BY {orderBy}) AS Posicao
            FROM F
        )
        SELECT TotalRows, FilteredRows, LastSync,
               CASE WHEN Passa = 1 AND Posicao > @offset AND Posicao <= @offset + @pageSize THEN 1 ELSE 0 END AS NaPagina,
               ID_Registro, Sequencia, Item, Schedule_ID, Discipline, NA_Document_Number, Client_Document_Number, Document_Title, Format,
               No_Pages, Planned_Date_Baseline, Replanned_Date, Emission_Date, Transmittal_Number, Issued_Purpose, Revision, Document_Status
        FROM G
        WHERE (Passa = 1 AND Posicao > @offset AND Posicao <= @offset + @pageSize) OR Sequencia = 1
        ORDER BY NaPagina DESC, Posicao
        {APS.WebApp.Services.Planning.PmoLdpSnapshot.OpcaoPlano};
        """;

    // Gates da página inteira de uma vez (mesma view usada por LdpDetailsReportRepository).
    public const string GatesQuery = """
        SELECT Ldp_Registro_Id, Gate_Percentual, Gate_Ativo, Document_Status, Planned_Baseline
        FROM dbo.VW_Ldp_Documento_Gates_Ultima_Versao
        WHERE Project_ID_ACC = @projectId AND Ldp_Registro_Id IN (SELECT value FROM STRING_SPLIT(@ids, ','));
        """;

    public async Task<CdeDocumentListReport> ReadAsync(string projectId, string? disciplina, string? search, string? sortKey, bool sortDesc,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(cancellationToken);

        int tipoLdp;
        using (var tipoCmd = new SqlCommand(TipoQuery, connection) { CommandTimeout = 30 })
        {
            tipoCmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            var tipo = await tipoCmd.ExecuteScalarAsync(cancellationToken);
            // Mesma regra de PmoLdpSnapshot/roteamento: só Tipo_LDP = 2 é Tipo 2; o resto segue Tipo 1.
            tipoLdp = tipo is not null && tipo != DBNull.Value && Convert.ToInt32(tipo) == 2 ? 2 : 1;
        }

        var linhas = new List<(int Id, Dictionary<string, object?> Valores)>();
        long total = 0, filtered = 0; DateTime? lastSync = null;
        using (var command = new SqlCommand(Query(CdeDocumentListDefinitions.OrderBy(tipoLdp, sortKey, sortDesc)), connection) { CommandTimeout = 60 })
        {
            command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            command.Parameters.Add("@disciplina", SqlDbType.NVarChar, 100).Value = (object?)disciplina ?? DBNull.Value;
            command.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";
            command.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
            command.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                total = r.GetInt64(0); filtered = r.GetInt64(1); lastSync = r.IsDBNull(2) ? null : r.GetDateTime(2);
                if (r.GetInt32(3) == 0) continue; // linha só de totais (fora da página/filtro)
                object? V(int i) => r.IsDBNull(i) ? null : r.GetValue(i);
                var v = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    // Tipo 1 publica o ITEM da própria LDP; Tipo 2 numera sequencialmente (gerador: index + 1).
                    ["item"] = tipoLdp == 2 ? r.GetInt64(5) : V(6),
                    ["scheduleId"] = V(7), ["discipline"] = V(8), ["naDocumentNumber"] = V(9), ["clientDocumentNumber"] = V(10),
                    ["documentTitle"] = V(11), ["format"] = V(12), ["pages"] = V(13),
                };
                if (tipoLdp == 1)
                {
                    v["plannedDateBaseline"] = V(14); v["replannedDate"] = V(15); v["emissionDate"] = V(16);
                    v["transmittalNumber"] = V(17); v["issuedPurpose"] = V(18); v["revision"] = V(19); v["documentStatus"] = V(20);
                }
                linhas.Add((r.GetInt32(4), v));
            }
        }

        if (tipoLdp == 2 && linhas.Count > 0)
        {
            var gates = new Dictionary<long, List<LdpTipo2Gate>>();
            using var gatesCmd = new SqlCommand(GatesQuery, connection) { CommandTimeout = 30 };
            gatesCmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            gatesCmd.Parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = string.Join(",", linhas.Select(l => l.Id));
            await using var g = await gatesCmd.ExecuteReaderAsync(cancellationToken);
            while (await g.ReadAsync(cancellationToken))
            {
                var gate = new LdpTipo2Gate
                {
                    LdpRegistroId = g.GetInt64(0), GatePercentual = g.GetByte(1), GateAtivo = g.GetBoolean(2),
                    DocumentStatus = g.IsDBNull(3) ? null! : g.GetString(3), PlannedBaseline = g.IsDBNull(4) ? null : g.GetDateTime(4),
                };
                if (!gates.TryGetValue(gate.LdpRegistroId!.Value, out var lista)) gates[gate.LdpRegistroId.Value] = lista = new();
                lista.Add(gate);
            }
            foreach (var (id, v) in linhas)
                PreencherGatesTipo2(v, gates.TryGetValue(id, out var lista) ? lista : new List<LdpTipo2Gate>());
        }

        return new(tipoLdp, CdeDocumentListDefinitions.Colunas(tipoLdp), total, filtered, lastSync, page, pageSize,
            linhas.Select(l => (IReadOnlyDictionary<string, object?>)l.Valores).ToList());
    }

    /// <summary>Mesma regra de LdpTipo2EspelhoService.Preencher para as colunas 9 e 18..23.</summary>
    internal static void PreencherGatesTipo2(Dictionary<string, object?> v, IReadOnlyList<LdpTipo2Gate> gates)
    {
        v["gatePlannedBaseline"] = LdpTipo2EspelhoService.EscolherGate(gates)?.PlannedBaseline;
        foreach (byte percentual in new byte[] { 30, 60, 90 })
        {
            var gate = gates.SingleOrDefault(x => x.GatePercentual == percentual);
            v[$"gate{percentual}Status"] = gate?.DocumentStatus;
            v[$"gate{percentual}Aplica"] = gate?.GateAtivo == true ? "X" : null;
        }
    }
}
