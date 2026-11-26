using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Costs;

public sealed record ProfessionalPeriodItemInput(string Category, string Description, string CalculationType, decimal Amount, decimal? Percentage, string CalculationBase = "REMUNERACAO");
public sealed record ProfessionalPeriodInput(DateOnly Start, DateOnly? End, decimal MonthlyHours, decimal Remuneration, decimal? ManualRate, bool ConfirmClosePrevious, IReadOnlyList<ProfessionalPeriodItemInput> Items, bool CopyPreviousComposition = true, IReadOnlyList<int>? ExcludePreviousItemIds = null);
public sealed record ProfessionalPeriodPreview(int? OpenPeriodId, DateOnly? CloseOpenPeriodOn, bool ConfirmationRequired, bool IsHistoricalBackfill = false);
public sealed record ProfessionalPeriodItem(int Id, string Category, string Description, string CalculationType, decimal Amount, decimal? Percentage, string CalculationBase, bool Active);
public sealed record ProfessionalCostPeriod(int Id, int ProfessionalId, DateOnly Start, DateOnly? End, decimal MonthlyHours, decimal Remuneration, decimal? ManualRate, decimal MonthlyCost, decimal HourlyRate, bool ManualRateUsed, bool Active, IReadOnlyList<ProfessionalPeriodItem> Items);
public sealed record ProjectTaxInput(string Name, string Type, DateOnly Start, DateOnly? End, string CalculationType, decimal? Percentage, decimal? FixedValue, string CalculationBase, bool ConfirmClosePrevious);
public sealed record ProjectTaxPeriod(int Id, DateOnly Start, DateOnly? End, string CalculationType, decimal? Percentage, decimal? FixedValue, string CalculationBase, bool Active);
public sealed record ProjectTax(int Id, string ProjectIdAcc, string Name, string Type, bool Active, IReadOnlyList<ProjectTaxPeriod> Periods);
public sealed record DocumentActualCost(string DocumentCode, decimal WorkedHours, decimal InternalActualCost, decimal PackageRecognizedCost, decimal ActualCost, decimal PackagePlannedCost, decimal PackageRemaining, decimal UncostedHours, int AmbiguousRows, bool PlannedIsPackageOnly,decimal RawPackageHours=0,decimal PackageExcessHours=0);
public sealed record CostCopyInput(string SourceProjectId, string DestinationProjectId, bool Professionals, bool ProfessionalComposition, bool ProjectTaxes, bool Packages, bool Expenses, bool SkipConflicts);
public sealed record CostCopyPreview(string DestinationProjectId, int ProfessionalsToCreate, int TaxDefinitionsToCreate, int PackagesToCreate, int ExpensesToCreate, IReadOnlyList<string> Conflicts);
public sealed record CostCopyResult(string DestinationProjectId, int ProfessionalsCreated, int TaxDefinitionsCreated, int PackagesCreated, int ExpensesCreated, IReadOnlyList<string> Skipped);
public sealed record CostCopyBatchInput(string SourceProjectId,IReadOnlyList<string> DestinationProjectIds,bool Professionals,bool ProfessionalComposition,bool ProjectTaxes,bool Packages,bool Expenses,bool SkipConflicts);
public sealed record CostCopyBatchPreview(IReadOnlyList<CostCopyPreview> Destinations);
public sealed record CostCopyBatchResult(IReadOnlyList<CostCopyResult> Destinations);

public sealed record ProfessionalImportRow(
    int Row,
    bool Valid,
    string Action,
    string? Funcao,
    string? Disciplina,
    string Nome,
    decimal Remuneracao,
    decimal Encargos,
    decimal Softwares,
    decimal Computadores,
    decimal TotalInformado,
    decimal TotalCalculado,
    decimal? CustoHora,
    DateOnly VigenciaInicio,
    DateOnly? VigenciaFim,
    decimal HHBaseMensal,
    string? Warning,
    string? Error);

public sealed record ProfessionalImportPreview(
    IReadOnlyList<ProfessionalImportRow> Rows,
    int Valid,
    int Invalid,
    int Warnings,
    int ProfessionalsToCreate,
    int PeriodsToCreate,
    int PeriodsToUpdate);

public sealed record ProfessionalImportResult(int ProfessionalsCreated,int ProfessionalsUpdated,int PeriodsCreated,int PeriodsUpdated);

public sealed record EligibleCostProject(string ProjectIdAcc,string Name,bool IsActive,bool HasSync);

public sealed record ProfessionalAssignmentInput(IReadOnlyList<int> ProfessionalIds,IReadOnlyList<string> DestinationProjectIds);
public sealed record ProfessionalAssignmentDestination(string ProjectIdAcc,string Name,int ToCreate,int AlreadyExists);
public sealed record ProfessionalAssignmentPreview(IReadOnlyList<ProfessionalAssignmentDestination> Destinations);
public sealed record ProfessionalAssignmentResult(int ProfessionalsCreated,IReadOnlyList<ProfessionalAssignmentDestination> Destinations);

public interface IProjectCostHistoryService
{
    Task<IReadOnlyList<ProfessionalCostPeriod>> ProfessionalPeriodsAsync(string projectId, int professionalId, CancellationToken ct);
    Task<ProfessionalPeriodPreview> PreviewProfessionalPeriodAsync(string projectId, int professionalId, DateOnly start, DateOnly? end, CancellationToken ct);
    Task<int> CreateProfessionalPeriodAsync(string projectId, int professionalId, ProfessionalPeriodInput input, CancellationToken ct);
    Task<IReadOnlyList<ProjectTax>> ProjectTaxesAsync(string projectId, CancellationToken ct);
    Task<ProfessionalPeriodPreview> PreviewProjectTaxPeriodAsync(string projectId, int taxId, DateOnly start, CancellationToken ct);
    Task<int> CreateProjectTaxPeriodAsync(string projectId, int? taxId, ProjectTaxInput input, CancellationToken ct);
    Task<bool> DeactivateProjectTaxAsync(string projectId,int taxId,CancellationToken ct);
    Task<IReadOnlyList<DocumentActualCost>> DocumentActualCostsAsync(string projectId, CancellationToken ct);
    Task<CostCopyPreview> PreviewCopyAsync(CostCopyInput input, CancellationToken ct);
    Task<CostCopyResult> CopyAsync(CostCopyInput input, CancellationToken ct);
    Task<CostCopyBatchPreview> PreviewCopyBatchAsync(CostCopyBatchInput input,CancellationToken ct);
    Task<CostCopyBatchResult> CopyBatchAsync(CostCopyBatchInput input,CancellationToken ct);
    Task<bool> UpdateProfessionalPeriodAsync(string projectId,int professionalId,int periodId,ProfessionalPeriodInput input,CancellationToken ct);
    Task<bool> DeactivateProfessionalPeriodAsync(string projectId,int professionalId,int periodId,CancellationToken ct);
    Task<byte[]> ProfessionalImportTemplateAsync(CancellationToken ct);
    Task<ProfessionalImportPreview> PreviewProfessionalImportAsync(string projectId,Stream file,CancellationToken ct);
    Task<ProfessionalImportResult> CommitProfessionalImportAsync(string projectId,Stream file,CancellationToken ct);
    Task<IReadOnlyList<EligibleCostProject>> EligibleProjectsAsync(CancellationToken ct);
    Task<ProfessionalAssignmentPreview> PreviewProfessionalAssignmentAsync(string sourceProjectId,ProfessionalAssignmentInput input,CancellationToken ct);
    Task<ProfessionalAssignmentResult> AssignProfessionalsAsync(string sourceProjectId,ProfessionalAssignmentInput input,CancellationToken ct);
}

public sealed class ProjectCostHistoryService(IConfiguration configuration, ICostManagementService costs) : IProjectCostHistoryService
{
    private async Task<SqlConnection> Open(CancellationToken ct) { var c = new SqlConnection(configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("Conexão indisponível.")); await c.OpenAsync(ct); return c; }

