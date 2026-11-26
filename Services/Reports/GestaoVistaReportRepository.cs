using APS.WebApp.Services.Planning;
using System.Data;
using Microsoft.Data.SqlClient;
namespace APS.WebApp.Services.Reports;
public interface IGestaoVistaReportRepository
{
    Task<GestaoVistaReport> ReadAsync(string projectId, CancellationToken cancellationToken);
}

public sealed record GestaoVistaReport
{
    public string ProjectId { get; init; }
    public string NomeProjeto { get; init; }
    public int TipoLdp { get; init; }
    public bool SemLdp { get; init; }
    public DateTime DataReferencia { get; init; }
    public IReadOnlyList<PmoDocumento> Documentos { get; init; }
    public IReadOnlyList<PmoLancamento> Lancamentos { get; init; }
    public IReadOnlyList<PmoWeekly> WeeklyAtividades { get; init; }
    public IReadOnlyList<PmoTarefa> Cronograma { get; init; }
    public ProjectPlanningFacts Planning { get; init; }
    public ProjectActualFacts Actual { get; init; }
    /// <summary>HH realizado oficial (Actual_HH_Total_Corrigido) e avanço previsto/físico — calculados UMA vez aqui, as telas só consomem.</summary>
    public PmoHhRealizado HhRealizado { get; init; }
    public PmoAvancoProjeto Avanco { get; init; }
    /// <summary>Fonte paralela, independente do cálculo documental. Não altera o Resumo original.</summary>
    public PmoAvancoCronograma AvancoCronograma { get; init; }
    public ResumoPhaseFacts ResumoFases { get; init; }
    /// <summary>Consolidados independentes por documento único: Situation (emitidos/cancelados/a emitir + prazo existente) e Document_Status.</summary>
    public APS.WebApp.Services.Documents.DocumentosConsolidado ConsolidadoDocumental { get; init; }
    public GestaoVistaReport(PmoExecutiveReport report, ProjectPlanningFacts planning, ProjectActualFacts actual, bool semLdp=false)
    {
        ProjectId = report.ProjectId; NomeProjeto = report.NomeProjeto; TipoLdp = report.TipoLdp; SemLdp = semLdp;
        DataReferencia = report.DataReferencia; Documentos = report.Documentos;
        Lancamentos = report.Lancamentos; WeeklyAtividades = report.WeeklyAtividades;
        Cronograma = report.Cronograma; Planning = planning; Actual = actual;
        // Regra oficial 06/10/2026: o REALIZADO do projeto é o Timesheet atual completo.
        // A LDP continua trazendo realizado por documento (somente o que casa por código corrigido), mas
        // códigos removidos/renomeados não podem desaparecer do total do projeto.
        HhRealizado = new PmoHhRealizado(
            actual.Total.Hh,
            actual.ByDiscipline.Select(d => new PmoHhDisciplina(d.Discipline, d.Values.Hh)
            {
                Custo = d.Values.KnownCalculatedCostSubtotal
            }).ToArray(),
            actual.Source)
        {
            CustoTotal = actual.Total.KnownCalculatedCostSubtotal,
            CustoOficial = true
        };
        Avanco = PmoAvancoRules.Calcular(report.TipoLdp, report.DataReferencia, report.Documentos, planning.PlannedHhTotal);
        AvancoCronograma = PmoAvancoRules.CalcularCronograma(report.Cronograma, report.TipoLdp);
        ResumoFases = ResumoPhaseRules.Read(report.TipoLdp, report.DataReferencia, report.Documentos);
        ConsolidadoDocumental = APS.WebApp.Services.Documents.DocumentSituationStatusRules.Consolidar(report.Documentos, semLdp);
    }
}

/// <summary>First consumer of canonical PMO facts; contains no planning or actual calculation rules.</summary>
public sealed class GestaoVistaReportRepository(IConfiguration configuration,
    IPmoPlanningFactsProvider planning, IPmoActualFactsProvider actual) : IGestaoVistaReportRepository
{
    public async Task<GestaoVistaReport> ReadAsync(string projectId, CancellationToken cancellationToken)
    {
        var report = await PmoExecutiveData.ReadAsync(configuration, projectId, true, cancellationToken, currentPlanningSnapshot: true);
        // HH realizado corrigido: só lançamentos de documentos da LDP ATUAL (= Σ Actual_HH_Corrigido da VW_Ldp_Unificada).
        var documentosLdp = report.Documentos.Select(d => ProjectPlanningRules.Key(d.Codigo)).Where(k => k != "").ToHashSet(StringComparer.Ordinal);
        var facts=await actual.ReadAsync(projectId,documentosLdp,cancellationToken);
        // Sem LDP: os códigos da fonte documental corrigida são válidos para o
        // inventário e agrupamento, mas não comprovam emissão, status ou HH prevista.
        var projetoSemLdp=false;
        await using(var cn=new SqlConnection(configuration.GetConnectionString("LocalConnection")))
        {
            await cn.OpenAsync(cancellationToken);
            await using var q=new SqlCommand(
                "SELECT Sem_LDP FROM dbo.Projetos WHERE Project_ID_ACC=@id AND Is_Ativo=1",cn);
            q.Parameters.Add("@id",SqlDbType.NVarChar,255).Value=projectId;
            if(await q.ExecuteScalarAsync(cancellationToken) is bool semLdp && semLdp)
            {
                projetoSemLdp=true;
            }
            if(projetoSemLdp)
            {
                report=report with { Documentos=facts.ProvisionalDocuments.Select(d=>new PmoDocumento(
                    "HISTORICO:"+d.Code,projectId,d.Code,null,d.Discipline,
                    null,null,null,null,null,null,null,null,null,null,null)
                    {
                        HHRealizado=d.Hours,Doc4=d.Doc4,DocTypeCode=d.Doc4,
                        DisciplinaCanonicaDoc3=d.Discipline,
                        DisciplinaNorma=d.Discipline==NA_Connect.Services.Coordination.CoordinationDisciplineResolver.SemDisciplinaLabel
                            ? null : d.Discipline,
                        SiglaDoc3=d.Doc3,
                        FonteDisciplinaNorma="NORMALIZADOR_SIGLAS_SEM_LDP",
                        DiagnosticoDisciplinaNorma=d.Discipline==NA_Connect.Services.Coordination.CoordinationDisciplineResolver.SemDisciplinaLabel
                            ? "Sigla não encontrada nas regras de normalização":null,
                        StatusDocumento="SEM_PRAZO"
                    }).ToArray() };
            }
        }
        var supplements=facts.HistoricalMeasurement?.Documents.GroupBy(d=>ProjectPlanningRules.Key(d.Document))
            .ToDictionary(g=>g.Key,g=>(Hours:g.Sum(d=>d.Hours),Cost:g.Sum(d=>d.Cost)));
        if(supplements is not null && supplements.Count>0)
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            report=report with { Documentos=report.Documentos.Select(d=>
            {
                var key=ProjectPlanningRules.Key(d.Codigo);
                if(!seen.Add(key)||!supplements.TryGetValue(key,out var complement))return d;
                return d with { HHTimesheetOriginal=d.HHRealizado,CustoTimesheetOriginal=d.CustoRealizado,
                    HHComplementoHistorico=complement.Hours,CustoComplementoHistorico=complement.Cost,
                    HHRealizado=(d.HHRealizado??0)+complement.Hours,CustoRealizado=(d.CustoRealizado??0)+complement.Cost };
            }).ToArray() };
        }
        return new(report,await planning.ReadAsync(projectId,cancellationToken),facts,projetoSemLdp);
    }
}
