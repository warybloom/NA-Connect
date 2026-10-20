using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Costs;

public sealed record CostProfessional(
    int Id,
    string ProjectIdAcc,
    string? Funcao,
    string? Disciplina,
    string Nome,
    string? Empresa,
    string? AliasTimesheet,
    decimal HHBaseMensal,
    decimal Remuneracao,
    decimal Encargos,
    decimal Softwares,
    decimal Computadores,
    decimal CustoMensal,
    decimal CustoHora,
    bool Ativo,
    DateTime? DesativadoEm=null);
public sealed record CostExpense(int Id,string ProjectIdAcc,string Categoria,string Descricao,decimal Valor,DateTime Competencia,string Tipo,bool Ativo);
public sealed record CostPackage(int Id,string ProjectIdAcc,string? Empresa,string NomeReferencia,string? AliasTimesheet,string? Descricao,decimal HHBase,decimal HHAddenda,decimal HHEfetiva,decimal CustoBase,decimal CustoAddenda,decimal CustoEfetivo,int Documents,bool Ativo,string ModeloCadastro=PackageModel.Documento,int Disciplinas=0);
public sealed record CostDocument(int Id,int PackageId,string DocumentoCodigo,string? Disciplina,decimal HHAlocada,decimal CustoAlocado,string ModoHH,string ModoCusto,bool Ativo,string? Doc3=null,string? DocumentStatus=null,decimal? StatusPercentual=null,decimal HHEfetiva=0,decimal CustoEfetivo=0,decimal CustoLiberado=0,bool Cancelado=false,string? Diagnostico=null,decimal HHOriginal=0,decimal CustoOriginal=0,decimal HHRedistribuida=0,decimal HHAdicional=0,decimal CustoRedistribuido=0,decimal CustoAdicional=0,decimal? ValorHora=null);
public sealed record CostItem(int Id,int ProfessionalId,string Categoria,string Descricao,decimal Valor,bool Ativo,int? PeriodId=null,string TipoCalculo="FIXO",decimal? Percentual=null,string BaseCalculo="REMUNERACAO");
public sealed record CostAddendum(int Id,int PackageId,DateTime Data,string Descricao,decimal HHAdicional,decimal CustoAdicional,bool Ativo);
/// <summary>Rateio FINAL de um documento ativo do pacote (mesma regra AUTO/MANUAL usada no reconhecimento).</summary>
public sealed record CostDocumentAllocation(int DocumentId,string DocumentoCodigo,string? Disciplina,decimal HHFinal,string ModoHH,decimal CustoFinal,string ModoCusto,string? Doc3=null,string? DocumentStatus=null,decimal? StatusPercentual=null,decimal HHOriginal=0,decimal CustoOriginal=0,decimal HHRedistribuida=0,decimal HHAdicional=0,decimal CustoRedistribuido=0,decimal CustoAdicional=0,decimal CustoLiberado=0,bool Cancelado=false,string? Diagnostico=null);
public sealed record CostPackageAllocation(int PackageId,decimal HHEfetiva,decimal CustoEfetivo,IReadOnlyList<CostDocumentAllocation> Documentos,string? Erro);
public sealed record CostSummary(string ProjectIdAcc,decimal Professionals,decimal Expenses,decimal Packages,decimal CostPredicted,decimal Recognized,decimal Excess,decimal PlannedHours,decimal WorkedHours,decimal RecognizedHours,decimal ExcessHours,string? TimesheetStatus=null,decimal RawTimesheetHours=0,decimal PendingHours=0);
public sealed record CostImportRow(int Row,int Valid,string? Project,string? Package,string? Document,string? Error,decimal? HHPrevistaDocumento=null,decimal? ValorHora=null,decimal? CustoPrevistoDocumento=null);
public sealed record CostImportPreview(IReadOnlyList<CostImportRow> Rows,int Valid,int Invalid,int Warnings);
public sealed record CostImportCommitResult(int PackagesCreated,int DocumentsCreated);
public sealed record Allocation(decimal Value, string Mode);
public sealed record AllocationResult(IReadOnlyList<Allocation> Values, decimal Total);
public sealed record TimesheetRecognition(decimal TimesheetHours,decimal ContractedHours,decimal RecognizedHours,decimal ExcessHours,decimal TheoreticalCost,decimal RecognizedCost,decimal ExcessCost);
public sealed record CostRecognitionLine(int PackageId,int? ProfessionalId,string DocumentoCodigo,string? Disciplina,decimal TimesheetHours,decimal ContractedHours,decimal RecognizedHours,decimal ExcessHours,decimal TheoreticalCost,decimal RecognizedCost,decimal ExcessCost,bool AmbiguousMatch,string? Doc3=null,string? DocumentStatus=null,decimal? StatusPercentual=null,decimal HHOriginal=0,decimal CustoOriginal=0,decimal CustoEfetivo=0,bool Cancelado=false,string? Diagnostico=null,decimal HHRedistribuida=0,decimal HHAdicional=0,decimal CustoRedistribuido=0,decimal CustoAdicional=0);
public sealed record TimesheetWork(string Name,string DocumentoCodigo,decimal Hours,DateOnly? Date=null,string? Doc3=null,string? Disciplina=null);
public sealed record PackageDocumentMatch(int PackageId,string DocumentoCodigo,string? FallbackAlias,IReadOnlyList<(string Text,string Type)> Aliases);
public sealed record TimesheetAssignment(int RowIndex,int? PackageId,string? DocumentoCodigo,bool Ambiguous,bool PackageNameOnly=false);

