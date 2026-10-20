using System.Data;
using Microsoft.Data.SqlClient;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Services.Planning;

// Regra OFICIAL definida 2026-09-22 (substitui a de 2026-09-16 por completo): disciplina do HH/Custo
// REALIZADO nunca mais é inferida via LDP/Cronograma nem via texto livre TimesheetLancamento.Disciplina.
// É resolvida por Project_ID_ACC + Doc3 (3ª parte do N&A Document Number) através da Norma de
// Coordenação vinculada ao PRÓPRIO projeto (CoordinationDisciplineResolver). Sem match (doc3 vazio ou
// sigla não cadastrada na Norma), classifica como SEM DISCIPLINA mas NUNCA descarta HH/custo — toda hora
// do snapshot entra no total. O Previsto (LDP.Planned_HH/Planned_Cost via ProjectDisciplineIndex) é
// outra rota, intocada por esta mudança.
public sealed record ActualEntry(long Id, string? Document, string Discipline, string DisciplineSource,
    decimal? Hh, decimal? CustoInformado, decimal? CustoCalculado, string NormalizedSigla, string? ProfessionalName = null);
public sealed record ActualValues(decimal? Hh, decimal? Cost, decimal? KnownCostSubtotal,
    decimal? KnownCalculatedCostSubtotal, int Entries, int MissingHh, int MissingCost)
{
    // Custo real operacional atual = CustoCalculado (CustoTimesheetInformado/CustoInformado não existe na
    // tabela — ver Services/Reports/PmoExecutiveData.cs). MissingCost acima conta ausência de CustoInformado,
    // que hoje é sempre 100%; este campo conta especificamente ausência de CustoCalculado.
    public int MissingCalculatedCost { get; init; }
}
public sealed record ActualDiscipline(string? Discipline, ActualValues Values);
public sealed record ActualDocument(string Document, ActualValues Values);
public sealed record UnmappedSiglaSummary(string Sigla, int Entries, decimal? Hh);
public sealed record ProjectActualFacts(string ProjectId, ActualValues Total, IReadOnlyList<ActualDiscipline> ByDiscipline,
    IReadOnlyList<ActualDocument> ByDocument, string Source, string CostSource,
    int? CoordinationStandardId, int MatchedExact, int MatchedBySuffix, int Unmatched,
    IReadOnlyList<UnmappedSiglaSummary> UnmappedSiglas, ActualValues SemDocumento)
{
    /// <summary>
    /// Lançamentos com documento que NÃO está na LDP atual. Desde 06/10/2026 isso é DIAGNÓSTICO:
    /// essas horas continuam no HH realizado do projeto, pois o total oficial é o Timesheet atual.
    /// O bucket serve para localizar documento renomeado/removido sem esconder esforço real.
    /// </summary>
    public ActualValues? ForaDaLdp { get; init; }
    public string EscopoHh { get; init; } = "TODOS_OS_LANCAMENTOS";
    public ActualValues? TimesheetOriginal { get; init; }
    public APS.WebApp.Services.Costs.PackageHistoricalFacts? HistoricalMeasurement { get; init; }
}
public interface IPmoActualFactsProvider
{
    Task<ProjectActualFacts> ReadAsync(string projectId, CancellationToken ct);
    /// <summary>HH/custo realizado = TODO o Timesheet atual; documentosLdp só alimenta o diagnóstico ForaDaLdp.</summary>
    Task<ProjectActualFacts> ReadAsync(string projectId, IReadOnlySet<string> documentosLdp, CancellationToken ct);
}

public static class ProjectActualRules
{
    // CustoInformado (gestao.TimesheetLancamento.CustoTimesheetInformado, once migrated) is the real cost
    // informed on the Timesheet lançamento — the canonical cost. CustoCalculado (HorasTrabalhadas x taxa
    // vigente do profissional) is an independent reconciliation figure, never silently substituted when
    // CustoInformado is unavailable — see AUDITORIA_FORGESYNC_NA/PROGRESSO.md, ajuste de 16/09/2026.
    public static ActualValues Aggregate(IEnumerable<ActualEntry> entries)
    {
        var rows = entries.ToArray();
        return new(ProjectPlanningRules.Sum(rows.Select(e => e.Hh)), ProjectPlanningRules.Sum(rows.Select(e => e.CustoInformado)),
            ProjectPlanningRules.Sum(rows.Where(e => e.CustoInformado.HasValue).Select(e => e.CustoInformado)),
            ProjectPlanningRules.Sum(rows.Where(e => e.CustoCalculado.HasValue).Select(e => e.CustoCalculado)),
            rows.Length, rows.Count(e => !e.Hh.HasValue), rows.Count(e => !e.CustoInformado.HasValue))
        { MissingCalculatedCost = rows.Count(e => !e.CustoCalculado.HasValue) };
    }

