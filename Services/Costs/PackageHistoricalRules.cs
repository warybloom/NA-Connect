using APS.WebApp.Services.Planning;

namespace APS.WebApp.Services.Costs;

public sealed record HistoricalBalance(decimal RecognizedHours, decimal RecognizedCost, decimal ComplementHours, decimal ComplementCost,
    decimal ExcessHours, decimal ExcessCost, IReadOnlyList<string> Diagnostics);
public sealed record HistoricalDiscipline(int PackageId, long ConfirmationId, string Package, string Professional, string Discipline,
    decimal ConfirmedHours, decimal ConfirmedCost, decimal ContractHours, decimal ContractCost, decimal TimesheetHours, decimal? TimesheetCost,
    HistoricalBalance Balance, DateTime ConfirmedAtUtc, string ConfirmedBy, string Origin);
public sealed record HistoricalDocument(int PackageId, long ConfirmationId, string Discipline, string Document, decimal Hours, decimal Cost)
{
    public decimal TimesheetHours { get; init; }
    public decimal RecognizedHours { get; init; }
    public decimal RecognizedCost { get; init; }
    public decimal ExcessHours { get; init; }
    public decimal ExcessCost { get; init; }
    public decimal ContractHours { get; init; }
    public decimal ContractCost { get; init; }
}
public sealed record PackageHistoricalFacts(string ProjectId, IReadOnlyList<HistoricalDiscipline> Disciplines,
    IReadOnlyList<HistoricalDocument> Documents, IReadOnlyList<string> Diagnostics)
{
    public decimal ComplementHours => Documents.Sum(x=>x.Hours);
    public decimal ComplementCost => Documents.Sum(x=>x.Cost);
    public decimal ExcessHours => Disciplines.Sum(x=>x.Balance.ExcessHours);
    public decimal ExcessCost => Disciplines.Sum(x=>x.Balance.ExcessCost);
    public static PackageHistoricalFacts Empty(string projectId) => new(projectId,Array.Empty<HistoricalDiscipline>(),Array.Empty<HistoricalDocument>(),Array.Empty<string>());
}

public static class PackageHistoricalRules
{
    public static HistoricalBalance Reconcile(decimal measuredHours, decimal measuredCost, decimal actualHours, decimal? actualCost,
        decimal contractHours, decimal contractCost, bool confirmed, bool eligible = true)
    {
        var messages=new List<string>();
        var rawHours=Math.Max(0,actualHours); var rawCost=Math.Max(0,actualCost??0);
        var hours=Math.Min(rawHours,Math.Max(0,contractHours)); var cost=Math.Min(rawCost,Math.Max(0,contractCost));
        var excessHours=Math.Max(0,rawHours-hours); var excessCost=Math.Max(0,rawCost-cost);
        if(excessHours>0)messages.Add("HH acima do teto disciplinar: excedente preservado.");
        if(excessCost>0)messages.Add("Custo do Timesheet acima do teto disciplinar: excedente preservado.");
        if(!actualCost.HasValue)messages.Add("Custo ausente em lançamento real: complemento financeiro bloqueado; não estimado.");
        if(!eligible)messages.Add("Sem documentos elegíveis ou evidência ambígua/pendente: complemento bloqueado.");
        var valid=measuredHours>=0&&measuredCost>=0&&measuredHours<=contractHours&&measuredCost<=contractCost;
        if(!valid)messages.Add("Medição confirmada incompatível com os limites contratuais atuais.");
        var addHours=confirmed&&valid&&eligible?Math.Max(0,Math.Min(measuredHours-hours,contractHours-rawHours)):0;
        var addCost=confirmed&&valid&&eligible&&actualCost.HasValue&&actualCost>=0?
            Math.Max(0,Math.Min(measuredCost-cost,contractCost-rawCost)):0;
        if(actualCost<0){addCost=0;messages.Add("Custo real negativo: complemento financeiro bloqueado.");}
        if(confirmed&&valid&&(rawHours>measuredHours||rawCost>measuredCost))
            messages.Add("Lançamentos atuais superam a medição confirmada; revisar no Planejamento.");
        return new(hours,cost,addHours,addCost,excessHours,excessCost,messages);
    }

    public static ProjectActualFacts Compose(ProjectActualFacts raw, PackageHistoricalFacts historical)
    {
        if(!string.Equals(raw.ProjectId,historical.ProjectId,StringComparison.Ordinal))throw new ArgumentException("Complemento de outro projeto.");
        if(raw.HistoricalMeasurement is not null)throw new InvalidOperationException("Fatos já consolidados: composição duplicada.");
        ActualValues Add(ActualValues? value,decimal h,decimal c)
        {
            var v=value??new ActualValues(null,null,null,null,0,0,0);
            return v with { Hh=h==0?v.Hh:(v.Hh??0)+h,
                KnownCalculatedCostSubtotal=c==0?v.KnownCalculatedCostSubtotal:(v.KnownCalculatedCostSubtotal??0)+c };
        }
        var byDiscipline=raw.ByDiscipline.ToDictionary(d=>PackageDisciplineRules.Key(d.Discipline),d=>d);
        foreach(var g in historical.Documents.GroupBy(d=>PackageDisciplineRules.Key(d.Discipline)))
        {
            byDiscipline.TryGetValue(g.Key,out var current);
            byDiscipline[g.Key]=new(current?.Discipline??g.First().Discipline,Add(current?.Values,g.Sum(d=>d.Hours),g.Sum(d=>d.Cost)));
        }
        var byDocument=raw.ByDocument.ToDictionary(d=>ProjectPlanningRules.Key(d.Document),d=>d);
        foreach(var g in historical.Documents.GroupBy(d=>ProjectPlanningRules.Key(d.Document)))
        {
            byDocument.TryGetValue(g.Key,out var current);
            byDocument[g.Key]=new(g.Key,Add(current?.Values,g.Sum(d=>d.Hours),g.Sum(d=>d.Cost)));
        }
        return raw with { TimesheetOriginal=raw.Total, HistoricalMeasurement=historical,
            Total=Add(raw.Total,historical.ComplementHours,historical.ComplementCost),
            ByDiscipline=byDiscipline.Values.ToArray(),ByDocument=byDocument.Values.ToArray(),
            Source=raw.Source+" Complemento derivado de MEDICAO_HISTORICA_CONFIRMADA; nunca lançamento de Timesheet.",
            CostSource=raw.CostSource+" + saldo financeiro de medição histórica confirmada (teto por disciplina/pacote)." };
    }
}
