using System.Data;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Services.Costs;

public sealed partial class CostManagementService
{
    private static IReadOnlyList<ExcelCostLine> ValidateExcelLines(IReadOnlyList<ExcelCostLine> lines)
    {
        var errors=new Dictionary<int,string>();
        foreach(var group in lines.Where(x=>x.Error is null).GroupBy(x=>x.Package,StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var totalHours=group.Where(x=>x.TotalHours.HasValue).Select(x=>x.TotalHours!.Value).Distinct().ToArray();
                var totalCosts=group.Where(x=>x.TotalCost.HasValue).Select(x=>x.TotalCost!.Value).Distinct().ToArray();
                if(totalHours.Length>1||totalCosts.Length>1)throw new InvalidOperationException("Totais explícitos conflitantes no mesmo pacote.");
                CostAllocationRules.Allocate(totalHours.FirstOrDefault(group.Sum(x=>x.DocumentHours??0m)),group.Select(x=>(x.DocumentHours,x.DocumentHours.HasValue?"MANUAL":"AUTO")).ToArray());
                CostAllocationRules.Allocate(totalCosts.FirstOrDefault(group.Sum(x=>x.DocumentCost??0m)),group.Select(x=>(x.DocumentCost,x.DocumentCost.HasValue?"MANUAL":"AUTO")).ToArray());
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
            {foreach(var line in group)errors[line.Row]=ex.Message;}
        }
        return lines.Select(x=>errors.TryGetValue(x.Row,out var error)?x with {Error=error}:x).ToArray();
    }

    private sealed record PackageContext(int? NormaId,IReadOnlyDictionary<string,string> Mapping,
        IReadOnlyDictionary<string,string> CanonicalNames,IReadOnlyDictionary<string,string?> Statuses,
        IReadOnlyDictionary<string,decimal> Percentages,IReadOnlySet<string> ConflictingStatuses);

    private static string PackageFallback(CostPackage package) => NullIfBlank(package.AliasTimesheet)??package.NomeReferencia;
    private static string? JoinDiagnostic(string? first,string? second) => string.IsNullOrWhiteSpace(first)?second:string.IsNullOrWhiteSpace(second)?first:first+" "+second;
    private static string DocumentDoc3(string code) { var parts=code.Split('-');return parts.Length>=3?CoordinationDisciplineResolver.Normalize(parts[2]):string.Empty; }

    private async Task<PackageContext> LoadPackageContextAsync(string projectId,CancellationToken ct)
    {
        await using var c=await Open(ct);
        int? normaId=null;var mappings=new Dictionary<string,string>(StringComparer.Ordinal);
        const string nq="SELECT PN.NormaId,M.Sigla,M.Disciplina FROM coordenacao.ProjetoNorma PN JOIN coordenacao.Norma N ON N.NormaId=PN.NormaId AND N.Ativa=1 LEFT JOIN coordenacao.NormaMapeamento M ON M.NormaId=PN.NormaId WHERE PN.Project_ID_ACC=@p";
        await using(var cmd=Cmd(nq,c,projectId))
        {
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct)) {normaId=r.GetInt32(0);if(!r.IsDBNull(1))mappings[CoordinationDisciplineResolver.Normalize(r.GetString(1))]=r.GetString(2);}
        }
        var standards=await new DisciplineStandardRepository(configuration).ListAsync(ct);
        var canonical=DisciplineStandardRepository.CanonicalNameMap(standards);
        var statusRows=new List<(string Code,string? Status)>();
        await using(var cmd=Cmd("SELECT NA_Document_Number,Document_Status FROM dbo.VW_Ldp_Unificada V WHERE V.Project_ID_ACC=@p AND "+LdpItemRules.SqlIsDocument("V"),c,projectId))
        {
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))if(!r.IsDBNull(0))statusRows.Add((Normalize(r.GetString(0)),Txt(r,1)));
        }
        var statuses=new Dictionary<string,string?>(StringComparer.Ordinal);var conflicts=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in statusRows.GroupBy(x=>x.Code,StringComparer.Ordinal))
        {
            var variants=group.Select(x=>DocumentStatusCanonical.Key(x.Status)).Distinct(StringComparer.Ordinal).ToArray();
            if(variants.Length>1)conflicts.Add(group.Key);
            // Cancellation always wins; conflicting current statuses otherwise recognize no cost.
            statuses[group.Key]=group.Any(x=>PackageAllocationRules.IsCancelled(x.Status))?group.First(x=>PackageAllocationRules.IsCancelled(x.Status)).Status:
                variants.Length==1?group.First().Status:null;
        }
        var percentages=new Dictionary<string,decimal>(StringComparer.Ordinal);
        await using(var exists=new SqlCommand("SELECT OBJECT_ID(N'qualidade.DocumentStatusAvancoConfig',N'U')",c))
        if(await exists.ExecuteScalarAsync(ct) is not DBNull)
        {
            var configs=new List<(string Key,decimal Percent)>();
            await using var cmd=Cmd("SELECT Document_Status_Normalizado,Percentual FROM qualidade.DocumentStatusAvancoConfig WHERE Project_ID_ACC=@p AND Ativo=1",c,projectId);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))if(!r.IsDBNull(0)&&!r.IsDBNull(1))configs.Add((DocumentStatusCanonical.Key(r.GetString(0)),Dec(r,1)));
            foreach(var group in configs.GroupBy(x=>x.Key,StringComparer.Ordinal))
                if(group.Key.Length>0&&group.Count()==1&&group.Single().Percent is >=0 and <=100)percentages[group.Key]=group.Single().Percent;
        }
        return new(normaId,mappings,canonical,statuses,percentages,conflicts);
    }

    public async Task<IReadOnlyList<CostDocument>> DocumentsAsync(string projectId,int packageId,CancellationToken ct)
    {
        var package=(await PackagesAsync(projectId,ct)).SingleOrDefault(x=>x.Id==packageId)??throw new ArgumentException("Pacote não pertence ao projeto atual.");
        // Por Disciplina não tem documentos manuais: o rateio vem da LDP (GET packages/{id}/disciplines).
        if(PackageModel.IsDiscipline(package.ModeloCadastro))return Array.Empty<CostDocument>();
        var docs=await RawDocumentsAsync(projectId,packageId,ct);var context=await LoadPackageContextAsync(projectId,ct);
        var allocation=BuildAllocation(package,docs,context).Documentos.ToDictionary(x=>x.DocumentId);
        return docs.Select(d=>
        {
            var metadata=DocumentMetadata(d,context);
            if(!allocation.TryGetValue(d.Id,out var a))return d with {Disciplina=metadata.Discipline,Doc3=metadata.Doc3,DocumentStatus=metadata.Status,StatusPercentual=metadata.Percent,Cancelado=metadata.Cancelled,Diagnostico=metadata.Diagnostic,HHOriginal=d.HHAlocada,CustoOriginal=d.CustoAlocado};
            return d with {Disciplina=a.Disciplina,Doc3=a.Doc3,DocumentStatus=a.DocumentStatus,StatusPercentual=a.StatusPercentual,HHEfetiva=a.HHFinal,CustoEfetivo=a.CustoFinal,CustoLiberado=a.CustoLiberado,Cancelado=a.Cancelado,Diagnostico=a.Diagnostico,HHOriginal=a.HHOriginal,CustoOriginal=a.CustoOriginal,HHRedistribuida=a.HHRedistribuida,HHAdicional=a.HHAdicional,CustoRedistribuido=a.CustoRedistribuido,CustoAdicional=a.CustoAdicional};
        }).ToArray();
    }

    private sealed record DocumentInfo(string Doc3,string Discipline,string? Status,decimal? Percent,bool Cancelled,string? Diagnostic);
    /// <summary>Disciplina canônica pela Norma de Coordenação (Doc3). Regra ÚNICA para os modelos Por Documento e Por Disciplina.</summary>
    private static (string Doc3,string Discipline,string? Diagnostic) DisciplineOf(string code,PackageContext context)=>DisciplineOfDoc3(DocumentDoc3(code),context);
    private static (string Doc3,string Discipline,string? Diagnostic) DisciplineOfDoc3(string doc3,PackageContext context)
    {
        var resolution=CoordinationDisciplineResolver.Resolve(doc3,context.NormaId,context.Mapping,context.CanonicalNames);
        var discipline=CoordinationDisciplineResolver.SemDisciplinaLabel;string? diagnostic=null;
        if(context.NormaId is null)diagnostic="SEM DISCIPLINA: projeto sem Norma de Coordenação ativa.";
        else if(!resolution.Matched)diagnostic="SEM DISCIPLINA: "+resolution.Diagnostic;
        else if(!context.CanonicalNames.TryGetValue(DisciplineCanonicalRules.Normalize(resolution.Discipline),out discipline))
        {discipline=CoordinationDisciplineResolver.SemDisciplinaLabel;diagnostic="SEM DISCIPLINA: nome da Norma sem canônico global único/ativo.";}
        return (doc3,discipline!,diagnostic);
    }

    private static DocumentInfo DocumentMetadata(CostDocument doc,PackageContext context)
    {
        var (doc3,discipline,diagnostic)=DisciplineOf(doc.DocumentoCodigo,context);
        var key=Normalize(doc.DocumentoCodigo);context.Statuses.TryGetValue(key,out var status);
        var cancelled=PackageAllocationRules.IsCancelled(status);
        decimal? percent=context.Percentages.TryGetValue(DocumentStatusCanonical.Key(status),out var p)?p:null;
        if(!context.Statuses.ContainsKey(key))diagnostic=JoinDiagnostic(diagnostic,"Documento não encontrado na LDP atual do projeto.");
        if(context.ConflictingStatuses.Contains(key))diagnostic=JoinDiagnostic(diagnostic,"Document_Status conflitante na LDP atual; custo bloqueado salvo cancelamento.");
        if(!cancelled&&percent is null)diagnostic=JoinDiagnostic(diagnostic,"Percentual do Document_Status ausente, inativo, inválido ou ambíguo; custo reconhecido zero.");
        return new(doc3,discipline,status,percent,cancelled,diagnostic);
    }

    private static CostPackageAllocation BuildAllocation(CostPackage package,IReadOnlyList<CostDocument> documents,PackageContext context)
    {
        var docs=documents.Where(x=>x.Ativo).ToArray();var info=docs.Select(d=>DocumentMetadata(d,context)).ToArray();
        var baseHours=docs.Select(d=>d.HHAlocada).ToArray();var baseCosts=docs.Select(d=>d.CustoAlocado).ToArray();
        var hours=new decimal[docs.Length];var costs=new decimal[docs.Length];var redistributedH=new decimal[docs.Length];var redistributedC=new decimal[docs.Length];var addH=new decimal[docs.Length];var addC=new decimal[docs.Length];
        string? error=null;
        try
        {
            if(docs.Length==0)throw new InvalidOperationException("Pacote sem documentos ativos para alocar HH/custo.");
            baseHours=CostAllocationRules.Allocate(package.HHBase,docs.Select(d=>(d.ModoHH.Equals("MANUAL",StringComparison.OrdinalIgnoreCase)?(decimal?)d.HHAlocada:null,d.ModoHH)).ToArray()).Values.Select(x=>x.Value).ToArray();
            baseCosts=CostAllocationRules.Allocate(package.CustoBase,docs.Select(d=>(d.ModoCusto.Equals("MANUAL",StringComparison.OrdinalIgnoreCase)?(decimal?)d.CustoAlocado:null,d.ModoCusto)).ToArray()).Values.Select(x=>x.Value).ToArray();
            var cancelled=info.Select(x=>x.Cancelled).ToArray();
            var hh=PackageAllocationRules.AllocateEffective(baseHours,package.HHAddenda,cancelled);
            var cc=PackageAllocationRules.AllocateEffective(baseCosts,package.CustoAddenda,cancelled);
            for(var i=0;i<docs.Length;i++)
            {
                hours[i]=hh[i].Effective;redistributedH[i]=hh[i].Redistributed;addH[i]=hh[i].Addendum;
                costs[i]=cc[i].Effective;redistributedC[i]=cc[i].Redistributed;addC[i]=cc[i].Addendum;
            }
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
        {error=ex.Message;Array.Clear(hours);Array.Clear(costs);Array.Clear(redistributedH);Array.Clear(redistributedC);Array.Clear(addH);Array.Clear(addC);}
        var output=docs.Select((d,i)=>
        {
            var metadata=info[i];var diagnostic=JoinDiagnostic(metadata.Diagnostic,error);
            if(metadata.Cancelled)diagnostic=JoinDiagnostic(diagnostic,error is null?"CANCELADO: HH/custo base redistribuídos aos documentos elegíveis do mesmo pacote.":"CANCELADO: redistribuição bloqueada; valores originais preservados.");
            var recognized=PackageAllocationRules.Recognize(0,hours[i],costs[i],metadata.Percent,metadata.Cancelled,error is not null).Cost;
            return new CostDocumentAllocation(d.Id,d.DocumentoCodigo,metadata.Discipline,hours[i],d.ModoHH,costs[i],d.ModoCusto,metadata.Doc3,metadata.Status,metadata.Percent,baseHours[i],baseCosts[i],redistributedH[i],addH[i],redistributedC[i],addC[i],recognized,metadata.Cancelled,diagnostic);
        }).ToArray();
        return new(package.Id,package.HHEfetiva,package.CustoEfetivo,output,error);
    }
}
