using APS.WebApp.Services.Costs;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services.Coordination;
using Microsoft.Data.SqlClient;

namespace NA_Connect.Planning.Tests;

// Explicit opt-in, read-only against the real local database. No Timesheet or LDP writes.
public class PackageHistoricalSqlTests
{
    const string Project="b.9907135f-ff67-43bb-887b-20c3a7f75807";
    private static IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {["ConnectionStrings:LocalConnection"]="Server=.\\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=true;TrustServerCertificate=true"}).Build();
    private static PmoActualFactsProvider Provider(IConfiguration c)=>new(c,new CoordinationStandardRepository(c,NullLogger<CoordinationStandardRepository>.Instance));

    [Ets001SqlFact] public async Task RealEts001_ConsolidatedReportAndPackage_Reconcile()
    {
        var c=Configuration();
        var actual=await Provider(c).ReadAsync(Project,CancellationToken.None);
        var h=actual.HistoricalMeasurement!;
        Assert.Equal(490,h.ComplementHours);Assert.Equal(49000,h.ComplementCost);Assert.Empty(h.Diagnostics);
        Assert.Equal(723.22m,actual.TimesheetOriginal!.Hh);
        Assert.Equal(1213.22m,actual.Total.Hh);
        Assert.Equal(109130.085713m,actual.Total.KnownCalculatedCostSubtotal);
        Assert.Equal(actual.Total.Hh,actual.ByDiscipline.Sum(d=>d.Values.Hh??0));
        Assert.Equal(actual.Total.KnownCalculatedCostSubtotal,actual.ByDiscipline.Sum(d=>d.Values.KnownCalculatedCostSubtotal??0));
        var service=new CostManagementService(c);
        var lines=(await service.RecognitionAsync(Project,CancellationToken.None)).Where(x=>x.PackageId==2).ToArray();
        Assert.Equal(710,lines.Sum(x=>x.RecognizedHours)); Assert.Equal(71000,lines.Sum(x=>x.RecognizedCost));
        Assert.Equal(220,lines.Sum(x=>x.TimesheetHours)); Assert.Equal(0,lines.Sum(x=>x.ExcessHours));
        var report=await new GestaoVistaReportRepository(c,new PmoPlanningFactsProvider(c),Provider(c)).ReadAsync(Project,CancellationToken.None);
        Assert.Equal(1213.22m,report.HhRealizado.Total); Assert.Equal(109130.085713m,report.HhRealizado.CustoTotal);
        Assert.Equal(490,report.Documentos.Sum(d=>d.HHComplementoHistorico));
        Assert.Equal(49000,report.Documentos.Sum(d=>d.CustoComplementoHistorico));
        Assert.Equal(1043.84m,decimal.Round(report.Planning.PlannedHhTotal??0,2));
        Assert.Equal(110269.29m,decimal.Round(report.Planning.DocumentPlannedCost??0,2));
        var detail=await service.DisciplinePackageDetailAsync(Project,2,CancellationToken.None);
        Assert.Equal(710,detail.HhGerencial);Assert.Equal(71000,detail.CustoGerencial);
        Assert.Contains(detail.Excedentes,x=>x.Situacao=="ALERTA_DOCUMENTAL_APROPRIADO");
        var rawDocs=report.Documentos.Select(d=>d with {HHRealizado=d.HHTimesheetOriginal??d.HHRealizado,
            CustoRealizado=d.CustoTimesheetOriginal??d.CustoRealizado,HHComplementoHistorico=0,CustoComplementoHistorico=0}).ToArray();
        var rawAdvance=PmoAvancoRules.Calcular(report.TipoLdp,report.DataReferencia,rawDocs,report.Planning.PlannedHhTotal);
        Assert.Equal(rawAdvance.Fisico,report.Avanco.Fisico);Assert.Equal(rawAdvance.Previsto,report.Avanco.Previsto);
        if(Environment.GetEnvironmentVariable("ETS001_REPORT_PATH") is {Length:>0} path)
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions
                {WriteIndented=true,PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase}));
    }
    [Ets001SqlFact] public async Task RealSnapshot_InMemoryNewLaunch_ReducesComplement_WithoutDatabaseMutation()
    {
        var c=Configuration();var current=await Provider(c).ReadAsync(Project,CancellationToken.None);
        // Nine real documentary groups; individual ids are irrelevant for package matching, not persisted.
        var entries=new List<ActualEntry>();
        await using var conn=new SqlConnection(c.GetConnectionString("LocalConnection"));await conn.OpenAsync();
        await using var cmd=new SqlCommand("SELECT Id,DocumentoCodigo_Corrigido,HorasTrabalhadas,CustoCalculado,NomeProfissional FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial WHERE Project_ID_ACC=@p AND NomeProfissional=N'Everton Eustaquio'",conn);
        cmd.Parameters.AddWithValue("@p",Project);
        await using(var reader=await cmd.ExecuteReaderAsync())
            while(await reader.ReadAsync())
            {
                var doc=reader.GetString(1);var discipline=current.HistoricalMeasurement!.Documents.Single(d=>d.Document==doc).Discipline;
                entries.Add(new(reader.GetInt64(0),doc,discipline,"NORMA_EXATA",reader.GetDecimal(2),null,reader.GetDecimal(3),doc.Split('-')[2],reader.GetString(4)));
            }
        var additional=current.HistoricalMeasurement!.Documents.First(d=>d.Discipline=="PIPING");
        entries.Add(new(-1,additional.Document,"PIPING","NORMA_EXATA",100,null,10000,"TU","Everton Eustaquio"));
        var h=await new CostManagementService(c).HistoricalMeasurementAsync(Project,entries,CancellationToken.None);
        Assert.Equal(390,h.ComplementHours);Assert.Equal(39000,h.ComplementCost);
        Assert.Equal(710,h.Disciplines.Sum(d=>d.Balance.RecognizedHours+d.Balance.ComplementHours));
        Assert.Equal(71000,h.Disciplines.Sum(d=>d.Balance.RecognizedCost+d.Balance.ComplementCost));
        var foreign=await new CostManagementService(c).HistoricalMeasurementAsync("b.c3e20d9d-779e-423c-9381-abec38cf11cb",entries,CancellationToken.None);
        Assert.Empty(foreign.Disciplines);Assert.Equal(0,foreign.ComplementHours);
    }
}

public sealed class Ets001SqlFactAttribute : FactAttribute
{
    public Ets001SqlFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("ETS001_SQL_TESTS")!="1")Skip="Read-only SQL audit requires ETS001_SQL_TESTS=1.";
    }
}