    /// <summary>
    /// Regra oficial 06/10/2026: HH e custo realizados do PROJETO vêm do Timesheet atual completo.
    /// documentosLdp NÃO recorta o total: serve apenas para separar o bucket ForaDaLdp e permitir corrigir
    /// códigos antigos via De/Para. Por documento, o Timesheet continua agrupado pelo código corrigido.
    /// </summary>
    public static ProjectActualFacts Read(string projectId, IReadOnlyList<ActualEntry> entries, int? coordinationStandardId, IReadOnlySet<string> documentosLdp)
    {
        var baseFacts = Read(projectId, entries, coordinationStandardId);
        var rows = entries.GroupBy(e => e.Id).Select(g => g.Single()).ToArray();
        bool NaLdp(ActualEntry e) => ProjectPlanningRules.Key(e.Document) is var k && k != "" && documentosLdp.Contains(k);
        var fora = rows.Where(e => !NaLdp(e) && ProjectPlanningRules.Key(e.Document) != "").ToArray();
        return baseFacts with
        {
            ForaDaLdp = Aggregate(fora),
            EscopoHh = "TODOS_OS_LANCAMENTOS_COM_DIAGNOSTICO_LDP",
            Source = "gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial (gestao.TimesheetSnapshotAtual + De/Para): HH realizado do projeto = TODOS os lançamentos atuais; ForaDaLdp é diagnóstico de códigos sem correspondência na LDP atual; disciplina por Doc3 via Norma de Coordenação.",
            CostSource = "gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial.CustoCalculado: todos os lançamentos atuais.",
        };
    }

    public static ProjectActualFacts Read(string projectId, IReadOnlyList<ActualEntry> entries, int? coordinationStandardId)
    {
        // One contribution per physical Timesheet entry; never join to professional-rate history for HH,
        // and never let a document lookup duplicate an entry.
        var rows = entries.GroupBy(e => e.Id).Select(g => g.Single()).ToArray();
        var semDisciplina = rows.Where(e => e.DisciplineSource == CoordinationDisciplineSources.SemDisciplina && e.NormalizedSigla != "").ToArray();
        // SEM DOCUMENTO: lançamento sem DocumentoCodigo e sem nenhum Doc1..Doc5 preenchido (ResolveDocumentCode
        // devolveu null). Nunca excluído do Total; exposto como bucket próprio para a reconciliação
        // "SUM(por documento) + SEM DOCUMENTO = Total" nunca ficar às escuras sobre a diferença.
        var semDocumento = rows.Where(e => ProjectPlanningRules.Key(e.Document) == "").ToArray();
        return new(projectId, Aggregate(rows),
            rows.GroupBy(e => e.Discipline).Select(g => new ActualDiscipline(g.Key, Aggregate(g))).ToArray(),
            rows.Where(e => ProjectPlanningRules.Key(e.Document) != "").GroupBy(e => ProjectPlanningRules.Key(e.Document))
                .Select(g => new ActualDocument(g.Key, Aggregate(g))).ToArray(),
            "gestao.TimesheetSnapshotAtual: HorasTrabalhadas/CustoCalculado classificados por Project_ID_ACC + Doc3 via Norma de Coordenação do projeto (nunca por LDP/Cronograma/texto livre). Sem correspondência = SEM DISCIPLINA, nunca excluído do total.",
            "gestao.TimesheetSnapshotAtual.CustoCalculado (CustoTimesheetInformado não existe na tabela)",
            coordinationStandardId,
            rows.Count(e => e.DisciplineSource == CoordinationDisciplineSources.NormaExata),
            rows.Count(e => e.DisciplineSource is CoordinationDisciplineSources.NormaSufixoNumerico or CoordinationDisciplineSources.NormaPrefixo
                or CoordinationDisciplineSources.NormaDoisCaracteres or CoordinationDisciplineSources.NormaConsensoDoisCaracteres),
            rows.Count(e => e.DisciplineSource == CoordinationDisciplineSources.SemDisciplina),
            semDisciplina.GroupBy(e => e.NormalizedSigla)
                .Select(g => new UnmappedSiglaSummary(g.Key, g.Count(), ProjectPlanningRules.Sum(g.Select(e => e.Hh))))
                .OrderByDescending(u => u.Entries).ToArray(),
            Aggregate(semDocumento));
    }
}

