using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using APS.WebApp.Services.Costs;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

/// <summary>
/// Filtros dos Lançamentos. Exact=true compara igualdade (drilldown); senão "contém". Status vale só para documentos
/// (ESTOURADO | DENTRO DO PLANEJADO | SEM HH PLANEJADA). Comparação de texto sem acento/caixa.
/// </summary>
public sealed record ProductionAuditFilters(DateTime? From, DateTime? To, string? Discipline, string? Document, string? Person, int Page = 1, int PageSize = 50,
    string? Status = null, bool Exact = false);

/// <summary>
/// Pessoa. PlannedHours = HH planejada RATEADA: o planejado de cada documento (único por documento na fonte) é dividido
/// entre as pessoas na proporção das HH realizadas por cada uma no documento. A fonte não tem planejado por pessoa.
/// </summary>
public sealed record ProductionAuditPerson(string Name, decimal PlannedHours, decimal WorkedHours, decimal Difference, int Entries, int Documents, DateTime? LastEntry);
public sealed record ProductionAuditPeoplePage(IReadOnlyList<ProductionAuditPerson> Items, int Total, string CrOs, decimal TotalPlannedHours, decimal TotalWorkedHours);

/// <summary>
/// Disciplina efetiva: a do DOCUMENTO (mesma em todas as linhas dele); lançamento sem documento usa a da própria linha.
/// PlannedHours = planejado total dos documentos da disciplina presentes no filtro; WorkedHours = realizado no filtro.
/// </summary>
public sealed record ProductionAuditDisciplineRow(string Discipline, decimal PlannedHours, decimal WorkedHours, decimal Difference, decimal? ConsumedPercent,
    int Documents, int People, int Entries, int OverrunDocuments);
public sealed record ProductionAuditDisciplinesResult(IReadOnlyList<ProductionAuditDisciplineRow> Items, string CrOs);

/// <summary>
/// Documento. Planejado/realizado TOTAL/diferença/excesso/%/status são do documento inteiro (não dependem do filtro);
/// WorkedHours = realizado dentro do filtro (período/pessoa).
/// </summary>
public sealed record ProductionAuditDocumentRow(string DocumentCode, string? Document, string Discipline, decimal? PlannedHours, decimal WorkedHours, decimal TotalWorkedHours,
    decimal Difference, decimal Excess, decimal? ConsumedPercent, string Status, int People, int Entries);
public sealed record ProductionAuditDocumentsPage(IReadOnlyList<ProductionAuditDocumentRow> Items, int Total, string CrOs, decimal TotalPlannedHours, decimal TotalWorkedHours,
    decimal WorkedHoursWithoutDocument);

public static class ProductionAuditStatus
{
    public const string Estourado = "ESTOURADO", Dentro = "DENTRO DO PLANEJADO", SemPlanejado = "SEM HH PLANEJADA", NaoMapeado = "NÃO MAPEADO", SemNome = "SEM NOME";
    public static readonly IReadOnlySet<string> Todos = new HashSet<string>(StringComparer.Ordinal) { Estourado, Dentro, SemPlanejado };
}
public sealed record ProductionAuditEntry(DateTime? Date, string? Name, decimal? PlannedHours, decimal WorkedHours, string? DocumentCode, string? Document, string? Description, string? Discipline);
public sealed record ProductionAuditPage(IReadOnlyList<ProductionAuditEntry> Items, int Total, string CrOs);
public sealed record ProductionAuditOverrun(string DocumentCode, string? Discipline, decimal? PlannedHours, decimal WorkedHours, decimal Difference, decimal? ConsumedPercent, decimal Excess, string Status);
public sealed record ProductionAuditDiscipline(string Discipline, decimal PlannedHours, decimal WorkedHours, decimal Difference, decimal? ConsumedPercent, int OverrunDocuments);
public sealed record ProductionAuditSource(string Name, decimal Hours, DateTime? UpdatedAt);
public sealed record ProductionAuditReconciliation(string DocumentCode, string? Discipline, decimal? LdpPlanned, decimal? TimesheetPlanned, decimal LdpActual, decimal TimesheetActual, decimal Difference, string Status, string Reason);
/// <summary>
/// Auditoria LDP × Timesheet. Difference = LDP − TIMESHEET (total e por documento). Tolerance = a MESMA tolerância usada
/// para conciliar (o frontend usa este valor no filtro "somente diferença de horas"). Horas sem código de documento ficam
/// FORA dos totais e da tabela (não há como casar documento) e são informadas à parte.
/// </summary>
public sealed record ProductionAuditOverview(string CrOs, IReadOnlyList<ProductionAuditSource> Sources, IReadOnlyList<ProductionAuditReconciliation> Documents, decimal Difference,
    decimal Tolerance = ProductionAuditReconciliationRules.Tolerancia, decimal LdpHoursWithoutDocument = 0, decimal TimesheetHoursWithoutDocument = 0);

public sealed record ProductionAuditUnassignedTimesheetRow(
    string RowKey,
    DateTime? Date,
    string? Name,
    string? Week,
    decimal Hours,
    string? Afe,
    string? Description,
    string Project,
    string? Function,
    string? Discipline,
    decimal? HourlyCost,
    decimal? Cost,
    string? CostSource,
    string? AssignedDiscipline,
    bool IsPackage,
    string TreatmentStatus,
    string? CostStatus = null);

public sealed record ProductionAuditUnassignedTimesheetOverview(
    string ProjectCode,
    decimal TotalBiHours,
    decimal HoursWithDocumentCode,
    decimal HoursWithoutDocumentCode,
    decimal CostWithoutDocumentCode,
    int RowsWithoutCost,
    int TotalPositiveRows,
    int RowsWithDocumentCode,
    int RowsWithoutDocumentCode,
    bool ScopeAmbiguous,
    string? ScopeMessage,
    IReadOnlyList<ProductionAuditUnassignedTimesheetRow> Items);

public sealed record ProductionAuditUnassignedTreatmentInput(
    IReadOnlyList<string> RowKeys,
    string? Discipline,
    bool? IsPackage,
    string? TreatmentType);

public sealed record ProductionAuditUnassignedTreatmentResult(
    int Updated,
    string TreatmentType,
    decimal Hours,
    decimal Cost,
    int RowsWithoutCost);

public static class ProductionAuditTimesheetRoutingModes
{
    public const string Project = "PROJECT";
    public const string ProjectAfe = "PROJECT_AFE";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Project, ProjectAfe };
}

public sealed record ProductionAuditTimesheetRoutingCandidate(string Afe, int Rows, decimal Hours);
public sealed record ProductionAuditTimesheetRoutingConfig(
    string ProjectCode,
    string BiProjectCode,
    string RoutingMode,
    string? AfeCode,
    string? SuggestedAfe,
    bool IsConfigured,
    bool RequiresConfiguration,
    IReadOnlyList<ProductionAuditTimesheetRoutingCandidate> Candidates);
public sealed record ProductionAuditTimesheetRoutingInput(string RoutingMode, string? AfeCode);

public sealed record ProductionExperimentalSummary(
    string ProjectCode,
    decimal TotalHours,
    decimal DocumentHours,
    decimal UnassignedHours,
    decimal ConfirmedAtHours,
    decimal PendingUnassignedHours,
    decimal PackageMarkedHours,
    decimal KnownCost,
    decimal DocumentCost,
    decimal UnassignedCost,
    decimal MissingCostHours,
    int MissingCostRows,
    bool ScopeAmbiguous,
    string? ScopeMessage,
    decimal ExcludedHours = 0m,
    decimal ExcludedKnownCost = 0m,
    int ExcludedRows = 0,
    decimal? AllocatedEngineeringHours = null,
    decimal? AllocatedEngineeringKnownCost = null,
    decimal? AllocatedTotalHours = null,
    decimal? AllocatedExcludedHours = null,
    decimal? AllocatedExcludedKnownCost = null,
    decimal? AllocatedMissingCostHours = null,
    int AllocatedRows = 0);