public static class CostTimesheetMatchingRules
{
    public static IReadOnlyList<TimesheetAssignment> AssignPackages(IReadOnlyList<TimesheetWork> rows,IReadOnlyList<PackageDocumentMatch> candidates)
    {
        var output=new List<TimesheetAssignment>(rows.Count);
        for(var index=0;index<rows.Count;index++)
        {
            var row=rows[index];
            var options=candidates.Where(x=>CostAllocationRules.NormalizeDocument(x.DocumentoCodigo)==CostAllocationRules.NormalizeDocument(row.DocumentoCodigo)&&Matches(x.FallbackAlias,x.Aliases,row.Name)).ToList();
            var packages=options.Select(x=>x.PackageId).Distinct().ToList();
            output.Add(packages.Count==1?new(index,packages[0],options[0].DocumentoCodigo,false):new(index,null,null,packages.Count>1,packages.Count==0&&candidates.Any(c=>Matches(c.FallbackAlias,c.Aliases,row.Name))));
        }
        return output;
    }
    public static string NormalizeName(string? value) => Regex.Replace(Regex.Replace(
        (value??string.Empty).Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", string.Empty),
        @"[^\p{L}\p{Nd}]+", " ").Trim().ToUpperInvariant();
    public static bool Matches(string? fallback,IReadOnlyList<(string Text,string Type)> aliases,string name)
    {
        var all=aliases.ToList();
        if(!string.IsNullOrWhiteSpace(fallback))all.Add((fallback,"CONTEM"));
        var normalized=NormalizeName(name);
        return all.Any(a=>NormalizeName(a.Text) is var text && text.Length>0 &&
            (string.Equals(a.Type,"EXATO",StringComparison.OrdinalIgnoreCase)?normalized==text:normalized.Contains(text,StringComparison.Ordinal)));
    }
}

public static class CostAllocationRules
{
    public static decimal CostPerHour(decimal total,decimal baseHours)=>baseHours<=0?0m:total/baseHours;
    public static AllocationResult Allocate(decimal total,IReadOnlyList<(decimal? Value,string Mode)> rows)
    {
        if(total<0 || rows.Count==0) throw new ArgumentException("Total e documentos devem ser válidos.");
        var manual=rows.Where(x=>string.Equals(x.Mode,"MANUAL",StringComparison.OrdinalIgnoreCase)).Sum(x=>x.Value??0m);
        if(manual<0 || rows.Any(x=>(x.Value??0m)<0)) throw new InvalidOperationException("Valores de rateio não podem ser negativos.");
        if(manual>total) throw new InvalidOperationException("Valores MANUAL excedem o total contratado.");
        var auto=rows.Count(x=>!string.Equals(x.Mode,"MANUAL",StringComparison.OrdinalIgnoreCase)); if(auto==0&&manual!=total) throw new InvalidOperationException("Sem linhas AUTO, o rateio manual precisa fechar o total contratado."); var remainder=total-manual;
        var result=new List<Allocation>(); var left=auto; var standard=auto==0?0m:decimal.Round(remainder/auto,6,MidpointRounding.ToEven);
        foreach(var row in rows){if(string.Equals(row.Mode,"MANUAL",StringComparison.OrdinalIgnoreCase))result.Add(new(row.Value??0m,"MANUAL"));else {var value=left==1?remainder:standard; result.Add(new(value,"AUTO")); remainder-=value;left--;}}
        return new(result, result.Sum(x=>x.Value));
    }
    public static TimesheetRecognition Recognize(decimal timesheet,decimal contracted,decimal contractedCost,decimal? hourly){
        timesheet=Math.Max(0,timesheet);contracted=Math.Max(0,contracted);contractedCost=Math.Max(0,contractedCost);var recognized=Math.Min(timesheet,contracted);var excess=Math.Max(timesheet-contracted,0);var theoretical=hourly.HasValue?timesheet*hourly.Value:contracted<=0?0m:contractedCost*(timesheet/contracted);var cost=Math.Min(theoretical,contractedCost);return new(timesheet,contracted,recognized,excess,theoretical,cost,Math.Max(theoretical-cost,0));
    }
    public static string NormalizeDocument(string? value)=>Regex.Replace((value??string.Empty).Trim().ToUpperInvariant(),"\\s+"," ");
}

public interface ICostManagementService
{
    Task<CostSummary> SummaryAsync(string projectId,CancellationToken ct);
    Task<IReadOnlyList<CostProfessional>> ProfessionalsAsync(string projectId,CancellationToken ct);
    Task<IReadOnlyList<CostExpense>> ExpensesAsync(string projectId,CancellationToken ct);
    Task<IReadOnlyList<CostPackage>> PackagesAsync(string projectId,CancellationToken ct);
    Task<IReadOnlyList<CostDocument>> DocumentsAsync(string projectId,int packageId,CancellationToken ct);
    Task<IReadOnlyList<CostItem>> ItemsAsync(string projectId,int professionalId,CancellationToken ct);
    Task<int> AddProfessionalAsync(string projectId,CostProfessionalInput input,CancellationToken ct);
    Task<int> AddExpenseAsync(string projectId,CostExpenseInput input,CancellationToken ct);
    Task<int> AddPackageAsync(string projectId,CostPackageInput input,CancellationToken ct);
    Task<bool> UpdatePackageAsync(string projectId,int packageId,CostPackageUpdate input,CancellationToken ct);
    Task<int> AddDocumentAsync(string projectId,int packageId,CostDocumentInput input,CancellationToken ct);
    Task<bool> UpdateDocumentAsync(string projectId,int packageId,int documentId,CostDocumentInput input,CancellationToken ct);
    Task<int> AddItemAsync(string projectId,int professionalId,CostItemInput input,CancellationToken ct);
    Task<int> AddDocumentsBatchAsync(string projectId,int packageId,IReadOnlyList<CostDocumentInput> inputs,CancellationToken ct);
    Task<int> AddAddendumAsync(string projectId,int packageId,CostAddendumInput input,CancellationToken ct);
    Task<bool> DeactivateAsync(string projectId,string entity,int id,CancellationToken ct);
    Task<byte[]> PackageImportTemplateAsync(CancellationToken ct);
    Task<CostImportPreview> PreviewExcelAsync(string projectId,Stream file,CancellationToken ct);
    Task<CostImportCommitResult> CommitExcelAsync(string projectId,Stream file,CancellationToken ct);
    Task<IReadOnlyList<CostRecognitionLine>> RecognitionAsync(string projectId,CancellationToken ct);
    Task<bool> UpdateProfessionalAsync(string projectId,int professionalId,CostProfessionalUpdate input,CancellationToken ct);
    Task<bool> UpdateItemAsync(string projectId,int professionalId,int itemId,CostItemUpdate input,CancellationToken ct);
    Task<IReadOnlyList<CostAddendum>> AddendaAsync(string projectId,int packageId,CancellationToken ct);
    Task<CostPackageAllocation> AllocationAsync(string projectId,int packageId,CancellationToken ct);
}
public sealed record CostProfessionalUpdate(string Nome,string? Empresa,string? AliasTimesheet,decimal HHBaseMensal,bool Ativo,string? Funcao=null,string? Disciplina=null);
public sealed record CostItemUpdate(string Descricao,decimal Valor,bool Ativo);
public sealed record CostProfessionalInput(string Nome,string? Empresa,string? AliasTimesheet,decimal HHBaseMensal,string? Funcao=null,string? Disciplina=null);
public sealed record CostExpenseInput(string Categoria,string Descricao,decimal Valor,DateTime Competencia,string Tipo,string? Observacao);
public sealed record CostPackageInput(string? Empresa,string NomeReferencia,string? AliasTimesheet,string? Descricao,decimal HHBase,decimal CustoBase);
public sealed record CostPackageUpdate(string? Empresa,string NomeReferencia,string? AliasTimesheet,string? Descricao,bool Ativo=true);
public sealed record CostDocumentInput(string DocumentoCodigo,string? Disciplina,decimal? HHAlocada,decimal? CustoAlocado,string ModoHH,string ModoCusto,decimal? ValorHora=null);
public sealed record CostAddendumInput(decimal HHAdicional,decimal CustoAdicional,DateTime Data,string Descricao);
public sealed record CostItemInput(string Categoria,string Descricao,decimal Valor);

public sealed partial class CostManagementService(IConfiguration configuration):ICostManagementService
{
    private async Task<SqlConnection> Open(CancellationToken ct){var c=new SqlConnection(configuration.GetConnectionString("LocalConnection")??throw new InvalidOperationException("Conexão indisponível."));await c.OpenAsync(ct);return c;}
    public async Task<IReadOnlyList<CostProfessional>> ProfessionalsAsync(string projectId,CancellationToken ct)
    {
        const string q = """
            SELECT
                p.Id,
                p.Project_ID_ACC,
                p.Funcao,
                p.Disciplina,
                p.Nome,
                p.Empresa,
                p.AliasTimesheet,
                COALESCE(h.HHBaseMensal,p.HHBaseMensal) AS HHBaseMensal,
                COALESCE(h.RemuneracaoBase,0) AS Remuneracao,
                COALESCE(ci.EncargosFixos,0)
                  + COALESCE(h.RemuneracaoBase,0) * COALESCE(ci.EncargosPercentual,0) / 100.0 AS Encargos,
                COALESCE(ci.SoftwaresFixos,0)
                  + COALESCE(h.RemuneracaoBase,0) * COALESCE(ci.SoftwaresPercentual,0) / 100.0 AS Softwares,
                COALESCE(ci.ComputadoresFixos,0)
                  + COALESCE(h.RemuneracaoBase,0) * COALESCE(ci.ComputadoresPercentual,0) / 100.0 AS Computadores,
                COALESCE(h.RemuneracaoBase,0)
                  + COALESCE(ci.ItensFixos,0)
                  + COALESCE(h.RemuneracaoBase,0) * COALESCE(ci.ItensPercentual,0) / 100.0 AS CustoMensal,
                h.CustoHoraManual,
                p.Ativo,
                p.DesativadoEm
            FROM costs.ProjectProfessional p
            OUTER APPLY(
                SELECT TOP(1) z.*
                FROM costs.ProjectProfessionalCostPeriod z
                WHERE z.ProfessionalId=p.Id
                  AND z.Ativo=1
                  AND z.VigenciaInicio<=CONVERT(date,GETDATE())
                  AND (z.VigenciaFim IS NULL OR z.VigenciaFim>=CONVERT(date,GETDATE()))
                ORDER BY z.VigenciaInicio DESC
            ) h
            OUTER APPLY(
                SELECT
                    SUM(CASE WHEN i.Ativo=1 AND COALESCE(i.TipoCalculo,'FIXO')<>'PERCENTUAL'
                             THEN COALESCE(i.Valor,0) ELSE 0 END) AS ItensFixos,
                    SUM(CASE WHEN i.Ativo=1 AND i.TipoCalculo='PERCENTUAL'
                             THEN COALESCE(i.Percentual,0) ELSE 0 END) AS ItensPercentual,
                    SUM(CASE WHEN i.Ativo=1
                                  AND UPPER(i.Categoria) IN ('ENCARGO','ENCARGOS','IMPOSTO')
                                  AND COALESCE(i.TipoCalculo,'FIXO')<>'PERCENTUAL'
                             THEN COALESCE(i.Valor,0) ELSE 0 END) AS EncargosFixos,
                    SUM(CASE WHEN i.Ativo=1
                                  AND UPPER(i.Categoria) IN ('ENCARGO','ENCARGOS','IMPOSTO')
                                  AND i.TipoCalculo='PERCENTUAL'
                             THEN COALESCE(i.Percentual,0) ELSE 0 END) AS EncargosPercentual,
                    SUM(CASE WHEN i.Ativo=1
                                  AND UPPER(i.Categoria) IN ('SOFTWARE','SOFTWARES')
                                  AND COALESCE(i.TipoCalculo,'FIXO')<>'PERCENTUAL'
                             THEN COALESCE(i.Valor,0) ELSE 0 END) AS SoftwaresFixos,
                    SUM(CASE WHEN i.Ativo=1
                                  AND UPPER(i.Categoria) IN ('SOFTWARE','SOFTWARES')
                                  AND i.TipoCalculo='PERCENTUAL'
                             THEN COALESCE(i.Percentual,0) ELSE 0 END) AS SoftwaresPercentual,
                    SUM(CASE WHEN i.Ativo=1
                                  AND UPPER(i.Categoria) IN ('COMPUTADOR','COMPUTADORES','EQUIPAMENTO')
                                  AND COALESCE(i.TipoCalculo,'FIXO')<>'PERCENTUAL'
                             THEN COALESCE(i.Valor,0) ELSE 0 END) AS ComputadoresFixos,
                    SUM(CASE WHEN i.Ativo=1
                                  AND UPPER(i.Categoria) IN ('COMPUTADOR','COMPUTADORES','EQUIPAMENTO')
                                  AND i.TipoCalculo='PERCENTUAL'
                             THEN COALESCE(i.Percentual,0) ELSE 0 END) AS ComputadoresPercentual
                FROM costs.ProfessionalCostItem i
                WHERE i.ProfessionalId=p.Id AND i.PeriodId=h.Id
            ) ci
            WHERE p.Project_ID_ACC=@p
            ORDER BY p.Nome;
            """;
        await using var c=await Open(ct);
        await using var cmd=Cmd(q,c,projectId);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var x=new List<CostProfessional>();
        while(await r.ReadAsync(ct))
        {
            var hh=Dec(r,7);
            var total=Dec(r,12);
            var rate=r.IsDBNull(13)?CostAllocationRules.CostPerHour(total,hh):Dec(r,13);
            x.Add(new(
                r.GetInt32(0),r.GetString(1),Txt(r,2),Txt(r,3),r.GetString(4),Txt(r,5),Txt(r,6),
                hh,Dec(r,8),Dec(r,9),Dec(r,10),Dec(r,11),total,rate,r.GetBoolean(14),r.IsDBNull(15)?null:r.GetDateTime(15)));
        }
        return x;
    }
    public async Task<IReadOnlyList<CostExpense>> ExpensesAsync(string projectId,CancellationToken ct){const string q="SELECT Id,Project_ID_ACC,Categoria,Descricao,Valor,Competencia,Tipo,Ativo FROM costs.ProjectExpense WHERE Project_ID_ACC=@p ORDER BY Competencia DESC,Id DESC";return await ReadMany(q,projectId,ct,r=>new CostExpense(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),Dec(r,4),r.GetDateTime(5),r.GetString(6),r.GetBoolean(7)));}
    public async Task<IReadOnlyList<CostPackage>> PackagesAsync(string projectId,CancellationToken ct)
    {
        // Antes da 09D (sem ModeloCadastro) todo pacote é DOCUMENTO: mesmo resultado de antes.
        var model=await DisciplineSchemaAsync(ct)
            ?"p.ModeloCadastro,(SELECT COUNT(*) FROM costs.PackageDiscipline x WHERE x.PackageId=p.Id AND x.Ativo=1)"
            :"'DOCUMENTO',0";
        var q="SELECT p.Id,p.Project_ID_ACC,p.Empresa,p.NomeReferencia,p.AliasTimesheet,p.Descricao,p.HHBase,COALESCE((SELECT SUM(a.HHAdicional) FROM costs.PackageAddendum a WHERE a.PackageId=p.Id AND a.Ativo=1),0),p.CustoBase,COALESCE((SELECT SUM(a.CustoAdicional) FROM costs.PackageAddendum a WHERE a.PackageId=p.Id AND a.Ativo=1),0),(SELECT COUNT(*) FROM costs.PackageDocument d WHERE d.PackageId=p.Id AND d.Ativo=1),p.Ativo,"+model+" FROM costs.Package p WHERE p.Project_ID_ACC=@p ORDER BY p.NomeReferencia";
        return await ReadMany(q,projectId,ct,r=>{var hh=Dec(r,6);var addh=Dec(r,7);var cb=Dec(r,8);var addc=Dec(r,9);return new CostPackage(r.GetInt32(0),r.GetString(1),Txt(r,2),r.GetString(3),Txt(r,4),Txt(r,5),hh,addh,hh+addh,cb,addc,cb+addc,r.GetInt32(10),r.GetBoolean(11),r.GetString(12),r.GetInt32(13));});
    }
    private async Task<IReadOnlyList<CostDocument>> RawDocumentsAsync(string projectId,int packageId,CancellationToken ct)
    {
        const string q="SELECT d.Id,d.PackageId,d.DocumentoCodigo,d.Disciplina,d.HHAlocada,d.CustoAlocado,d.ModoHH,d.ModoCusto,d.Ativo FROM costs.PackageDocument d JOIN costs.Package p ON p.Id=d.PackageId WHERE p.Project_ID_ACC=@p AND d.PackageId=@id ORDER BY d.DocumentoCodigo";
        await using var c=await Open(ct);
        await using var cmd=Cmd(q,c,projectId);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var x=new List<CostDocument>();
        while(await r.ReadAsync(ct))
        {
            var hh=Dec(r,4);
            var cost=Dec(r,5);
            x.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),Txt(r,3),hh,cost,r.GetString(6),r.GetString(7),r.GetBoolean(8),
                ValorHora:hh>0?decimal.Round(cost/hh,6,MidpointRounding.AwayFromZero):null));
        }
        return x;
    }
    public async Task<IReadOnlyList<CostItem>> ItemsAsync(string projectId,int professionalId,CancellationToken ct){const string q="SELECT i.Id,i.ProfessionalId,i.Categoria,i.Descricao,i.Valor,i.Ativo,i.PeriodId,i.TipoCalculo,i.Percentual,i.BaseCalculo FROM costs.ProfessionalCostItem i JOIN costs.ProjectProfessional p ON p.Id=i.ProfessionalId WHERE p.Project_ID_ACC=@p AND i.ProfessionalId=@id AND i.PeriodId=(SELECT TOP(1) h.Id FROM costs.ProjectProfessionalCostPeriod h WHERE h.ProfessionalId=@id AND h.Ativo=1 AND h.VigenciaInicio<=CONVERT(date,SYSUTCDATETIME()) AND (h.VigenciaFim IS NULL OR h.VigenciaFim>=CONVERT(date,SYSUTCDATETIME())) ORDER BY h.VigenciaInicio DESC) ORDER BY i.Id";await using var c=await Open(ct);await using var cmd=Cmd(q,c,projectId);cmd.Parameters.Add("@id",SqlDbType.Int).Value=professionalId;await using var r=await cmd.ExecuteReaderAsync(ct);var x=new List<CostItem>();while(await r.ReadAsync(ct))x.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),Txt(r,3)??"",Dec(r,4),r.GetBoolean(5),r.IsDBNull(6)?null:r.GetInt32(6),r.GetString(7),r.IsDBNull(8)?null:Dec(r,8),r.GetString(9)));return x;}
    public async Task<CostSummary> SummaryAsync(string projectId,CancellationToken ct){var pros=await ProfessionalsAsync(projectId,ct);var expenses=await ExpensesAsync(projectId,ct);var evaluation=await EvaluateAsync(projectId,pros,ct);var planned=evaluation.Packages.Where(x=>x.Ativo).Sum(x=>x.HHEfetiva);var packageCost=evaluation.Packages.Where(x=>x.Ativo).Sum(x=>x.CustoEfetivo);var expense=expenses.Where(x=>x.Ativo).Sum(x=>x.Valor);var worked=evaluation.InternalHours+evaluation.Lines.Sum(x=>x.RecognizedHours);var packageRecognized=evaluation.Lines.Sum(x=>x.RecognizedCost);var packageExcess=evaluation.Lines.Sum(x=>x.ExcessCost);var recognizedHours=evaluation.InternalHours+evaluation.Lines.Sum(x=>x.RecognizedHours);var excessHours=evaluation.Lines.Sum(x=>x.ExcessHours);var professionalPlanned=pros.Where(x=>x.Ativo).Sum(x=>x.CustoMensal);var status=await TimesheetStatusAsync(projectId,ct);return new(projectId,professionalPlanned,expense,packageCost,professionalPlanned+expense+packageCost,evaluation.InternalRecognizedCost+expense+packageRecognized,packageExcess,planned,worked,recognizedHours,excessHours,status,evaluation.Rows.Sum(x=>Math.Max(0,x.Hours)),evaluation.PendingHours);}
    public async Task<int> AddProfessionalAsync(string projectId,CostProfessionalInput i,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(i.Nome)||i.HHBaseMensal<=0)throw new ArgumentException("Nome e HH base positiva são obrigatórios.");
        await using var c=await Open(ct);
        await using var cmd=Cmd("""
            INSERT INTO costs.ProjectProfessional(Project_ID_ACC,Nome,Empresa,AliasTimesheet,HHBaseMensal,Funcao,Disciplina)
            VALUES(@p,@n,@e,@a,@h,@f,@d);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,c,projectId);
        cmd.Parameters.AddWithValue("@n",i.Nome.Trim());
        cmd.Parameters.AddWithValue("@e",(object?)NullIfBlank(i.Empresa)??DBNull.Value);
        cmd.Parameters.AddWithValue("@a",(object?)NullIfBlank(i.AliasTimesheet)??DBNull.Value);
        cmd.Parameters.AddWithValue("@f",(object?)NullIfBlank(i.Funcao)??DBNull.Value);
        cmd.Parameters.AddWithValue("@d",(object?)NullIfBlank(i.Disciplina)??DBNull.Value);
        var hours=cmd.Parameters.Add("@h",SqlDbType.Decimal);hours.Precision=18;hours.Scale=6;hours.Value=i.HHBaseMensal;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
    }
    public async Task<int> AddExpenseAsync(string projectId,CostExpenseInput i,CancellationToken ct){if(i.Valor<0||string.IsNullOrWhiteSpace(i.Categoria)||string.IsNullOrWhiteSpace(i.Descricao)||i.Tipo is not ("UNICA" or "RECORRENTE"))throw new ArgumentException("Despesa inválida.");return await Insert("INSERT INTO costs.ProjectExpense(Project_ID_ACC,Categoria,Descricao,Valor,Competencia,Tipo,Observacao) VALUES(@p,@c,@d,@v,@dt,@t,@o);SELECT CAST(SCOPE_IDENTITY() AS int);",projectId,ct,c=>{c.Parameters.AddWithValue("@c",i.Categoria.Trim());c.Parameters.AddWithValue("@d",i.Descricao.Trim());c.Parameters.Add("@v",SqlDbType.Decimal).Value=i.Valor;c.Parameters.Add("@dt",SqlDbType.Date).Value=i.Competencia.Date;c.Parameters.AddWithValue("@t",i.Tipo);c.Parameters.AddWithValue("@o",(object?)i.Observacao??DBNull.Value);});}
    public async Task<int> AddPackageAsync(string projectId,CostPackageInput i,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(i.NomeReferencia)||i.HHBase<0||i.CustoBase<0)throw new ArgumentException("Pacote inválido.");
        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await EnsureUniquePackageNameAsync(c,tx,projectId,i.NomeReferencia,null,ct);
            await using var cmd=new SqlCommand("""
                INSERT INTO costs.Package(Project_ID_ACC,Empresa,NomeReferencia,AliasTimesheet,Descricao,HHBase,CustoBase)
                VALUES(@p,@e,@n,@a,@d,@h,@c);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """,c,tx);
            cmd.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;
            cmd.Parameters.Add("@e",SqlDbType.NVarChar,200).Value=(object?)NullIfBlank(i.Empresa)??DBNull.Value;
            cmd.Parameters.Add("@n",SqlDbType.NVarChar,200).Value=i.NomeReferencia.Trim();
            cmd.Parameters.Add("@a",SqlDbType.NVarChar,200).Value=(object?)NullIfBlank(i.AliasTimesheet)??DBNull.Value;
            cmd.Parameters.Add("@d",SqlDbType.NVarChar,-1).Value=(object?)NullIfBlank(i.Descricao)??DBNull.Value;
            Decimal(cmd,"@h",i.HHBase);
            Decimal(cmd,"@c",i.CustoBase);
            var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
            await tx.CommitAsync(ct);
            return id;
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public async Task<bool> UpdatePackageAsync(string projectId,int packageId,CostPackageUpdate i,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(i.NomeReferencia))throw new ArgumentException("Informe o nome do pacote/profissional.");
        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await EnsurePackageAsync(c,tx,projectId,packageId,ct);
            await EnsureUniquePackageNameAsync(c,tx,projectId,i.NomeReferencia,packageId,ct);
            await using var cmd=new SqlCommand("""
                UPDATE costs.Package
                SET Empresa=@e,NomeReferencia=@n,AliasTimesheet=@a,Descricao=@d,Ativo=@ativo,AtualizadoEm=SYSUTCDATETIME()
                WHERE Id=@id AND Project_ID_ACC=@p;
                """,c,tx);
            cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
            cmd.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;
            cmd.Parameters.Add("@e",SqlDbType.NVarChar,200).Value=(object?)NullIfBlank(i.Empresa)??DBNull.Value;
            cmd.Parameters.Add("@n",SqlDbType.NVarChar,200).Value=i.NomeReferencia.Trim();
            cmd.Parameters.Add("@a",SqlDbType.NVarChar,200).Value=(object?)NullIfBlank(i.AliasTimesheet)??DBNull.Value;
            cmd.Parameters.Add("@d",SqlDbType.NVarChar,-1).Value=(object?)NullIfBlank(i.Descricao)??DBNull.Value;
            cmd.Parameters.Add("@ativo",SqlDbType.Bit).Value=i.Ativo;
            var changed=await cmd.ExecuteNonQueryAsync(ct)==1;
            await tx.CommitAsync(ct);
            return changed;
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public async Task<int> AddDocumentAsync(string projectId,int packageId,CostDocumentInput i,CancellationToken ct)
        =>await AddDocumentsBatchAsync(projectId,packageId,new[]{i},ct);

    public async Task<bool> UpdateDocumentAsync(string projectId,int packageId,int documentId,CostDocumentInput input,CancellationToken ct)
    {
        var i=NormalizeDocumentInput(input);
        ValidateDocument(i);
        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await EnsurePackageAsync(c,tx,projectId,packageId,ct);
            await EnsureDocumentModelAsync(c,tx,packageId,ct);
            await EnsureDocumentAsync(c,tx,packageId,documentId,ct);
            await EnsureUniqueDocumentAsync(c,tx,packageId,i.DocumentoCodigo,documentId,ct);

            await using var cmd=new SqlCommand("""
                UPDATE costs.PackageDocument
                SET DocumentoCodigo=@doc,Disciplina=NULL,HHAlocada=@hh,CustoAlocado=@cost,
                    ModoHH=@modeH,ModoCusto=@modeC,AtualizadoEm=SYSUTCDATETIME()
                WHERE Id=@document AND PackageId=@package;
                """,c,tx);
            cmd.Parameters.Add("@document",SqlDbType.Int).Value=documentId;
            cmd.Parameters.Add("@package",SqlDbType.Int).Value=packageId;
            cmd.Parameters.Add("@doc",SqlDbType.NVarChar,255).Value=Normalize(i.DocumentoCodigo);
            Decimal(cmd,"@hh",i.HHAlocada??0m);
            Decimal(cmd,"@cost",i.CustoAlocado??0m);
            cmd.Parameters.Add("@modeH",SqlDbType.VarChar,20).Value=i.ModoHH.ToUpperInvariant();
            cmd.Parameters.Add("@modeC",SqlDbType.VarChar,20).Value=i.ModoCusto.ToUpperInvariant();
            var changed=await cmd.ExecuteNonQueryAsync(ct)==1;
            await RecalculatePackageBaseIfFullyManualAsync(c,tx,packageId,ct);
            await ValidatePackageAllocationAsync(c,tx,packageId,ct);
            await tx.CommitAsync(ct);
            return changed;
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public Task<int> AddItemAsync(string projectId,int professionalId,CostItemInput i,CancellationToken ct)
        =>Task.FromException<int>(new InvalidOperationException("Custos profissionais são históricos. Registre a alteração como nova vigência para não recalcular o passado."));

    public async Task<int> AddDocumentsBatchAsync(string projectId,int packageId,IReadOnlyList<CostDocumentInput> inputs,CancellationToken ct)
    {
        if(inputs.Count==0)return 0;
        var normalized=inputs.Select(NormalizeDocumentInput).ToArray();
        foreach(var input in normalized)ValidateDocument(input);
        if(normalized.GroupBy(x=>Normalize(x.DocumentoCodigo),StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))
            throw new InvalidOperationException("Documento duplicado no lote.");

        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await EnsurePackageAsync(c,tx,projectId,packageId,ct);
            await EnsureDocumentModelAsync(c,tx,packageId,ct);
            foreach(var i in normalized)
            {
                await EnsureUniqueDocumentAsync(c,tx,packageId,i.DocumentoCodigo,null,ct);
                await using var cmd=new SqlCommand("""
                    INSERT INTO costs.PackageDocument(PackageId,DocumentoCodigo,Disciplina,HHAlocada,CustoAlocado,ModoHH,ModoCusto)
                    VALUES(@id,@doc,NULL,@hh,@cost,@modeH,@modeC);
                    """,c,tx);
                cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
                cmd.Parameters.Add("@doc",SqlDbType.NVarChar,255).Value=Normalize(i.DocumentoCodigo);
                Decimal(cmd,"@hh",i.HHAlocada??0m);
                Decimal(cmd,"@cost",i.CustoAlocado??0m);
                cmd.Parameters.Add("@modeH",SqlDbType.VarChar,20).Value=i.ModoHH.ToUpperInvariant();
                cmd.Parameters.Add("@modeC",SqlDbType.VarChar,20).Value=i.ModoCusto.ToUpperInvariant();
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await RecalculatePackageBaseIfFullyManualAsync(c,tx,packageId,ct);
            await ValidatePackageAllocationAsync(c,tx,packageId,ct);
            await tx.CommitAsync(ct);
            return normalized.Length;
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }
    public async Task<int> AddAddendumAsync(string projectId,int packageId,CostAddendumInput i,CancellationToken ct){if(i.HHAdicional<0||i.CustoAdicional<0||string.IsNullOrWhiteSpace(i.Descricao))throw new ArgumentException("Aditivo inválido.");
        await using(var model=await Open(ct))if(PackageModel.IsDiscipline(await PackageModelAsync(model,null,packageId,ct)))throw new InvalidOperationException("Pacote Por Disciplina: aditivos ainda não são suportados. Ajuste o HH/custo previsto da disciplina (a versão anterior fica no histórico).");
        return await Insert("INSERT INTO costs.PackageAddendum(PackageId,HHAdicional,CustoAdicional,Data,Descricao) SELECT @id,@h,@c,@dt,@d WHERE EXISTS(SELECT 1 FROM costs.Package WHERE Id=@id AND Project_ID_ACC=@p);SELECT CAST(SCOPE_IDENTITY() AS int);",projectId,ct,c=>{c.Parameters.Add("@id",SqlDbType.Int).Value=packageId;c.Parameters.Add("@h",SqlDbType.Decimal).Value=i.HHAdicional;c.Parameters.Add("@c",SqlDbType.Decimal).Value=i.CustoAdicional;c.Parameters.Add("@dt",SqlDbType.Date).Value=i.Data.Date;c.Parameters.AddWithValue("@d",i.Descricao.Trim());});}
    /// <summary>
    /// Modelo Excel compatível com ReadExcelAsync: a PRIMEIRA aba possui cabeçalhos na linha 1.
    /// Exemplos ficam apenas na segunda aba para não gerar cadastros fictícios.
    /// </summary>
    public Task<byte[]> PackageImportTemplateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var workbook = new OfficeOpenXml.ExcelPackage();
        var ws = workbook.Workbook.Worksheets.Add("Pacotes");
        var headers = new[]
        {
            "Pacote", "Número do documento", "HH previsto", "Valor/hora", "Empresa", "AliasTimesheet"
        };
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        var header = ws.Cells[1, 1, 1, headers.Length];
        header.Style.Font.Bold = true;
        header.Style.Font.Color.SetColor(System.Drawing.Color.White);
        header.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(24, 35, 56));
        header.AutoFilter = true;
        ws.View.FreezePanes(2, 1);
        ws.Row(1).Height = 26;
        ws.Column(1).Width = 28;
        ws.Column(2).Width = 46;
        ws.Column(3).Width = 18;
        ws.Column(4).Width = 18;
        ws.Column(5).Width = 26;
        ws.Column(6).Width = 28;
        ws.Cells[2, 3, 201, 3].Style.Numberformat.Format = "#,##0.00";
        ws.Cells[2, 4, 201, 4].Style.Numberformat.Format = "#,##0.00";

        var help = workbook.Workbook.Worksheets.Add("Instruções");
        help.Cells["A1"].Value = "N&A CONNECT | MODELO DE IMPORTAÇÃO DE PACOTES";
        help.Cells["A1"].Style.Font.Bold = true;
        help.Cells["A1"].Style.Font.Size = 14;
        help.Cells["A3"].Value = "1. Preencha a aba Pacotes a partir da linha 2. Não altere os cabeçalhos da linha 1.";
        help.Cells["A4"].Value = "2. Repita o nome do pacote em cada linha: uma linha para cada Número do Documento.";
        help.Cells["A5"].Value = "3. HH previsto e Valor/hora devem ser números >= 0. O servidor calcula o custo = HH previsto × Valor/hora.";
        help.Cells["A6"].Value = "4. Empresa e AliasTimesheet são opcionais. Alias ajuda a identificar lançamentos do Timesheet.";
        help.Cells["A7"].Value = "5. Não informe totais do pacote: o servidor soma os documentos. A disciplina vem da Norma de Coordenação.";
        help.Cells["A8"].Value = "6. Importe o arquivo, confira a prévia e só depois confirme. A importação é transacional.";
        help.Cells["A10"].Value = "EXEMPLO ILUSTRATIVO — NÃO É IMPORTADO";
        help.Cells["A10"].Style.Font.Bold = true;
        for (var i = 0; i < headers.Length; i++) help.Cells[11, i + 1].Value = headers[i];
        help.Cells["A12"].Value = "PACOTE TESTE MECÂNICA";
        help.Cells["B12"].Value = "EXEMPLO-000-ME0-AT-001";
        help.Cells["C12"].Value = 40;
        help.Cells["D12"].Value = 150;
        help.Cells["E12"].Value = "EMPRESA EXEMPLO";
        help.Cells["F12"].Value = "NOME NO TIMESHEET";
        help.Cells["A13"].Value = "PACOTE TESTE MECÂNICA";
        help.Cells["B13"].Value = "EXEMPLO-000-ME0-AT-002";
        help.Cells["C13"].Value = 20;
        help.Cells["D13"].Value = 150;
        help.Cells["E13"].Value = "EMPRESA EXEMPLO";
        help.Cells["F13"].Value = "NOME NO TIMESHEET";
        help.Cells[11, 1, 11, headers.Length].Style.Font.Bold = true;
        help.Column(1).Width = 34;
        help.Column(2).Width = 46;
        help.Column(3).Width = 18;
        help.Column(4).Width = 18;
        help.Column(5).Width = 27;
        help.Column(6).Width = 28;
        return Task.FromResult(workbook.GetAsByteArray());
    }

    public async Task<CostImportPreview> PreviewExcelAsync(string projectId,Stream file,CancellationToken ct){var lines=await ReadExcelAsync(file,ct);var rows=lines.Select(x=>new CostImportRow(x.Row,string.IsNullOrWhiteSpace(x.Error)?1:0,projectId,x.Package,x.Document,x.Error,x.DocumentHours,x.HourlyRate,x.DocumentCost)).ToList();var duplicate=lines.Where(x=>string.IsNullOrWhiteSpace(x.Error)).GroupBy(x=>$"{x.Package}|{Normalize(x.Document)}",StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1).SelectMany(x=>x).Select(x=>x.Row).ToHashSet();rows=rows.Select(x=>duplicate.Contains(x.Row)?x with{Valid=0,Error="Documento duplicado no mesmo pacote"}:x).ToList();return new(rows,rows.Count(x=>x.Valid==1),rows.Count(x=>x.Valid==0),0);}
    public async Task<CostImportCommitResult> CommitExcelAsync(string projectId,Stream file,CancellationToken ct){var lines=await ReadExcelAsync(file,ct);if(lines.Any(x=>!string.IsNullOrWhiteSpace(x.Error)))throw new InvalidOperationException("A importação possui linhas inválidas.");if(lines.GroupBy(x=>$"{x.Package}|{Normalize(x.Document)}",StringComparer.OrdinalIgnoreCase).Any(x=>x.Count()>1))throw new InvalidOperationException("A importação possui documentos duplicados.");await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var packages=0;var documents=0;try{foreach(var group in lines.GroupBy(x=>x.Package,StringComparer.OrdinalIgnoreCase)){var first=group.First();await using var existing=new SqlCommand("SELECT COUNT(1) FROM costs.Package WHERE Project_ID_ACC=@p AND NomeReferencia=@n AND Ativo=1",c,(SqlTransaction)tx);existing.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;existing.Parameters.AddWithValue("@n",first.Package);if(Convert.ToInt32(await existing.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)>0)throw new InvalidOperationException($"Pacote já existe: {first.Package}.");var hhTotal=group.Select(x=>x.TotalHours).FirstOrDefault(x=>x.HasValue)??group.Sum(x=>x.DocumentHours??0m);var costTotal=group.Select(x=>x.TotalCost).FirstOrDefault(x=>x.HasValue)??group.Sum(x=>x.DocumentCost??0m);if(hhTotal<0||costTotal<0)throw new InvalidOperationException("Totais não podem ser negativos.");await using var insertPackage=new SqlCommand("INSERT INTO costs.Package(Project_ID_ACC,Empresa,NomeReferencia,AliasTimesheet,HHBase,CustoBase) VALUES(@p,@e,@n,@a,@h,@c);SELECT CAST(SCOPE_IDENTITY() AS int)",c,(SqlTransaction)tx);insertPackage.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;insertPackage.Parameters.AddWithValue("@e",(object?)first.Company??DBNull.Value);insertPackage.Parameters.AddWithValue("@n",first.Package);insertPackage.Parameters.AddWithValue("@a",(object?)first.Alias??DBNull.Value);insertPackage.Parameters.Add("@h",SqlDbType.Decimal).Value=hhTotal;insertPackage.Parameters.Add("@c",SqlDbType.Decimal).Value=costTotal;var packageId=Convert.ToInt32(await insertPackage.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);packages++;foreach(var line in group){var input=new CostDocumentInput(line.Document,line.Discipline,line.DocumentHours,line.DocumentCost,line.DocumentHours.HasValue?"MANUAL":"AUTO",line.DocumentCost.HasValue?"MANUAL":"AUTO");ValidateDocument(input);await using var doc=new SqlCommand("INSERT INTO costs.PackageDocument(PackageId,DocumentoCodigo,Disciplina,HHAlocada,CustoAlocado,ModoHH,ModoCusto) VALUES(@id,@d,@disc,@h,@c,@mh,@mc)",c,(SqlTransaction)tx);doc.Parameters.Add("@id",SqlDbType.Int).Value=packageId;doc.Parameters.AddWithValue("@d",Normalize(line.Document));doc.Parameters.AddWithValue("@disc",(object?)line.Discipline??DBNull.Value);doc.Parameters.Add("@h",SqlDbType.Decimal).Value=line.DocumentHours??0m;doc.Parameters.Add("@c",SqlDbType.Decimal).Value=line.DocumentCost??0m;doc.Parameters.AddWithValue("@mh",input.ModoHH);doc.Parameters.AddWithValue("@mc",input.ModoCusto);documents+=await doc.ExecuteNonQueryAsync(ct);}await ValidatePackageAllocationAsync(c,(SqlTransaction)tx,packageId,ct);}await tx.CommitAsync(ct);return new(packages,documents);}catch{await tx.RollbackAsync(ct);throw;}}
    public async Task<IReadOnlyList<CostRecognitionLine>> RecognitionAsync(string projectId,CancellationToken ct)=> (await EvaluateAsync(projectId,Array.Empty<CostProfessional>(),ct)).Lines;
    public async Task<bool> UpdateProfessionalAsync(string projectId,int professionalId,CostProfessionalUpdate i,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(i.Nome)||i.HHBaseMensal<=0)throw new ArgumentException("Nome e HH base positiva são obrigatórios.");
        const string q="UPDATE costs.ProjectProfessional SET Nome=@n,Empresa=@e,AliasTimesheet=@a,HHBaseMensal=@h,Funcao=@f,Disciplina=@d,Ativo=@at,DesativadoEm=CASE WHEN @at=0 AND Ativo=1 THEN SYSUTCDATETIME() WHEN @at=1 THEN NULL ELSE DesativadoEm END,AtualizadoEm=sysutcdatetime() WHERE Id=@id AND Project_ID_ACC=@p";
        await using var c=await Open(ct);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);try{await using var cmd=Cmd(q,c,projectId);cmd.Transaction=tx;cmd.Parameters.Add("@id",SqlDbType.Int).Value=professionalId;cmd.Parameters.AddWithValue("@n",i.Nome.Trim());cmd.Parameters.AddWithValue("@e",(object?)NullIfBlank(i.Empresa)??DBNull.Value);cmd.Parameters.AddWithValue("@a",(object?)NullIfBlank(i.AliasTimesheet)??DBNull.Value);cmd.Parameters.AddWithValue("@f",(object?)NullIfBlank(i.Funcao)??DBNull.Value);cmd.Parameters.AddWithValue("@d",(object?)NullIfBlank(i.Disciplina)??DBNull.Value);var h=cmd.Parameters.Add("@h",SqlDbType.Decimal);h.Precision=18;h.Scale=6;h.Value=i.HHBaseMensal;cmd.Parameters.Add("@at",SqlDbType.Bit).Value=i.Ativo;var changed=await cmd.ExecuteNonQueryAsync(ct)>0;if(changed&&!i.Ativo)await CloseProfessionalPeriodsAsync(c,tx,professionalId,ct);await tx.CommitAsync(ct);return changed;}catch{await tx.RollbackAsync(ct);throw;}
    }
    public async Task<bool> UpdateItemAsync(string projectId,int professionalId,int itemId,CostItemUpdate i,CancellationToken ct)
    {
        return await Task.FromException<bool>(new InvalidOperationException("Custos profissionais são históricos. Registre a alteração como nova vigência para não recalcular o passado."));
    }
    public async Task<IReadOnlyList<CostAddendum>> AddendaAsync(string projectId,int packageId,CancellationToken ct)
    {
        const string q="SELECT a.Id,a.PackageId,a.Data,a.Descricao,a.HHAdicional,a.CustoAdicional,a.Ativo FROM costs.PackageAddendum a JOIN costs.Package p ON p.Id=a.PackageId WHERE p.Project_ID_ACC=@p AND a.PackageId=@id ORDER BY a.Data DESC,a.Id DESC";
        await using var c=await Open(ct);await using var cmd=Cmd(q,c,projectId);cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
        await using var r=await cmd.ExecuteReaderAsync(ct);var x=new List<CostAddendum>();
        while(await r.ReadAsync(ct))x.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetDateTime(2),r.GetString(3),Dec(r,4),Dec(r,5),r.GetBoolean(6)));return x;
    }
    /// <summary>Rateio final AUTO/MANUAL dos documentos ATIVOS — mesma CostAllocationRules.Allocate do reconhecimento.</summary>
    public async Task<CostPackageAllocation> AllocationAsync(string projectId,int packageId,CancellationToken ct)
    {
        var package=(await PackagesAsync(projectId,ct)).SingleOrDefault(x=>x.Id==packageId)??throw new ArgumentException("Pacote não pertence ao projeto atual.");
        if(PackageModel.IsDiscipline(package.ModeloCadastro))return await DisciplineAllocationAsync(projectId,package,ct);
        var docs=await RawDocumentsAsync(projectId,packageId,ct);
        return BuildAllocation(package,docs,await LoadPackageContextAsync(projectId,ct));
    }
    private static string? NullIfBlank(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
    private sealed record CostEvaluation(IReadOnlyList<CostPackage> Packages,IReadOnlyList<TimesheetWork> Rows,IReadOnlyList<CostRecognitionLine> Lines,decimal InternalRecognizedCost,decimal InternalHours,decimal PendingHours);
    private async Task<CostEvaluation> EvaluateAsync(string projectId,IReadOnlyList<CostProfessional> professionals,CancellationToken ct)
    {
        var packages=await PackagesAsync(projectId,ct);var aliases=await PackageAliasesAsync(projectId,ct);var rows=await TimesheetRowsAsync(projectId,ct);var docs=new Dictionary<int,IReadOnlyList<CostDocument>>();
        foreach(var package in packages.Where(x=>x.Ativo))docs[package.Id]=await RawDocumentsAsync(projectId,package.Id,ct);
        var context=await LoadPackageContextAsync(projectId,ct);
        var candidates=docs.SelectMany(x=>x.Value.Where(d=>d.Ativo).Select(d=>new PackageDocumentMatch(x.Key,d.DocumentoCodigo,PackageFallback(packages.Single(p=>p.Id==x.Key)),aliases.TryGetValue(x.Key,out var a)?a:Array.Empty<(string,string)>()))).ToList();
        var assignments=CostTimesheetMatchingRules.AssignPackages(rows,candidates);var lines=new List<CostRecognitionLine>();
        foreach(var package in packages.Where(x=>x.Ativo)){
            if(!docs.TryGetValue(package.Id,out var packageDocs)||packageDocs.Count==0)continue;
            var activeDocs=packageDocs.Where(d=>d.Ativo).ToList();if(activeDocs.Count==0)continue;
            var allocation=BuildAllocation(package,activeDocs,context);
            foreach(var doc in allocation.Documentos) {
                var docKey=Normalize(doc.DocumentoCodigo);
                var assigned=assignments.Where(a=>a.PackageId==package.Id&&Normalize(a.DocumentoCodigo)==docKey).Select(a=>rows[a.RowIndex]).ToList();
                var ambiguous=assignments.Any(a=>a.Ambiguous&&Normalize(rows[a.RowIndex].DocumentoCodigo)==docKey&&
                    candidates.Any(c=>c.PackageId==package.Id&&Normalize(c.DocumentoCodigo)==docKey&&CostTimesheetMatchingRules.Matches(c.FallbackAlias,c.Aliases,rows[a.RowIndex].Name)));
                var hours=assigned.Sum(x=>Math.Max(0,x.Hours));
                var capped=allocation.Erro is null?Math.Min(hours,doc.HHFinal):0m;
                lines.Add(new(package.Id,null,doc.DocumentoCodigo,doc.Disciplina,hours,doc.HHFinal,capped,Math.Max(hours-capped,0),doc.CustoFinal,doc.CustoLiberado,0m,ambiguous,
                    doc.Doc3,doc.DocumentStatus,doc.StatusPercentual,doc.HHOriginal,doc.CustoOriginal,doc.CustoFinal,doc.Cancelado,
                    ambiguous?JoinDiagnostic(doc.Diagnostico,"AMBIGUOUS: HH pendente de de/para."):doc.Diagnostico,doc.HHRedistribuida,doc.HHAdicional,doc.CustoRedistribuido,doc.CustoAdicional));
            }
        }
        // Pacotes Por Disciplina: só linhas livres (sem pacote Por Documento). Indicadores recebem o APROPRIADO; bruto/excedente ficam na linha.
        var free=assignments.Where(a=>a.PackageId is null&&!a.Ambiguous&&!a.PackageNameOnly).Select(a=>a.RowIndex).ToList();
        var disciplines=await EvaluateDisciplinesAsync(projectId,packages,rows,free,context,ct);
        lines.AddRange(disciplines.Lines.Select(RecognitionLine));
        if(disciplines.Assignments.Count>0)
        {
            // Linhas do profissional do pacote não viram custo interno (sem dupla contagem); pendências continuam em PendingHours.
            var owned=disciplines.Assignments.ToDictionary(x=>x.RowIndex);
            assignments=assignments.Select(a=>owned.TryGetValue(a.RowIndex,out var d)?a with {PackageId=d.PackageId,DocumentoCodigo=d.DocumentoCodigo,Ambiguous=d.PackageId is null}:a).ToList();
        }
        var canonical=await new APS.WebApp.Services.Planning.PmoActualFactsProvider(configuration,new NA_Connect.Services.Coordination.CoordinationStandardRepository(configuration,Microsoft.Extensions.Logging.Abstractions.NullLogger<NA_Connect.Services.Coordination.CoordinationStandardRepository>.Instance)).ReadAsync(projectId,ct);
        var historical=canonical.HistoricalMeasurement;
        if(historical is not null)
        {
            foreach(var packageGroup in historical.Documents.GroupBy(d=>d.PackageId))
            {
                lines.RemoveAll(l=>l.PackageId==packageGroup.Key);
                lines.AddRange(packageGroup.Select(d=>new CostRecognitionLine(d.PackageId,null,d.Document,d.Discipline,d.TimesheetHours,d.ContractHours,
                    d.RecognizedHours+d.Hours,d.ExcessHours,d.ContractCost,d.RecognizedCost+d.Cost,d.ExcessCost,false,
                    Diagnostico:"MEDICAO_HISTORICA_CONFIRMADA: Timesheet + complemento derivado; teto disciplinar. Complemento HH="+d.Hours.ToString(CultureInfo.InvariantCulture))));
            }
        }
        var internalCost=await InternalCostByDateAsync(projectId,rows,assignments,professionals,ct);
        return new(packages,rows,lines,internalCost.Cost,internalCost.Hours,rows.Sum(x=>Math.Max(0,x.Hours))-lines.Sum(x=>x.TimesheetHours)-internalCost.Hours);
    }
    private async Task<(decimal Cost,decimal Hours)> InternalCostByDateAsync(string projectId,IReadOnlyList<TimesheetWork> rows,IReadOnlyList<TimesheetAssignment> assignments,IReadOnlyList<CostProfessional> professionals,CancellationToken ct){
        // Mesma regra de custo/hora da tela de HH sem documento (AT) e do Resumo LAB: ProfessionalHourlyCostCatalog.
        if(professionals.Count==0)return (0m,0m);
        await using var c=await Open(ct);var catalog=await ProfessionalHourlyCostCatalog.LoadAsync(c,projectId,ct);
        var known=professionals.Select(p=>p.Id).ToHashSet();
        decimal total=0,hours=0;
        foreach(var assignment in assignments.Where(x=>x.PackageId is null&&!x.Ambiguous&&!x.PackageNameOnly))
        {
            var row=rows[assignment.RowIndex];
            var resolution=catalog.Resolve(row.Name,row.Date);
            if(resolution.ProfessionalId is not int id||!known.Contains(id))continue;
            hours+=Math.Max(0,row.Hours);
            total+=resolution.CostFor(Math.Max(0,row.Hours))??0m;
        }
        return (total,hours);
    }

    private sealed record ExcelCostLine(int Row,string Package,string Document,string? Company,string? Alias,string? Discipline,decimal? DocumentHours,decimal? DocumentCost,decimal? TotalHours,decimal? TotalCost,string? Error,decimal? HourlyRate=null);
    private static async Task<IReadOnlyList<ExcelCostLine>> ReadExcelAsync(Stream file,CancellationToken ct){if(!file.CanRead||file.Length<=0||file.Length>10*1024*1024)throw new InvalidOperationException("Arquivo Excel inválido ou excede 10 MB.");using var ms=new MemoryStream();await file.CopyToAsync(ms,ct);using var workbook=new OfficeOpenXml.ExcelPackage(ms);var ws=workbook.Workbook.Worksheets.FirstOrDefault()??throw new InvalidOperationException("Planilha vazia.");if(ws.Dimension is null)throw new InvalidOperationException("Planilha vazia.");var headers=new Dictionary<string,int>();for(var col=1;col<=ws.Dimension.End.Column;col++){var header=Header(ws.Cells[1,col].Text);if(!string.IsNullOrWhiteSpace(header))headers[header]=col;}int Col(params string[] names)=>names.Select(x=>headers.TryGetValue(x,out var col)?col:0).FirstOrDefault(x=>x>0);var packageCol=Col("PACOTE","NOMEPACOTE","PROFISSIONAL","PACOTEPROFISSIONAL","NOMEPROFISSIONAL");var documentCol=Col("DOCUMENTOCODIGO","DOCUMENTO","NUMERODOCUMENTO","NUMERODODOCUMENTO","NADOCUMENTNUMBER");if(packageCol==0||documentCol==0)throw new InvalidOperationException("Headers Pacote e DocumentoCodigo são obrigatórios.");var companyCol=Col("EMPRESA");var aliasCol=Col("ALIASTIMESHEET");var disciplineCol=Col("DISCIPLINA");var documentHoursCol=Col("HHPREVISTADOCUMENTO","HHALOCADA","HHPREVISTODOCUMENTO","HHPREVISTODODOCUMENTO","HHPREVISTO","HHPREVISTA");var hourlyRateCol=Col("VALORHORA","CUSTOHORA","VALORPORHORA","VALORHORARIO");var documentCostCol=Col("CUSTODOCUMENTO","CUSTOALOCADO");var totalHoursCol=Col("HHTOTALPACOTE","HHBASE");var totalCostCol=Col("CUSTOTOTALPACOTE","CUSTOBASE");var lines=new List<ExcelCostLine>();for(var row=2;row<=ws.Dimension.End.Row;row++){var package=ws.Cells[row,packageCol].Text.Trim();var document=ws.Cells[row,documentCol].Text.Trim();if(string.IsNullOrWhiteSpace(package)&&string.IsNullOrWhiteSpace(document))continue;string? error=null;decimal? dh=null,dc=null,th=null,tc=null,rate=null;try{dh=Number(ws,row,documentHoursCol);dc=Number(ws,row,documentCostCol);rate=Number(ws,row,hourlyRateCol);if(dh.HasValue&&rate.HasValue)dc=dh.Value*rate.Value;th=Number(ws,row,totalHoursCol);tc=Number(ws,row,totalCostCol);if(string.IsNullOrWhiteSpace(package)||string.IsNullOrWhiteSpace(document))error="Pacote e DocumentoCodigo são obrigatórios.";}catch(FormatException){error="Valor numérico inválido ou negativo.";}lines.Add(new(row,package,document,Text(ws,row,companyCol),Text(ws,row,aliasCol),Text(ws,row,disciplineCol),dh,dc,th,tc,error,rate));}return ValidateExcelLines(lines);}
    private static string Header(string? value)=>Regex.Replace(CostTimesheetMatchingRules.NormalizeName(value),@"[^\p{L}\p{Nd}]",string.Empty);

    private static string? Text(OfficeOpenXml.ExcelWorksheet ws,int row,int col)=>col==0?null:ws.Cells[row,col].Text?.Trim();
    private static decimal? Number(OfficeOpenXml.ExcelWorksheet ws,int row,int col){if(col==0)return null;var cell=ws.Cells[row,col];if(cell.Value is double or float or decimal or int or long){var number=Convert.ToDecimal(cell.Value,CultureInfo.InvariantCulture);if(number<0)throw new FormatException();return number;}var raw=cell.Text?.Trim();if(string.IsNullOrWhiteSpace(raw))return null;if(decimal.TryParse(raw,NumberStyles.Number,CultureInfo.GetCultureInfo("pt-BR"),out var value)&&value>=0)return value;throw new FormatException();}
    private async Task<string> TimesheetStatusAsync(string projectId,CancellationToken ct){await using var c=await Open(ct);const string q="SELECT P.CR_OS,M.Status_Mapeamento FROM dbo.Projetos P LEFT JOIN dbo.VW_CR_OS_Project_Map M ON M.CR_OS=P.CR_OS WHERE P.Project_ID_ACC=@p AND P.Is_Ativo=1";await using var cmd=Cmd(q,c,projectId);await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return "Projeto não encontrado ou inativo";var cr=Txt(r,0);if(string.IsNullOrWhiteSpace(cr))return "CR/OS não configurado";var map=Txt(r,1);return map is "UNICO" or "ATIVO_UNICO"?"OK":"CR/OS ambíguo — revisar cadastro";}
    private async Task<decimal> WorkedAsync(string projectId,CancellationToken ct){await using var c=await Open(ct);const string q="SELECT COALESCE(SUM(ISNULL(Trabalhadas,0)),0) FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Project_ID_ACC_Canonico=@p AND Status_Mapeamento_CR_OS IN ('UNICO','ATIVO_UNICO')";await using var cmd=Cmd(q,c,projectId);return Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct)??0m,CultureInfo.InvariantCulture);}
    private async Task<IReadOnlyList<TimesheetWork>> TimesheetRowsAsync(string projectId,CancellationToken ct){await using var c=await Open(ct);const string q="SELECT ISNULL(Nome,''),ISNULL(DocumentoCodigo,''),ISNULL(Trabalhadas,0),CONVERT(date,[data]),doc3,Disciplina_PT_Padrao FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Project_ID_ACC_Canonico=@p AND Status_Mapeamento_CR_OS IN ('UNICO','ATIVO_UNICO')";await using var cmd=Cmd(q,c,projectId);await using var r=await cmd.ExecuteReaderAsync(ct);var rows=new List<TimesheetWork>();while(await r.ReadAsync(ct))rows.Add(new(r.GetString(0),r.GetString(1),Dec(r,2),r.IsDBNull(3)?null:DateOnly.FromDateTime(r.GetDateTime(3)),Txt(r,4),Txt(r,5)));return rows;}
    private async Task<Dictionary<int,List<(string Text,string Type)>>> PackageAliasesAsync(string projectId,CancellationToken ct){const string q="SELECT a.PackageId,a.TextoMatch,a.TipoMatch FROM costs.TimesheetMatchAlias a JOIN costs.Package p ON p.Id=a.PackageId WHERE p.Project_ID_ACC=@p AND a.Ativo=1";await using var c=await Open(ct);await using var cmd=Cmd(q,c,projectId);await using var r=await cmd.ExecuteReaderAsync(ct);var d=new Dictionary<int,List<(string,string)>>();while(await r.ReadAsync(ct)){var id=r.GetInt32(0);if(!d.TryGetValue(id,out var list))d[id]=list=new();list.Add((r.GetString(1),r.GetString(2)));}return d;}
    private static async Task ValidatePackageAllocationAsync(SqlConnection c,SqlTransaction tx,int packageId,CancellationToken ct)
    {
        const string q="SELECT p.HHBase,p.CustoBase FROM costs.Package p WHERE p.Id=@id;SELECT HHAlocada,ModoHH,CustoAlocado,ModoCusto FROM costs.PackageDocument WHERE PackageId=@id AND Ativo=1;";
        await using var cmd=new SqlCommand(q,c,tx);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
        await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new InvalidOperationException("Pacote inexistente.");
        var hh=Dec(r,0);var cost=Dec(r,1);
        await r.NextResultAsync(ct);
        var values=new List<(decimal? H,string MH,decimal? C,string MC)>();
        while(await r.ReadAsync(ct))values.Add((Dec(r,0),r.GetString(1),Dec(r,2),r.GetString(3)));
        if(values.Count==0)return;
        CostAllocationRules.Allocate(hh,values.Select(x=>(x.MH.Equals("MANUAL",StringComparison.OrdinalIgnoreCase)?x.H:(decimal?)null,x.MH)).ToList());
        CostAllocationRules.Allocate(cost,values.Select(x=>(x.MC.Equals("MANUAL",StringComparison.OrdinalIgnoreCase)?x.C:(decimal?)null,x.MC)).ToList());
    }

    private static CostDocumentInput NormalizeDocumentInput(CostDocumentInput input)
    {
        var code=Normalize(input.DocumentoCodigo);
        if(input.ValorHora is null)return input with {DocumentoCodigo=code,Disciplina=null};
        if(input.ValorHora<0)throw new ArgumentException("Valor/hora não pode ser negativo.");
        if(input.HHAlocada is null)throw new ArgumentException("Informe o HH previsto para calcular o custo pelo valor/hora.");
        if(input.HHAlocada<0)throw new ArgumentException("HH previsto não pode ser negativo.");
        var cost=decimal.Round(input.HHAlocada.Value*input.ValorHora.Value,6,MidpointRounding.AwayFromZero);
        return input with {
            DocumentoCodigo=code,
            Disciplina=null,
            CustoAlocado=cost,
            ModoHH="MANUAL",
            ModoCusto="MANUAL"
        };
    }

    private static void ValidateDocument(CostDocumentInput input)
    {
        if(string.IsNullOrWhiteSpace(Normalize(input.DocumentoCodigo)))throw new ArgumentException("Número do documento é obrigatório.");
        if((input.HHAlocada??0)<0||(input.CustoAlocado??0)<0||(input.ValorHora??0)<0)throw new ArgumentException("HH, custo e valor/hora não podem ser negativos.");
        if(!new[]{"AUTO","MANUAL"}.Contains(input.ModoHH,StringComparer.OrdinalIgnoreCase)||!new[]{"AUTO","MANUAL"}.Contains(input.ModoCusto,StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Modo de rateio inválido.");
        if(input.ValorHora.HasValue&&input.HHAlocada is null)throw new ArgumentException("HH previsto é obrigatório quando Valor/hora é informado.");
    }

    private static async Task EnsurePackageAsync(SqlConnection c,SqlTransaction tx,string projectId,int packageId,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("SELECT COUNT(1) FROM costs.Package WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id AND Project_ID_ACC=@p",c,tx);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
        cmd.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=1)
            throw new InvalidOperationException("Pacote não pertence ao projeto atual.");
    }

    private static async Task EnsureUniquePackageNameAsync(SqlConnection c,SqlTransaction tx,string projectId,string name,int? excludeId,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1)
            FROM costs.Package WITH(UPDLOCK,HOLDLOCK)
            WHERE Project_ID_ACC=@p AND Ativo=1
              AND UPPER(LTRIM(RTRIM(NomeReferencia)))=UPPER(LTRIM(RTRIM(@name)))
              AND (@exclude IS NULL OR Id<>@exclude);
            """,c,tx);
        cmd.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;
        cmd.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=name.Trim();
        cmd.Parameters.Add("@exclude",SqlDbType.Int).Value=excludeId is null?DBNull.Value:excludeId.Value;
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)>0)
            throw new InvalidOperationException("Já existe um pacote/profissional ativo com esse nome neste projeto.");
    }

    private static async Task EnsureDocumentAsync(SqlConnection c,SqlTransaction tx,int packageId,int documentId,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("SELECT COUNT(1) FROM costs.PackageDocument WITH(UPDLOCK,HOLDLOCK) WHERE Id=@document AND PackageId=@package",c,tx);
        cmd.Parameters.Add("@document",SqlDbType.Int).Value=documentId;
        cmd.Parameters.Add("@package",SqlDbType.Int).Value=packageId;
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=1)
            throw new InvalidOperationException("Documento não pertence ao pacote atual.");
    }

    private static async Task EnsureUniqueDocumentAsync(SqlConnection c,SqlTransaction tx,int packageId,string documentCode,int? excludeId,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1)
            FROM costs.PackageDocument WITH(UPDLOCK,HOLDLOCK)
            WHERE PackageId=@package AND Ativo=1
              AND UPPER(LTRIM(RTRIM(DocumentoCodigo)))=@doc
              AND (@exclude IS NULL OR Id<>@exclude);
            """,c,tx);
        cmd.Parameters.Add("@package",SqlDbType.Int).Value=packageId;
        cmd.Parameters.Add("@doc",SqlDbType.NVarChar,255).Value=Normalize(documentCode);
        cmd.Parameters.Add("@exclude",SqlDbType.Int).Value=excludeId is null?DBNull.Value:excludeId.Value;
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)>0)
            throw new InvalidOperationException("Este Número do documento já está ativo neste pacote.");
    }

    private static async Task RecalculatePackageBaseIfFullyManualAsync(SqlConnection c,SqlTransaction tx,int packageId,CancellationToken ct)
    {
        const string q="""
            SELECT
                COUNT(*) AS Docs,
                SUM(CASE WHEN UPPER(ModoHH)='MANUAL' THEN 1 ELSE 0 END) AS ManualHH,
                SUM(CASE WHEN UPPER(ModoCusto)='MANUAL' THEN 1 ELSE 0 END) AS ManualCost,
                COALESCE(SUM(HHAlocada),0) AS HH,
                COALESCE(SUM(CustoAlocado),0) AS Cost
            FROM costs.PackageDocument
            WHERE PackageId=@id AND Ativo=1;
            """;
        int count,manualH,manualC;decimal hh,cost;
        await using(var cmd=new SqlCommand(q,c,tx))
        {
            cmd.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
            await using var r=await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            count=r.GetInt32(0);
            manualH=r.IsDBNull(1)?0:r.GetInt32(1);
            manualC=r.IsDBNull(2)?0:r.GetInt32(2);
            hh=Dec(r,3);
            cost=Dec(r,4);
        }

        if(count>0&&(manualH!=count||manualC!=count))return;
        await using var update=new SqlCommand("UPDATE costs.Package SET HHBase=@hh,CustoBase=@cost,AtualizadoEm=SYSUTCDATETIME() WHERE Id=@id",c,tx);
        update.Parameters.Add("@id",SqlDbType.Int).Value=packageId;
        Decimal(update,"@hh",count==0?0m:hh);
        Decimal(update,"@cost",count==0?0m:cost);
        await update.ExecuteNonQueryAsync(ct);
    }

    private static string Normalize(string? value)=>CostAllocationRules.NormalizeDocument(value);

    public async Task<bool> DeactivateAsync(string projectId,string entity,int id,CancellationToken ct)
    {
        if(entity=="item")throw new InvalidOperationException("Itens de custo não podem ser desativados retroativamente. Crie nova vigência e remova o item na prévia.");
        if(entity=="professional")return await DeactivateProfessionalAsync(projectId,id,ct);

        if(entity=="document")
        {
            await using var c=await Open(ct);
            await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try
            {
                int packageId;
                await using(var find=new SqlCommand("""
                    SELECT d.PackageId
                    FROM costs.PackageDocument d
                    JOIN costs.Package p ON p.Id=d.PackageId
                    WHERE d.Id=@id AND p.Project_ID_ACC=@p;
                    """,c,tx))
                {
                    find.Parameters.Add("@id",SqlDbType.Int).Value=id;
                    find.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;
                    var value=await find.ExecuteScalarAsync(ct);
                    if(value is null||value is DBNull)throw new InvalidOperationException("Documento não pertence ao projeto atual.");
                    packageId=Convert.ToInt32(value,CultureInfo.InvariantCulture);
                }
                await using(var update=new SqlCommand("UPDATE costs.PackageDocument SET Ativo=0,AtualizadoEm=SYSUTCDATETIME() WHERE Id=@id AND PackageId=@package",c,tx))
                {
                    update.Parameters.Add("@id",SqlDbType.Int).Value=id;
                    update.Parameters.Add("@package",SqlDbType.Int).Value=packageId;
                    if(await update.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("Documento não encontrado.");
                }
                await RecalculatePackageBaseIfFullyManualAsync(c,tx,packageId,ct);
                await ValidatePackageAllocationAsync(c,tx,packageId,ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch{await tx.RollbackAsync(ct);throw;}
        }

        var table=entity switch{"expense"=>"ProjectExpense","package"=>"Package","addendum"=>"PackageAddendum",_=>throw new ArgumentException("Tipo de custo inválido.")};
        var scope="p.Project_ID_ACC=@p";
        var q=entity switch{
            "expense"=>$"UPDATE p SET Ativo=0,AtualizadoEm=sysutcdatetime() FROM costs.{table} p WHERE p.Id=@id AND {scope}",
            "package"=>$"UPDATE p SET Ativo=0,AtualizadoEm=sysutcdatetime() FROM costs.{table} p WHERE p.Id=@id AND {scope}",
            "addendum"=>$"UPDATE a SET Ativo=0,AtualizadoEm=sysutcdatetime() FROM costs.{table} a JOIN costs.Package p ON p.Id=a.PackageId WHERE a.Id=@id AND {scope}",
            _=>throw new ArgumentException()
        };
        await using var connection=await Open(ct);
        await using var cmd=Cmd(q,connection,projectId);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=id;
        return await cmd.ExecuteNonQueryAsync(ct)>0;
    }
    private async Task<bool> DeactivateProfessionalAsync(string projectId,int id,CancellationToken ct){await using var c=await Open(ct);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);try{await using var cmd=new SqlCommand("UPDATE costs.ProjectProfessional SET Ativo=0,DesativadoEm=CASE WHEN Ativo=1 THEN SYSUTCDATETIME() ELSE DesativadoEm END,AtualizadoEm=SYSUTCDATETIME() WHERE Id=@id AND Project_ID_ACC=@p",c,tx);cmd.Parameters.Add("@id",SqlDbType.Int).Value=id;cmd.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=projectId;var changed=await cmd.ExecuteNonQueryAsync(ct)>0;if(changed)await CloseProfessionalPeriodsAsync(c,tx,id,ct);await tx.CommitAsync(ct);return changed;}catch{await tx.RollbackAsync(ct);throw;}}
    private static async Task CloseProfessionalPeriodsAsync(SqlConnection c,SqlTransaction tx,int id,CancellationToken ct){const string q="DECLARE @today date=CONVERT(date,SYSUTCDATETIME()); UPDATE costs.ProjectProfessionalCostPeriod SET Ativo=CASE WHEN VigenciaInicio>@today THEN 0 ELSE Ativo END,VigenciaFim=CASE WHEN VigenciaInicio<=@today THEN @today ELSE VigenciaFim END,AtualizadoEm=SYSUTCDATETIME() WHERE ProfessionalId=@id AND Ativo=1 AND VigenciaFim IS NULL;";await using var cmd=new SqlCommand(q,c,tx);cmd.Parameters.Add("@id",SqlDbType.Int).Value=id;await cmd.ExecuteNonQueryAsync(ct);}
    private async Task<int> Insert(string q,string p,CancellationToken ct,Action<SqlCommand> parameters){await using var c=await Open(ct);await using var cmd=Cmd(q,c,p);parameters(cmd);return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);}
    private async Task<IReadOnlyList<T>> ReadMany<T>(string q,string p,CancellationToken ct,Func<SqlDataReader,T> map){await using var c=await Open(ct);await using var cmd=Cmd(q,c,p);await using var r=await cmd.ExecuteReaderAsync(ct);var x=new List<T>();while(await r.ReadAsync(ct))x.Add(map(r));return x;}
    private static SqlCommand Cmd(string q,SqlConnection c,string p){var x=new SqlCommand(q,c){CommandTimeout=45};x.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=p;return x;}
    private static void Decimal(SqlCommand cmd,string name,decimal value){var p=cmd.Parameters.Add(name,SqlDbType.Decimal);p.Precision=18;p.Scale=6;p.Value=value;}
    private static string? Txt(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static decimal Dec(SqlDataReader r,int i)=>r.IsDBNull(i)?0m:Convert.ToDecimal(r.GetValue(i),CultureInfo.InvariantCulture);
}
