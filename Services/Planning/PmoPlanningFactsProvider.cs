using System.Data;
using System.Text.Json;
using APS.WebApp.Services.CronogramaTipo2;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Planning;

public interface IPmoPlanningFactsProvider
{
    Task<ProjectPlanningFacts> ReadAsync(string projectId, CancellationToken ct);
}

public sealed class PmoPlanningFactsProvider(IConfiguration configuration) : IPmoPlanningFactsProvider
{
    public const string CurrentTipo1Predicate = """
        AND (Data_Leitura_Cronograma=(SELECT MAX(Data_Leitura_Cronograma) FROM dbo.Cronograma_Projetos
             WHERE Project_ID_ACC=@projectId AND Tipo_LDP=1)
          OR NOT EXISTS(SELECT 1 FROM dbo.Cronograma_Projetos WHERE Project_ID_ACC=@projectId AND Tipo_LDP=1
             AND Data_Leitura_Cronograma IS NOT NULL))
        """;
    /// <summary>Statement da LDP atual (2º do batch). Hint de plano: ver PmoLdpSnapshot.OpcaoPlano.</summary>
    public static readonly string LdpSnapshotSql =
        $"SELECT ID_Registro,NA_Document_Number,Client_Document_Number,Discipline,Planned_Cost,Planned_HH " +
        $"FROM {PmoLdpSnapshot.Tabela} WHERE Project_ID_ACC=@projectId {PmoLdpSnapshot.OpcaoPlano};";

    public async Task<ProjectPlanningFacts> ReadAsync(string projectId, CancellationToken ct)
    {
        using var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection"));
        await conn.OpenAsync(ct);
        using var typeCommand = new SqlCommand("SELECT Tipo_LDP FROM dbo.Projetos WHERE Project_ID_ACC=@projectId AND Is_Ativo=1", conn);
        typeCommand.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        var tipo = Convert.ToInt32(await typeCommand.ExecuteScalarAsync(ct));
        if (tipo is not (1 or 2)) throw new InvalidOperationException("Tipo_LDP indisponível.");
        var source = tipo == 1 ? "dbo.Cronograma_Projetos" : "dbo.VW_Cronograma_Tipo2_Ultima_Versao";
        using var cmd = new SqlCommand($"""
            SELECT ID_Cronograma,UID_Tarefa,EDT_WBS,Eh_Tarefa_Resumo,Disciplina_Cronograma,Trabalho,
                   NA_DOCUMENT_NUMBER,Campos_Customizados_JSON
            FROM {source} WHERE Project_ID_ACC=@projectId AND Tipo_LDP=@tipo
            {(tipo == 1 ? CurrentTipo1Predicate : "")};
            {LdpSnapshotSql}
            """, conn) { CommandTimeout = 45 };
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        cmd.Parameters.Add("@tipo", SqlDbType.Int).Value = tipo;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var tasks = new List<PlanningTask>();
        while (await reader.ReadAsync(ct))
        {
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            decimal? work = reader.IsDBNull(5) ? null : reader.GetDecimal(5);
            Tipo1AnalyticsMetadata? metadata = null;
            if (Text(7) is { } json)
            {
                try
                {
                    using var parsed = JsonDocument.Parse(json);
                    if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (tipo == 1 && parsed.RootElement.TryGetProperty(CronogramaTipo1Analytics.MetadataKey, out var meta))
                            metadata = meta.Deserialize<Tipo1AnalyticsMetadata>();
                        // This task's persisted Work, never the fallback TotalHoras of the snapshot.
                        if (tipo == 2 && !work.HasValue && parsed.RootElement.TryGetProperty(CronogramaTipo2Work.ChaveSnapshot, out var snap)
                            && snap.ValueKind == JsonValueKind.String)
                            work = JsonSerializer.Deserialize<CronogramaTipo2Work.Snapshot>(snap.GetString()!)?.ItemHoras;
                    }
                }
                catch (JsonException) { /* Missing evidence remains unavailable. */ }
            }
            tasks.Add(new(reader.GetInt32(0), Text(1)!, Text(2), !reader.IsDBNull(3) && reader.GetBoolean(3),
                Text(4), work is >= 0 ? work : null, Text(6), metadata));
        }
        await reader.NextResultAsync(ct);
        var docs = new List<LdpPlanRow>();
        // Colunas por NOME (nunca por posição): a leitura por índice fixo já causou IndexOutOfRange quando Planned_HH foi acrescentado à query.
        int oCode = reader.GetOrdinal("NA_Document_Number"), oClient = reader.GetOrdinal("Client_Document_Number"), oDisc = reader.GetOrdinal("Discipline"),
            oCost = reader.GetOrdinal("Planned_Cost"), oHh = reader.GetOrdinal("Planned_HH");
        while (await reader.ReadAsync(ct)) docs.Add(new(reader.IsDBNull(oCode) ? null : reader.GetString(oCode), reader.IsDBNull(oClient) ? null : reader.GetString(oClient),
            reader.IsDBNull(oDisc) ? null : reader.GetString(oDisc), reader.IsDBNull(oHh) ? null : reader.GetDecimal(oHh), reader.IsDBNull(oCost) ? null : reader.GetDecimal(oCost)));
        // Mesmo resolver de disciplina homologado (LDP N&A > LDP Cliente > Cronograma); o Cronograma entra só como fallback de disciplina.
        var index = ProjectDisciplineIndex.Build(
            docs.Where(d => ProjectPlanningRules.Key(d.Code) != "").Select(d => (d.Code, d.Discipline)),
            docs.Where(d => d.Client != null).Select(d => (d.Client, d.Discipline)),
            tasks.Where(t => ProjectPlanningRules.Key(t.Document) != "").Select(t => ((string?)t.Document, (string?)t.Discipline)));
        // O Cronograma segue lido (somente leitura) para disciplina/diagnóstico; o HH previsto OFICIAL vem da LDP (LdpPlanningRules).
        string? ResolveDiscipline(PlanningTask t)
        {
            var propria = ProjectPlanningRules.Key(t.Discipline);
            if (propria != "") return propria;
            var doc = ProjectPlanningRules.Key(t.Document);
            return doc == "" ? null : index.Resolve(t.Document).DisciplineKey;
        }
        var cronograma = ProjectPlanningRules.Read(projectId, tipo, tasks, source, ResolveDiscipline);
        // 15/10/2026: disciplina do previsto = 2 primeiros caracteres do DOC3 via Norma do projeto (mesma rota do realizado).
        var doc3 = await NA_Connect.Services.Coordination.ProjectDoc3Disciplines.LoadAsync(configuration, projectId, ct);
        return LdpPlanningRules.Apply(cronograma, docs, index, doc3.Canonical);
    }
}