/// <summary>Totais de um documento numa fonte (LDP ou Timesheet), já agrupado por código normalizado (trim/upper).</summary>
public sealed record ProductionAuditSourceDocument(string? Discipline, decimal Planned, decimal Actual, DateTime? UpdatedAt);

/// <summary>
/// REGRA ÚNICA da conciliação LDP × Timesheet (pura, testável).
///  • HH LDP = SUM(Actual_HH) — o valor REAL da LDP. Actual_HH_Total_Corrigido NÃO é usado aqui: a auditoria existe para
///    expor a diferença que a correção esconderia.
///  • HH Timesheet = SUM(Trabalhadas).
///  • Diferença = LDP − TIMESHEET.
///  • Situação: só na LDP / só no Timesheet (presença na fonte) → HH realizada diferente (|diferença| &gt; tolerância) →
///    HH planejada diferente entre fontes → conciliado. Nenhum match é inventado.
/// </summary>
public static class ProductionAuditReconciliationRules
{
    public const decimal Tolerancia = 0.005m;
    public const string Conciliado = "CONCILIADO", RealizadaDiferente = "HH REALIZADA DIFERENTE", SomenteTimesheet = "DOCUMENTO SOMENTE NO TIMESHEET",
        SomenteLdp = "DOCUMENTO SOMENTE NA LDP", PlanejadaDiferente = "HH PLANEJADA DIFERENTE ENTRE FONTES";
    public const string NomeLdp = "HH Trabalhado LDP", NomeTimesheet = "Horas Trabalhadas Timesheet";

    public static ProductionAuditReconciliation Documento(string code, ProductionAuditSourceDocument? ldp, ProductionAuditSourceDocument? ts)
    {
        var la = ldp?.Actual ?? 0m; var ta = ts?.Actual ?? 0m; var lp = ldp?.Planned ?? 0m; var tp = ts?.Planned ?? 0m;
        var diff = la - ta;
        var status = ldp is null ? SomenteTimesheet : ts is null ? SomenteLdp : Math.Abs(diff) > Tolerancia ? RealizadaDiferente
            : Math.Abs(tp - lp) > Tolerancia ? PlanejadaDiferente : Conciliado;
        // Diagnóstico com os números das fontes — nunca uma causa que os dados não comprovam.
        var reason = status switch
        {
            Conciliado => "Valores compatíveis após normalização trim/upper.",
            SomenteTimesheet => $"Código não encontrado na LDP. Timesheet={ta:0.##}.",
            SomenteLdp => $"Código sem lançamentos no Timesheet. LDP={la:0.##}.",
            PlanejadaDiferente => $"HH trabalhadas iguais; planejado LDP={lp:0.##}; Timesheet={tp:0.##}.",
            _ => $"LDP={la:0.##}; Timesheet={ta:0.##}.",
        };
        return new(code, ts?.Discipline ?? ldp?.Discipline, ldp is null || lp == 0 ? null : lp, ts is null || tp == 0 ? null : tp, la, ta, diff, status, reason);
    }

    public static ProductionAuditOverview Conciliar(string crOs, IReadOnlyDictionary<string, ProductionAuditSourceDocument> ldp,
        IReadOnlyDictionary<string, ProductionAuditSourceDocument> ts, decimal ldpSemDocumento = 0, decimal tsSemDocumento = 0)
    {
        var docs = ldp.Keys.Union(ts.Keys).OrderBy(x => x, StringComparer.Ordinal)
            .Select(code => Documento(code, ldp.TryGetValue(code, out var l) ? l : null, ts.TryGetValue(code, out var t) ? t : null)).ToList();
        var ldpTotal = ldp.Values.Sum(x => x.Actual); var tsTotal = ts.Values.Sum(x => x.Actual);
        return new(crOs,
            new[] { new ProductionAuditSource(NomeLdp, ldpTotal, ldp.Values.Max(x => x.UpdatedAt)), new ProductionAuditSource(NomeTimesheet, tsTotal, ts.Values.Max(x => x.UpdatedAt)) },
            docs, ldpTotal - tsTotal, Tolerancia, ldpSemDocumento, tsSemDocumento);
    }
}

public enum ProductionAuditScopeStatus { Ok, CrNotConfigured, CrAmbiguous, NotFound }
public sealed record ProductionAuditScope(ProductionAuditScopeStatus Status, string? CrOs, string? Message);

public interface IProductionAuditRepository
{
    Task<ProductionAuditScope> ResolveScopeAsync(string projectId, CancellationToken ct);
    Task<ProductionAuditPage> ReadEntriesAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<IReadOnlyList<ProductionAuditOverrun>> ReadDocumentOverrunsAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<IReadOnlyList<ProductionAuditDiscipline>> ReadDisciplineOverrunsAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<ProductionAuditOverview> ReadReconciliationAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<ProductionAuditPeoplePage> ReadPeopleAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<ProductionAuditDisciplinesResult> ReadDisciplinesAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<ProductionAuditDocumentsPage> ReadDocumentsAsync(string projectId, ProductionAuditFilters filters, CancellationToken ct);
    Task<ProductionAuditUnassignedTimesheetOverview> ReadUnassignedTimesheetAsync(string projectId, CancellationToken ct);
    Task<ProductionAuditUnassignedTreatmentResult> SaveUnassignedTimesheetTreatmentAsync(
        string projectId, ProductionAuditUnassignedTreatmentInput input, string? alteredBy, CancellationToken ct);
    Task<ProductionAuditTimesheetRoutingConfig> ReadTimesheetRoutingConfigAsync(string projectId, CancellationToken ct);
    Task<ProductionAuditTimesheetRoutingConfig> SaveTimesheetRoutingConfigAsync(
        string projectId, ProductionAuditTimesheetRoutingInput input, string? alteredBy, CancellationToken ct);
    Task<ProductionExperimentalSummary> ReadExperimentalSummaryAsync(string projectId, CancellationToken ct);
    Task<AuditDifferenceResult> ReadAuditDifferencesAsync(string projectId, CancellationToken ct);
    Task<OutsideLdpOverview> ReadOutsideLdpAsync(string projectId, CancellationToken ct);
    Task<OutsideLdpDirectionResult> SaveOutsideLdpDirectionAsync(string projectId, OutsideLdpDirectionInput input, string? actor, CancellationToken ct);
    Task<EngineeringProfessionalOverview> ReadEngineeringProfessionalScopeAsync(string projectId, CancellationToken ct);
    Task<bool> SaveEngineeringProfessionalScopeAsync(string projectId, EngineeringProfessionalScopeInput input, string? alteredBy, CancellationToken ct);
}

