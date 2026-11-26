using APS.WebApp.Services.DocumentQuality;

namespace APS.WebApp.Services.Costs;

/// <summary>Independent HH and cost weights; final recipient absorbs decimal rounding.</summary>
public sealed record PackageBudgetAllocation(decimal Original,decimal Effective,decimal Redistributed,decimal Addendum);

public static class PackageAllocationRules
{
    public static bool IsCancelled(string? status) => DocumentStatusCanonical.Text(status) is "CANCELADO" or "CANCELED" or "CANCELLED";

    public static IReadOnlyList<decimal> Distribute(decimal total,IReadOnlyList<decimal> weights)
    {
        if(total<0||weights.Any(x=>x<0))throw new ArgumentException("Valores de rateio não podem ser negativos.");
        if(weights.Count==0)throw new InvalidOperationException("Sem documentos elegíveis para preservar o total do pacote.");
        var sum=weights.Sum();var remaining=total;var result=new List<decimal>(weights.Count);
        for(var i=0;i<weights.Count;i++)
        {
            var value=i==weights.Count-1?remaining:Math.Min(remaining,decimal.Round(sum>0?total*weights[i]/sum:total/weights.Count,6,MidpointRounding.ToEven));
            result.Add(value);remaining-=value;
        }
        return result;
    }

    public static IReadOnlyList<PackageBudgetAllocation> AllocateEffective(IReadOnlyList<decimal> baseValues,decimal addendum,IReadOnlyList<bool> cancelled)
    {
        if(baseValues.Count!=cancelled.Count||baseValues.Any(x=>x<0)||addendum<0)throw new ArgumentException("Bases de alocação inválidas.");
        var eligible=Enumerable.Range(0,baseValues.Count).Where(i=>!cancelled[i]).ToArray();
        if(eligible.Length==0)throw new InvalidOperationException("Todos os documentos ativos estão cancelados: totais preservados no pacote, sem alocação/reconhecimento de HH ou custo.");
        var cancelledTotal=Enumerable.Range(0,baseValues.Count).Where(i=>cancelled[i]).Sum(i=>baseValues[i]);
        var redistributed=Distribute(cancelledTotal,eligible.Select(i=>baseValues[i]).ToArray());
        var effective=eligible.Select((i,j)=>baseValues[i]+redistributed[j]).ToArray();
        var adds=Distribute(addendum,effective);
        var output=baseValues.Select(x=>new PackageBudgetAllocation(x,0,0,0)).ToArray();
        for(var j=0;j<eligible.Length;j++) {var i=eligible[j];output[i]=new(baseValues[i],effective[j]+adds[j],redistributed[j],adds[j]);}
        return output;
    }

    public static (decimal Hours,decimal Excess,decimal Cost) Recognize(decimal timesheet,decimal effectiveHours,decimal effectiveCost,decimal? statusPercent,bool cancelled,bool allocationError=false)
    {
        timesheet=Math.Max(0,timesheet);
        var hours=cancelled||allocationError?0:Math.Min(timesheet,Math.Max(0,effectiveHours));
        var cost=cancelled||allocationError||statusPercent is null?0:effectiveCost*statusPercent.Value/100m;
        return (hours,timesheet-hours,cost);
    }
}
