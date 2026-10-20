using APS.WebApp.Services.Reports;

namespace NA_Connect.Planning.Tests;

public sealed class ResumoPhaseTests
{
    private static readonly DateTime Today = new(2026, 10, 14);
    private static PmoDocumento Doc(string code, params PmoGate[] gates) => new(code, "P", code, code, "CIVIL",
        100, 1000, null, null, null, null, null, "APPROVED", null, 70, 700)
        { Gates = gates, HHRealizado = 140, CustoRealizado = 1400, StatusDocumento = "EMITIDO", DisciplinaNorma = "Civil" };
    private static PmoGate Gate(int n, string? status = null, DateTime? date = null, bool cancelled = false, decimal? work = 10)
        => new(n, true, date, null, null, null) {DocumentStatus=status, Cancelado=cancelled, EsforcoHoras=work};

    [Fact] public void Tipo1HasNoArtificialPhases() => Assert.Empty(ResumoPhaseRules.Read(1, Today, [Doc("A", Gate(30))]).Fases);
    [Fact] public void GeneralEmissionNeverReplacesGateStatus()
    {
        var r=ResumoPhaseRules.Read(2,Today,[Doc("A",Gate(30,"APPROVED"),Gate(60,"IN PROGRESS",Today.AddDays(-1)),Gate(90,"CANCELED",null,true))]);
        Assert.Equal(1,r.Fases[0].Emitidos);Assert.Equal(0,r.Fases[1].Emitidos);
        Assert.Equal(1,r.Fases[1].Atrasados);Assert.Equal(1,r.Fases[2].Cancelados);
        Assert.Equal(0,r.Fases[2].AEmitir);
    }
    [Fact] public void SameDocumentCanParticipateInMultipleGatesButNotTwiceInOneGate()
    {
        var d=Doc("A",Gate(30),Gate(60));
        var r=ResumoPhaseRules.Read(2,Today,[d,d]);
        Assert.Single(r.Documentos);Assert.Equal(1,r.Fases[0].Total);Assert.Equal(1,r.Fases[1].Total);
        Assert.Equal(10,r.Fases[0].HhPrevisto);
    }
    [Fact] public void ReplannedDateHasPriorityAndNoDateIsNotOnTime()
    {
        var r=ResumoPhaseRules.Read(2,Today,[Doc("A",Gate(30,null,Today.AddDays(-2)) with {Reprogramada=Today.AddDays(1)},Gate(60),Gate(90,null,new DateTime(1900,1,1)))]);
        Assert.Equal(1,r.Fases[0].NoPrazo);Assert.Equal(0,r.Fases[0].Atrasados);
        Assert.Equal(1,r.Fases[1].SemPrazo);Assert.Equal(0,r.Fases[1].NoPrazo);Assert.Equal(1,r.Fases[2].SemPrazo);
    }
    [Fact] public void ActualAndCostAreNotAllocatedAndConsolidatedDocumentIsUntouched()
    {
        var d=Doc("A",Gate(30),Gate(60),Gate(90)) with {HHComplementoHistorico=40};
        var before=d;
        var r=ResumoPhaseRules.Read(2,Today,[d]);
        Assert.All(r.Fases,f=>{Assert.Null(f.HhRealizado);Assert.Null(f.SaldoHh);Assert.Null(f.ConsumoHh);Assert.Null(f.CustoPrevisto);Assert.Null(f.CustoRealizado);Assert.Null(f.ConsumoCusto);});
        Assert.Equal(before,d);Assert.Equal(140,d.HHRealizado);Assert.Equal(40,d.HHComplementoHistorico);
    }
    [Fact] public void IncompleteWorkDoesNotPresentAPartialSumAsThePhaseTotal()
    {
        var r=ResumoPhaseRules.Read(2,Today,[Doc("A",Gate(30)),Doc("B",Gate(30,work:null))]);
        Assert.Equal(2,r.Fases[0].Total);Assert.Null(r.Fases[0].HhPrevisto);Assert.Equal(1,r.Fases[0].SemHhPrevisto);
    }
    [Fact] public void ActivitiesKeepHoursButNeverCountAsDocuments()
    {
        var r=ResumoPhaseRules.Read(2,Today,[Doc("AT",Gate(30,work:20)) with {DocTypeCode="AT"}]);
        Assert.Equal(0,r.Fases[0].Total);Assert.Equal(20,r.Fases[0].HhPrevisto);
    }
    [Fact] public void ConflictingDuplicateGateDoesNotInventAnOpenStatus()
    {
        var r=ResumoPhaseRules.Read(2,Today,[Doc("A",Gate(30,"APPROVED")),Doc("A",Gate(30,"CANCELED",cancelled:true))]);
        Assert.Equal(1,r.Fases[0].Total);Assert.Equal(1,r.Fases[0].Conflitos);
        Assert.Null(r.Fases[0].Emitidos);Assert.Null(r.Fases[0].Cancelados);Assert.Null(r.Fases[0].AEmitir);
        Assert.Null(r.Fases[0].Atrasados);Assert.Null(r.Fases[0].NoPrazo);Assert.Null(r.Fases[0].SemPrazo);
    }
}