public sealed class PmoActualFactsProvider(IConfiguration configuration, CoordinationStandardRepository coordinationRepository) : IPmoActualFactsProvider
{
    public async Task<ProjectActualFacts> ReadAsync(string projectId, IReadOnlySet<string> documentosLdp, CancellationToken ct)
    {
        var (coordinationStandardId, entries) = await LerAsync(projectId, ct);
        return APS.WebApp.Services.Costs.PackageHistoricalRules.Compose(ProjectActualRules.Read(projectId, entries, coordinationStandardId, documentosLdp), await new APS.WebApp.Services.Costs.CostManagementService(configuration).HistoricalMeasurementAsync(projectId, entries, ct));
    }

    public async Task<ProjectActualFacts> ReadAsync(string projectId, CancellationToken ct)
    {
        var (coordinationStandardId, entries) = await LerAsync(projectId, ct);
        return APS.WebApp.Services.Costs.PackageHistoricalRules.Compose(ProjectActualRules.Read(projectId, entries, coordinationStandardId), await new APS.WebApp.Services.Costs.CostManagementService(configuration).HistoricalMeasurementAsync(projectId, entries, ct));
    }

    private async Task<(int? CoordinationStandardId, List<ActualEntry> Entries)> LerAsync(string projectId, CancellationToken ct)
    {
        // Norma de Coordenação do PRÓPRIO projeto — nunca a de outro projeto (regra multi-projeto:
        // "não aplicar a norma de um projeto aos outros"). Projeto sem Norma vinculada devolve mapa
        // vazio: todo lançamento cai em SEM DISCIPLINA, nunca lança exceção.
        var (coordinationStandardId, mapeamentos) = coordinationRepository.ObterMapaDoProjeto(projectId);

        using var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection"));
        await conn.OpenAsync(ct);
        // 30/09/2026: snapshot atual CORRIGIDO — gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial = gestao.TimesheetSnapshotAtual + De/Para
        // ativo (nunca o ledger histórico). Documento = DocumentoCodigo_Corrigido (mesma chave de Actual_HH_Corrigido).
        using var cmd = new SqlCommand("""
            SELECT Id, DocumentoCodigo_Corrigido AS DocumentoCodigo, Doc1, Doc2, Doc3, Doc4, Doc5,
                   HorasTrabalhadas, CustoCalculado, Disciplina, Tipo_Correcao, NomeProfissional
            FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial WHERE Project_ID_ACC=@projectId
            """, conn) { CommandTimeout = 45 };
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        using var reader = await cmd.ExecuteReaderAsync(ct);

        var entries = new List<ActualEntry>();
        while (await reader.ReadAsync(ct))
        {
            string? S(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            var documentoCodigo = S(1);
            var doc1 = S(2); var doc2 = S(3); var doc3 = S(4); var doc4 = S(5); var doc5 = S(6);
            var manualDiscipline = S(9);
            var tipoCorrecao = S(10);
            var isManualAt = string.Equals(tipoCorrecao, CoordinationDisciplineSources.AtManual, StringComparison.OrdinalIgnoreCase);

            var document = isManualAt
                ? null
                : CoordinationDisciplineResolver.ResolveDocumentCode(documentoCodigo, doc1, doc2, doc3, doc4, doc5);
            if(!isManualAt) {var parts=document?.Split('-');doc3=parts is {Length:>=3}?parts[2]:null;}
            var resolution = CoordinationDisciplineResolver.Resolve(doc3, coordinationStandardId, mapeamentos);
            var discipline = isManualAt && !string.IsNullOrWhiteSpace(manualDiscipline)
                ? manualDiscipline!
                : resolution.Discipline ?? CoordinationDisciplineResolver.SemDisciplinaLabel;
            var disciplineSource = isManualAt
                ? CoordinationDisciplineSources.AtManual
                : resolution.Source;

            entries.Add(new(reader.GetInt64(0), document,
                discipline, disciplineSource,
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                null, // CustoInformado: não existe fisicamente na tabela (ver ActualValues.MissingCalculatedCost).
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                CoordinationDisciplineResolver.Normalize(doc3), S(11)));
        }
        return (coordinationStandardId, entries);
    }
}
