using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.Costs;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Configuration;
using NA_Connect.Services.Coordination;
using OfficeOpenXml;
using Xunit;

namespace NA_Connect.Planning.Tests;

public sealed class PackageBackendRulesTests
{
    [Fact] public void CancellationAndAddendaPreserveIndependentTotals()
    {
        var cancelled=new[]{true,false,false};
        var hh=PackageAllocationRules.AllocateEffective(new[]{30m,20m,50m},20m,cancelled);
        var cost=PackageAllocationRules.AllocateEffective(new[]{300m,500m,200m},100m,cancelled);
        Assert.Equal(0m,hh[0].Effective);Assert.Equal(30m,hh[0].Original);
        Assert.Equal(120m,hh.Sum(x=>x.Effective));Assert.Equal(1100m,cost.Sum(x=>x.Effective));
        Assert.Equal(30m,hh.Sum(x=>x.Redistributed));Assert.Equal(300m,cost.Sum(x=>x.Redistributed));
        Assert.Equal(20m,hh.Sum(x=>x.Addendum));Assert.Equal(100m,cost.Sum(x=>x.Addendum));
        Assert.True(hh[2].Effective>hh[1].Effective);Assert.True(cost[1].Effective>cost[2].Effective);
    }
    [Fact] public void ZeroEligibleBasesUseEqualShares()
    {
        var r=PackageAllocationRules.AllocateEffective(new[]{12m,0m,0m},4m,new[]{true,false,false});
        Assert.Equal(8m,r[1].Effective);Assert.Equal(8m,r[2].Effective);
    }
    [Fact] public void ZeroAllBasesDistributeAddendumEqually()
    {
        var r=PackageAllocationRules.AllocateEffective(new[]{0m,0m},10m,new[]{false,false});
        Assert.Equal(5m,r[0].Effective);Assert.Equal(10m,r.Sum(x=>x.Effective));
    }
    [Fact] public void AllCancelledRejectAllocation()
    {
        Assert.Throws<InvalidOperationException>(()=>PackageAllocationRules.AllocateEffective(new[]{10m,20m},5m,new[]{true,true}));
    }
    [Fact] public void ManualBaseClosesBeforeAddendum()
    {
        var b=CostAllocationRules.Allocate(100m,new[]{((decimal?)25m,"MANUAL"),((decimal?)75m,"MANUAL")});
        var r=PackageAllocationRules.AllocateEffective(b.Values.Select(x=>x.Value).ToArray(),20m,new[]{false,false});
        Assert.Equal(30m,r[0].Effective);Assert.Equal(90m,r[1].Effective);
    }
    [Fact] public void StatusCostIsIndependentOfTimesheetAndHoursAreCapped()
    {
        var r=PackageAllocationRules.Recognize(500m,50m,10000m,30m,false);
        Assert.Equal(50m,r.Hours);Assert.Equal(450m,r.Excess);Assert.Equal(3000m,r.Cost);
        Assert.Equal(3000m,PackageAllocationRules.Recognize(0m,50m,10000m,30m,false).Cost);
        Assert.Equal(0m,PackageAllocationRules.Recognize(500m,50m,10000m,null,false).Cost);
        Assert.Equal(0m,PackageAllocationRules.Recognize(500m,50m,10000m,100m,true).Cost);
        Assert.Equal(0m,PackageAllocationRules.Recognize(500m,50m,10000m,100m,true).Hours);
        Assert.Equal(0m,PackageAllocationRules.Recognize(500m,50m,10000m,100m,false,true).Cost);
    }
    [Fact] public void CancellationAliasesAlwaysExclude()
    {
        foreach(var s in new[]{"CANCELADO"," canceled ","Cancelled"})Assert.True(PackageAllocationRules.IsCancelled(s));
    }
    [Fact] public void LargestPrefixAndExactMatchUseCanonicalName()
    {
        var m=new Dictionary<string,string>{{"P","short"},{"PX","Elétrica"},{"CI","civil"},{"CIM","instrumentation"}};
        var c=new Dictionary<string,string>{{"ELETRICA","ELECTRICAL"},{"INSTRUMENTATION","INSTRUMENTATION"}};
        foreach(var code in new[]{"PX2","PXM","PX0"}) {var r=CoordinationDisciplineResolver.Resolve(code,1,m,c);Assert.Equal("PX",r.MatchedCode);Assert.Equal("ELECTRICAL",r.Discipline);Assert.Equal(CoordinationDisciplineSources.NormaDoisCaracteres,r.Source);}
        // Regra 15/10/2026: CIM é CI (2 primeiros caracteres), nunca a sigla CIM da Norma.
        Assert.Equal("CI",CoordinationDisciplineResolver.Resolve("CIM",1,m,c).MatchedCode);
        Assert.False(CoordinationDisciplineResolver.Resolve("CI",1,m,c).Matched);
        Assert.False(CoordinationDisciplineResolver.Resolve("PX",null,m,c).Matched);
    }
    [Fact] public void CanonicalCatalogUsesStandardNameAndRejectsConflictingAliases()
    {
        var map=DisciplineStandardRepository.CanonicalNameMap(new[]{new DisciplineStandardDto(1,"ELETRICA","Elétrica","Electrical",true,new[]{"EL","duplicate"}),new DisciplineStandardDto(2,"CIVIL","Civil","Civil",true,new[]{"duplicate"})});
        Assert.Equal("ELETRICA",map["ELECTRICAL"]);Assert.False(map.ContainsKey("DUPLICATE"));
    }
    [Fact] public void MatchingNormalizesNamesAndNeverChoosesFirstPackage()
    {
        Assert.True(CostTimesheetMatchingRules.Matches(null,new[]{("JOAO SILVA","EXATO")}," João.Silva "));
        Assert.True(CostTimesheetMatchingRules.Matches("João Silva",new[]{("unrelated","EXATO")},"JOAO-SILVA / X"));
        Assert.False(CostTimesheetMatchingRules.Matches(null,new[]{("JOAO SILVA","EXATO")},"JOAO SILVA X"));
        var candidates=new[]{new PackageDocumentMatch(1,"D","João Silva",Array.Empty<(string,string)>()),new PackageDocumentMatch(2,"D","joao.silva",Array.Empty<(string,string)>())};
        var r=CostTimesheetMatchingRules.AssignPackages(new[]{new TimesheetWork("João-Silva","D",500m)},candidates).Single();Assert.True(r.Ambiguous);Assert.True(r.PackageId is null);
        var pending=CostTimesheetMatchingRules.AssignPackages(new[]{new TimesheetWork("João-Silva","UNKNOWN",500m)},candidates).Single();Assert.True(pending.PackageNameOnly);Assert.True(pending.PackageId is null);
    }
    [Fact] public void ResidualAndCancellationConserveTotalsAcrossManyWeights()
    {
        var random=new Random(1234);
        for(var n=2;n<20;n++)for(var iteration=0;iteration<50;iteration++)
        {
            var bases=Enumerable.Range(0,n).Select(_=>(decimal)random.Next(0,100)/100m).ToArray();
            var cancelled=Enumerable.Range(0,n).Select(i=>i!=n-1&&random.Next(2)==0).ToArray();
            var r=PackageAllocationRules.AllocateEffective(bases,13.123456m,cancelled);
            Assert.Equal(bases.Sum()+13.123456m,r.Sum(x=>x.Effective));
            for(var i=0;i<n;i++){Assert.True(r[i].Effective>=0);if(cancelled[i])Assert.Equal(0m,r[i].Effective);}
        }
    }
    [Fact] public async Task ImportAcceptsHumanHeadersAndComputesCost()
    {
        ExcelPackage.LicenseContext=LicenseContext.NonCommercial;
        using var workbook=new ExcelPackage();var ws=workbook.Workbook.Worksheets.Add("Pacotes");
        var headers=new[]{"Pacote/Profissional","Número do documento","HH previsto do documento","Valor/hora","CustoDocumento"};
        for(var i=0;i<headers.Length;i++)ws.Cells[1,i+1].Value=headers[i];
        ws.Cells[2,1].Value="João";ws.Cells[2,2].Value="BML011.013-000-PX2-XX-001";ws.Cells[2,3].Value=50;ws.Cells[2,4].Value=200.5;ws.Cells[2,5].Value=1;
        using var stream=new MemoryStream(workbook.GetAsByteArray());
        var result=await new CostManagementService(new ConfigurationBuilder().Build()).PreviewExcelAsync("P",stream,CancellationToken.None);
        Assert.Equal(1,result.Valid);Assert.Equal(10025m,result.Rows.Single().CustoPrevistoDocumento);Assert.Equal(50m,result.Rows.Single().HHPrevistaDocumento);
    }
    [Fact] public async Task LegacyImportStillAcceptsExplicitDocumentCost()
    {
        ExcelPackage.LicenseContext=LicenseContext.NonCommercial;
        using var workbook=new ExcelPackage();var ws=workbook.Workbook.Worksheets.Add("Pacotes");
        ws.Cells[1,1].Value="Pacote";ws.Cells[1,2].Value="DocumentoCodigo";ws.Cells[1,3].Value="HHAlocada";ws.Cells[1,4].Value="CustoDocumento";
        ws.Cells[2,1].Value="A";ws.Cells[2,2].Value="D";ws.Cells[2,3].Value=10;ws.Cells[2,4].Value=1000;
        using var stream=new MemoryStream(workbook.GetAsByteArray());
        var result=await new CostManagementService(new ConfigurationBuilder().Build()).PreviewExcelAsync("P",stream,CancellationToken.None);
        Assert.Equal(1,result.Valid);Assert.Equal(1000m,result.Rows.Single().CustoPrevistoDocumento);
    }
}