    public async Task<IReadOnlyList<ProfessionalCostPeriod>> ProfessionalPeriodsAsync(string projectId, int professionalId, CancellationToken ct)
    {
        await using var c = await Open(ct);
        const string ownerSql = "SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Id=@id AND Project_ID_ACC=@p";
        await using (var owner = new SqlCommand(ownerSql, c)) { owner.Parameters.Add("@id", SqlDbType.Int).Value = professionalId; owner.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId; if (Convert.ToInt32(await owner.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1) throw new KeyNotFoundException("Profissional não encontrado neste projeto."); }
        const string q = "SELECT Id,VigenciaInicio,VigenciaFim,HHBaseMensal,RemuneracaoBase,CustoHoraManual,Ativo FROM costs.ProjectProfessionalCostPeriod WHERE ProfessionalId=@id ORDER BY VigenciaInicio DESC,Id DESC";
        var raw = new List<(int Id, DateOnly Start, DateOnly? End, decimal Hours, decimal Remuneration, decimal? Manual, bool Active)>();
        await using (var cmd = new SqlCommand(q, c)) { cmd.Parameters.Add("@id", SqlDbType.Int).Value = professionalId; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) raw.Add((r.GetInt32(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.IsDBNull(2) ? null : DateOnly.FromDateTime(r.GetDateTime(2)), D(r, 3), D(r, 4), r.IsDBNull(5) ? null : D(r, 5), r.GetBoolean(6))); }
        var result = new List<ProfessionalCostPeriod>();
        foreach (var p in raw)
        {
            var items = new List<ProfessionalPeriodItem>();
            const string iq = "SELECT Id,Categoria,ISNULL(Descricao,''),TipoCalculo,Valor,Percentual,BaseCalculo,Ativo FROM costs.ProfessionalCostItem WHERE ProfessionalId=@professional AND PeriodId=@period ORDER BY Id";
            await using (var cmd = new SqlCommand(iq, c)) { cmd.Parameters.Add("@professional", SqlDbType.Int).Value = professionalId; cmd.Parameters.Add("@period", SqlDbType.Int).Value = p.Id; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) items.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), D(r, 4), r.IsDBNull(5) ? null : D(r, 5), r.GetString(6), r.GetBoolean(7))); }
            var activeItems = items.Where(x => x.Active).Select(x => new ProfessionalCostComponentRule(x.CalculationType, x.Amount, x.Percentage, x.CalculationBase));
            var rule = new ProfessionalCostPeriodRule(p.Id, p.Start, p.End, p.Hours, p.Remuneration, p.Manual, p.Active);
            var monthly = ProjectCostHistoryRules.MonthlyCost(p.Remuneration, activeItems);
            var hourly = ProjectCostHistoryRules.HourlyRate(rule, activeItems);
            result.Add(new(p.Id, professionalId, p.Start, p.End, p.Hours, p.Remuneration, p.Manual, monthly, hourly, p.Manual.HasValue, p.Active, items));
        }
        return result;
    }

    public async Task<ProfessionalPeriodPreview> PreviewProfessionalPeriodAsync(string projectId, int professionalId, DateOnly start, DateOnly? end, CancellationToken ct)
    {
        ValidatePeriod(start, end);
        await using var c = await Open(ct); await EnsureProfessionalAsync(c, null, projectId, professionalId, ct);
        const string q = "SELECT TOP(1) Id,VigenciaInicio FROM costs.ProjectProfessionalCostPeriod WHERE ProfessionalId=@id AND Ativo=1 AND VigenciaFim IS NULL ORDER BY VigenciaInicio DESC";
        int? id = null; DateOnly? oldStart = null;
        await using (var cmd = new SqlCommand(q, c))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = professionalId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) { id = r.GetInt32(0); oldStart = DateOnly.FromDateTime(r.GetDateTime(1)); }
        }
        if (!id.HasValue) return new(null, null, false);
        if (ProjectCostHistoryRules.RequiresClosingOpenPeriod(start, oldStart!.Value)) return new(id, start.AddDays(-1), true);

        await EnsureNoProfessionalPeriodOverlapAsync(c, null, professionalId, start, end, ct);
        return new(null, null, false, true);
    }

    public async Task<int> CreateProfessionalPeriodAsync(string projectId, int professionalId, ProfessionalPeriodInput input, CancellationToken ct)
    {
        ValidatePeriod(input.Start, input.End); ValidateFinancial(input.MonthlyHours > 0 && input.Remuneration >= 0 && (input.ManualRate is null or >= 0), "HH, remuneração ou custo/hora manual inválido.");
        foreach (var item in input.Items) ValidateItem(item);
        await using var c = await Open(ct); await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await EnsureProfessionalAsync(c, tx, projectId, professionalId, ct);
            const string openSql = "SELECT TOP(1) Id,VigenciaInicio FROM costs.ProjectProfessionalCostPeriod WITH(UPDLOCK,HOLDLOCK) WHERE ProfessionalId=@id AND Ativo=1 AND VigenciaFim IS NULL ORDER BY VigenciaInicio DESC";
            int? openId = null; DateOnly? openStart = null;
            await using (var cmd = new SqlCommand(openSql, c, tx)) { cmd.Parameters.Add("@id", SqlDbType.Int).Value = professionalId; await using var r = await cmd.ExecuteReaderAsync(ct); if (await r.ReadAsync(ct)) { openId = r.GetInt32(0); openStart = DateOnly.FromDateTime(r.GetDateTime(1)); } }
            var closesOpenPeriod = openId.HasValue && ProjectCostHistoryRules.RequiresClosingOpenPeriod(input.Start, openStart!.Value);
            if (closesOpenPeriod)
            {
                if (!input.ConfirmClosePrevious) throw new InvalidOperationException($"Confirme o encerramento da vigência {openId} em {input.Start.AddDays(-1):yyyy-MM-dd}.");
                await using var close = new SqlCommand("UPDATE costs.ProjectProfessionalCostPeriod SET VigenciaFim=@end,AtualizadoEm=sysutcdatetime() WHERE Id=@id AND ProfessionalId=@professional AND VigenciaFim IS NULL", c, tx);
                close.Parameters.Add("@end", SqlDbType.Date).Value = input.Start.AddDays(-1).ToDateTime(TimeOnly.MinValue); close.Parameters.Add("@id", SqlDbType.Int).Value = openId.Value; close.Parameters.Add("@professional", SqlDbType.Int).Value = professionalId;
                if (await close.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("A vigência atual mudou; recarregue a prévia.");
            }
            await EnsureNoProfessionalPeriodOverlapAsync(c, tx, professionalId, input.Start, input.End, ct);
            int periodId;
            const string insert = "INSERT costs.ProjectProfessionalCostPeriod(ProfessionalId,VigenciaInicio,VigenciaFim,HHBaseMensal,RemuneracaoBase,CustoHoraManual) VALUES(@professional,@start,@end,@hours,@remuneration,@manual);SELECT CAST(SCOPE_IDENTITY() AS int)";
            await using (var cmd = new SqlCommand(insert, c, tx)) { cmd.Parameters.Add("@professional", SqlDbType.Int).Value = professionalId; cmd.Parameters.Add("@start", SqlDbType.Date).Value = input.Start.ToDateTime(TimeOnly.MinValue); cmd.Parameters.Add("@end", SqlDbType.Date).Value = input.End?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value; Decimal(cmd, "@hours", input.MonthlyHours); Decimal(cmd, "@remuneration", input.Remuneration); cmd.Parameters.Add("@manual", SqlDbType.Decimal).Value = input.ManualRate is null ? DBNull.Value : input.ManualRate.Value; periodId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture); }
            if(input.CopyPreviousComposition&&closesOpenPeriod){var excluded=input.ExcludePreviousItemIds??Array.Empty<int>();var exclusion=excluded.Count==0?string.Empty:" AND Id NOT IN ("+string.Join(",",excluded.Select((_,ix)=>"@exclude"+ix))+")";await using var copy=new SqlCommand("INSERT costs.ProfessionalCostItem(ProfessionalId,PeriodId,Categoria,Descricao,Valor,TipoCalculo,Percentual,BaseCalculo,Ativo) SELECT ProfessionalId,@newPeriod,Categoria,Descricao,Valor,TipoCalculo,Percentual,BaseCalculo,1 FROM costs.ProfessionalCostItem WHERE ProfessionalId=@professional AND PeriodId=(SELECT TOP(1) Id FROM costs.ProjectProfessionalCostPeriod WHERE ProfessionalId=@professional AND VigenciaFim=@previousEnd) AND Ativo=1"+exclusion,c,tx);copy.Parameters.Add("@newPeriod",SqlDbType.Int).Value=periodId;copy.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;copy.Parameters.Add("@previousEnd",SqlDbType.Date).Value=input.Start.AddDays(-1).ToDateTime(TimeOnly.MinValue);for(var ix=0;ix<excluded.Count;ix++)copy.Parameters.Add("@exclude"+ix,SqlDbType.Int).Value=excluded[ix];await copy.ExecuteNonQueryAsync(ct);}
            foreach (var item in input.Items) await InsertPeriodItemAsync(c, tx, professionalId, periodId, item, ct);
            await tx.CommitAsync(ct); return periodId;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<bool> UpdateProfessionalPeriodAsync(string projectId,int professionalId,int periodId,ProfessionalPeriodInput input,CancellationToken ct)
    {
        ValidatePeriod(input.Start,input.End);
        ValidateFinancial(input.MonthlyHours>0&&input.Remuneration>=0&&(input.ManualRate is null or >=0),"HH, remuneração ou custo/hora manual inválido.");
        foreach(var item in input.Items) ValidateItem(item);

        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await EnsureProfessionalAsync(c,tx,projectId,professionalId,ct);
            await using(var owner=new SqlCommand("SELECT COUNT(1) FROM costs.ProjectProfessionalCostPeriod WHERE Id=@period AND ProfessionalId=@professional",c,tx))
            {
                owner.Parameters.Add("@period",SqlDbType.Int).Value=periodId;
                owner.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
                if(Convert.ToInt32(await owner.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=1)
                    throw new KeyNotFoundException("Vigência não encontrada para este profissional.");
            }

            await EnsureNoProfessionalPeriodOverlapAsync(c,tx,professionalId,input.Start,input.End,ct,periodId);

            const string update="""
                UPDATE costs.ProjectProfessionalCostPeriod
                SET VigenciaInicio=@start,VigenciaFim=@end,HHBaseMensal=@hours,RemuneracaoBase=@remuneration,
                    CustoHoraManual=@manual,Ativo=1,AtualizadoEm=SYSUTCDATETIME()
                WHERE Id=@period AND ProfessionalId=@professional;
                """;
            await using(var cmd=new SqlCommand(update,c,tx))
            {
                cmd.Parameters.Add("@period",SqlDbType.Int).Value=periodId;
                cmd.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
                cmd.Parameters.Add("@start",SqlDbType.Date).Value=input.Start.ToDateTime(TimeOnly.MinValue);
                cmd.Parameters.Add("@end",SqlDbType.Date).Value=input.End?.ToDateTime(TimeOnly.MinValue)??(object)DBNull.Value;
                Decimal(cmd,"@hours",input.MonthlyHours);
                Decimal(cmd,"@remuneration",input.Remuneration);
                var manual=cmd.Parameters.Add("@manual",SqlDbType.Decimal);manual.Precision=18;manual.Scale=6;manual.Value=input.ManualRate is null?DBNull.Value:input.ManualRate.Value;
                if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("A vigência mudou; recarregue e tente novamente.");
            }

            await using(var deactivate=new SqlCommand("UPDATE costs.ProfessionalCostItem SET Ativo=0,AtualizadoEm=SYSUTCDATETIME() WHERE ProfessionalId=@professional AND PeriodId=@period AND Ativo=1",c,tx))
            {
                deactivate.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
                deactivate.Parameters.Add("@period",SqlDbType.Int).Value=periodId;
                await deactivate.ExecuteNonQueryAsync(ct);
            }
            foreach(var item in input.Items)await InsertPeriodItemAsync(c,tx,professionalId,periodId,item,ct);

            await tx.CommitAsync(ct);
            return true;
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public async Task<bool> DeactivateProfessionalPeriodAsync(string projectId,int professionalId,int periodId,CancellationToken ct)
    {
        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await EnsureProfessionalAsync(c,tx,projectId,professionalId,ct);
            await using var cmd=new SqlCommand("""
                UPDATE costs.ProjectProfessionalCostPeriod
                SET Ativo=0,AtualizadoEm=SYSUTCDATETIME()
                WHERE Id=@period AND ProfessionalId=@professional AND Ativo=1;
                UPDATE costs.ProfessionalCostItem
                SET Ativo=0,AtualizadoEm=SYSUTCDATETIME()
                WHERE PeriodId=@period AND ProfessionalId=@professional AND Ativo=1;
                """,c,tx);
            cmd.Parameters.Add("@period",SqlDbType.Int).Value=periodId;
            cmd.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
            var changed=await cmd.ExecuteNonQueryAsync(ct)>0;
            await tx.CommitAsync(ct);
            return changed;
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public async Task<IReadOnlyList<ProjectTax>> ProjectTaxesAsync(string projectId, CancellationToken ct)
    {
        await using var c = await Open(ct); const string q = "SELECT Id,Project_ID_ACC,Nome,Tipo,Ativo FROM costs.ProjectTax WHERE Project_ID_ACC=@p ORDER BY Nome";
        var rows = new List<ProjectTax>(); await using (var cmd = new SqlCommand(q, c)) { cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) rows.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), Array.Empty<ProjectTaxPeriod>())); }
        var result = new List<ProjectTax>(); foreach (var tax in rows) { var periods = new List<ProjectTaxPeriod>(); const string pq = "SELECT Id,VigenciaInicio,VigenciaFim,TipoCalculo,Percentual,ValorFixo,BaseCalculo,Ativo FROM costs.ProjectTaxPeriod WHERE ProjectTaxId=@id ORDER BY VigenciaInicio DESC"; await using var cmd = new SqlCommand(pq, c); cmd.Parameters.Add("@id", SqlDbType.Int).Value = tax.Id; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) periods.Add(new(r.GetInt32(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.IsDBNull(2) ? null : DateOnly.FromDateTime(r.GetDateTime(2)), r.GetString(3), r.IsDBNull(4) ? null : D(r, 4), r.IsDBNull(5) ? null : D(r, 5), r.GetString(6), r.GetBoolean(7))); result.Add(tax with { Periods = periods }); }
        return result;
    }

    public async Task<int> CreateProjectTaxPeriodAsync(string projectId, int? taxId, ProjectTaxInput input, CancellationToken ct)
    {
        ValidatePeriod(input.Start, input.End); ValidateTax(input);
        await using var c = await Open(ct); await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            int id;
            if (taxId is null) { if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Type)) throw new ArgumentException("Nome e tipo do imposto são obrigatórios."); const string add = "INSERT costs.ProjectTax(Project_ID_ACC,Nome,Tipo) VALUES(@p,@n,@t);SELECT CAST(SCOPE_IDENTITY() AS int)"; await using var cmd = new SqlCommand(add, c, tx); cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId; cmd.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = input.Name.Trim(); cmd.Parameters.Add("@t", SqlDbType.NVarChar, 60).Value = input.Type.Trim(); id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture); }
            else { id = taxId.Value; await EnsureTaxAsync(c, tx, projectId, id, ct); }
            int? openId = null; DateOnly? openStart = null; const string getOpen = "SELECT TOP(1) Id,VigenciaInicio FROM costs.ProjectTaxPeriod WITH(UPDLOCK,HOLDLOCK) WHERE ProjectTaxId=@id AND Ativo=1 AND VigenciaFim IS NULL ORDER BY VigenciaInicio DESC"; await using (var cmd = new SqlCommand(getOpen, c, tx)) { cmd.Parameters.Add("@id", SqlDbType.Int).Value = id; await using var r = await cmd.ExecuteReaderAsync(ct); if (await r.ReadAsync(ct)) { openId = r.GetInt32(0); openStart = DateOnly.FromDateTime(r.GetDateTime(1)); } }
            if (openId.HasValue) { if (!input.ConfirmClosePrevious || openStart >= input.Start) throw new InvalidOperationException("Confirme a prévia e informe uma data após o início da vigência atual."); await using var close = new SqlCommand("UPDATE costs.ProjectTaxPeriod SET VigenciaFim=@end,AtualizadoEm=sysutcdatetime() WHERE Id=@id", c, tx); close.Parameters.Add("@end", SqlDbType.Date).Value = input.Start.AddDays(-1).ToDateTime(TimeOnly.MinValue); close.Parameters.Add("@id", SqlDbType.Int).Value = openId; await close.ExecuteNonQueryAsync(ct); }
            const string overlap = "SELECT COUNT(1) FROM costs.ProjectTaxPeriod WITH(UPDLOCK,HOLDLOCK) WHERE ProjectTaxId=@id AND VigenciaInicio<=ISNULL(@end,CONVERT(date,'99991231')) AND @start<=ISNULL(VigenciaFim,CONVERT(date,'99991231'))";
            await using (var cmd = new SqlCommand(overlap, c, tx)) { cmd.Parameters.Add("@id", SqlDbType.Int).Value = id; cmd.Parameters.Add("@start", SqlDbType.Date).Value = input.Start.ToDateTime(TimeOnly.MinValue); cmd.Parameters.Add("@end", SqlDbType.Date).Value = input.End?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value; if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0) throw new InvalidOperationException("A vigência do imposto se sobrepõe a outra."); }
            const string ins = "INSERT costs.ProjectTaxPeriod(ProjectTaxId,VigenciaInicio,VigenciaFim,TipoCalculo,Percentual,ValorFixo,BaseCalculo) VALUES(@id,@start,@end,@calc,@percent,@fixed,@base);SELECT CAST(SCOPE_IDENTITY() AS int)";
            await using var insert = new SqlCommand(ins, c, tx); insert.Parameters.Add("@id", SqlDbType.Int).Value = id; insert.Parameters.Add("@start", SqlDbType.Date).Value = input.Start.ToDateTime(TimeOnly.MinValue); insert.Parameters.Add("@end", SqlDbType.Date).Value = input.End?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value; insert.Parameters.Add("@calc", SqlDbType.VarChar, 12).Value = input.CalculationType; insert.Parameters.Add("@percent", SqlDbType.Decimal).Value = input.Percentage is null ? DBNull.Value : input.Percentage.Value; insert.Parameters.Add("@fixed", SqlDbType.Decimal).Value = input.FixedValue is null ? DBNull.Value : input.FixedValue.Value; insert.Parameters.Add("@base", SqlDbType.VarChar, 24).Value = input.CalculationBase; var period = Convert.ToInt32(await insert.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture); await tx.CommitAsync(ct); return period;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<bool> DeactivateProjectTaxAsync(string projectId,int taxId,CancellationToken ct){await using var c=await Open(ct);await using var cmd=new SqlCommand("UPDATE costs.ProjectTax SET Ativo=0,AtualizadoEm=sysutcdatetime() WHERE Id=@id AND Project_ID_ACC=@p",c);cmd.Parameters.Add("@id",SqlDbType.Int).Value=taxId;cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=projectId;return await cmd.ExecuteNonQueryAsync(ct)>0;}

    public async Task<ProfessionalPeriodPreview> PreviewProjectTaxPeriodAsync(string projectId,int taxId,DateOnly start,CancellationToken ct){await using var c=await Open(ct);await using(var owner=new SqlCommand("SELECT COUNT(1) FROM costs.ProjectTax WHERE Id=@id AND Project_ID_ACC=@p",c)){owner.Parameters.Add("@id",SqlDbType.Int).Value=taxId;owner.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=projectId;if(Convert.ToInt32(await owner.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=1)throw new KeyNotFoundException("Imposto não encontrado neste projeto.");}const string q="SELECT TOP(1) Id,VigenciaInicio FROM costs.ProjectTaxPeriod WHERE ProjectTaxId=@id AND Ativo=1 AND VigenciaFim IS NULL ORDER BY VigenciaInicio DESC";await using var cmd=new SqlCommand(q,c);cmd.Parameters.Add("@id",SqlDbType.Int).Value=taxId;await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return new(null,null,false);var id=r.GetInt32(0);var old=DateOnly.FromDateTime(r.GetDateTime(1));if(start<=old)throw new InvalidOperationException("A nova vigência deve começar após a vigência aberta.");return new(id,start.AddDays(-1),true);}

    public async Task<IReadOnlyList<DocumentActualCost>> DocumentActualCostsAsync(string projectId, CancellationToken ct)
    {
        var internalRows = await InternalActualsAsync(projectId, ct);
        var packageLines = await costs.RecognitionAsync(projectId, ct);
        var packages = await costs.PackagesAsync(projectId, ct);
        var packageIds = packages.Where(x => x.Ativo).Select(x => x.Id).ToArray();
        var planned = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in packageIds)
        {
            var allocation = await costs.AllocationAsync(projectId, id, ct);
            if (allocation.Erro is null) foreach (var d in allocation.Documentos) { var key = CostAllocationRules.NormalizeDocument(d.DocumentoCodigo); planned[key] = planned.GetValueOrDefault(key) + d.CustoFinal; }
        }
        var pkg = packageLines.GroupBy(x => CostAllocationRules.NormalizeDocument(x.DocumentoCodigo), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (Hours: g.Sum(x => x.RecognizedHours), Recognized: g.Sum(x => x.RecognizedCost), Excess: g.Sum(x => x.ExcessCost), Ambiguous: g.Count(x => x.AmbiguousMatch), RawHours: g.Sum(x=>x.TimesheetHours), ExcessHours: g.Sum(x=>x.ExcessHours)), StringComparer.OrdinalIgnoreCase);
        var all = internalRows.Keys.Union(pkg.Keys, StringComparer.OrdinalIgnoreCase).Union(planned.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        return all.Select(doc => { var x = internalRows.GetValueOrDefault(doc,new InternalAgg(0m,0m,0m)); var p = pkg.GetValueOrDefault(doc); var plan = planned.GetValueOrDefault(doc); var pkgActual = p.Recognized; var remaining = ProjectCostHistoryRules.Remaining(plan, pkgActual); return new DocumentActualCost(doc, x.Hours + p.Hours, x.Cost, pkgActual, x.Cost + pkgActual, plan, remaining.Remaining, x.UncostedHours, p.Ambiguous, true,p.RawHours,p.ExcessHours); }).ToArray();
    }

    private sealed record InternalAgg(decimal Hours, decimal Cost, decimal UncostedHours);
    private sealed record WorkRow(string Name, string Document, DateOnly? Date, decimal Hours);

    private async Task<Dictionary<string, InternalAgg>> InternalActualsAsync(string projectId, CancellationToken ct)
    {
        await using var c = await Open(ct); var work = new List<WorkRow>();
        const string tq = "SELECT ISNULL(Nome,''),ISNULL(DocumentoCodigo,''),CONVERT(date,[data]),ISNULL(Trabalhadas,0) FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Project_ID_ACC_Canonico=@p AND Status_Mapeamento_CR_OS IN('UNICO','ATIVO_UNICO')";
        await using (var cmd = new SqlCommand(tq, c)) { cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) work.Add(new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : DateOnly.FromDateTime(r.GetDateTime(2)), D(r, 3))); }
        var packages = new List<PackageDocumentMatch>(); const string pq = "SELECT p.Id,d.DocumentoCodigo,COALESCE(NULLIF(LTRIM(RTRIM(p.AliasTimesheet)),''),p.NomeReferencia),a.TextoMatch,a.TipoMatch FROM costs.Package p JOIN costs.PackageDocument d ON d.PackageId=p.Id AND d.Ativo=1 LEFT JOIN costs.TimesheetMatchAlias a ON a.PackageId=p.Id AND a.Ativo=1 WHERE p.Project_ID_ACC=@p AND p.Ativo=1";
        await using (var cmd = new SqlCommand(pq, c)) { cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId; await using var r = await cmd.ExecuteReaderAsync(ct); var rows = new Dictionary<(int,string), (string?,List<(string,string)>)>(); while (await r.ReadAsync(ct)) { var key=(r.GetInt32(0),r.GetString(1)); if(!rows.TryGetValue(key,out var value)) value=(r.IsDBNull(2)?null:r.GetString(2),new()); if(!r.IsDBNull(3)) value.Item2.Add((r.GetString(3),r.GetString(4))); rows[key]=value; } packages=rows.Select(x=>new PackageDocumentMatch(x.Key.Item1,x.Key.Item2,x.Value.Item1,x.Value.Item2)).ToList(); }
        var timesheet = work.Select(x => new TimesheetWork(x.Name,x.Document,x.Hours)).ToArray(); var pkgAssignment = CostTimesheetMatchingRules.AssignPackages(timesheet,packages);
        // Horas do profissional de pacote Por Disciplina são apropriadas pelo pacote (RecognitionAsync), nunca como custo interno.
        var disciplinePackages = await CostManagementService.DisciplinePackageAliasesAsync(c, projectId, ct);
        // Custo/hora pela regra única do cadastro de profissionais (mesma da tela AT e do Resumo LAB).
        var catalog = await ProfessionalHourlyCostCatalog.LoadAsync(c, projectId, ct);
        var output=new Dictionary<string,InternalAgg>(StringComparer.OrdinalIgnoreCase);
        for(var i=0;i<work.Count;i++)
        {
            var row=work[i];var pkg=pkgAssignment[i];if(pkg.PackageId is not null)continue;
            if(!pkg.Ambiguous&&!pkg.PackageNameOnly&&disciplinePackages.Any(d=>CostTimesheetMatchingRules.Matches(d.Fallback,d.Aliases,row.Name)))continue;if(pkg.Ambiguous||pkg.PackageNameOnly){var ambiguousKey=CostAllocationRules.NormalizeDocument(row.Document);var ambiguousAgg=output.GetValueOrDefault(ambiguousKey,new(0m,0m,0m));output[ambiguousKey]=new(ambiguousAgg.Hours,ambiguousAgg.Cost,ambiguousAgg.UncostedHours+row.Hours);continue;}
            var resolution=catalog.Resolve(row.Name,row.Date);
            if(resolution.ProfessionalId is null){var uncostedKey=CostAllocationRules.NormalizeDocument(row.Document);var uncostedAgg=output.GetValueOrDefault(uncostedKey,new(0m,0m,0m));output[uncostedKey]=new(uncostedAgg.Hours,uncostedAgg.Cost,uncostedAgg.UncostedHours+row.Hours);continue;}
            decimal amount=0m,uncosted=0m;
            var cost=resolution.CostFor(row.Hours);if(cost.HasValue)amount=cost.Value;else uncosted=row.Hours;
            var key=CostAllocationRules.NormalizeDocument(row.Document);if(!output.TryGetValue(key,out var agg))agg=new(0m,0m,0m);output[key]=new(agg.Hours+row.Hours,agg.Cost+amount,agg.UncostedHours+uncosted);
        }
        return output;
    }

    public async Task<CostCopyPreview> PreviewCopyAsync(CostCopyInput input, CancellationToken ct)
    {
        ValidateCopy(input); await using var c=await Open(ct);await EnsureProjectsAsync(c,input.SourceProjectId,input.DestinationProjectId,ct);
        var conflicts=new List<string>();
        if(input.Professionals&&await ExistsAsync(c,"SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Project_ID_ACC=@d",input.DestinationProjectId,ct)>0)conflicts.Add("professionals");
        if(input.ProjectTaxes&&await ExistsAsync(c,"SELECT COUNT(1) FROM costs.ProjectTax WHERE Project_ID_ACC=@d",input.DestinationProjectId,ct)>0)conflicts.Add("taxes");
        if(input.Packages&&await ExistsAsync(c,"SELECT COUNT(1) FROM costs.Package WHERE Project_ID_ACC=@d",input.DestinationProjectId,ct)>0)conflicts.Add("packages");
        if(input.Expenses&&await ExistsAsync(c,"SELECT COUNT(1) FROM costs.ProjectExpense WHERE Project_ID_ACC=@d",input.DestinationProjectId,ct)>0)conflicts.Add("expenses");
        return new(input.DestinationProjectId,
            input.Professionals&&!conflicts.Contains("professionals")?await CountAsync(c,"SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Project_ID_ACC=@s AND Ativo=1",input.SourceProjectId,ct):0,
            input.ProjectTaxes&&!conflicts.Contains("taxes")?await CountAsync(c,"SELECT COUNT(1) FROM costs.ProjectTax WHERE Project_ID_ACC=@s AND Ativo=1",input.SourceProjectId,ct):0,
            input.Packages&&!conflicts.Contains("packages")?await CountAsync(c,"SELECT COUNT(1) FROM costs.Package WHERE Project_ID_ACC=@s AND Ativo=1",input.SourceProjectId,ct):0,
            input.Expenses&&!conflicts.Contains("expenses")?await CountAsync(c,"SELECT COUNT(1) FROM costs.ProjectExpense WHERE Project_ID_ACC=@s AND Ativo=1",input.SourceProjectId,ct):0,conflicts);
    }

    public async Task<CostCopyResult> CopyAsync(CostCopyInput input, CancellationToken ct)
    {
        ValidateCopy(input);await using var c=await Open(ct);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var skipped=new List<string>();int pros=0,taxes=0,packages=0,expenses=0;
        try
        {
            await EnsureProjectsAsync(c,input.SourceProjectId,input.DestinationProjectId,ct,tx);
            var preview=await PreviewCopyOnConnectionAsync(c,tx,input,ct);
            if(preview.Conflicts.Count>0&&!input.SkipConflicts)throw new InvalidOperationException("Destino contém configurações existentes: "+string.Join(", ",preview.Conflicts)+". Confira a prévia; para preservar o destino, use SkipConflicts.");
            skipped.AddRange(preview.Conflicts);
            if(input.Professionals&&!preview.Conflicts.Contains("professionals"))pros=await CopyProfessionalsAsync(c,tx,input,ct);
            if(input.ProjectTaxes&&!preview.Conflicts.Contains("taxes"))taxes=await CopyTaxesAsync(c,tx,input,ct);
            if(input.Packages&&!preview.Conflicts.Contains("packages"))packages=await CopyPackagesAsync(c,tx,input,ct);
            if(input.Expenses&&!preview.Conflicts.Contains("expenses"))expenses=await CopyExpensesAsync(c,tx,input,ct);
            await tx.CommitAsync(ct);return new(input.DestinationProjectId,pros,taxes,packages,expenses,skipped);
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public async Task<CostCopyBatchPreview> PreviewCopyBatchAsync(CostCopyBatchInput input,CancellationToken ct)
    {
        ValidateCopyBatch(input);await using var c=await Open(ct);var previews=new List<CostCopyPreview>();
        foreach(var destination in input.DestinationProjectIds){var single=ToSingle(input,destination);await EnsureProjectsAsync(c,input.SourceProjectId,destination,ct);previews.Add(await PreviewCopyOnConnectionAsync(c,null!,single,ct));}
        return new(previews);
    }

    public async Task<CostCopyBatchResult> CopyBatchAsync(CostCopyBatchInput input,CancellationToken ct)
    {
        ValidateCopyBatch(input);await using var c=await Open(ct);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);var results=new List<CostCopyResult>();
        try{foreach(var destination in input.DestinationProjectIds){var single=ToSingle(input,destination);await EnsureProjectsAsync(c,input.SourceProjectId,destination,ct,tx);var preview=await PreviewCopyOnConnectionAsync(c,tx,single,ct);if(preview.Conflicts.Count>0&&!input.SkipConflicts)throw new InvalidOperationException($"Destino {destination} possui configurações: {string.Join(", ",preview.Conflicts)}. Nada foi copiado.");var skipped=preview.Conflicts;var pros=input.Professionals&&!skipped.Contains("professionals")?await CopyProfessionalsAsync(c,tx,single,ct):0;var taxes=input.ProjectTaxes&&!skipped.Contains("taxes")?await CopyTaxesAsync(c,tx,single,ct):0;var packages=input.Packages&&!skipped.Contains("packages")?await CopyPackagesAsync(c,tx,single,ct):0;var expenses=input.Expenses&&!skipped.Contains("expenses")?await CopyExpensesAsync(c,tx,single,ct):0;results.Add(new(destination,pros,taxes,packages,expenses,skipped));}await tx.CommitAsync(ct);return new(results);}catch{await tx.RollbackAsync(ct);throw;}
    }

    private static CostCopyInput ToSingle(CostCopyBatchInput input,string destination)=>new(input.SourceProjectId,destination,input.Professionals,input.ProfessionalComposition,input.ProjectTaxes,input.Packages,input.Expenses,input.SkipConflicts);
    private static void ValidateCopyBatch(CostCopyBatchInput input){if(input.DestinationProjectIds is null||input.DestinationProjectIds.Count==0||input.DestinationProjectIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=input.DestinationProjectIds.Count)throw new ArgumentException("Selecione destinos distintos para copiar.");foreach(var destination in input.DestinationProjectIds)ValidateCopy(ToSingle(input,destination));}

    private static async Task<int> CopyProfessionalsAsync(SqlConnection c,SqlTransaction tx,CostCopyInput input,CancellationToken ct)
    {
        var ids=new List<int>();
        await using(var cmd=new SqlCommand("SELECT Id FROM costs.ProjectProfessional WHERE Project_ID_ACC=@p AND Ativo=1 ORDER BY Id",c,tx))
        {
            cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=input.SourceProjectId;
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))ids.Add(r.GetInt32(0));
        }
        foreach(var id in ids)await CopyOneProfessionalAsync(c,tx,id,input.DestinationProjectId,ct,input.ProfessionalComposition);
        return ids.Count;
    }

    private static async Task<int> CopyTaxesAsync(SqlConnection c,SqlTransaction tx,CostCopyInput input,CancellationToken ct){var ids=new List<(int Id,string Name,string Type)>();await using(var cmd=new SqlCommand("SELECT Id,Nome,Tipo FROM costs.ProjectTax WHERE Project_ID_ACC=@p AND Ativo=1",c,tx)){cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=input.SourceProjectId;await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add((r.GetInt32(0),r.GetString(1),r.GetString(2)));}foreach(var t in ids){await using var ins=new SqlCommand("INSERT costs.ProjectTax(Project_ID_ACC,Nome,Tipo,Ativo) VALUES(@p,@n,@t,1);SELECT CAST(SCOPE_IDENTITY() AS int)",c,tx);ins.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=input.DestinationProjectId;ins.Parameters.Add("@n",SqlDbType.NVarChar,200).Value=t.Name;ins.Parameters.Add("@t",SqlDbType.NVarChar,60).Value=t.Type;var id=Convert.ToInt32(await ins.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);await using var copy=new SqlCommand("INSERT costs.ProjectTaxPeriod(ProjectTaxId,VigenciaInicio,VigenciaFim,TipoCalculo,Percentual,ValorFixo,BaseCalculo,Ativo) SELECT @dest,VigenciaInicio,VigenciaFim,TipoCalculo,Percentual,ValorFixo,BaseCalculo,Ativo FROM costs.ProjectTaxPeriod WHERE ProjectTaxId=@src",c,tx);copy.Parameters.Add("@dest",SqlDbType.Int).Value=id;copy.Parameters.Add("@src",SqlDbType.Int).Value=t.Id;await copy.ExecuteNonQueryAsync(ct);}return ids.Count;}

    private static async Task<int> CopyPackagesAsync(SqlConnection c,SqlTransaction tx,CostCopyInput input,CancellationToken ct){var rows=new List<(int Id,string? Company,string Name,string? Alias,string? Description,decimal Hours,decimal Cost)>();await using(var cmd=new SqlCommand("IF COL_LENGTH(N'costs.Package',N'ModeloCadastro') IS NULL SELECT Id,Empresa,NomeReferencia,AliasTimesheet,Descricao,HHBase,CustoBase FROM costs.Package WHERE Project_ID_ACC=@p AND Ativo=1 ELSE EXEC sp_executesql N'SELECT Id,Empresa,NomeReferencia,AliasTimesheet,Descricao,HHBase,CustoBase FROM costs.Package WHERE Project_ID_ACC=@p AND Ativo=1 AND ModeloCadastro=''DOCUMENTO''',N'@p nvarchar(255)',@p=@p",c,tx)){/* Pacotes Por Disciplina dependem da LDP do projeto de origem: não são copiados. */cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=input.SourceProjectId;await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add((r.GetInt32(0),N(r,1),r.GetString(2),N(r,3),N(r,4),D(r,5),D(r,6)));}foreach(var p in rows){await using var ins=new SqlCommand("INSERT costs.Package(Project_ID_ACC,Empresa,NomeReferencia,AliasTimesheet,Descricao,HHBase,CustoBase,Ativo) VALUES(@dest,@e,@n,@a,@d,@h,@c,1);SELECT CAST(SCOPE_IDENTITY() AS int)",c,tx);ins.Parameters.Add("@dest",SqlDbType.NVarChar,255).Value=input.DestinationProjectId;ins.Parameters.Add("@e",SqlDbType.NVarChar,200).Value=(object?)p.Company??DBNull.Value;ins.Parameters.Add("@n",SqlDbType.NVarChar,200).Value=p.Name;ins.Parameters.Add("@a",SqlDbType.NVarChar,200).Value=(object?)p.Alias??DBNull.Value;ins.Parameters.Add("@d",SqlDbType.NVarChar,500).Value=(object?)p.Description??DBNull.Value;DecParam(ins,"@h",p.Hours);DecParam(ins,"@c",p.Cost);var id=Convert.ToInt32(await ins.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);await using(var docs=new SqlCommand("INSERT costs.PackageDocument(PackageId,DocumentoCodigo,Disciplina,HHAlocada,CustoAlocado,ModoHH,ModoCusto,Ativo) SELECT @dest,DocumentoCodigo,Disciplina,HHAlocada,CustoAlocado,ModoHH,ModoCusto,Ativo FROM costs.PackageDocument WHERE PackageId=@src AND Ativo=1",c,tx)){docs.Parameters.Add("@dest",SqlDbType.Int).Value=id;docs.Parameters.Add("@src",SqlDbType.Int).Value=p.Id;await docs.ExecuteNonQueryAsync(ct);}await using(var adds=new SqlCommand("INSERT costs.PackageAddendum(PackageId,HHAdicional,CustoAdicional,Data,Descricao,Ativo) SELECT @dest,HHAdicional,CustoAdicional,Data,Descricao,Ativo FROM costs.PackageAddendum WHERE PackageId=@src AND Ativo=1",c,tx)){adds.Parameters.Add("@dest",SqlDbType.Int).Value=id;adds.Parameters.Add("@src",SqlDbType.Int).Value=p.Id;await adds.ExecuteNonQueryAsync(ct);}await using(var als=new SqlCommand("INSERT costs.TimesheetMatchAlias(PackageId,TextoMatch,TipoMatch,Ativo) SELECT @dest,TextoMatch,TipoMatch,Ativo FROM costs.TimesheetMatchAlias WHERE PackageId=@src AND Ativo=1",c,tx)){als.Parameters.Add("@dest",SqlDbType.Int).Value=id;als.Parameters.Add("@src",SqlDbType.Int).Value=p.Id;await als.ExecuteNonQueryAsync(ct);}}return rows.Count;}

    private static async Task<int> CopyExpensesAsync(SqlConnection c,SqlTransaction tx,CostCopyInput input,CancellationToken ct){await using var cmd=new SqlCommand("INSERT costs.ProjectExpense(Project_ID_ACC,Categoria,Descricao,Valor,Competencia,Tipo,Observacao,Ativo) SELECT @d,Categoria,Descricao,Valor,Competencia,Tipo,Observacao,Ativo FROM costs.ProjectExpense WHERE Project_ID_ACC=@s AND Ativo=1",c,tx);cmd.Parameters.Add("@d",SqlDbType.NVarChar,255).Value=input.DestinationProjectId;cmd.Parameters.Add("@s",SqlDbType.NVarChar,255).Value=input.SourceProjectId;return await cmd.ExecuteNonQueryAsync(ct);}

    private sealed record RawProfessionalImportLine(
        int Row,string? Funcao,string? Disciplina,string Nome,decimal Remuneracao,decimal Encargos,decimal Softwares,
        decimal Computadores,decimal? Total,decimal? CustoHora,DateOnly? Inicio,DateOnly? Fim,decimal HHBase,string? Error);

    private sealed record ExistingProfessional(int Id,string NameKey);
    private sealed record ExistingPeriod(int Id,int ProfessionalId,DateOnly Start,DateOnly? End,bool Active);

    public Task<byte[]> ProfessionalImportTemplateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var package=new OfficeOpenXml.ExcelPackage();
        var ws=package.Workbook.Worksheets.Add("Profissionais");
        var headers=new[]{
            "função","disciplina","nome","Remuneração","Encargos","Softwares","Computadores","Total","Custo/hora",
            "Vigência início","Vigência fim","HH base mensal"
        };
        for(var i=0;i<headers.Length;i++)ws.Cells[1,i+1].Value=headers[i];
        ws.Cells[1,1,1,headers.Length].Style.Font.Bold=true;
        ws.Cells[1,1,1,headers.Length].AutoFilter=true;
        ws.View.FreezePanes(2,1);
        ws.Cells[1,1,1,headers.Length].AutoFitColumns();
        ws.Column(3).Width=Math.Max(ws.Column(3).Width,28);
        ws.Column(1).Width=Math.Max(ws.Column(1).Width,20);
        ws.Column(2).Width=Math.Max(ws.Column(2).Width,20);
        ws.Cells[2,10].Style.Numberformat.Format="dd/mm/yyyy";
        ws.Cells[2,11].Style.Numberformat.Format="dd/mm/yyyy";
        return Task.FromResult(package.GetAsByteArray());
    }

    public async Task<ProfessionalImportPreview> PreviewProfessionalImportAsync(string projectId,Stream file,CancellationToken ct)
    {
        var raw=await ReadProfessionalExcelAsync(file,ct);
        return await BuildProfessionalImportPreviewAsync(projectId,raw,ct);
    }

    public async Task<ProfessionalImportResult> CommitProfessionalImportAsync(string projectId,Stream file,CancellationToken ct)
    {
        var raw=await ReadProfessionalExcelAsync(file,ct);
        var preview=await BuildProfessionalImportPreviewAsync(projectId,raw,ct);
        if(preview.Invalid>0)throw new InvalidOperationException("A importação possui linhas inválidas. Corrija a prévia antes de confirmar.");

        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var createdProfessionals=0;
        var updatedProfessionals=0;
        var createdPeriods=0;
        var updatedPeriods=0;
        try
        {
            var existing=await LoadExistingProfessionalsAsync(c,tx,projectId,ct);
            foreach(var group in preview.Rows.GroupBy(x=>NameKey(x.Nome),StringComparer.Ordinal).OrderBy(x=>x.Key))
            {
                var ordered=group.OrderBy(x=>x.VigenciaInicio).ToList();
                var matches=existing.Where(x=>x.NameKey==group.Key).ToList();
                int professionalId;
                if(matches.Count==0)
                {
                    var latest=ordered[^1];
                    const string insertProfessional="""
                        INSERT costs.ProjectProfessional(Project_ID_ACC,Nome,Empresa,AliasTimesheet,HHBaseMensal,Funcao,Disciplina,Ativo)
                        VALUES(@p,@name,NULL,@alias,@hours,@function,@discipline,1);
                        SELECT CAST(SCOPE_IDENTITY() AS int);
                        """;
                    await using var ins=new SqlCommand(insertProfessional,c,tx);
                    ins.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=projectId;
                    ins.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=latest.Nome.Trim();
                    ins.Parameters.Add("@alias",SqlDbType.NVarChar,200).Value=latest.Nome.Trim();
                    DecParam(ins,"@hours",latest.HHBaseMensal);
                    ins.Parameters.Add("@function",SqlDbType.NVarChar,200).Value=(object?)NullText(latest.Funcao)??DBNull.Value;
                    ins.Parameters.Add("@discipline",SqlDbType.NVarChar,200).Value=(object?)NullText(latest.Disciplina)??DBNull.Value;
                    professionalId=Convert.ToInt32(await ins.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
                    existing.Add(new(professionalId,group.Key));
                    createdProfessionals++;
                }
                else
                {
                    if(matches.Count!=1)throw new InvalidOperationException($"Existe mais de um cadastro para {ordered[0].Nome} no projeto.");
                    professionalId=matches[0].Id;
                    var latest=ordered[^1];
                    await using var up=new SqlCommand("""
                        UPDATE costs.ProjectProfessional
                        SET Nome=@name,AliasTimesheet=COALESCE(NULLIF(AliasTimesheet,''),@alias),HHBaseMensal=@hours,
                            Funcao=@function,Disciplina=@discipline,Ativo=1,DesativadoEm=NULL,AtualizadoEm=SYSUTCDATETIME()
                        WHERE Id=@id AND Project_ID_ACC=@p;
                        """,c,tx);
                    up.Parameters.Add("@id",SqlDbType.Int).Value=professionalId;
                    up.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=projectId;
                    up.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=latest.Nome.Trim();
                    up.Parameters.Add("@alias",SqlDbType.NVarChar,200).Value=latest.Nome.Trim();
                    DecParam(up,"@hours",latest.HHBaseMensal);
                    up.Parameters.Add("@function",SqlDbType.NVarChar,200).Value=(object?)NullText(latest.Funcao)??DBNull.Value;
                    up.Parameters.Add("@discipline",SqlDbType.NVarChar,200).Value=(object?)NullText(latest.Disciplina)??DBNull.Value;
                    await up.ExecuteNonQueryAsync(ct);
                    updatedProfessionals++;
                }

                foreach(var row in ordered)
                {
                    var sameStart=await FindPeriodByStartAsync(c,tx,professionalId,row.VigenciaInicio,ct);
                    if(sameStart is null)
                    {
                        var open=await FindOpenPeriodBeforeAsync(c,tx,professionalId,row.VigenciaInicio,ct);
                        if(open is not null)
                        {
                            await using var close=new SqlCommand("UPDATE costs.ProjectProfessionalCostPeriod SET VigenciaFim=@end,AtualizadoEm=SYSUTCDATETIME() WHERE Id=@id AND ProfessionalId=@professional AND Ativo=1 AND VigenciaFim IS NULL",c,tx);
                            close.Parameters.Add("@end",SqlDbType.Date).Value=row.VigenciaInicio.AddDays(-1).ToDateTime(TimeOnly.MinValue);
                            close.Parameters.Add("@id",SqlDbType.Int).Value=open.Id;
                            close.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
                            await close.ExecuteNonQueryAsync(ct);
                        }
                        await EnsureNoProfessionalPeriodOverlapAsync(c,tx,professionalId,row.VigenciaInicio,row.VigenciaFim,ct);
                        await using var ins=new SqlCommand("""
                            INSERT costs.ProjectProfessionalCostPeriod(ProfessionalId,VigenciaInicio,VigenciaFim,HHBaseMensal,RemuneracaoBase,CustoHoraManual,Ativo)
                            VALUES(@professional,@start,@end,@hours,@remuneration,@rate,1);
                            SELECT CAST(SCOPE_IDENTITY() AS int);
                            """,c,tx);
                        ins.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
                        ins.Parameters.Add("@start",SqlDbType.Date).Value=row.VigenciaInicio.ToDateTime(TimeOnly.MinValue);
                        ins.Parameters.Add("@end",SqlDbType.Date).Value=row.VigenciaFim?.ToDateTime(TimeOnly.MinValue)??(object)DBNull.Value;
                        DecParam(ins,"@hours",row.HHBaseMensal);
                        DecParam(ins,"@remuneration",row.Remuneracao);
                        var rate=ins.Parameters.Add("@rate",SqlDbType.Decimal);rate.Precision=18;rate.Scale=6;rate.Value=row.CustoHora is null?DBNull.Value:row.CustoHora.Value;
                        var periodId=Convert.ToInt32(await ins.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
                        await InsertImportedCompositionAsync(c,tx,professionalId,periodId,row,ct);
                        createdPeriods++;
                    }
                    else
                    {
                        await EnsureNoProfessionalPeriodOverlapAsync(c,tx,professionalId,row.VigenciaInicio,row.VigenciaFim,ct,sameStart.Id);
                        await using var up=new SqlCommand("""
                            UPDATE costs.ProjectProfessionalCostPeriod
                            SET VigenciaFim=@end,HHBaseMensal=@hours,RemuneracaoBase=@remuneration,CustoHoraManual=@rate,
                                Ativo=1,AtualizadoEm=SYSUTCDATETIME()
                            WHERE Id=@id AND ProfessionalId=@professional;
                            UPDATE costs.ProfessionalCostItem SET Ativo=0,AtualizadoEm=SYSUTCDATETIME()
                            WHERE PeriodId=@id AND ProfessionalId=@professional AND Ativo=1;
                            """,c,tx);
                        up.Parameters.Add("@id",SqlDbType.Int).Value=sameStart.Id;
                        up.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
                        up.Parameters.Add("@end",SqlDbType.Date).Value=row.VigenciaFim?.ToDateTime(TimeOnly.MinValue)??(object)DBNull.Value;
                        DecParam(up,"@hours",row.HHBaseMensal);
                        DecParam(up,"@remuneration",row.Remuneracao);
                        var rate=up.Parameters.Add("@rate",SqlDbType.Decimal);rate.Precision=18;rate.Scale=6;rate.Value=row.CustoHora is null?DBNull.Value:row.CustoHora.Value;
                        await up.ExecuteNonQueryAsync(ct);
                        await InsertImportedCompositionAsync(c,tx,professionalId,sameStart.Id,row,ct);
                        updatedPeriods++;
                    }
                }
            }

            await tx.CommitAsync(ct);
            return new(createdProfessionals,updatedProfessionals,createdPeriods,updatedPeriods);
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    public async Task<IReadOnlyList<EligibleCostProject>> EligibleProjectsAsync(CancellationToken ct)
    {
        await using var c=await Open(ct);
        const string q="""
            SELECT p.Project_ID_ACC,LTRIM(RTRIM(p.Nome_Projeto)),CAST(p.Is_Ativo AS bit),
                   CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Ldp_Unificada l WHERE l.Project_ID_ACC=p.Project_ID_ACC) THEN 1 ELSE 0 END AS bit)
            FROM dbo.Projetos p
            WHERE NULLIF(LTRIM(RTRIM(p.Project_ID_ACC)),'') IS NOT NULL
              AND (p.Is_Ativo=1 OR EXISTS(SELECT 1 FROM dbo.Ldp_Unificada l WHERE l.Project_ID_ACC=p.Project_ID_ACC))
            ORDER BY p.Is_Ativo DESC,LTRIM(RTRIM(p.Nome_Projeto));
            """;
        await using var cmd=new SqlCommand(q,c);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var rows=new List<EligibleCostProject>();
        while(await r.ReadAsync(ct))rows.Add(new(r.GetString(0),r.GetString(1),r.GetBoolean(2),r.GetBoolean(3)));
        return rows;
    }

    public async Task<ProfessionalAssignmentPreview> PreviewProfessionalAssignmentAsync(string sourceProjectId,ProfessionalAssignmentInput input,CancellationToken ct)
    {
        ValidateAssignment(input);
        await using var c=await Open(ct);
        var source=await LoadSelectedProfessionalsForAssignmentAsync(c,null,sourceProjectId,input.ProfessionalIds,ct);
        if(source.Count!=input.ProfessionalIds.Distinct().Count())throw new InvalidOperationException("Um ou mais profissionais não pertencem ao projeto de origem.");

        var eligible=(await EligibleProjectsAsync(ct)).ToDictionary(x=>x.ProjectIdAcc,StringComparer.OrdinalIgnoreCase);
        var destinations=new List<ProfessionalAssignmentDestination>();
        foreach(var destination in input.DestinationProjectIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(destination.Equals(sourceProjectId,StringComparison.OrdinalIgnoreCase))continue;
            if(!eligible.TryGetValue(destination,out var project))throw new InvalidOperationException("O projeto destino não está ativo e nunca foi sincronizado.");
            var already=0;
            foreach(var person in source)
            {
                await using var cmd=new SqlCommand("SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Nome)))=UPPER(LTRIM(RTRIM(@name)))",c);
                cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=destination;
                cmd.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=person.Name;
                if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)>0)already++;
            }
            destinations.Add(new(destination,project.Name,source.Count-already,already));
        }
        if(destinations.Count==0)throw new ArgumentException("Selecione ao menos um projeto destino diferente do projeto atual.");
        return new(destinations);
    }

    public async Task<ProfessionalAssignmentResult> AssignProfessionalsAsync(string sourceProjectId,ProfessionalAssignmentInput input,CancellationToken ct)
    {
        var preview=await PreviewProfessionalAssignmentAsync(sourceProjectId,input,ct);
        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var source=await LoadSelectedProfessionalsForAssignmentAsync(c,tx,sourceProjectId,input.ProfessionalIds,ct);
        var created=0;
        try
        {
            foreach(var destination in preview.Destinations)
            {
                foreach(var person in source)
                {
                    await using var exists=new SqlCommand("SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Nome)))=UPPER(LTRIM(RTRIM(@name)))",c,tx);
                    exists.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=destination.ProjectIdAcc;
                    exists.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=person.Name;
                    if(Convert.ToInt32(await exists.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)>0)continue;
                    await CopyOneProfessionalAsync(c,tx,person.Id,destination.ProjectIdAcc,ct);
                    created++;
                }
            }
            await tx.CommitAsync(ct);
            return new(created,preview.Destinations);
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    private async Task<ProfessionalImportPreview> BuildProfessionalImportPreviewAsync(string projectId,IReadOnlyList<RawProfessionalImportLine> raw,CancellationToken ct)
    {
        if(raw.Count==0)throw new InvalidOperationException("A planilha não possui profissionais para importar.");

        var normalized=new List<ProfessionalImportRow>();
        var today=DateOnly.FromDateTime(DateTime.Today);

        foreach(var group in raw.GroupBy(x=>NameKey(x.Nome),StringComparer.Ordinal))
        {
            var source=group.OrderBy(x=>x.Inicio??DateOnly.MaxValue).ThenBy(x=>x.Row).ToList();
            var multiple=source.Count>1;
            var missingStart=multiple&&source.Any(x=>x.Inicio is null);

            for(var i=0;i<source.Count;i++)
            {
                var line=source[i];
                var start=line.Inicio??today;
                DateOnly? end=line.Fim;
                if(end is null&&i<source.Count-1&&source[i+1].Inicio is not null)end=source[i+1].Inicio!.Value.AddDays(-1);

                var calculated=line.Remuneracao+line.Encargos+line.Softwares+line.Computadores;
                var informed=line.Total??calculated;
                string? warning=null;
                if(line.Total.HasValue&&Math.Abs(line.Total.Value-calculated)>0.01m)
                    warning=$"Total informado ({line.Total.Value:0.00}) difere da soma ({calculated:0.00}); será usada a soma calculada.";

                var error=line.Error;
                if(missingStart&&line.Inicio is null)error=JoinError(error,"Informe Vigência início quando o mesmo profissional possui mais de uma linha.");
                if(end is not null&&end<start)error=JoinError(error,"Vigência fim anterior à vigência início.");
                if(line.HHBase<=0)error=JoinError(error,"HH base mensal deve ser maior que zero.");

                normalized.Add(new(
                    line.Row,string.IsNullOrWhiteSpace(error),"PENDENTE",line.Funcao,line.Disciplina,line.Nome,
                    line.Remuneracao,line.Encargos,line.Softwares,line.Computadores,informed,calculated,line.CustoHora,
                    start,end,line.HHBase,warning,error));
            }
        }

        // Validação de sobreposição dentro do próprio arquivo.
        foreach(var group in normalized.GroupBy(x=>NameKey(x.Nome),StringComparer.Ordinal))
        {
            var ordered=group.OrderBy(x=>x.VigenciaInicio).ThenBy(x=>x.Row).ToList();
            for(var i=1;i<ordered.Count;i++)
            {
                var previous=ordered[i-1];
                var current=ordered[i];
                if(previous.VigenciaInicio==current.VigenciaInicio||RangesOverlap(previous.VigenciaInicio,previous.VigenciaFim,current.VigenciaInicio,current.VigenciaFim))
                {
                    ReplaceImportRow(normalized,previous.Row,previous with{Valid=false,Error=JoinError(previous.Error,"Vigência sobreposta/duplicada no Excel.")});
                    ReplaceImportRow(normalized,current.Row,current with{Valid=false,Error=JoinError(current.Error,"Vigência sobreposta/duplicada no Excel.")});
                }
            }
        }

        await using var c=await Open(ct);
        var professionals=await LoadExistingProfessionalsAsync(c,null,projectId,ct);
        var periods=await LoadExistingPeriodsAsync(c,null,projectId,ct);
        var output=new List<ProfessionalImportRow>();

        foreach(var group in normalized.GroupBy(x=>NameKey(x.Nome),StringComparer.Ordinal))
        {
            var dbMatches=professionals.Where(x=>x.NameKey==group.Key).ToList();
            var ordered=group.OrderBy(x=>x.VigenciaInicio).ThenBy(x=>x.Row).ToList();

            if(dbMatches.Count>1)
            {
                output.AddRange(ordered.Select(x=>x with{Valid=false,Action="CONFLITO",Error=JoinError(x.Error,"Existe mais de um cadastro com esse nome no projeto.")}));
                continue;
            }

            if(dbMatches.Count==0)
            {
                var first=true;
                foreach(var row in ordered)
                {
                    output.Add(row with{Action=first?"NOVO_PROFISSIONAL":"NOVA_VIGENCIA"});
                    first=false;
                }
                continue;
            }

            var professionalId=dbMatches[0].Id;
            var simulated=periods.Where(x=>x.ProfessionalId==professionalId&&x.Active).OrderBy(x=>x.Start).ToList();
            var nextFakeId=-1;
            foreach(var original in ordered)
            {
                var row=original;
                if(!row.Valid){output.Add(row with{Action="CONFLITO"});continue;}

                var same=simulated.FirstOrDefault(x=>x.Start==row.VigenciaInicio);
                if(same is not null)
                {
                    var overlap=simulated.Any(x=>x.Id!=same.Id&&RangesOverlap(x.Start,x.End,row.VigenciaInicio,row.VigenciaFim));
                    if(overlap)
                    {
                        output.Add(row with{Valid=false,Action="CONFLITO",Error=JoinError(row.Error,"A vigência atualizada se sobrepõe a outra vigência cadastrada.")});
                        continue;
                    }
                    var index=simulated.FindIndex(x=>x.Id==same.Id);
                    simulated[index]=same with{End=row.VigenciaFim,Active=true};
                    output.Add(row with{Action="ATUALIZAR_VIGENCIA"});
                    continue;
                }

                var overlaps=simulated.Where(x=>RangesOverlap(x.Start,x.End,row.VigenciaInicio,row.VigenciaFim)).ToList();
                if(overlaps.Count==1&&overlaps[0].End is null&&overlaps[0].Start<row.VigenciaInicio)
                {
                    var open=overlaps[0];
                    var index=simulated.FindIndex(x=>x.Id==open.Id);
                    simulated[index]=open with{End=row.VigenciaInicio.AddDays(-1)};
                    simulated.Add(new(nextFakeId--,professionalId,row.VigenciaInicio,row.VigenciaFim,true));
                    output.Add(row with{Action="NOVA_VIGENCIA_FECHA_ANTERIOR",Warning=JoinWarning(row.Warning,$"A vigência aberta desde {open.Start:dd/MM/yyyy} será encerrada em {row.VigenciaInicio.AddDays(-1):dd/MM/yyyy}.")});
                }
                else if(overlaps.Count==0)
                {
                    simulated.Add(new(nextFakeId--,professionalId,row.VigenciaInicio,row.VigenciaFim,true));
                    output.Add(row with{Action="NOVA_VIGENCIA"});
                }
                else
                {
                    output.Add(row with{Valid=false,Action="CONFLITO",Error=JoinError(row.Error,"A vigência se sobrepõe ao histórico já cadastrado.")});
                }
            }
        }

        output=output.OrderBy(x=>x.Row).ToList();
        return new(
            output,
            output.Count(x=>x.Valid),
            output.Count(x=>!x.Valid),
            output.Count(x=>!string.IsNullOrWhiteSpace(x.Warning)),
            output.Where(x=>x.Valid&&x.Action=="NOVO_PROFISSIONAL").Select(x=>NameKey(x.Nome)).Distinct().Count(),
            output.Count(x=>x.Valid&&x.Action is "NOVO_PROFISSIONAL" or "NOVA_VIGENCIA" or "NOVA_VIGENCIA_FECHA_ANTERIOR"),
            output.Count(x=>x.Valid&&x.Action=="ATUALIZAR_VIGENCIA"));
    }

    private static async Task<IReadOnlyList<RawProfessionalImportLine>> ReadProfessionalExcelAsync(Stream file,CancellationToken ct)
    {
        if(!file.CanRead||file.Length<=0||file.Length>15*1024*1024)throw new InvalidOperationException("Arquivo Excel inválido ou excede 15 MB.");
        using var ms=new MemoryStream();
        await file.CopyToAsync(ms,ct);
        ms.Position=0;
        using var workbook=new OfficeOpenXml.ExcelPackage(ms);
        var ws=workbook.Workbook.Worksheets.FirstOrDefault()??throw new InvalidOperationException("Planilha vazia.");
        if(ws.Dimension is null)throw new InvalidOperationException("Planilha vazia.");

        var headers=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(var col=1;col<=ws.Dimension.End.Column;col++)
        {
            var key=ExcelHeader(ws.Cells[1,col].Text);
            if(!string.IsNullOrWhiteSpace(key))headers[key]=col;
        }
        int Col(params string[] names)=>names.Select(ExcelHeader).Select(x=>headers.TryGetValue(x,out var c)?c:0).FirstOrDefault(x=>x>0);

        var functionCol=Col("função","funcao");
        var disciplineCol=Col("disciplina");
        var nameCol=Col("nome");
        var remunerationCol=Col("Remuneração","remuneracao");
        var chargesCol=Col("Encargos");
        var softwareCol=Col("Softwares","Software");
        var computerCol=Col("Computadores","Computador");
        var totalCol=Col("Total");
        var hourlyCol=Col("Custo/hora","Custo Hora","CustoHora");

        var missing=new List<string>();
        if(functionCol==0)missing.Add("função");
        if(disciplineCol==0)missing.Add("disciplina");
        if(nameCol==0)missing.Add("nome");
        if(remunerationCol==0)missing.Add("Remuneração");
        if(chargesCol==0)missing.Add("Encargos");
        if(softwareCol==0)missing.Add("Softwares");
        if(computerCol==0)missing.Add("Computadores");
        if(totalCol==0)missing.Add("Total");
        if(hourlyCol==0)missing.Add("Custo/hora");
        if(missing.Count>0)throw new InvalidOperationException($"Colunas obrigatórias ausentes: {string.Join(", ",missing)}.");

        var startCol=Col("Vigência início","Vigencia inicio","Inicio vigencia");
        var endCol=Col("Vigência fim","Vigencia fim","Fim vigencia");
        var hoursCol=Col("HH base mensal","HHBaseMensal","HH base");

        var rows=new List<RawProfessionalImportLine>();
        for(var row=2;row<=ws.Dimension.End.Row;row++)
        {
            ct.ThrowIfCancellationRequested();
            var name=ws.Cells[row,nameCol].Text?.Trim()??string.Empty;
            var hasAnything=!string.IsNullOrWhiteSpace(name)||Enumerable.Range(1,ws.Dimension.End.Column).Any(c=>!string.IsNullOrWhiteSpace(ws.Cells[row,c].Text));
            if(!hasAnything)continue;

            string? error=null;
            decimal remuneration=0,charges=0,softwares=0,computers=0,hh=160;
            decimal? total=null,hourly=null;
            DateOnly? start=null,end=null;
            try
            {
                if(string.IsNullOrWhiteSpace(name))error=JoinError(error,"Nome é obrigatório.");
                remuneration=ExcelNumber(ws.Cells[row,remunerationCol],required:true)??0m;
                charges=ExcelNumber(ws.Cells[row,chargesCol])??0m;
                softwares=ExcelNumber(ws.Cells[row,softwareCol])??0m;
                computers=ExcelNumber(ws.Cells[row,computerCol])??0m;
                total=ExcelNumber(ws.Cells[row,totalCol]);
                hourly=ExcelNumber(ws.Cells[row,hourlyCol]);
                if(hoursCol>0)hh=ExcelNumber(ws.Cells[row,hoursCol])??160m;
                if(startCol>0)start=ExcelDate(ws.Cells[row,startCol]);
                if(endCol>0)end=ExcelDate(ws.Cells[row,endCol]);
                if(remuneration<0||charges<0||softwares<0||computers<0||(total is <0)||(hourly is <0)||hh<=0)
                    error=JoinError(error,"Valores financeiros e HH não podem ser negativos; HH base deve ser positiva.");
            }
            catch(FormatException ex){error=JoinError(error,ex.Message);}

            rows.Add(new(
                row,NullText(ws.Cells[row,functionCol].Text),NullText(ws.Cells[row,disciplineCol].Text),name,
                remuneration,charges,softwares,computers,total,hourly,start,end,hh,error));
        }
        return rows;
    }

    private static decimal? ExcelNumber(OfficeOpenXml.ExcelRangeBase cell,bool required=false)
    {
        if(cell.Value is null||string.IsNullOrWhiteSpace(cell.Text))
        {
            if(required)throw new FormatException($"Valor obrigatório ausente em {cell.Address}.");
            return null;
        }
        if(cell.Value is decimal dec)return dec;
        if(cell.Value is double dbl)return Convert.ToDecimal(dbl,CultureInfo.InvariantCulture);
        if(cell.Value is int integer)return integer;
        var raw=cell.Text.Trim();
        if(decimal.TryParse(raw,NumberStyles.Number|NumberStyles.AllowCurrencySymbol,CultureInfo.GetCultureInfo("pt-BR"),out var br))return br;
        if(decimal.TryParse(raw,NumberStyles.Number|NumberStyles.AllowCurrencySymbol,CultureInfo.InvariantCulture,out var inv))return inv;
        throw new FormatException($"Número inválido em {cell.Address}: {raw}.");
    }

    private static DateOnly? ExcelDate(OfficeOpenXml.ExcelRangeBase cell)
    {
        if(cell.Value is null||string.IsNullOrWhiteSpace(cell.Text))return null;
        if(cell.Value is DateTime dt)return DateOnly.FromDateTime(dt);
        if(cell.Value is double oa)return DateOnly.FromDateTime(DateTime.FromOADate(oa));
        var raw=cell.Text.Trim();
        var cultures=new[]{CultureInfo.GetCultureInfo("pt-BR"),CultureInfo.InvariantCulture};
        foreach(var culture in cultures)if(DateTime.TryParse(raw,culture,DateTimeStyles.None,out var parsed))return DateOnly.FromDateTime(parsed);
        throw new FormatException($"Data inválida em {cell.Address}: {raw}.");
    }

    private static string ExcelHeader(string? value)
    {
        var decomposed=(value??string.Empty).Normalize(NormalizationForm.FormD);
        var sb=new StringBuilder();
        foreach(var ch in decomposed)
            if(CharUnicodeInfo.GetUnicodeCategory(ch)!=UnicodeCategory.NonSpacingMark&&char.IsLetterOrDigit(ch))sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    private static string NameKey(string value)
    {
        var decomposed=(value??string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var sb=new StringBuilder();
        foreach(var ch in decomposed)
            if(CharUnicodeInfo.GetUnicodeCategory(ch)!=UnicodeCategory.NonSpacingMark)sb.Append(char.ToUpperInvariant(ch));
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC),"\\s+"," ").Trim();
    }

    private static string? NullText(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string? JoinError(string? current,string next)=>string.IsNullOrWhiteSpace(current)?next:$"{current} {next}";
    private static string? JoinWarning(string? current,string next)=>string.IsNullOrWhiteSpace(current)?next:$"{current} {next}";
    private static bool RangesOverlap(DateOnly aStart,DateOnly? aEnd,DateOnly bStart,DateOnly? bEnd)=>
        aStart<=(bEnd??DateOnly.MaxValue)&&bStart<=(aEnd??DateOnly.MaxValue);
    private static void ReplaceImportRow(List<ProfessionalImportRow> rows,int row,ProfessionalImportRow replacement)
    {
        var index=rows.FindIndex(x=>x.Row==row);
        if(index>=0)rows[index]=replacement;
    }

    private static async Task<List<ExistingProfessional>> LoadExistingProfessionalsAsync(SqlConnection c,SqlTransaction? tx,string projectId,CancellationToken ct)
    {
        var result=new List<ExistingProfessional>();
        await using var cmd=new SqlCommand("SELECT Id,Nome FROM costs.ProjectProfessional WHERE Project_ID_ACC=@p",c,tx);
        cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=projectId;
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))result.Add(new(r.GetInt32(0),NameKey(r.GetString(1))));
        return result;
    }

    private static async Task<List<ExistingPeriod>> LoadExistingPeriodsAsync(SqlConnection c,SqlTransaction? tx,string projectId,CancellationToken ct)
    {
        var result=new List<ExistingPeriod>();
        await using var cmd=new SqlCommand("""
            SELECT h.Id,h.ProfessionalId,h.VigenciaInicio,h.VigenciaFim,h.Ativo
            FROM costs.ProjectProfessionalCostPeriod h
            JOIN costs.ProjectProfessional p ON p.Id=h.ProfessionalId
            WHERE p.Project_ID_ACC=@p;
            """,c,tx);
        cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=projectId;
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))result.Add(new(r.GetInt32(0),r.GetInt32(1),DateOnly.FromDateTime(r.GetDateTime(2)),r.IsDBNull(3)?null:DateOnly.FromDateTime(r.GetDateTime(3)),r.GetBoolean(4)));
        return result;
    }

    private static async Task<ExistingPeriod?> FindPeriodByStartAsync(SqlConnection c,SqlTransaction tx,int professionalId,DateOnly start,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("SELECT TOP(1) Id,ProfessionalId,VigenciaInicio,VigenciaFim,Ativo FROM costs.ProjectProfessionalCostPeriod WHERE ProfessionalId=@professional AND VigenciaInicio=@start ORDER BY Id DESC",c,tx);
        cmd.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
        cmd.Parameters.Add("@start",SqlDbType.Date).Value=start.ToDateTime(TimeOnly.MinValue);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetInt32(0),r.GetInt32(1),DateOnly.FromDateTime(r.GetDateTime(2)),r.IsDBNull(3)?null:DateOnly.FromDateTime(r.GetDateTime(3)),r.GetBoolean(4)):null;
    }

    private static async Task<ExistingPeriod?> FindOpenPeriodBeforeAsync(SqlConnection c,SqlTransaction tx,int professionalId,DateOnly start,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("SELECT TOP(1) Id,ProfessionalId,VigenciaInicio,VigenciaFim,Ativo FROM costs.ProjectProfessionalCostPeriod WHERE ProfessionalId=@professional AND Ativo=1 AND VigenciaFim IS NULL AND VigenciaInicio<@start ORDER BY VigenciaInicio DESC",c,tx);
        cmd.Parameters.Add("@professional",SqlDbType.Int).Value=professionalId;
        cmd.Parameters.Add("@start",SqlDbType.Date).Value=start.ToDateTime(TimeOnly.MinValue);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetInt32(0),r.GetInt32(1),DateOnly.FromDateTime(r.GetDateTime(2)),null,r.GetBoolean(4)):null;
    }

    private static async Task InsertImportedCompositionAsync(SqlConnection c,SqlTransaction tx,int professionalId,int periodId,ProfessionalImportRow row,CancellationToken ct)
    {
        if(row.Encargos>0)await InsertPeriodItemAsync(c,tx,professionalId,periodId,new("ENCARGOS","Encargos","FIXO",row.Encargos,null),ct);
        if(row.Softwares>0)await InsertPeriodItemAsync(c,tx,professionalId,periodId,new("SOFTWARE","Softwares","FIXO",row.Softwares,null),ct);
        if(row.Computadores>0)await InsertPeriodItemAsync(c,tx,professionalId,periodId,new("COMPUTADOR","Computadores","FIXO",row.Computadores,null),ct);
    }

    private sealed record AssignmentPerson(int Id,string Name);

    private static void ValidateAssignment(ProfessionalAssignmentInput input)
    {
        if(input.ProfessionalIds is null||input.ProfessionalIds.Count==0)throw new ArgumentException("Selecione ao menos um profissional.");
        if(input.DestinationProjectIds is null||input.DestinationProjectIds.Count==0)throw new ArgumentException("Selecione ao menos um projeto destino.");
    }

    private static async Task<List<AssignmentPerson>> LoadSelectedProfessionalsForAssignmentAsync(SqlConnection c,SqlTransaction? tx,string sourceProjectId,IReadOnlyList<int> ids,CancellationToken ct)
    {
        var distinct=ids.Distinct().ToArray();
        if(distinct.Length==0)return new();
        var parameters=distinct.Select((_,i)=>$"@id{i}").ToArray();
        var q=$"SELECT Id,Nome FROM costs.ProjectProfessional WHERE Project_ID_ACC=@p AND Ativo=1 AND Id IN ({string.Join(",",parameters)}) ORDER BY Nome";
        await using var cmd=new SqlCommand(q,c,tx);
        cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=sourceProjectId;
        for(var i=0;i<distinct.Length;i++)cmd.Parameters.Add(parameters[i],SqlDbType.Int).Value=distinct[i];
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var result=new List<AssignmentPerson>();
        while(await r.ReadAsync(ct))result.Add(new(r.GetInt32(0),r.GetString(1)));
        return result;
    }

    private static async Task<int> CopyOneProfessionalAsync(SqlConnection c,SqlTransaction tx,int sourceProfessionalId,string destinationProjectId,CancellationToken ct,bool copyComposition=true)
    {
        int sourceId;string name;string? company;string? alias;string? function;string? discipline;decimal hours;bool active;
        await using(var source=new SqlCommand("SELECT Id,Nome,Empresa,AliasTimesheet,Funcao,Disciplina,HHBaseMensal,Ativo FROM costs.ProjectProfessional WHERE Id=@id",c,tx))
        {
            source.Parameters.Add("@id",SqlDbType.Int).Value=sourceProfessionalId;
            await using var r=await source.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new KeyNotFoundException("Profissional de origem não encontrado.");
            sourceId=r.GetInt32(0);name=r.GetString(1);company=N(r,2);alias=N(r,3);function=N(r,4);discipline=N(r,5);hours=D(r,6);active=r.GetBoolean(7);
        }

        await using var insert=new SqlCommand("""
            INSERT costs.ProjectProfessional(Project_ID_ACC,Nome,Empresa,AliasTimesheet,Funcao,Disciplina,HHBaseMensal,Ativo)
            VALUES(@project,@name,@company,@alias,@function,@discipline,@hours,@active);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,c,tx);
        insert.Parameters.Add("@project",SqlDbType.NVarChar,255).Value=destinationProjectId;
        insert.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=name;
        insert.Parameters.Add("@company",SqlDbType.NVarChar,200).Value=(object?)company??DBNull.Value;
        insert.Parameters.Add("@alias",SqlDbType.NVarChar,200).Value=(object?)alias??DBNull.Value;
        insert.Parameters.Add("@function",SqlDbType.NVarChar,200).Value=(object?)function??DBNull.Value;
        insert.Parameters.Add("@discipline",SqlDbType.NVarChar,200).Value=(object?)discipline??DBNull.Value;
        DecParam(insert,"@hours",hours);
        insert.Parameters.Add("@active",SqlDbType.Bit).Value=active;
        var destId=Convert.ToInt32(await insert.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);

        var periods=new List<(int Id,DateOnly Start,DateOnly? End,decimal Hours,decimal Rem,decimal? Manual,bool Active)>();
        await using(var cmd=new SqlCommand("SELECT Id,VigenciaInicio,VigenciaFim,HHBaseMensal,RemuneracaoBase,CustoHoraManual,Ativo FROM costs.ProjectProfessionalCostPeriod WHERE ProfessionalId=@id ORDER BY VigenciaInicio",c,tx))
        {
            cmd.Parameters.Add("@id",SqlDbType.Int).Value=sourceId;
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))periods.Add((r.GetInt32(0),DateOnly.FromDateTime(r.GetDateTime(1)),r.IsDBNull(2)?null:DateOnly.FromDateTime(r.GetDateTime(2)),D(r,3),D(r,4),r.IsDBNull(5)?null:D(r,5),r.GetBoolean(6)));
        }

        var periodMap=new Dictionary<int,int>();
        foreach(var period in periods)
        {
            await using var cmd=new SqlCommand("""
                INSERT costs.ProjectProfessionalCostPeriod(ProfessionalId,VigenciaInicio,VigenciaFim,HHBaseMensal,RemuneracaoBase,CustoHoraManual,Ativo)
                VALUES(@professional,@start,@end,@hours,@remuneration,@manual,@active);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """,c,tx);
            cmd.Parameters.Add("@professional",SqlDbType.Int).Value=destId;
            cmd.Parameters.Add("@start",SqlDbType.Date).Value=period.Start.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@end",SqlDbType.Date).Value=period.End?.ToDateTime(TimeOnly.MinValue)??(object)DBNull.Value;
            DecParam(cmd,"@hours",period.Hours);
            DecParam(cmd,"@remuneration",copyComposition?period.Rem:0m);
            var manual=cmd.Parameters.Add("@manual",SqlDbType.Decimal);manual.Precision=18;manual.Scale=6;manual.Value=copyComposition&&period.Manual is not null?period.Manual.Value:DBNull.Value;
            cmd.Parameters.Add("@active",SqlDbType.Bit).Value=period.Active;
            periodMap[period.Id]=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
        }

        if(copyComposition)
        {
            foreach(var pair in periodMap)
            {
                await using var items=new SqlCommand("""
                    INSERT costs.ProfessionalCostItem(ProfessionalId,PeriodId,Categoria,Descricao,Valor,TipoCalculo,Percentual,BaseCalculo,Ativo)
                    SELECT @dest,@period,Categoria,Descricao,Valor,TipoCalculo,Percentual,BaseCalculo,Ativo
                    FROM costs.ProfessionalCostItem
                    WHERE ProfessionalId=@src AND PeriodId=@sourcePeriod;
                    """,c,tx);
                items.Parameters.Add("@dest",SqlDbType.Int).Value=destId;
                items.Parameters.Add("@period",SqlDbType.Int).Value=pair.Value;
                items.Parameters.Add("@src",SqlDbType.Int).Value=sourceId;
                items.Parameters.Add("@sourcePeriod",SqlDbType.Int).Value=pair.Key;
                await items.ExecuteNonQueryAsync(ct);
            }
        }

        await using(var aliases=new SqlCommand("""
            INSERT costs.TimesheetMatchAlias(ProfessionalId,TextoMatch,TipoMatch,Ativo)
            SELECT @dest,TextoMatch,TipoMatch,Ativo
            FROM costs.TimesheetMatchAlias
            WHERE ProfessionalId=@src AND Ativo=1;
            """,c,tx))
        {
            aliases.Parameters.Add("@dest",SqlDbType.Int).Value=destId;
            aliases.Parameters.Add("@src",SqlDbType.Int).Value=sourceId;
            await aliases.ExecuteNonQueryAsync(ct);
        }
        return destId;
    }

    private static async Task<CostCopyPreview> PreviewCopyOnConnectionAsync(SqlConnection c,SqlTransaction tx,CostCopyInput input,CancellationToken ct){var conflicts=new List<string>();async Task<bool> Has(string q){await using var cmd=new SqlCommand(q,c,tx);cmd.Parameters.Add("@d",SqlDbType.NVarChar,255).Value=input.DestinationProjectId;return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)>0;}if(input.Professionals&&await Has("SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Project_ID_ACC=@d"))conflicts.Add("professionals");if(input.ProjectTaxes&&await Has("SELECT COUNT(1) FROM costs.ProjectTax WHERE Project_ID_ACC=@d"))conflicts.Add("taxes");if(input.Packages&&await Has("SELECT COUNT(1) FROM costs.Package WHERE Project_ID_ACC=@d"))conflicts.Add("packages");if(input.Expenses&&await Has("SELECT COUNT(1) FROM costs.ProjectExpense WHERE Project_ID_ACC=@d"))conflicts.Add("expenses");async Task<int> Count(string q){await using var cmd=new SqlCommand(q,c,tx);cmd.Parameters.Add("@s",SqlDbType.NVarChar,255).Value=input.SourceProjectId;return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);}return new(input.DestinationProjectId,input.Professionals?await Count("SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Project_ID_ACC=@s AND Ativo=1"):0,input.ProjectTaxes?await Count("SELECT COUNT(1) FROM costs.ProjectTax WHERE Project_ID_ACC=@s AND Ativo=1"):0,input.Packages?await Count("SELECT COUNT(1) FROM costs.Package WHERE Project_ID_ACC=@s AND Ativo=1"):0,input.Expenses?await Count("SELECT COUNT(1) FROM costs.ProjectExpense WHERE Project_ID_ACC=@s AND Ativo=1"):0,conflicts);}
    private static async Task EnsureNoProfessionalPeriodOverlapAsync(SqlConnection c, SqlTransaction? tx, int professionalId, DateOnly start, DateOnly? end, CancellationToken ct, int? excludePeriodId = null)
    {
        const string overlap = """
            SELECT COUNT(1)
            FROM costs.ProjectProfessionalCostPeriod WITH(UPDLOCK,HOLDLOCK)
            WHERE ProfessionalId=@id
              AND Ativo=1
              AND (@exclude IS NULL OR Id<>@exclude)
              AND VigenciaInicio<=ISNULL(@end,CONVERT(date,'99991231'))
              AND @start<=ISNULL(VigenciaFim,CONVERT(date,'99991231'));
            """;
        await using var cmd = new SqlCommand(overlap, c, tx);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = professionalId;
        cmd.Parameters.Add("@exclude", SqlDbType.Int).Value = excludePeriodId is null ? DBNull.Value : excludePeriodId.Value;
        cmd.Parameters.Add("@start", SqlDbType.Date).Value = start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@end", SqlDbType.Date).Value = end?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value;
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
            throw new InvalidOperationException("A vigência se sobrepõe a outra configuração ativa.");
    }

    private static async Task EnsureProfessionalAsync(SqlConnection c,SqlTransaction? tx,string project,int id,CancellationToken ct){await using var cmd=new SqlCommand("SELECT COUNT(1) FROM costs.ProjectProfessional WHERE Id=@id AND Project_ID_ACC=@p",c,tx);cmd.Parameters.Add("@id",SqlDbType.Int).Value=id;cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=project;if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=1)throw new KeyNotFoundException("Profissional não encontrado neste projeto.");}
    private static async Task EnsureTaxAsync(SqlConnection c,SqlTransaction tx,string project,int id,CancellationToken ct){await using var cmd=new SqlCommand("SELECT COUNT(1) FROM costs.ProjectTax WHERE Id=@id AND Project_ID_ACC=@p",c,tx);cmd.Parameters.Add("@id",SqlDbType.Int).Value=id;cmd.Parameters.Add("@p",SqlDbType.NVarChar,255).Value=project;if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=1)throw new KeyNotFoundException("Imposto não encontrado neste projeto.");}
    private static async Task EnsureProjectsAsync(SqlConnection c,string source,string dest,CancellationToken ct,SqlTransaction? tx=null){if(source==dest)throw new ArgumentException("Origem e destino devem ser projetos diferentes.");await using var cmd=new SqlCommand("SELECT COUNT(DISTINCT Project_ID_ACC) FROM dbo.Projetos WHERE Project_ID_ACC IN(@s,@d) AND Is_Ativo=1",c,tx);cmd.Parameters.Add("@s",SqlDbType.NVarChar,255).Value=source;cmd.Parameters.Add("@d",SqlDbType.NVarChar,255).Value=dest;if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=2)throw new KeyNotFoundException("Projeto de origem ou destino inexistente/inativo.");}
    private static async Task<int> ExistsAsync(SqlConnection c,string q,string project,CancellationToken ct){await using var cmd=new SqlCommand(q,c);cmd.Parameters.Add("@d",SqlDbType.NVarChar,255).Value=project;return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);}
    private static async Task<int> CountAsync(SqlConnection c,string q,string project,CancellationToken ct){await using var cmd=new SqlCommand(q,c);cmd.Parameters.Add("@s",SqlDbType.NVarChar,255).Value=project;return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);}
    private static async Task InsertPeriodItemAsync(SqlConnection c,SqlTransaction tx,int professional,int period,ProfessionalPeriodItemInput item,CancellationToken ct){const string q="INSERT costs.ProfessionalCostItem(ProfessionalId,PeriodId,Categoria,Descricao,Valor,TipoCalculo,Percentual,BaseCalculo) VALUES(@pro,@period,@cat,@desc,@amount,@calc,@percent,@base)";await using var cmd=new SqlCommand(q,c,tx);cmd.Parameters.Add("@pro",SqlDbType.Int).Value=professional;cmd.Parameters.Add("@period",SqlDbType.Int).Value=period;cmd.Parameters.Add("@cat",SqlDbType.VarChar,30).Value=item.Category;cmd.Parameters.Add("@desc",SqlDbType.NVarChar,300).Value=item.Description;Decimal(cmd,"@amount",item.CalculationType=="PERCENTUAL"?0m:item.Amount);cmd.Parameters.Add("@calc",SqlDbType.VarChar,12).Value=item.CalculationType;cmd.Parameters.Add("@percent",SqlDbType.Decimal).Value=item.Percentage is null?DBNull.Value:item.Percentage.Value;cmd.Parameters.Add("@base",SqlDbType.VarChar,24).Value=item.CalculationBase;await cmd.ExecuteNonQueryAsync(ct);}
    private static void ValidateItem(ProfessionalPeriodItemInput x){if(string.IsNullOrWhiteSpace(x.Category)||string.IsNullOrWhiteSpace(x.Description)||x.Amount<0||x.CalculationType is not("FIXO" or "PERCENTUAL"))throw new ArgumentException("Item de custo inválido.");if(x.CalculationType=="PERCENTUAL"&&(x.Percentage is null or <0||x.CalculationBase!="REMUNERACAO"))throw new ArgumentException("Encargo percentual exige percentual válido e base REMUNERACAO.");if(x.CalculationType=="FIXO"&&x.Percentage is not null)throw new ArgumentException("Item fixo não deve informar percentual.");}
    private static void ValidatePeriod(DateOnly start,DateOnly? end){if(end is not null&&end<start)throw new ArgumentException("Vigência final anterior ao início.");}
    private static void ValidateTax(ProjectTaxInput x){if(x.CalculationType=="PERCENTUAL"){if(x.Percentage is null or <0||x.FixedValue is not null||x.CalculationBase is not("RECEITA" or "CUSTO" or "OUTRO"))throw new ArgumentException("Imposto percentual inválido.");}else if(x.CalculationType=="FIXO"){if(x.FixedValue is null or <0||x.Percentage is not null||x.CalculationBase!="VALOR_FIXO")throw new ArgumentException("Imposto fixo deve usar base VALOR_FIXO e um valor não negativo.");}else throw new ArgumentException("Tipo de cálculo de imposto inválido.");}
    private static void ValidateFinancial(bool condition,string message){if(!condition)throw new ArgumentException(message);}
    private static void ValidateCopy(CostCopyInput x){if(x.SourceProjectId==x.DestinationProjectId)throw new ArgumentException("Origem e destino devem ser diferentes.");if(!x.Professionals&&!x.ProjectTaxes&&!x.Packages&&!x.Expenses)throw new ArgumentException("Selecione pelo menos uma configuração para copiar.");}
    private static void Decimal(SqlCommand c,string name,decimal value){var p=c.Parameters.Add(name,SqlDbType.Decimal);p.Precision=18;p.Scale=6;p.Value=value;}
    private static void DecParam(SqlCommand c,string name,decimal value)=>Decimal(c,name,value);
    private static decimal D(SqlDataReader r,int i)=>r.IsDBNull(i)?0m:Convert.ToDecimal(r.GetValue(i),CultureInfo.InvariantCulture);
    private static string? N(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
}
