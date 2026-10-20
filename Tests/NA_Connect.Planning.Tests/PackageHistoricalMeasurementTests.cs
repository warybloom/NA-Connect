using APS.WebApp.Services.Costs;
using APS.WebApp.Services.Planning;

namespace NA_Connect.Planning.Tests;

public class PackageHistoricalMeasurementTests
{
    private static HistoricalBalance Balance(decimal measuredH, decimal measuredC, decimal actualH, decimal? actualC, decimal contractH, decimal contractC)
        => PackageHistoricalRules.Reconcile(measuredH, measuredC, actualH, actualC, contractH, contractC, true);

    [Fact] public void Ets001_ReconcilesFourDisciplines()
    {
        var rows = new[] { Balance(190,19000,47,4700,190,19000), Balance(246,24600,139,13900,246,24600),
            Balance(144,14400,34,3400,144,14400), Balance(130,13000,0,0,130,13000) };
        Assert.Equal(490, rows.Sum(x=>x.ComplementHours));
        Assert.Equal(49000, rows.Sum(x=>x.ComplementCost));
        Assert.Equal(710, rows.Sum(x=>x.RecognizedHours+x.ComplementHours));
        Assert.Equal(71000, rows.Sum(x=>x.RecognizedCost+x.ComplementCost));
    }
    [Fact] public void NewTimesheetHours_ReplaceComplement()
    {
        var x=Balance(246,24600,239,23900,246,24600);
        Assert.Equal(7,x.ComplementHours);
        Assert.Equal(700,x.ComplementCost);
        Assert.Equal(390,143+x.ComplementHours+110+130);
    }
    [Fact] public void ConcentratedDocument_DoesNotLoseValidDisciplineHours()
    {
        var x=PackageDisciplineRules.Appropriate(new[]{("A",10m),("B",90m)}, new Dictionary<string,decimal>{{"A",99}},0);
        Assert.Equal(99,x.Sum(d=>d.HHApropriado));
        Assert.Equal(0,x.Sum(d=>d.HHExcedente));
        Assert.Equal(99,x.Single(d=>d.DocumentoCodigo=="A").HHDireto);
    }
    [Fact] public void DisciplineExcess_IsVisibleAndCapped()
    {
        var x=PackageDisciplineRules.Appropriate(new[]{("A",50m),("B",50m)},new Dictionary<string,decimal>{{"A",150}},0);
        Assert.Equal(100,x.Sum(d=>d.HHApropriado)); Assert.Equal(50,x.Sum(d=>d.HHExcedente));
        var b=Balance(100,10000,150,15000,100,10000);
        Assert.Equal(0,b.ComplementHours); Assert.Equal(0,b.ComplementCost);
        Assert.Equal(50,b.ExcessHours); Assert.Equal(5000,b.ExcessCost);
    }
    [Fact] public void UnconfirmedPackage_DoesNotGenerateComplement()
    {
        var x=PackageHistoricalRules.Reconcile(710,71000,220,22000,710,71000,false);
        Assert.Equal(0,x.ComplementHours); Assert.Equal(0,x.ComplementCost);
    }
    [Fact] public void MissingCost_IsNotInvented()
    {
        var x=Balance(710,71000,220,null,710,71000);
        Assert.Equal(490,x.ComplementHours); Assert.Equal(0,x.ComplementCost); Assert.NotEmpty(x.Diagnostics);
    }
    [Fact] public void PartialMeasurement_AndUnequalCost_AreIndependent()
    {
        var x=Balance(300,17000,220,13000,710,71000);
        Assert.Equal(80,x.ComplementHours); Assert.Equal(4000,x.ComplementCost);
    }
    [Fact] public void InvalidConfirmation_IsBlocked()
    {
        var x=Balance(800,80000,220,22000,710,71000);
        Assert.Equal(0,x.ComplementHours); Assert.Equal(0,x.ComplementCost); Assert.NotEmpty(x.Diagnostics);
    }
    [Fact] public void NoEligibleDocuments_BlocksComplement()
    {
        var x=PackageHistoricalRules.Reconcile(710,71000,220,22000,710,71000,true,false);
        Assert.Equal(0,x.ComplementHours); Assert.Equal(0,x.ComplementCost);
    }
    [Fact] public void ProjectComposition_PreservesRawAndAddsOnlyComplement()
    {
        var raw=ProjectActualRules.Read("project",new[]{new ActualEntry(1,"A","CIVIL","NORMA_EXATA",723.22m,null,60130.085713m,"CI")},null);
        var historical=new PackageHistoricalFacts("project",Array.Empty<HistoricalDiscipline>(),new[]{new HistoricalDocument(2,1,"CIVIL","A",490,49000)},Array.Empty<string>());
        var combined=PackageHistoricalRules.Compose(raw,historical);
        Assert.Equal(1213.22m,combined.Total.Hh);
        Assert.Equal(109130.085713m,combined.Total.KnownCalculatedCostSubtotal);
        Assert.Equal(723.22m,combined.TimesheetOriginal!.Hh);
        Assert.Equal(490,combined.HistoricalMeasurement!.ComplementHours);
        Assert.Equal(1,combined.Total.Entries);
        Assert.Throws<ArgumentException>(()=>PackageHistoricalRules.Compose(raw,historical with {ProjectId="other"}));
        var unchanged=PackageHistoricalRules.Compose(raw,PackageHistoricalFacts.Empty("project"));
        Assert.Equal(raw.Total,unchanged.Total);
    }
}
