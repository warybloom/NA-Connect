using APS.WebApp.Services.Costs;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/project-costs/history")]
public sealed class ProjectCostHistoryController(IProjectAccessService access, IPermissionService permissions, IProjectCostHistoryService costs, ILogger<ProjectCostHistoryController> logger) : ControllerBase
{
    private string CostPermission()
    {
        var path=Request.Path.Value??"";
        var after="/api/project-costs/history/";
        var section=path.StartsWith(after,StringComparison.OrdinalIgnoreCase)
            ? path[after.Length..].Split('/',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??""
            : "";
        var category=section.ToLowerInvariant() switch
        {
            "taxes"=>"taxes",
            "professionals"=>"professionals",
            "documents"=>"professionals",
            "copy"=>"professionals",
            "eligible-projects"=>"professionals",
            _=>"__unknown__"
        };
        var op=HttpMethods.IsGet(Request.Method)?"view":"edit";
        return $"planning.costs.{category}.{op}";
    }

    [HttpGet("professionals/{professionalId:int}/periods")]
    public Task<IActionResult> ProfessionalPeriods(int professionalId, [FromQuery] string projectId, CancellationToken ct) => Gate(projectId, () => costs.ProfessionalPeriodsAsync(projectId, professionalId, ct), ct);

    [HttpPost("professionals/{professionalId:int}/periods/preview")]
    public Task<IActionResult> PreviewProfessionalPeriod(int professionalId, [FromQuery] string projectId, [FromBody] ProfessionalPeriodStart request, CancellationToken ct) => Gate(projectId, () => costs.PreviewProfessionalPeriodAsync(projectId, professionalId, request.Start, request.End, ct), ct);

    [HttpPost("professionals/{professionalId:int}/periods")]
    public Task<IActionResult> CreateProfessionalPeriod(int professionalId, [FromQuery] string projectId, [FromBody] ProfessionalPeriodInput input, CancellationToken ct) => Gate(projectId, () => costs.CreateProfessionalPeriodAsync(projectId, professionalId, input, ct), ct);

    [HttpPut("professionals/{professionalId:int}/periods/{periodId:int}")]
    public Task<IActionResult> UpdateProfessionalPeriod(int professionalId,int periodId,[FromQuery]string projectId,[FromBody]ProfessionalPeriodInput input,CancellationToken ct)
        => Gate(projectId,()=>costs.UpdateProfessionalPeriodAsync(projectId,professionalId,periodId,input,ct),ct);

    [HttpPost("professionals/{professionalId:int}/periods/{periodId:int}/deactivate")]
    public Task<IActionResult> DeactivateProfessionalPeriod(int professionalId,int periodId,[FromQuery]string projectId,CancellationToken ct)
        => Gate(projectId,()=>costs.DeactivateProfessionalPeriodAsync(projectId,professionalId,periodId,ct),ct);

    [HttpGet("professionals/import/template")]
    public async Task<IActionResult> ProfessionalImportTemplate([FromQuery]string projectId,CancellationToken ct)
    {
        var decision=await GateDecision(projectId,ct);
        if(decision is not null)return decision;
        var bytes=await costs.ProfessionalImportTemplateAsync(ct);
        return File(bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","Modelo_Custos_Profissionais.xlsx");
    }

    [HttpPost("professionals/import/preview")]
    public Task<IActionResult> PreviewProfessionalImport([FromQuery]string projectId,IFormFile file,CancellationToken ct)
        => Gate(projectId,()=>costs.PreviewProfessionalImportAsync(projectId,file.OpenReadStream(),ct),ct);

    [HttpPost("professionals/import/commit")]
    public Task<IActionResult> CommitProfessionalImport([FromQuery]string projectId,IFormFile file,CancellationToken ct)
        => Gate(projectId,()=>costs.CommitProfessionalImportAsync(projectId,file.OpenReadStream(),ct),ct);

    [HttpGet("eligible-projects")]
    public async Task<IActionResult> EligibleProjects(CancellationToken ct)
    {
        var user=await permissions.GetCurrentUserAsync(ct);
        if(user is null)return Unauthorized();
        if(!permissions.Enforced && !user.IsMasterAdmin)return NotFound();
        if(await permissions.CheckAsync(["planning.costs.professionals.view"],null,ct,anyProject:true)!=PermissionCheck.Allowed)
            return NotFound();

        var result=new List<EligibleCostProject>();
        foreach(var project in await costs.EligibleProjectsAsync(ct))
        {
            var decision=await access.CheckProjectAccessAsync(project.ProjectIdAcc,ct);
            if(decision==PermissionCheck.Allowed &&
               await permissions.CheckAsync(["planning.costs.professionals.view"],project.ProjectIdAcc,ct)==PermissionCheck.Allowed)
                result.Add(project);
        }
        return Ok(result);
    }

    [HttpPost("professionals/assign/preview")]
    public Task<IActionResult> PreviewProfessionalAssignment([FromQuery]string projectId,[FromBody]ProfessionalAssignmentInput input,CancellationToken ct)
        => GateAssignment(projectId,input,()=>costs.PreviewProfessionalAssignmentAsync(projectId,input,ct),ct);

    [HttpPost("professionals/assign/commit")]
    public Task<IActionResult> CommitProfessionalAssignment([FromQuery]string projectId,[FromBody]ProfessionalAssignmentInput input,CancellationToken ct)
        => GateAssignment(projectId,input,()=>costs.AssignProfessionalsAsync(projectId,input,ct),ct);

    [HttpGet("taxes")]
    public Task<IActionResult> ProjectTaxes([FromQuery] string projectId, CancellationToken ct) => Gate(projectId, () => costs.ProjectTaxesAsync(projectId, ct), ct);

    [HttpPost("taxes/{taxId:int}/deactivate")]
    public Task<IActionResult> DeactivateTax(int taxId, [FromQuery] string projectId, CancellationToken ct) => Gate(projectId, () => costs.DeactivateProjectTaxAsync(projectId, taxId, ct), ct);

    [HttpPost("taxes")]
    public Task<IActionResult> CreateTax([FromQuery] string projectId, [FromBody] ProjectTaxInput input, CancellationToken ct) => Gate(projectId, () => costs.CreateProjectTaxPeriodAsync(projectId, null, input, ct), ct);

    [HttpPost("taxes/{taxId:int}/periods")]
    public Task<IActionResult> AddTaxPeriod(int taxId, [FromQuery] string projectId, [FromBody] ProjectTaxInput input, CancellationToken ct) => Gate(projectId, () => costs.CreateProjectTaxPeriodAsync(projectId, taxId, input, ct), ct);

    [HttpPost("taxes/{taxId:int}/periods/preview")]
    public Task<IActionResult> PreviewTaxPeriod(int taxId, [FromQuery] string projectId, [FromBody] ProfessionalPeriodStart request, CancellationToken ct) => Gate(projectId, () => costs.PreviewProjectTaxPeriodAsync(projectId, taxId, request.Start, ct), ct);

    [HttpGet("documents/actuals")]
    public Task<IActionResult> DocumentActuals([FromQuery] string projectId, CancellationToken ct) => Gate(projectId, () => costs.DocumentActualCostsAsync(projectId, ct), ct);

    [HttpPost("copy/preview")]
    public Task<IActionResult> PreviewCopy([FromBody] CostCopyInput input, CancellationToken ct) => GateBoth(input, () => costs.PreviewCopyAsync(input, ct), ct);

    [HttpPost("copy/commit")]
    public Task<IActionResult> CommitCopy([FromBody] CostCopyInput input, CancellationToken ct) => GateBoth(input, () => costs.CopyAsync(input, ct), ct);

    [HttpPost("copy/batch/preview")]
    public Task<IActionResult> PreviewCopyBatch([FromBody] CostCopyBatchInput input,CancellationToken ct)=>GateCopyBatch(input,()=>costs.PreviewCopyBatchAsync(input,ct),ct);

    [HttpPost("copy/batch/commit")]
    public Task<IActionResult> CommitCopyBatch([FromBody] CostCopyBatchInput input,CancellationToken ct)=>GateCopyBatch(input,()=>costs.CopyBatchAsync(input,ct),ct);

    private async Task<IActionResult> GateBoth<T>(CostCopyInput input, Func<Task<T>> operation, CancellationToken ct)
    {
        var source = await GateDecision(input.SourceProjectId, ct); if (source is not null) return source;
        var destination = await GateDecision(input.DestinationProjectId, ct); if (destination is not null) return destination;
        return await Execute(operation, ct);
    }

    private async Task<IActionResult> GateCopyBatch<T>(CostCopyBatchInput input,Func<Task<T>> operation,CancellationToken ct)
    {
        var source=await GateDecision(input.SourceProjectId,ct);if(source is not null)return source;
        if(input.DestinationProjectIds is null||input.DestinationProjectIds.Count==0)return BadRequest(new{message="Selecione ao menos um projeto destino."});
        foreach(var projectId in input.DestinationProjectIds){var destination=await GateDecision(projectId,ct);if(destination is not null)return destination;}
        return await Execute(operation,ct);
    }

    private async Task<IActionResult> GateAssignment<T>(string sourceProjectId,ProfessionalAssignmentInput input,Func<Task<T>> operation,CancellationToken ct)
    {
        var source=await GateDecision(sourceProjectId,ct);
        if(source is not null)return source;
        if(input.DestinationProjectIds is null||input.DestinationProjectIds.Count==0)
            return BadRequest(new{message="Selecione ao menos um projeto destino."});

        foreach(var projectId in input.DestinationProjectIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(projectId.Equals(sourceProjectId,StringComparison.OrdinalIgnoreCase))continue;
            var destination=await GateDecision(projectId,ct);
            if(destination is not null)return destination;
        }
        return await Execute(operation,ct);
    }

    private async Task<IActionResult> Gate<T>(string projectId, Func<Task<T>> operation, CancellationToken ct)
    {
        if (!ValidProject(projectId)) return BadRequest(new { message = "Project_ID_ACC inválido." });
        var decision = await GateDecision(projectId, ct); if (decision is not null) return decision;
        return await Execute(operation, ct);
    }

    private async Task<IActionResult?> GateDecision(string projectId, CancellationToken ct)
    {
        if (!ValidProject(projectId)) return BadRequest(new { message = "Project_ID_ACC inválido." });
        var project = await access.CheckProjectAccessAsync(projectId, ct);
        if (project == PermissionCheck.Unauthenticated) return Unauthorized();
        if (project == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (project != PermissionCheck.Allowed) return StatusCode(503);
        var key = CostPermission();
        var permission = await permissions.CheckAsync([key], projectId, ct);
        if (permission == PermissionCheck.Unauthenticated) return Unauthorized();
        if (permission == PermissionCheck.Denied) return NotFound();
        if (permission != PermissionCheck.Allowed) return StatusCode(503);
        var user = await permissions.GetCurrentUserAsync(ct);
        if (user is null) return Unauthorized();
        return permissions.Enforced || user.IsMasterAdmin ? null : NotFound();
    }

    private async Task<IActionResult> Execute<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        try { return Ok(await operation()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { logger.LogWarning("Falha em histórico de custos. Tipo={Type}", ex.GetType().Name); return StatusCode(503, new { message = "Histórico de custos temporariamente indisponível." }); }
    }

    private static bool ValidProject(string? projectId) => projectId is { Length: 38 } && projectId.StartsWith("b.", StringComparison.Ordinal) && Guid.TryParseExact(projectId[2..], "D", out _);
}

public sealed record ProfessionalPeriodStart(DateOnly Start, DateOnly? End);