/// <summary>Leitura SQL da auditoria. A identidade do Timesheet é Project_ID_ACC + CR_OS canônico; nunca nome visual/hub.</summary>
public sealed partial class ProductionAuditRepository(IConfiguration config) : IProductionAuditRepository
{
    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(config.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("Conexão local indisponível."));
        await c.OpenAsync(ct); return c;
    }

    private async Task<SqlConnection> OpenBiAsync(CancellationToken ct)
    {
        var c = new SqlConnection(config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Conexão do BI indisponível."));
        await c.OpenAsync(ct); return c;
    }

    public async Task<ProductionAuditScope> ResolveScopeAsync(string projectId, CancellationToken ct)
    {
        const string sql = """
            SELECT P.CR_OS, M.Status_Mapeamento
            FROM dbo.Projetos P
            LEFT JOIN dbo.VW_CR_OS_Project_Map M ON M.CR_OS=P.CR_OS
            WHERE P.Project_ID_ACC=@projectId AND P.Is_Ativo=1;
            """;
        await using var c = await OpenAsync(ct); await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new(ProductionAuditScopeStatus.NotFound, null, "Projeto não encontrado ou inativo.");
        var cr = r.IsDBNull(0) ? null : r.GetString(0).Trim();
        if (string.IsNullOrWhiteSpace(cr)) return new(ProductionAuditScopeStatus.CrNotConfigured, null, "CR/OS não configurado para este projeto.");
        var map = r.IsDBNull(1) ? "" : r.GetString(1);
        if (string.Equals(map, "AMBIGUO", StringComparison.OrdinalIgnoreCase)) return new(ProductionAuditScopeStatus.CrAmbiguous, cr, "CR/OS ambíguo no cadastro de projetos.");
        if (map is not ("UNICO" or "ATIVO_UNICO")) return new(ProductionAuditScopeStatus.CrAmbiguous, cr, "CR/OS sem mapeamento canônico único.");
        return new(ProductionAuditScopeStatus.Ok, cr, null);
    }

    /// <summary>
    /// Base ÚNICA dos Lançamentos → #F (linhas filtradas, já com o contexto do documento).
    ///  • HH_Previsto_Corrigido vem numa ÚNICA linha por documento: o planejado do documento é somado sobre TODAS as
    ///    linhas dele ANTES dos filtros (filtro de pessoa/período nunca descarta o planejado).
    ///  • Disciplina efetiva = a do documento (ignorando NÃO MAPEADO se houver outra); sem documento = a da linha.
    /// </summary>
    internal const string BaseSql = """
        ;WITH B AS (
          SELECT V.data, NULLIF(LTRIM(RTRIM(V.Nome)),'') Nome, ISNULL(V.HH_Previsto_Corrigido,0) Prev, ISNULL(V.Trabalhadas,0) Trab,
                 V.HH_Previsto_Corrigido PrevLinha, NULLIF(UPPER(LTRIM(RTRIM(V.DocumentoCodigo))),'') Codigo, V.DocumentoCodigo CodigoOriginal,
                 V.Documento, V.Descricao,
                 COALESCE(NULLIF(LTRIM(RTRIM(V.Disciplina_PT_Padrao)),''),NULLIF(LTRIM(RTRIM(V.Disciplina)),''),N'NÃO MAPEADO') DiscLinha
          FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto V
          WHERE V.Project_ID_ACC_Canonico=@projectId AND V.CR_OS_Canonico=@cr AND V.Status_Mapeamento_CR_OS IN ('UNICO','ATIVO_UNICO')),
        D AS (
          SELECT Codigo, SUM(Prev) PrevDoc, SUM(Trab) TrabDoc,
                 COALESCE(MAX(NULLIF(DiscLinha,N'NÃO MAPEADO')),N'NÃO MAPEADO') DiscDoc, MAX(NULLIF(LTRIM(RTRIM(Documento)),'')) NomeDoc
          FROM B WHERE Codigo IS NOT NULL GROUP BY Codigo)
        SELECT B.data, B.Nome, B.Trab, B.PrevLinha, B.Codigo, B.CodigoOriginal, B.Documento, B.Descricao,
               COALESCE(D.DiscDoc, B.DiscLinha) Disc, D.PrevDoc, D.TrabDoc, D.NomeDoc
        INTO #F
        FROM B LEFT JOIN D ON D.Codigo = B.Codigo
        WHERE (@from IS NULL OR B.data>=@from) AND (@to IS NULL OR B.data<DATEADD(day,1,@to))
          AND (@discipline IS NULL
               OR (@exact=1 AND COALESCE(D.DiscDoc, B.DiscLinha) COLLATE Latin1_General_CI_AI = @discipline)
               OR (@exact=0 AND COALESCE(D.DiscDoc, B.DiscLinha) COLLATE Latin1_General_CI_AI LIKE N'%' + @discipline + N'%'))
          AND (@document IS NULL
               OR (@exact=1 AND B.Codigo = UPPER(@document))
               OR (@exact=0 AND (B.Codigo LIKE N'%' + UPPER(@document) + N'%' OR COALESCE(B.Documento,'') COLLATE Latin1_General_CI_AI LIKE N'%' + @document + N'%')))
          AND (@person IS NULL
               OR (@exact=1 AND COALESCE(B.Nome,N'SEM NOME') COLLATE Latin1_General_CI_AI = @person)
               OR (@exact=0 AND COALESCE(B.Nome,'') COLLATE Latin1_General_CI_AI LIKE N'%' + @person + N'%'));
        """;

    public async Task<ProductionAuditPage> ReadEntriesAsync(string projectId, ProductionAuditFilters f, CancellationToken ct)
    {
        var scope = await RequireScope(projectId, ct);
        const string sql = BaseSql + """
            SELECT COUNT(1) FROM #F;
            SELECT data,Nome,PrevLinha,Trab,CodigoOriginal,Documento,Descricao,Disc FROM #F
            ORDER BY data DESC, Codigo OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var c = await OpenAsync(ct); await using var cmd = New(cmd: sql, c, projectId, scope.CrOs!, f); Paginar(cmd, f);
        await using var r = await cmd.ExecuteReaderAsync(ct); await r.ReadAsync(ct); var total = r.GetInt32(0); await r.NextResultAsync(ct);
        var rows = new List<ProductionAuditEntry>();
        while (await r.ReadAsync(ct)) rows.Add(new(r.IsDBNull(0) ? null : r.GetDateTime(0), Text(r,1), Num(r,2), Num(r,3) ?? 0m, Text(r,4), Text(r,5), Text(r,6), Text(r,7)));
        return new(rows, total, scope.CrOs!);
    }

    /// <summary>Compatibilidade: mesma agregação de ReadDocumentsAsync (todos os filtros valem), sem paginação.</summary>
    public async Task<IReadOnlyList<ProductionAuditOverrun>> ReadDocumentOverrunsAsync(string projectId, ProductionAuditFilters f, CancellationToken ct)
    {
        var page = await ReadDocumentsAsync(projectId, f with { Page = 1, PageSize = int.MaxValue }, ct);
        return page.Items.Select(d => new ProductionAuditOverrun(d.DocumentCode, d.Discipline, d.PlannedHours, d.TotalWorkedHours, d.Difference, d.ConsumedPercent, d.Excess, d.Status)).ToList();
    }

    /// <summary>Compatibilidade: projeção de ReadDisciplinesAsync — os filtros (período/pessoa/documento/disciplina) agora valem.</summary>
    public async Task<IReadOnlyList<ProductionAuditDiscipline>> ReadDisciplineOverrunsAsync(string projectId, ProductionAuditFilters f, CancellationToken ct) =>
        (await ReadDisciplinesAsync(projectId, f, ct)).Items
            .Select(d => new ProductionAuditDiscipline(d.Discipline, d.PlannedHours, d.WorkedHours, d.Difference, d.ConsumedPercent, d.OverrunDocuments)).ToList();

    public async Task<ProductionAuditPeoplePage> ReadPeopleAsync(string projectId, ProductionAuditFilters f, CancellationToken ct)
    {
        var scope = await RequireScope(projectId, ct);
        const string sql = BaseSql + """
            SELECT COALESCE(Nome,N'SEM NOME') Nome, SUM(Trab) Trab,
                   SUM(CASE WHEN TrabDoc > 0 THEN CAST(PrevDoc AS decimal(19,6)) * CAST(Trab AS decimal(19,6)) / CAST(TrabDoc AS decimal(19,6)) ELSE 0 END) PrevRateado,
                   COUNT(1) Lancamentos, COUNT(DISTINCT Codigo) Documentos, MAX(data) Ultima
            INTO #P FROM #F GROUP BY COALESCE(Nome,N'SEM NOME');
            SELECT COUNT(1), ISNULL(SUM(PrevRateado),0), ISNULL(SUM(Trab),0) FROM #P;
            SELECT Nome, PrevRateado, Trab, Lancamentos, Documentos, Ultima FROM #P
            ORDER BY Trab DESC, Nome OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var c = await OpenAsync(ct); await using var cmd = New(sql, c, projectId, scope.CrOs!, f); Paginar(cmd, f);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct); var total = r.GetInt32(0); var plan = Num(r, 1) ?? 0; var work = Num(r, 2) ?? 0; await r.NextResultAsync(ct);
        var list = new List<ProductionAuditPerson>();
        while (await r.ReadAsync(ct))
        {
            var planned = decimal.Round(Num(r, 1) ?? 0, 2); var worked = Num(r, 2) ?? 0;
            list.Add(new(Text(r, 0) ?? ProductionAuditStatus.SemNome, planned, worked, worked - planned, r.GetInt32(3), r.GetInt32(4), Date(r, 5)));
        }
        return new(list, total, scope.CrOs!, decimal.Round(plan, 2), work);
    }

    public async Task<ProductionAuditDisciplinesResult> ReadDisciplinesAsync(string projectId, ProductionAuditFilters f, CancellationToken ct)
    {
        var scope = await RequireScope(projectId, ct);
        const string sql = BaseSql + """
            SELECT A.Disc, ISNULL(P.Prev,0) Prev, A.Trab, A.Docs, A.Pessoas, A.Lanc, ISNULL(P.Estourados,0) Estourados
            FROM (SELECT Disc, SUM(Trab) Trab, COUNT(DISTINCT Codigo) Docs, COUNT(DISTINCT COALESCE(Nome,N'SEM NOME')) Pessoas, COUNT(1) Lanc FROM #F GROUP BY Disc) A
            LEFT JOIN (
              SELECT Disc, SUM(PrevDoc) Prev, SUM(CASE WHEN PrevDoc > 0 AND TrabDoc > PrevDoc THEN 1 ELSE 0 END) Estourados
              FROM (SELECT DISTINCT Disc, Codigo, PrevDoc, TrabDoc FROM #F WHERE Codigo IS NOT NULL) X GROUP BY Disc) P ON P.Disc = A.Disc
            ORDER BY A.Trab DESC, A.Disc;
            """;
        await using var c = await OpenAsync(ct); await using var cmd = New(sql, c, projectId, scope.CrOs!, f);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ProductionAuditDisciplineRow>();
        while (await r.ReadAsync(ct))
        {
            var planned = Num(r, 1) ?? 0; var worked = Num(r, 2) ?? 0;
            list.Add(new(Text(r, 0) ?? ProductionAuditStatus.NaoMapeado, planned, worked, worked - planned, planned > 0 ? worked * 100m / planned : null,
                r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
        }
        return new(list, scope.CrOs!);
    }

    public async Task<ProductionAuditDocumentsPage> ReadDocumentsAsync(string projectId, ProductionAuditFilters f, CancellationToken ct)
    {
        var scope = await RequireScope(projectId, ct);
        const string sql = BaseSql + """
            SELECT Codigo, MAX(NomeDoc) NomeDoc, MAX(Disc) Disc, MAX(PrevDoc) PrevDoc, SUM(Trab) TrabFiltro, MAX(TrabDoc) TrabDoc,
                   COUNT(DISTINCT COALESCE(Nome,N'SEM NOME')) Pessoas, COUNT(1) Lanc,
                   CASE WHEN ISNULL(MAX(PrevDoc),0) = 0 THEN N'SEM HH PLANEJADA' WHEN MAX(TrabDoc) > MAX(PrevDoc) THEN N'ESTOURADO' ELSE N'DENTRO DO PLANEJADO' END Situacao
            INTO #D FROM #F WHERE Codigo IS NOT NULL GROUP BY Codigo;
            DELETE FROM #D WHERE @status IS NOT NULL AND Situacao <> @status;
            SELECT COUNT(1), ISNULL(SUM(PrevDoc),0), ISNULL(SUM(TrabFiltro),0) FROM #D;
            SELECT ISNULL(SUM(Trab),0) FROM #F WHERE Codigo IS NULL;
            SELECT Codigo, NomeDoc, Disc, PrevDoc, TrabFiltro, TrabDoc, Pessoas, Lanc, Situacao FROM #D
            ORDER BY CASE Situacao WHEN N'ESTOURADO' THEN 0 ELSE 1 END, TrabDoc - ISNULL(PrevDoc,0) DESC, Codigo
            OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var c = await OpenAsync(ct); await using var cmd = New(sql, c, projectId, scope.CrOs!, f); Paginar(cmd, f);
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 40).Value = (object?)Empty(f.Status) ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct); var total = r.GetInt32(0); var plan = Num(r, 1) ?? 0; var work = Num(r, 2) ?? 0;
        await r.NextResultAsync(ct); await r.ReadAsync(ct); var semDocumento = Num(r, 0) ?? 0; await r.NextResultAsync(ct);
        var list = new List<ProductionAuditDocumentRow>();
        while (await r.ReadAsync(ct))
        {
            var planned = Num(r, 3); var totalWorked = Num(r, 5) ?? 0; var p = planned ?? 0;
            list.Add(new(Text(r, 0)!, Text(r, 1), Text(r, 2) ?? ProductionAuditStatus.NaoMapeado, planned, Num(r, 4) ?? 0, totalWorked,
                totalWorked - p, p > 0 && totalWorked > p ? totalWorked - p : 0, p > 0 ? totalWorked * 100m / p : null, Text(r, 8)!, r.GetInt32(6), r.GetInt32(7)));
        }
        return new(list, total, scope.CrOs!, plan, work, semDocumento);
    }

    public async Task<ProductionAuditOverview> ReadReconciliationAsync(string projectId, ProductionAuditFilters f, CancellationToken ct)
    {
        var scope=await RequireScope(projectId,ct);
        await using var c=await OpenAsync(ct); await using var cmd=New(ReconciliationSql,c,projectId,scope.CrOs!,f); await using var r=await cmd.ExecuteReaderAsync(ct);
        var ldp=new Dictionary<string,ProductionAuditSourceDocument>(StringComparer.Ordinal);
        while(await r.ReadAsync(ct)) ldp[Text(r,0)!]=new(Text(r,1),Num(r,2)??0,Num(r,3)??0,Date(r,4));
        await r.NextResultAsync(ct);
        var ts=new Dictionary<string,ProductionAuditSourceDocument>(StringComparer.Ordinal);
        while(await r.ReadAsync(ct)) ts[Text(r,0)!]=new(Text(r,1),Num(r,2)??0,Num(r,3)??0,Date(r,4));
        await r.NextResultAsync(ct); await r.ReadAsync(ct); var ldpSemDoc=Num(r,0)??0;
        await r.NextResultAsync(ct); await r.ReadAsync(ct); var tsSemDoc=Num(r,0)??0;
        return ProductionAuditReconciliationRules.Conciliar(scope.CrOs!,ldp,ts,ldpSemDoc,tsSemDoc);
    }

    /// <summary>
    /// Fontes da auditoria: LDP = SUM(Actual_HH) (valor REAL, sem Actual_HH_Total_Corrigido); Timesheet = SUM(Trabalhadas)
    /// pelo Project_ID_ACC canônico + CR/OS. Os dois últimos SELECTs medem as horas sem código de documento (fora dos totais).
    /// </summary>
    public const string ReconciliationSql = """
        SELECT UPPER(LTRIM(RTRIM(NA_Document_Number))) Codigo,MAX(Discipline) Disciplina,SUM(ISNULL(Planned_HH,0)) Planejado,SUM(ISNULL(Actual_HH,0)) Realizado,MAX(Data_Sincronizacao) Atualizado
        FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@projectId AND NULLIF(LTRIM(RTRIM(NA_Document_Number)),'') IS NOT NULL GROUP BY UPPER(LTRIM(RTRIM(NA_Document_Number)));
        SELECT UPPER(LTRIM(RTRIM(DocumentoCodigo))) Codigo,MAX(COALESCE(NULLIF(Disciplina_PT_Padrao,''),Disciplina)) Disciplina,SUM(HH_Previsto_Corrigido) Planejado,SUM(ISNULL(Trabalhadas,0)) Realizado,MAX(data) Atualizado
        FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Project_ID_ACC_Canonico=@projectId AND CR_OS_Canonico=@cr AND Status_Mapeamento_CR_OS IN ('UNICO','ATIVO_UNICO') AND NULLIF(LTRIM(RTRIM(DocumentoCodigo)),'') IS NOT NULL
        GROUP BY UPPER(LTRIM(RTRIM(DocumentoCodigo)));
        SELECT ISNULL(SUM(ISNULL(Actual_HH,0)),0) FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@projectId AND NULLIF(LTRIM(RTRIM(NA_Document_Number)),'') IS NULL;
        SELECT ISNULL(SUM(ISNULL(Trabalhadas,0)),0) FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Project_ID_ACC_Canonico=@projectId AND CR_OS_Canonico=@cr AND Status_Mapeamento_CR_OS IN ('UNICO','ATIVO_UNICO') AND NULLIF(LTRIM(RTRIM(DocumentoCodigo)),'') IS NULL;
        """;

    private sealed record RawBiTimesheetRow(
        DateTime? Date, string? Name, string? Week, DateTime? Entry, DateTime? Exit, decimal? Interval,
        decimal Hours, string? Afe, string? Description, string Project, string? Function, string? Discipline);

    private sealed record TreatmentState(string? Discipline, bool IsPackage, string Type);
    private sealed record ProjectIdentity(string ProjectCode, string BiProjectCode);
    private sealed record RoutingState(string Mode, string? AfeCode, bool IsConfigured);

    private async Task<ProjectIdentity> ReadProjectIdentityAsync(string projectId, CancellationToken ct)
    {
        string? projectName;
        await using var local = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT TOP(1) Nome_Projeto FROM dbo.Projetos WHERE Project_ID_ACC=@p AND Is_Ativo=1", local);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        projectName = (await cmd.ExecuteScalarAsync(ct) as string)?.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException("Projeto não encontrado ou inativo.");

        var projectCode = projectName.Split(" - ", 2, StringSplitOptions.TrimEntries)[0].Trim().ToUpperInvariant();
        var biProjectCode = projectCode.Contains('.') ? projectCode.Split('.', 2)[0] : projectCode;
        return new(projectCode, biProjectCode);
    }

    private async Task<RoutingState> ReadRoutingStateAsync(string projectId, ProjectIdentity identity, CancellationToken ct)
    {
        await using var local = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP(1) RoutingMode,AfeCode
            FROM qualidade.TimesheetRoutingConfig
            WHERE Project_ID_ACC=@p AND Ativo=1;
            """, local);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return new(reader.GetString(0), Text(reader, 1), true);

        var requiresAfe = !string.Equals(identity.ProjectCode, identity.BiProjectCode, StringComparison.OrdinalIgnoreCase);
        return requiresAfe
            ? new(ProductionAuditTimesheetRoutingModes.ProjectAfe, SuggestedAfe(identity.ProjectCode), false)
            : new(ProductionAuditTimesheetRoutingModes.Project, null, false);
    }

    private static Regex TargetDocumentRegex(string projectCode) => new(
        $@"(?<![A-Z0-9.]){Regex.Escape(projectCode)}(?:-[A-Z0-9]+){{4}}(?![A-Z0-9-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Regex AnyDocumentRegex(string biProjectCode) => new(
        $@"(?<![A-Z0-9.]){Regex.Escape(biProjectCode)}(?:\.\d{{3}})?(?:-[A-Z0-9]+){{4}}(?![A-Z0-9-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string? NormalizeAfeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        var match = Regex.Match(normalized, @"^([A-Z0-9_-]+)\.(\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success) return normalized;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix))
            return normalized;
        return $"{match.Groups[1].Value}.{suffix}";
    }

    private static string? SuggestedAfe(string projectCode)
    {
        var dot = projectCode.LastIndexOf('.');
        if (dot <= 0 || dot == projectCode.Length - 1) return null;
        var prefix = projectCode[..dot];
        var suffix = projectCode[(dot + 1)..];
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? $"{prefix}.{number}"
            : projectCode;
    }

    private static bool MatchesRouting(RawBiTimesheetRow row, RoutingState routing) =>
        routing.Mode == ProductionAuditTimesheetRoutingModes.Project ||
        (routing.Mode == ProductionAuditTimesheetRoutingModes.ProjectAfe &&
         string.Equals(NormalizeAfeCode(row.Afe), routing.AfeCode, StringComparison.OrdinalIgnoreCase));

    public async Task<ProductionAuditTimesheetRoutingConfig> ReadTimesheetRoutingConfigAsync(string projectId, CancellationToken ct)
    {
        var identity = await ReadProjectIdentityAsync(projectId, ct);
        var routing = await ReadRoutingStateAsync(projectId, identity, ct);
        var anyDocument = AnyDocumentRegex(identity.BiProjectCode);

        const string sql = """
            SELECT [AFE],[Descricao],ISNULL([Trabalhadas],0)
            FROM dbo.tbTimeSheet
            WHERE UPPER(LTRIM(RTRIM([Projeto])))=@project
              AND ISNULL([Trabalhadas],0) > 0;
            """;

        var candidates = new Dictionary<string, (int Rows, decimal Hours)>(StringComparer.OrdinalIgnoreCase);
        await using (var bi = await OpenBiAsync(ct))
        await using (var cmd = new SqlCommand(sql, bi) { CommandTimeout = 45 })
        {
            cmd.Parameters.Add("@project", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var afe = NormalizeAfeCode(Text(reader, 0));
                var description = Text(reader, 1) ?? string.Empty;
                var hours = Num(reader, 2) ?? 0m;
                if (string.IsNullOrWhiteSpace(afe) || anyDocument.IsMatch(description)) continue;
                if (candidates.TryGetValue(afe, out var current))
                    candidates[afe] = (current.Rows + 1, current.Hours + hours);
                else
                    candidates[afe] = (1, hours);
            }
        }

        var requiresConfiguration = !routing.IsConfigured &&
            !string.Equals(identity.ProjectCode, identity.BiProjectCode, StringComparison.OrdinalIgnoreCase);

        return new(
            identity.ProjectCode,
            identity.BiProjectCode,
            routing.Mode,
            routing.AfeCode,
            SuggestedAfe(identity.ProjectCode),
            routing.IsConfigured,
            requiresConfiguration,
            candidates
                .OrderByDescending(x => x.Value.Hours)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ProductionAuditTimesheetRoutingCandidate(x.Key, x.Value.Rows, x.Value.Hours))
                .ToList());
    }

    public async Task<ProductionAuditTimesheetRoutingConfig> SaveTimesheetRoutingConfigAsync(
        string projectId, ProductionAuditTimesheetRoutingInput input, string? alteredBy, CancellationToken ct)
    {
        var identity = await ReadProjectIdentityAsync(projectId, ct);
        var mode = input.RoutingMode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!ProductionAuditTimesheetRoutingModes.All.Contains(mode))
            throw new ArgumentException("Modo de roteamento inválido.");

        if (mode == ProductionAuditTimesheetRoutingModes.Project &&
            !string.Equals(identity.ProjectCode, identity.BiProjectCode, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Este projeto pertence a uma família com múltiplas CR-OS. Configure uma AFE específica.");

        var afe = mode == ProductionAuditTimesheetRoutingModes.ProjectAfe
            ? NormalizeAfeCode(input.AfeCode)
            : null;
        if (mode == ProductionAuditTimesheetRoutingModes.ProjectAfe && string.IsNullOrWhiteSpace(afe))
            throw new ArgumentException("Informe a AFE correspondente à CR-OS.");

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (mode == ProductionAuditTimesheetRoutingModes.Project)
            {
                await using var conflict = new SqlCommand("""
                    SELECT COUNT(1)
                    FROM qualidade.TimesheetRoutingConfig WITH(UPDLOCK,HOLDLOCK)
                    WHERE BiProjectCode=@bi AND Ativo=1 AND Project_ID_ACC<>@p;
                    """, connection, (SqlTransaction)transaction);
                conflict.Parameters.Add("@bi", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
                conflict.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
                if (Convert.ToInt32(await conflict.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
                    throw new InvalidOperationException("Este Projeto do BI já possui roteamento para outra CR-OS. Use Projeto + AFE.");
            }
            else
            {
                await using var conflict = new SqlCommand("""
                    SELECT COUNT(1)
                    FROM qualidade.TimesheetRoutingConfig WITH(UPDLOCK,HOLDLOCK)
                    WHERE BiProjectCode=@bi
                      AND Ativo=1
                      AND Project_ID_ACC<>@p
                      AND (
                            RoutingMode='PROJECT'
                            OR (RoutingMode='PROJECT_AFE' AND UPPER(LTRIM(RTRIM(AfeCode)))=@afe)
                          );
                    """, connection, (SqlTransaction)transaction);
                conflict.Parameters.Add("@bi", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
                conflict.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
                conflict.Parameters.Add("@afe", SqlDbType.NVarChar, 100).Value = afe!;
                if (Convert.ToInt32(await conflict.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
                    throw new InvalidOperationException("Esta AFE já está atribuída a outra CR-OS ou o Projeto do BI já está reservado integralmente.");
            }

            const string upsert = """
                UPDATE qualidade.TimesheetRoutingConfig
                SET BiProjectCode=@bi,RoutingMode=@mode,AfeCode=@afe,Ativo=1,AlteradoPor=@user,AlteradoEm=SYSUTCDATETIME()
                WHERE Project_ID_ACC=@p;
                IF @@ROWCOUNT=0
                    INSERT qualidade.TimesheetRoutingConfig(Project_ID_ACC,BiProjectCode,RoutingMode,AfeCode,Ativo,AlteradoPor)
                    VALUES(@p,@bi,@mode,@afe,1,@user);
                """;
            await using var cmd = new SqlCommand(upsert, connection, (SqlTransaction)transaction);
            cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
            cmd.Parameters.Add("@bi", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
            cmd.Parameters.Add("@mode", SqlDbType.VarChar, 20).Value = mode;
            cmd.Parameters.Add("@afe", SqlDbType.NVarChar, 100).Value = (object?)afe ?? DBNull.Value;
            cmd.Parameters.Add("@user", SqlDbType.NVarChar, 300).Value = (object?)Empty(alteredBy) ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return await ReadTimesheetRoutingConfigAsync(projectId, ct);
    }

    public async Task<ProductionAuditUnassignedTimesheetOverview> ReadUnassignedTimesheetAsync(string projectId, CancellationToken ct)
    {
        var identity = await ReadProjectIdentityAsync(projectId, ct);
        var routing = await ReadRoutingStateAsync(projectId, identity, ct);

        if (!routing.IsConfigured &&
            !string.Equals(identity.ProjectCode, identity.BiProjectCode, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                identity.ProjectCode, 0m, 0m, 0m, 0m, 0, 0, 0, 0, true,
                $"Configure a AFE/CR-OS antes de analisar HH sem documento de {identity.ProjectCode}. Projeto do BI: {identity.BiProjectCode}; sugestão de AFE: {routing.AfeCode ?? "não identificada"}.",
                Array.Empty<ProductionAuditUnassignedTimesheetRow>());
        }

        const string sql = """
            SELECT [data],[Nome],[SemanaDesk],[Entrada],[Saida],[Intervalo],ISNULL([Trabalhadas],0),
                   [AFE],[Descricao],[Projeto],[Funcao],[Disciplina]
            FROM dbo.tbTimeSheet
            WHERE UPPER(LTRIM(RTRIM([Projeto])))=@project
              AND ISNULL([Trabalhadas],0) > 0
            ORDER BY [data] DESC,[Nome],[Descricao];
            """;

        var raw = new List<RawBiTimesheetRow>();
        await using (var bi = await OpenBiAsync(ct))
        await using (var cmd = new SqlCommand(sql, bi) { CommandTimeout = 45 })
        {
            cmd.Parameters.Add("@project", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                raw.Add(new(
                    Date(reader, 0),
                    Text(reader, 1),
                    Text(reader, 2),
                    Date(reader, 3),
                    Date(reader, 4),
                    Num(reader, 5),
                    Num(reader, 6) ?? 0m,
                    Text(reader, 7),
                    Text(reader, 8),
                    Text(reader, 9) ?? identity.BiProjectCode,
                    Text(reader, 10),
                    Text(reader, 11)));
            }
        }

        var targetDocument = TargetDocumentRegex(identity.ProjectCode);
        var anyDocument = AnyDocumentRegex(identity.BiProjectCode);

        // Regra de autoridade:
        // 1) se existe QUALQUER Número do documento válido na descrição, a linha NÃO é tratada por AFE;
        // 2) somente linhas realmente sem documento entram no roteamento Projeto / Projeto+AFE.
        var withoutDocument = raw
            .Where(x => !anyDocument.IsMatch(x.Description ?? string.Empty))
            .Where(x => MatchesRouting(x, routing))
            .ToList();

        var withTargetDocument = raw
            .Where(x => targetDocument.IsMatch(x.Description ?? string.Empty))
            .ToList();

        var (costs, treatments) = await ReadCostCatalogAndTreatmentsAsync(projectId, ct);

        var items = new List<ProductionAuditUnassignedTimesheetRow>(withoutDocument.Count);
        foreach (var row in withoutDocument)
        {
            var rowKey = BuildRowKey(projectId, row);
            var costResolution = ResolveHourlyCost(costs, row);
            var hourlyCost = costResolution.Rate;
            var cost = costResolution.CostFor(row.Hours);
            treatments.TryGetValue(rowKey, out var treatment);

            items.Add(new(
                rowKey,
                row.Date,
                row.Name,
                row.Week,
                row.Hours,
                row.Afe,
                row.Description,
                row.Project,
                row.Function,
                row.Discipline,
                hourlyCost,
                cost,
                hourlyCost.HasValue ? costResolution.Status : null,
                treatment?.Discipline,
                treatment?.IsPackage ?? false,
                treatment?.Type ?? "PENDENTE",
                costResolution.Status));
        }

        var hoursWithout = items.Sum(x => x.Hours);
        var hoursWithDocument = withTargetDocument.Sum(x => x.Hours);
        var totalHours = hoursWithDocument + hoursWithout;
        var costWithout = items.Where(x => x.Cost.HasValue).Sum(x => x.Cost!.Value);
        var rowsWithoutCost = items.Count(x => !x.Cost.HasValue);

        return new(
            identity.ProjectCode,
            totalHours,
            hoursWithDocument,
            hoursWithout,
            costWithout,
            rowsWithoutCost,
            withTargetDocument.Count + items.Count,
            withTargetDocument.Count,
            items.Count,
            false,
            null,
            items);
    }

    public async Task<ProductionAuditUnassignedTreatmentResult> SaveUnassignedTimesheetTreatmentAsync(
        string projectId, ProductionAuditUnassignedTreatmentInput input, string? alteredBy, CancellationToken ct)
    {
        if (input.RowKeys is null || input.RowKeys.Count == 0 || input.RowKeys.Count > 5000)
            throw new ArgumentException("Selecione ao menos um lançamento.");

        var keys = input.RowKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (keys.Count == 0 || keys.Any(x => x.Length != 64 || x.Any(c => !Uri.IsHexDigit(c))))
            throw new ArgumentException("Identificador de lançamento inválido.");

        var requestedType = string.IsNullOrWhiteSpace(input.TreatmentType)
            ? null
            : input.TreatmentType.Trim().ToUpperInvariant();
        if (requestedType is not null && requestedType is not ("PENDENTE" or "AT"))
            throw new ArgumentException("Tipo de tratamento inválido.");

        var overview = await ReadUnassignedTimesheetAsync(projectId, ct);
        if (overview.ScopeAmbiguous)
            throw new InvalidOperationException(overview.ScopeMessage ?? "Escopo do Timesheet ambíguo.");

        var selected = overview.Items.Where(x => keys.Contains(x.RowKey)).ToList();
        if (selected.Count != keys.Count)
            throw new InvalidOperationException("Um ou mais lançamentos mudaram no BI. Atualize a auditoria e tente novamente.");

        var effective = selected.Select(row => new
        {
            Row = row,
            Discipline = input.Discipline is null ? row.AssignedDiscipline : Empty(input.Discipline),
            IsPackage = input.IsPackage ?? row.IsPackage,
            Type = requestedType ?? row.TreatmentStatus
        }).ToList();

        if (effective.Any(x => x.Type == "AT" && string.IsNullOrWhiteSpace(x.Discipline)))
            throw new ArgumentException("Defina a disciplina antes de confirmar como AT.");

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            const string sql = """
                UPDATE gestao.TimesheetSemDocumentoTratamento
                SET NomeProfissional=@name,DataLancamento=@date,DescricaoSnapshot=@description,HHOriginal=@hours,
                    DisciplinaDestino=@discipline,EhPacote=@package,TipoTratamento=@type,
                    CustoHoraAplicado=@hourlyCost,CustoCalculado=@cost,AlteradoPor=@user,AlteradoEm=SYSUTCDATETIME()
                WHERE Project_ID_ACC=@projectId AND RowKey=@rowKey AND Ativo=1;

                IF @@ROWCOUNT=0
                BEGIN
                    INSERT INTO gestao.TimesheetSemDocumentoTratamento
                        (Project_ID_ACC,RowKey,NomeProfissional,DataLancamento,DescricaoSnapshot,HHOriginal,
                         DisciplinaDestino,EhPacote,TipoTratamento,CustoHoraAplicado,CustoCalculado,AlteradoPor)
                    VALUES
                        (@projectId,@rowKey,@name,@date,@description,@hours,
                         @discipline,@package,@type,@hourlyCost,@cost,@user);
                END;
                """;

            foreach (var item in effective)
            {
                await using var cmd = new SqlCommand(sql, connection, (SqlTransaction)transaction);
                cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
                cmd.Parameters.Add("@rowKey", SqlDbType.Char, 64).Value = item.Row.RowKey;
                cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = (object?)item.Row.Name ?? DBNull.Value;
                cmd.Parameters.Add("@date", SqlDbType.Date).Value = (object?)item.Row.Date?.Date ?? DBNull.Value;
                cmd.Parameters.Add("@description", SqlDbType.NVarChar, 2000).Value = (object?)item.Row.Description ?? DBNull.Value;
                AddDecimal(cmd, "@hours", item.Row.Hours);
                cmd.Parameters.Add("@discipline", SqlDbType.NVarChar, 200).Value = (object?)item.Discipline ?? DBNull.Value;
                cmd.Parameters.Add("@package", SqlDbType.Bit).Value = item.IsPackage;
                cmd.Parameters.Add("@type", SqlDbType.VarChar, 30).Value = item.Type;
                AddNullableDecimal(cmd, "@hourlyCost", item.Row.HourlyCost);
                AddNullableDecimal(cmd, "@cost", item.Row.Cost);
                cmd.Parameters.Add("@user", SqlDbType.NVarChar, 300).Value = (object?)Empty(alteredBy) ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        var typeResult = requestedType ?? (effective.Select(x => x.Type).Distinct().Count() == 1 ? effective[0].Type : "MISTO");
        return new(
            effective.Count,
            typeResult,
            effective.Sum(x => x.Row.Hours),
            effective.Where(x => x.Row.Cost.HasValue).Sum(x => x.Row.Cost!.Value),
            effective.Count(x => !x.Row.Cost.HasValue));
    }

    public async Task<ProductionExperimentalSummary> ReadExperimentalSummaryAsync(string projectId, CancellationToken ct)
    {
        var identity = await ReadProjectIdentityAsync(projectId, ct);
        var routing = await ReadRoutingStateAsync(projectId, identity, ct);

        if (!routing.IsConfigured &&
            !string.Equals(identity.ProjectCode, identity.BiProjectCode, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                identity.ProjectCode, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0, true,
                $"Configure a AFE/CR-OS antes de usar o Resumo LAB de {identity.ProjectCode}. Projeto do BI: {identity.BiProjectCode}; sugestão: {routing.AfeCode ?? "não identificada"}.");
        }

        const string rawSql = """
            SELECT [data],[Nome],[SemanaDesk],[Entrada],[Saida],[Intervalo],ISNULL([Trabalhadas],0),
                   [AFE],[Descricao],[Projeto],[Funcao],[Disciplina]
            FROM dbo.tbTimeSheet
            WHERE UPPER(LTRIM(RTRIM([Projeto])))=@project
              AND ISNULL([Trabalhadas],0) > 0;
            """;

        var raw = new List<RawBiTimesheetRow>();
        await using (var bi = await OpenBiAsync(ct))
        await using (var cmd = new SqlCommand(rawSql, bi) { CommandTimeout = 45 })
        {
            cmd.Parameters.Add("@project", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                raw.Add(new(
                    Date(reader, 0),
                    Text(reader, 1),
                    Text(reader, 2),
                    Date(reader, 3),
                    Date(reader, 4),
                    Num(reader, 5),
                    Num(reader, 6) ?? 0m,
                    Text(reader, 7),
                    Text(reader, 8),
                    Text(reader, 9) ?? identity.BiProjectCode,
                    Text(reader, 10),
                    Text(reader, 11)));
            }
        }

        var (costs, treatments) = await ReadCostCatalogAndTreatmentsAsync(projectId, ct);
        var engineeringScope = await LoadEngineeringScopeAsync(projectId, ct);

        var targetDocument = TargetDocumentRegex(identity.ProjectCode);
        var anyDocument = AnyDocumentRegex(identity.BiProjectCode);

        decimal totalHours = 0m, documentHours = 0m, unassignedHours = 0m;
        decimal confirmedAtHours = 0m, packageMarkedHours = 0m;
        decimal knownCost = 0m, documentCost = 0m, unassignedCost = 0m, missingCostHours = 0m;
        decimal excludedHours = 0m, excludedKnownCost = 0m;
        var missingCostRows = 0;
        var excludedRows = 0;

        foreach (var row in raw)
        {
            var description = row.Description ?? string.Empty;
            var hasTargetDocument = targetDocument.IsMatch(description);
            var hasAnyDocument = anyDocument.IsMatch(description);
            var isRoutedUnassigned = !hasAnyDocument && MatchesRouting(row, routing);

            // Documento válido é autoridade. Uma AFE divergente nunca desloca HH para outra CR-OS.
            // Linhas com documento de outra CR-OS também não entram no total deste projeto.
            if (!hasTargetDocument && !isRoutedUnassigned) continue;

            // Gestão: exclui as HH e o custo SOMENTE dos indicadores de engenharia.
            // O lançamento bruto do Timesheet e os dados oficiais da LDP ficam intactos.
            if (engineeringScope.TryGetValue(CostTimesheetMatchingRules.NormalizeName(row.Name), out var scope) && scope.Excluded)
            {
                excludedHours += row.Hours;
                excludedKnownCost += ResolveHourlyCost(costs, row).CostFor(row.Hours) ?? 0m;
                excludedRows++;
                continue;
            }

            totalHours += row.Hours;
            if (hasTargetDocument) documentHours += row.Hours;
            else unassignedHours += row.Hours;

            var rowCost = ResolveHourlyCost(costs, row).CostFor(row.Hours);
            if (rowCost.HasValue)
            {
                knownCost += rowCost.Value;
                if (hasTargetDocument) documentCost += rowCost.Value;
                else unassignedCost += rowCost.Value;
            }
            else
            {
                missingCostRows++;
                missingCostHours += row.Hours;
            }

            if (isRoutedUnassigned)
            {
                var rowKey = BuildRowKey(projectId, row);
                if (treatments.TryGetValue(rowKey, out var treatment))
                {
                    if (string.Equals(treatment.Type, "AT", StringComparison.OrdinalIgnoreCase))
                        confirmedAtHours += row.Hours;
                    if (treatment.IsPackage)
                        packageMarkedHours += row.Hours;
                }
            }
        }

        // O comparativo gerencial precisa da MESMA fonte por Número do Documento usada na auditoria:
        // tbTimeSheetRealizadoDocumento. O total diário permanece disponível apenas para
        // conciliar horas ainda sem rateio; nunca o atribuir inteiro ao primeiro documento.
        var allocatedRows = await ReadDocumentAllocatedRowsAsync(identity, ct);
        decimal allocatedTotal = 0m, allocatedEngineering = 0m;
        decimal allocatedCost = 0m, allocatedManagementHh = 0m, allocatedManagementCost = 0m;
        decimal allocatedMissingCostHh = 0m;
        foreach (var row in allocatedRows)
        {
            var hourCost = costs.Resolve(row.Name, row.Date.HasValue ? DateOnly.FromDateTime(row.Date.Value) : null)
                .CostFor(row.Hours);
            allocatedTotal += row.Hours;
            if (engineeringScope.TryGetValue(CostTimesheetMatchingRules.NormalizeName(row.Name), out var category)
                && category.Excluded)
            {
                allocatedManagementHh += row.Hours;
                allocatedManagementCost += hourCost ?? 0m;
            }
            else
            {
                allocatedEngineering += row.Hours;
                if (hourCost.HasValue) allocatedCost += hourCost.Value;
                else allocatedMissingCostHh += row.Hours;
            }
        }

        return new(
            identity.ProjectCode,
            totalHours,
            documentHours,
            unassignedHours,
            confirmedAtHours,
            Math.Max(0m, unassignedHours - confirmedAtHours),
            packageMarkedHours,
            knownCost,
            documentCost,
            unassignedCost,
            missingCostHours,
            missingCostRows,
            false,
            null,
            excludedHours,
            excludedKnownCost,
            excludedRows,
            allocatedEngineering,
            allocatedCost,
            allocatedTotal,
            allocatedManagementHh,
            allocatedManagementCost,
            allocatedMissingCostHh,
            allocatedRows.Count);
    }

    /// <summary>
    /// Custo/hora = cadastro de profissionais do projeto (ProfessionalHourlyCostCatalog), a mesma regra do custo por documento.
    /// gestao.CustoProfissionalRealHistorico (carga da planilha Gestão à Vista) não é mais usado aqui: ele trazia 0 para
    /// profissionais com CUSTO/HORA manual no cadastro, gerando custo zero silencioso nas HH sem documento e no Resumo LAB.
    /// Sem cadastro/vigência o custo fica null com o motivo em Status — nunca zero e nunca "custo atual" de outra data.
    /// </summary>
    private static ProfessionalHourlyCostResolution ResolveHourlyCost(ProfessionalHourlyCostCatalog costs, RawBiTimesheetRow row) =>
        costs.Resolve(row.Name, row.Date.HasValue ? DateOnly.FromDateTime(row.Date.Value) : null);

    private async Task<(ProfessionalHourlyCostCatalog Costs, Dictionary<string, TreatmentState> Treatments)> ReadCostCatalogAndTreatmentsAsync(
        string projectId, CancellationToken ct)
    {
        await using var local = await OpenAsync(ct);
        var costs = await ProfessionalHourlyCostCatalog.LoadAsync(local, projectId, ct);

        var treatments = new Dictionary<string, TreatmentState>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand("""
            SELECT RowKey,DisciplinaDestino,EhPacote,TipoTratamento
            FROM gestao.TimesheetSemDocumentoTratamento
            WHERE Project_ID_ACC=@p AND Ativo=1;
            """, local);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = Text(reader, 0);
            if (!string.IsNullOrWhiteSpace(key))
                treatments[key] = new(Text(reader, 1), reader.GetBoolean(2), Text(reader, 3) ?? "PENDENTE");
        }
        return (costs, treatments);
    }

    private static string BuildRowKey(string projectId, RawBiTimesheetRow row)
    {
        static string D(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? "";
        static string N(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
        static string T(string? value) => value?.Trim() ?? "";

        var payload = string.Join("\u001F",
            projectId,
            D(row.Date),
            T(row.Name),
            T(row.Week),
            D(row.Entry),
            D(row.Exit),
            N(row.Interval),
            N(row.Hours),
            T(row.Afe),
            T(row.Description),
            T(row.Project),
            T(row.Function),
            T(row.Discipline));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }


    private static void AddDecimal(SqlCommand cmd, string name, decimal value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 6;
        p.Value = value;
    }

    private static void AddNullableDecimal(SqlCommand cmd, string name, decimal? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 6;
        p.Value = (object?)value ?? DBNull.Value;
    }

    private async Task<ProductionAuditScope> RequireScope(string projectId,CancellationToken ct){var s=await ResolveScopeAsync(projectId,ct); if(s.Status!=ProductionAuditScopeStatus.Ok) throw new ProductionAuditScopeException(s); return s;}
    private static void Paginar(SqlCommand cmd, ProductionAuditFilters f)
    {
        var take = f.PageSize == int.MaxValue ? int.MaxValue : f.PageSize;
        cmd.Parameters.Add("@offset", SqlDbType.Int).Value = take == int.MaxValue ? 0 : (f.Page - 1) * f.PageSize;
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
    }
    private static SqlCommand New(string cmd,SqlConnection c,string pid,string cr,ProductionAuditFilters f){var x=new SqlCommand(cmd,c){CommandTimeout=45};x.Parameters.Add("@projectId",SqlDbType.NVarChar,100).Value=pid;x.Parameters.Add("@cr",SqlDbType.NVarChar,100).Value=cr;x.Parameters.Add("@from",SqlDbType.Date).Value=(object?)f.From?.Date??DBNull.Value;x.Parameters.Add("@to",SqlDbType.Date).Value=(object?)f.To?.Date??DBNull.Value;x.Parameters.Add("@discipline",SqlDbType.NVarChar,150).Value=(object?)Empty(f.Discipline)??DBNull.Value;x.Parameters.Add("@document",SqlDbType.NVarChar,300).Value=(object?)Empty(f.Document)??DBNull.Value;x.Parameters.Add("@person",SqlDbType.NVarChar,200).Value=(object?)Empty(f.Person)??DBNull.Value;x.Parameters.Add("@exact",SqlDbType.Bit).Value=f.Exact;return x;}
    private static string? Empty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim(); private static string? Text(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i); private static decimal? Num(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDecimal(r.GetValue(i)); private static DateTime? Date(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDateTime(r.GetValue(i));
}
public sealed class ProductionAuditScopeException(ProductionAuditScope scope):Exception(scope.Message){public ProductionAuditScope Scope {get;}=scope;}
