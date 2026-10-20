using System.Data;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Costs;

public sealed partial class CostManagementService
{
    private sealed record HistoricalConfirmation(long Id,int PackageId,string ProjectId,string Professional,DateTime Date,string Author,string Origin,
        decimal Hours,decimal Cost,string Discipline,decimal DisciplineHours,decimal DisciplineCost);

    /// <summary>Reconcilia o MESMO snapshot lido pelo PMO. Não relê/soma Timesheet de outro projeto e não persiste complementos congelados.</summary>
    public async Task<PackageHistoricalFacts> HistoricalMeasurementAsync(string projectId,IReadOnlyList<ActualEntry> entries,CancellationToken ct)
    {
        var confirmations=new List<HistoricalConfirmation>();
        await using(var c=await Open(ct))
        {
            await using(var exists=new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'costs.PackageMeasuredHistorical',N'U') IS NULL THEN 0 ELSE 1 END",c))
                if(Convert.ToInt32(await exists.ExecuteScalarAsync(ct))==0)return PackageHistoricalFacts.Empty(projectId);
            await using var cmd=Cmd("""
                SELECT h.Id,h.PackageId,h.Project_ID_ACC,h.ProfessionalName,h.ConfirmedAtUtc,h.ConfirmedBy,h.Origin,
                       h.ConfirmedHours,h.ConfirmedCost,d.Discipline,d.ConfirmedHours,d.ConfirmedCost
                FROM costs.PackageMeasuredHistorical h JOIN costs.PackageMeasuredHistoricalDiscipline d ON d.ConfirmationId=h.Id
                WHERE h.Project_ID_ACC=@p AND h.Status='CONFIRMADA'
                ORDER BY h.Id,d.Discipline
                """,c,projectId);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))confirmations.Add(new(reader.GetInt64(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3),
                reader.GetDateTime(4),reader.GetString(5),reader.GetString(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetString(9),reader.GetDecimal(10),reader.GetDecimal(11)));
        }
        if(confirmations.Count==0)return PackageHistoricalFacts.Empty(projectId);
        var packages=await PackagesAsync(projectId,ct);
        var stored=(await StoredDisciplinesAsync(projectId,null,ct)).GroupBy(d=>d.PackageId).ToDictionary(g=>g.Key,g=>g.ToList());
        var context=await LoadPackageContextAsync(projectId,ct);
        var ldp=await LoadLdpDocumentsAsync(projectId,context,ct);
        var plans=packages.Where(p=>p.Ativo&&PackageModel.IsDiscipline(p.ModeloCadastro)).ToDictionary(p=>p.Id,
            p=>PackageDisciplineRules.Preview(InputFrom(p,stored.GetValueOrDefault(p.Id)??new()),ldp,NA_Connect.Services.Coordination.CoordinationDisciplineResolver.SemDisciplinaLabel));
        var aliases=await PackageAliasesAsync(projectId,ct);
        var rows=entries.Select(e=>new TimesheetWork(e.ProfessionalName??"",e.Document??"",e.Hh??0,null,e.NormalizedSigla,e.Discipline)).ToList();
        var documentAssignments=CostTimesheetMatchingRules.AssignPackages(rows,await DocumentPackageCandidatesAsync(projectId,packages,ct));
        var free=documentAssignments.Where(a=>a.PackageId is null&&!a.Ambiguous&&!a.PackageNameOnly).Select(a=>a.RowIndex);
        var matchers=packages.Where(p=>plans.ContainsKey(p.Id)).Select(p=>new DisciplinePackageMatcher(p.Id,PackageFallback(p),
            aliases.TryGetValue(p.Id,out var a)?a:new(),plans[p.Id].Disciplinas.ToDictionary(d=>d.Disciplina,
                d=>(IReadOnlySet<string>)d.Documentos.Select(x=>Normalize(x.DocumentoCodigo)).ToHashSet(StringComparer.Ordinal)))).ToList();
        var assignments=PackageDisciplineRules.AssignRows(rows,free,matchers,ldp.Select(d=>d.Codigo).ToHashSet(StringComparer.Ordinal),r=>r.Disciplina);
        var output=new List<HistoricalDiscipline>(); var documents=new List<HistoricalDocument>(); var diagnostics=new List<string>();
        foreach(var group in confirmations.GroupBy(c=>c.PackageId))
        {
            var h=group.First(); var package=packages.SingleOrDefault(p=>p.Id==group.Key);
            var valid=package is not null&&package.Ativo&&PackageModel.IsDiscipline(package.ModeloCadastro)&&plans.ContainsKey(group.Key)
                &&group.Sum(x=>x.DisciplineHours)==h.Hours&&group.Sum(x=>x.DisciplineCost)==h.Cost
                &&h.Hours<=package.HHBase&&h.Cost<=package.CustoBase&&plans[group.Key].HHTotal<=package.HHBase&&plans[group.Key].CustoTotal<=package.CustoBase
                &&CostTimesheetMatchingRules.NormalizeName(h.Professional)==CostTimesheetMatchingRules.NormalizeName(PackageFallback(package));
            if(!valid){diagnostics.Add($"Pacote {group.Key}: confirmação incompatível com identidade/totais atuais; complemento bloqueado.");continue;}
            var matcher=matchers.Single(m=>m.PackageId==group.Key);
            var mine=assignments.Where(a=>a.PackageId==group.Key).ToList();
            var relevant=Enumerable.Range(0,rows.Count).Where(i=>CostTimesheetMatchingRules.Matches(matcher.FallbackAlias,matcher.Aliases,rows[i].Name)).ToList();
            var accounted=mine.Where(a=>a.Kind is DisciplineRowKind.Direto or DisciplineRowKind.SemDocumento).Select(a=>a.RowIndex).ToHashSet();
            var uncertain=relevant.Any(i=>!accounted.Contains(i)||entries[i].Hh is null||entries[i].Hh<0);
            if(uncertain)diagnostics.Add($"Pacote {group.Key}: evidência ambígua, fora da abrangência ou sem classificação; complemento bloqueado até tratamento.");
            foreach(var confirmation in group)
            {
                var plan=plans[group.Key].Disciplinas.SingleOrDefault(d=>PackageDisciplineRules.Key(d.Disciplina)==PackageDisciplineRules.Key(confirmation.Discipline));
                if(plan is null){diagnostics.Add($"Pacote {group.Key}: disciplina confirmada {confirmation.Discipline} não cadastrada.");continue;}
                var indices=mine.Where(a=>a.Disciplina==plan.Disciplina&&a.Kind is DisciplineRowKind.Direto or DisciplineRowKind.SemDocumento).Select(a=>a.RowIndex).ToArray();
                var hours=indices.Sum(i=>Math.Max(0,entries[i].Hh??0));
                decimal? cost=indices.Any(i=>entries[i].CustoCalculado is null||entries[i].CustoCalculado<0)?null:indices.Sum(i=>entries[i].CustoCalculado??0);
                var balance=PackageHistoricalRules.Reconcile(confirmation.DisciplineHours,confirmation.DisciplineCost,hours,cost,
                    plan.HHPrevisto,plan.CustoPrevisto,true,plans[group.Key].Valido&&plan.Valido&&plan.Documentos.Count>0&&!uncertain);
                output.Add(new(group.Key,h.Id,package!.NomeReferencia,h.Professional,plan.Disciplina,confirmation.DisciplineHours,confirmation.DisciplineCost,
                    plan.HHPrevisto,plan.CustoPrevisto,hours,cost,balance,h.Date,h.Author,h.Origin));
                diagnostics.AddRange(balance.Diagnostics.Select(s=>$"{package.NomeReferencia}/{plan.Disciplina}: {s}"));
                if(plan.Valido&&plan.Documentos.Count>0)
                {
                    var splitH=PackageDisciplineRules.EqualSplit(balance.ComplementHours,plan.Documentos.Count,6);
                    var splitC=PackageDisciplineRules.EqualSplit(balance.ComplementCost,plan.Documentos.Count,6);
                    var direct=indices.Where(i=>!string.IsNullOrWhiteSpace(entries[i].Document))
                        .GroupBy(i=>Normalize(entries[i].Document)).ToDictionary(g=>g.Key,g=>g.Sum(i=>Math.Max(0,entries[i].Hh??0)));
                    var noDocument=indices.Where(i=>string.IsNullOrWhiteSpace(entries[i].Document)).Sum(i=>Math.Max(0,entries[i].Hh??0));
                    var appropriation=PackageDisciplineRules.Appropriate(plan.Documentos.Select(d=>(d.DocumentoCodigo,d.HHPrevisto)).ToArray(),direct,noDocument);
                    // Financeiro segue os custos reais calculados, preservando o vínculo documental.
                    var undistributedCost=indices.Where(i=>string.IsNullOrWhiteSpace(entries[i].Document)).Sum(i=>Math.Max(0,entries[i].CustoCalculado??0));
                    var costShares=PackageDisciplineRules.EqualSplit(undistributedCost,plan.Documentos.Count,6);
                    var directCosts=indices.Where(i=>!string.IsNullOrWhiteSpace(entries[i].Document))
                        .GroupBy(i=>Normalize(entries[i].Document)).ToDictionary(g=>g.Key,g=>g.Sum(i=>Math.Max(0,entries[i].CustoCalculado??0)));
                    var remainingCost=balance.RecognizedCost;
                    var remainingExcess=balance.ExcessCost;
                    for(var i=0;i<plan.Documentos.Count;i++)
                    {
                        var d=plan.Documentos[i]; var a=appropriation[i];
                        var rawCost=directCosts.GetValueOrDefault(Normalize(d.DocumentoCodigo))+costShares[i];
                        var recognizedCost=cost>0?Math.Min(remainingCost,rawCost*balance.RecognizedCost/cost.Value):0;
                        var excessCost=cost>0?Math.Min(remainingExcess,rawCost*balance.ExcessCost/cost.Value):0;
                        if(i==plan.Documentos.Count-1){recognizedCost=remainingCost;excessCost=remainingExcess;}
                        remainingCost-=recognizedCost;remainingExcess-=excessCost;
                        documents.Add(new(group.Key,h.Id,plan.Disciplina,d.DocumentoCodigo,splitH[i],splitC[i])
                        { TimesheetHours=a.HHBruto,RecognizedHours=a.HHApropriado,RecognizedCost=recognizedCost,
                          ExcessHours=a.HHExcedente,ExcessCost=excessCost,ContractHours=d.HHPrevisto,ContractCost=d.CustoPrevisto });
                    }
                }
            }
        }
        return new(projectId,output,documents,diagnostics);
    }
}
