using APS.WebApp.Services.Costs;
using Xunit;

namespace NA_Connect.Planning.Tests;

public sealed class CostAllocationRulesTests
{
    [Fact] public void CostPerHour_UsesDecimal() => Assert.Equal(100m, CostAllocationRules.CostPerHour(16000m,160m));
    [Fact] public void InactiveItemsAreRepresentedByCallerAndRateDoesNotUseDouble() => Assert.Equal(50m, CostAllocationRules.CostPerHour(8000m,160m));
    [Fact] public void AutoAllocationClosesExactly() { var r=CostAllocationRules.Allocate(100m,new[]{((decimal?)null,"AUTO"),((decimal?)null,"AUTO"),((decimal?)null,"AUTO")}); Assert.Equal(100m,r.Total); }
    [Fact] public void ManualValuesArePreservedAndAutoGetsRemainder() { var r=CostAllocationRules.Allocate(1000m,new[]{((decimal?)100m,"MANUAL"),((decimal?)50m,"MANUAL"),((decimal?)null,"AUTO")}); Assert.Equal(new[]{100m,50m,850m},r.Values.Select(x=>x.Value)); }
    [Fact] public void ManualAboveTotalIsRejected() => Assert.Throws<InvalidOperationException>(()=>CostAllocationRules.Allocate(100m,new[]{((decimal?)101m,"MANUAL")}));
    [Fact] public void AddendumIsIncludedInEffectiveTotalsByCaller() { var r=CostAllocationRules.Allocate(1150m,new[]{((decimal?)null,"AUTO")}); Assert.Equal(1150m,r.Total); }
    [Fact] public void RecognitionCapsHoursAndCost() { var r=CostAllocationRules.Recognize(62m,50m,5000m,100m); Assert.Equal(50m,r.RecognizedHours); Assert.Equal(12m,r.ExcessHours); Assert.Equal(5000m,r.RecognizedCost); Assert.Equal(1200m,r.ExcessCost); }
    [Fact] public void FixedPackageCostUsesProportionAndNeverExceedsContract() { var r=CostAllocationRules.Recognize(25m,50m,5000m,null); Assert.Equal(2500m,r.TheoreticalCost); Assert.Equal(2500m,r.RecognizedCost); Assert.Equal(0m,r.ExcessCost); }
    [Fact] public void FixedPackageExcessIsSeparate() { var r=CostAllocationRules.Recognize(62m,50m,5000m,null); Assert.Equal(5000m,r.RecognizedCost); Assert.Equal(1200m,r.ExcessCost); }
    [Fact] public void DocumentNormalizationIsStable() => Assert.Equal("DOC A",CostAllocationRules.NormalizeDocument(" doc   a "));
    [Fact] public void AutoAllocationAppliesResidualDeterministicallyAndCloses() { var r=CostAllocationRules.Allocate(100m,new[]{((decimal?)null,"AUTO"),((decimal?)null,"AUTO"),((decimal?)null,"AUTO")}); Assert.Equal(100m,r.Total); Assert.Equal(1,r.Values.Count(x=>x.Value==33.333334m)); }
    [Fact] public void ManualTotalLeavesZeroForAuto() { var r=CostAllocationRules.Allocate(100m,new[]{((decimal?)100m,"MANUAL"),((decimal?)null,"AUTO")}); Assert.Equal(0m,r.Values[1].Value); }
    [Fact] public void PackageMatchAssignsEachTimesheetRowOnce() { var rows=new[]{new TimesheetWork("JOAO SILVA - X","DOC-1",10m)};var candidates=new[]{new PackageDocumentMatch(1,"DOC-1","JOAO SILVA",Array.Empty<(string,string)>())};var a=CostTimesheetMatchingRules.AssignPackages(rows,candidates);Assert.Equal(1,a.Single().PackageId);Assert.Equal(10m,rows.Where((_,i)=>a[i].PackageId is not null).Sum(x=>x.Hours)); }
    [Fact] public void PackageMatchTreatsCompetingPackagesAsAmbiguousWithoutDuplication() { var rows=new[]{new TimesheetWork("JOAO SILVA - X","DOC-1",10m)};var candidates=new[]{new PackageDocumentMatch(1,"DOC-1","JOAO SILVA",Array.Empty<(string,string)>()),new PackageDocumentMatch(2,"DOC-1","JOAO SILVA",Array.Empty<(string,string)>())};var a=CostTimesheetMatchingRules.AssignPackages(rows,candidates);Assert.True(a.Single().Ambiguous);Assert.Equal(0m,rows.Where((_,i)=>a[i].PackageId is not null).Sum(x=>x.Hours));Assert.Equal(10m,rows.Where((_,i)=>a[i].PackageId is null).Sum(x=>x.Hours)); }
    [Fact] public void ProjectCostCompositionUsesDecimalAndKeepsSixFractionDigits() { var total=ProjectCostHistoryRules.MonthlyCost(11000m,new[]{new ProfessionalCostComponentRule("FIXO",660m,null,"REMUNERACAO"),new ProfessionalCostComponentRule("FIXO",1600m,null,"REMUNERACAO"),new ProfessionalCostComponentRule("FIXO",500m,null,"REMUNERACAO")});Assert.Equal(13760m,total);Assert.Equal(78.181818181818181818181818182m,ProjectCostHistoryRules.HourlyRate(new(1,new(2026,1,1),null,176m,11000m,null),new[]{new ProfessionalCostComponentRule("FIXO",660m,null,"REMUNERACAO"),new ProfessionalCostComponentRule("FIXO",1600m,null,"REMUNERACAO"),new ProfessionalCostComponentRule("FIXO",500m,null,"REMUNERACAO")})); }
    [Fact] public void ProfessionalEncargoPercentageIsBasedOnRemuneration() => Assert.Equal(600m,ProjectCostHistoryRules.MonthlyCost(10000m,new[]{new ProfessionalCostComponentRule("PERCENTUAL",0m,6m,"REMUNERACAO")} )-10000m);
    [Fact] public void ProfessionalFixedEncargoIsAddedAsFixedValue() => Assert.Equal(10750m,ProjectCostHistoryRules.MonthlyCost(10000m,new[]{new ProfessionalCostComponentRule("FIXO",750m,null,"REMUNERACAO")}));
    [Fact] public void ManualHourlyRateOverridesCalculatedComposition() => Assert.Equal(95m,ProjectCostHistoryRules.HourlyRate(new(1,new(2026,1,1),null,176m,13760m,95m),Array.Empty<ProfessionalCostComponentRule>()));
    [Fact] public void TimesheetDateResolvesDifferentRatesAcrossPeriods() { var periods=new[]{new ProfessionalCostPeriodRule(1,new(2026,1,1),new(2026,6,30),176m,14080m,null),new ProfessionalCostPeriodRule(2,new(2026,7,1),new(2026,12,31),176m,17600m,null)};Assert.Equal(80m,ProjectCostHistoryRules.HourlyRate(ProjectCostHistoryRules.Resolve(periods,new(2026,5,10))!,Array.Empty<ProfessionalCostComponentRule>()));Assert.Equal(100m,ProjectCostHistoryRules.HourlyRate(ProjectCostHistoryRules.Resolve(periods,new(2026,8,10))!,Array.Empty<ProfessionalCostComponentRule>())); }
    [Fact] public void OverlappingProfessionalPeriodsAreRejected() => Assert.Throws<InvalidOperationException>(()=>ProjectCostHistoryRules.EnsureNoOverlap(new(2026,5,1),new(2026,12,31),new[]{new ProfessionalCostPeriodRule(1,new(2026,1,1),new(2026,6,30),176m,10000m,null)}));
    [Fact] public void HistoricalBackfillBeforeOpenPeriodIsAllowedWithoutChangingTheFuturePeriod()
    {
        var future = new ProfessionalCostPeriodRule(1, new(2026,8,1), null, 176m, 10000m, null);
        ProjectCostHistoryRules.EnsureNoOverlap(new(2025,6,1), new(2026,7,31), new[] { future });
        Assert.Null(future.End);
    }
    [Fact] public void HistoricalOpenBackfillIsRejectedWhenItWouldReachFuturePeriod() => Assert.Throws<InvalidOperationException>(() =>
        ProjectCostHistoryRules.EnsureNoOverlap(new(2025,6,1), null, new[] { new ProfessionalCostPeriodRule(1,new(2026,8,1),null,176m,10000m,null) }));
    [Fact] public void HistoricalGapBetweenPeriodsIsAllowed() => ProjectCostHistoryRules.EnsureNoOverlap(new(2026,1,1), new(2026,1,31), new[]
    {
        new ProfessionalCostPeriodRule(1,new(2025,1,1),new(2025,12,31),176m,10000m,null),
        new ProfessionalCostPeriodRule(2,new(2026,2,1),null,176m,10000m,null)
    });
    [Fact] public void PartialHistoricalOverlapIsRejected() => Assert.Throws<InvalidOperationException>(() =>
        ProjectCostHistoryRules.EnsureNoOverlap(new(2026,7,31), new(2026,8,2), new[] { new ProfessionalCostPeriodRule(1,new(2026,8,1),null,176m,10000m,null) }));
    [Fact] public void LaterPeriodStillRequiresExplicitOpenPeriodClosure() => Assert.True(ProjectCostHistoryRules.RequiresClosingOpenPeriod(new(2026,10,1),new(2026,1,1)));
    [Fact] public void HistoricalBackfillNeverRequestsOpenPeriodClosure() => Assert.False(ProjectCostHistoryRules.RequiresClosingOpenPeriod(new(2025,6,1),new(2026,8,1)));
    [Fact] public void MissingEffectivePeriodStaysMissing() => Assert.Null(ProjectCostHistoryRules.Resolve(new[]{new ProfessionalCostPeriodRule(1,new(2026,1,1),new(2026,6,30),176m,10000m,null)},new(2026,8,1)));
    [Fact] public void ProjectTaxPercentageUsesConfiguredRevenueBase() => Assert.Equal(20000m,ProjectCostHistoryRules.Tax(20m,null,"PERCENTUAL","RECEITA",100000m,70000m));
    [Fact] public void RemainingCanBeNegativeAndExcessIsExplicit() { Assert.Equal(new CostRemainingRule(100000m,70000m,30000m,0m),ProjectCostHistoryRules.Remaining(100000m,70000m));Assert.Equal(new CostRemainingRule(100000m,120000m,-20000m,20000m),ProjectCostHistoryRules.Remaining(100000m,120000m)); }
}
