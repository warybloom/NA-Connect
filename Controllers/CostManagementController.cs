using APS.WebApp.Services.Costs;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/project-costs")]
// Mesma key da checagem interna (Gate): o atributo cumpre o contrato de endpoints; a ação ainda valida o projeto (UserProjects).
public sealed class CostManagementController(IProjectAccessService access, IPermissionService permissions, ICostManagementService costs, ILogger<CostManagementController> log) : ControllerBase
{
    private string CostPermission()
    {
        var path=Request.Path.Value??"";
        var root="/api/project-costs/";
        var section=path.StartsWith(root,StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].Split('/',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??""
            : "";
        var group=section.ToLowerInvariant() switch
        {
            "packages"=>"packages",
            "expenses"=>"softwares",
            "professionals"=>"professionals",
            "recognition"=>"professionals",
            "import"=>"packages",
            "summary"=>"professionals",
            "deactivate"=>string.Equals(RouteData.Values["entity"]?.ToString(),"professional",StringComparison.OrdinalIgnoreCase)
                ? "professionals" : "packages",
            _=>"__unknown__"
        };
        // Pré-visualizações POST também são ações de tratamento; não conceder gravação com .view.
        var operation=HttpMethods.IsGet(Request.Method)?"view":"edit";
        return $"planning.costs.{group}.{operation}";
    }
    [HttpGet("summary")] public Task<IActionResult> Summary([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.SummaryAsync(projectId,ct),ct);
    [HttpGet("professionals")] public Task<IActionResult> Professionals([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.ProfessionalsAsync(projectId,ct),ct);
    [HttpGet("professionals/{professionalId:int}/items")] public Task<IActionResult> Items([FromRoute]int professionalId,[FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.ItemsAsync(projectId,professionalId,ct),ct);
    [HttpGet("expenses")] public Task<IActionResult> Expenses([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.ExpensesAsync(projectId,ct),ct);
    [HttpGet("packages")] public Task<IActionResult> Packages([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.PackagesAsync(projectId,ct),ct);
    [HttpGet("packages/{packageId:int}/documents")] public Task<IActionResult> Documents([FromRoute]int packageId,[FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.DocumentsAsync(projectId,packageId,ct),ct);
    [HttpGet("packages/{packageId:int}/addenda")] public Task<IActionResult> Addenda([FromRoute]int packageId,[FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.AddendaAsync(projectId,packageId,ct),ct);
    [HttpGet("packages/{packageId:int}/allocation")] public Task<IActionResult> Allocation([FromRoute]int packageId,[FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.AllocationAsync(projectId,packageId,ct),ct);
    [HttpPut("professionals/{professionalId:int}")] public Task<IActionResult> UpdateProfessional([FromRoute]int professionalId,[FromQuery]string projectId,[FromBody]CostProfessionalUpdate input,CancellationToken ct)=>Read(projectId,()=>costs.UpdateProfessionalAsync(projectId,professionalId,input,ct),ct);
    [HttpPut("professionals/{professionalId:int}/items/{itemId:int}")] public Task<IActionResult> UpdateItem([FromRoute]int professionalId,[FromRoute]int itemId,[FromQuery]string projectId,[FromBody]CostItemUpdate input,CancellationToken ct)=>Read(projectId,()=>costs.UpdateItemAsync(projectId,professionalId,itemId,input,ct),ct);
    [HttpGet("recognition")] public Task<IActionResult> Recognition([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.RecognitionAsync(projectId,ct),ct);
    [HttpPost("professionals")] public Task<IActionResult> AddProfessional([FromQuery]string projectId,[FromBody]CostProfessionalInput input,CancellationToken ct)=>Write(projectId,()=>costs.AddProfessionalAsync(projectId,input,ct),ct);
    [HttpPost("professionals/{professionalId:int}/items")] public Task<IActionResult> AddItem([FromRoute]int professionalId,[FromQuery]string projectId,[FromBody]CostItemInput input,CancellationToken ct)=>Write(projectId,()=>costs.AddItemAsync(projectId,professionalId,input,ct),ct);
    [HttpPost("expenses")] public Task<IActionResult> AddExpense([FromQuery]string projectId,[FromBody]CostExpenseInput input,CancellationToken ct)=>Write(projectId,()=>costs.AddExpenseAsync(projectId,input,ct),ct);
    [HttpPost("packages")] public Task<IActionResult> AddPackage([FromQuery]string projectId,[FromBody]CostPackageInput input,CancellationToken ct)=>Write(projectId,()=>costs.AddPackageAsync(projectId,input,ct),ct);
    [HttpPut("packages/{packageId:int}")] public Task<IActionResult> UpdatePackage([FromRoute]int packageId,[FromQuery]string projectId,[FromBody]CostPackageUpdate input,CancellationToken ct)=>Read(projectId,()=>costs.UpdatePackageAsync(projectId,packageId,input,ct),ct);
    [HttpPost("packages/{packageId:int}/documents")] public Task<IActionResult> AddDocument([FromRoute]int packageId,[FromQuery]string projectId,[FromBody]CostDocumentInput input,CancellationToken ct)=>Write(projectId,()=>costs.AddDocumentAsync(projectId,packageId,input,ct),ct);
    [HttpPut("packages/{packageId:int}/documents/{documentId:int}")] public Task<IActionResult> UpdateDocument([FromRoute]int packageId,[FromRoute]int documentId,[FromQuery]string projectId,[FromBody]CostDocumentInput input,CancellationToken ct)=>Read(projectId,()=>costs.UpdateDocumentAsync(projectId,packageId,documentId,input,ct),ct);
    [HttpPost("packages/{packageId:int}/documents/batch")] public Task<IActionResult> AddDocumentsBatch([FromRoute]int packageId,[FromQuery]string projectId,[FromBody]IReadOnlyList<CostDocumentInput> input,CancellationToken ct)=>Write(projectId,()=>costs.AddDocumentsBatchAsync(projectId,packageId,input,ct),ct);
    [HttpPost("packages/{packageId:int}/addenda")] public Task<IActionResult> AddAddendum([FromRoute]int packageId,[FromQuery]string projectId,[FromBody]CostAddendumInput input,CancellationToken ct)=>Write(projectId,()=>costs.AddAddendumAsync(projectId,packageId,input,ct),ct);
    [HttpPost("{entity}/{id:int}/deactivate")] public Task<IActionResult> Deactivate([FromRoute]string entity,[FromRoute]int id,[FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>costs.DeactivateAsync(projectId,entity,id,ct),ct);
    [HttpGet("import/template")]
    public Task<IActionResult> DownloadPackageImportTemplate([FromQuery] string projectId,CancellationToken ct)
        => Read(projectId,async()=>File(
            await costs.PackageImportTemplateAsync(ct),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Modelo_Custos_Pacotes.xlsx"),ct);

    [HttpPost("import/preview")] public Task<IActionResult> PreviewImport([FromQuery]string projectId,IFormFile file,CancellationToken ct)=>Read(projectId,()=>costs.PreviewExcelAsync(projectId,file.OpenReadStream(),ct),ct);
    [HttpPost("import/commit")] public Task<IActionResult> CommitImport([FromQuery]string projectId,IFormFile file,CancellationToken ct)=>Read(projectId,()=>costs.CommitExcelAsync(projectId,file.OpenReadStream(),ct),ct);
    // Pacotes Por Disciplina: mesmo Gate (packages.view para GET, packages.edit para POST/PUT, inclusive prévias).
    private IPackageDisciplineService Disciplines=>costs as IPackageDisciplineService??throw new InvalidOperationException("Modelo Por Disciplina indisponível neste servidor.");
    private async Task<string?> Author(CancellationToken ct)=>(await permissions.GetCurrentUserAsync(ct))?.Email;
    [HttpGet("packages/model-support")] public Task<IActionResult> PackageModelSupport([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>Disciplines.PackageModelSupportAsync(ct),ct);
    [HttpGet("packages/ldp-disciplines")] public Task<IActionResult> LdpDisciplines([FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>Disciplines.LdpDisciplinesAsync(projectId,ct),ct);
    [HttpPost("packages/discipline/preview")] public Task<IActionResult> PreviewDisciplinePackage([FromQuery]string projectId,[FromBody]DisciplinePackageInput input,CancellationToken ct)=>Read(projectId,()=>Disciplines.PreviewDisciplinePackageAsync(projectId,input,ct),ct);
    [HttpPost("packages/discipline")] public Task<IActionResult> CreateDisciplinePackage([FromQuery]string projectId,[FromBody]DisciplinePackageInput input,CancellationToken ct)=>Write(projectId,async()=>await Disciplines.CreateDisciplinePackageAsync(projectId,input,await Author(ct),ct),ct);
    [HttpPut("packages/{packageId:int}/discipline")] public Task<IActionResult> UpdateDisciplinePackage([FromRoute]int packageId,[FromQuery]string projectId,[FromBody]DisciplinePackageInput input,CancellationToken ct)=>Read(projectId,async()=>await Disciplines.UpdateDisciplinePackageAsync(projectId,packageId,input,await Author(ct),ct),ct);
    [HttpGet("packages/{packageId:int}/disciplines")] public Task<IActionResult> DisciplinePackageDetail([FromRoute]int packageId,[FromQuery]string projectId,CancellationToken ct)=>Read(projectId,()=>Disciplines.DisciplinePackageDetailAsync(projectId,packageId,ct),ct);
    [HttpGet("import/discipline/template")]
    public Task<IActionResult> DownloadDisciplineImportTemplate([FromQuery]string projectId,CancellationToken ct)
        => Read(projectId,async()=>File(await Disciplines.DisciplineImportTemplateAsync(ct),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","Modelo_Pacotes_Por_Disciplina.xlsx"),ct);
    [HttpPost("import/discipline/preview")] public Task<IActionResult> PreviewDisciplineImport([FromQuery]string projectId,IFormFile file,CancellationToken ct)=>Read(projectId,()=>Disciplines.PreviewDisciplineExcelAsync(projectId,file.OpenReadStream(),ct),ct);
    [HttpPost("import/discipline/commit")] public Task<IActionResult> CommitDisciplineImport([FromQuery]string projectId,IFormFile file,CancellationToken ct)=>Read(projectId,async()=>await Disciplines.CommitDisciplineExcelAsync(projectId,file.OpenReadStream(),await Author(ct),ct),ct);
    private async Task<IActionResult> Read<T>(string projectId,Func<Task<T>> operation,CancellationToken ct)=>await Gate(projectId,operation,ct);
    private async Task<IActionResult> Write(string projectId,Func<Task<int>> operation,CancellationToken ct)=>await Gate(projectId,async()=>new {id=await operation()},ct);
    private async Task<IActionResult> Gate<T>(string projectId, Func<Task<T>> operation, CancellationToken ct)
    {
        if (!IsProject(projectId)) return BadRequest(new { message = "Project_ID_ACC inválido." });
        try
        {
            var accessDecision = await access.CheckProjectAccessAsync(projectId, ct);
            if (accessDecision == PermissionCheck.Unauthenticated) return Unauthorized();
            if (accessDecision == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
            if (accessDecision != PermissionCheck.Allowed) return StatusCode(503);
            var key = CostPermission();
            var permissionDecision = await permissions.CheckAsync([key], projectId, ct);
            if (permissionDecision == PermissionCheck.Unauthenticated) return Unauthorized();
            if (permissionDecision == PermissionCheck.Denied) return NotFound();
            if (permissionDecision != PermissionCheck.Allowed) return StatusCode(503);
            var currentUser = await permissions.GetCurrentUserAsync(ct);
            if (currentUser is null) return Unauthorized();
            if (!permissions.Enforced && !currentUser.IsMasterAdmin) return NotFound();
            var result = await operation();
            return result is IActionResult actionResult ? actionResult : Ok(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { log.LogWarning("Falha em Custos do Projeto. Tipo={Type}", ex.GetType().Name); return StatusCode(503, new { message = "Custos temporariamente indisponíveis." }); }
    }
    private static bool IsProject(string? p)=>p is{Length:38}&&p.StartsWith("b.",StringComparison.Ordinal)&&Guid.TryParseExact(p[2..],"D",out _);
}
