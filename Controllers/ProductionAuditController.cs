using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/production-audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductionAuditController(IProjectAccessService projectAccess, IPermissionService permissions,
    IProductionAuditRepository repository, INaCurrentUserAccessor naCurrentUser, ICurrentUserAccessor currentUser,
    ILogger<ProductionAuditController> logger) : ControllerBase
{
    private static readonly string[] Keys = ["production.managerial.hh-custo.view", "reports.gestao.view"];
    private const int ResumoLabOwnerUserId = 1;
    private const string ResumoLabOwnerAutodeskId = "WDV284PFQTJDQZEG";
    // Acesso restrito ao responsável e ao gerente João França. Não se basear em nome exibido.
    private const int ResumoComparativoGerenteUserId = 12;
    private const string ResumoComparativoGerenteEmail = "joao.franca@naconsult.com.br";
    private const string ResumoComparativoGerenteAutodeskId = "JNR26CVQN4SC37D6";

    [HttpGet("entries")]
    public Task<IActionResult> Entries([FromQuery] string projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? discipline, [FromQuery] string? document, [FromQuery] string? person, [FromQuery] int page=1, [FromQuery] int pageSize=50,
        [FromQuery] bool exact=false, CancellationToken ct=default)
        => Read(projectId, new(from,to,discipline,document,person,page,pageSize,null,exact), x => repository.ReadEntriesAsync(projectId,x,ct), ct);

    [HttpGet("overruns/documents")]
    public Task<IActionResult> DocumentOverruns([FromQuery] string projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? discipline, [FromQuery] string? document, [FromQuery] string? person, CancellationToken ct=default)
        => Read(projectId, new(from,to,discipline,document,person), x => repository.ReadDocumentOverrunsAsync(projectId,x,ct), ct);

    [HttpGet("overruns/disciplines")]
    public Task<IActionResult> DisciplineOverruns([FromQuery] string projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? discipline, [FromQuery] string? document, [FromQuery] string? person, CancellationToken ct=default)
        => Read(projectId, new(from,to,discipline,document,person), x => repository.ReadDisciplineOverrunsAsync(projectId,x,ct), ct);

    [HttpGet("people")]
    public Task<IActionResult> People([FromQuery] string projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? discipline,
        [FromQuery] string? document, [FromQuery] string? person, [FromQuery] int page=1, [FromQuery] int pageSize=50, CancellationToken ct=default)
        => Read(projectId, new(from,to,discipline,document,person,page,pageSize), x => repository.ReadPeopleAsync(projectId,x,ct), ct);

    [HttpGet("disciplines")]
    public Task<IActionResult> Disciplines([FromQuery] string projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? discipline,
        [FromQuery] string? document, [FromQuery] string? person, CancellationToken ct=default)
        => Read(projectId, new(from,to,discipline,document,person), x => repository.ReadDisciplinesAsync(projectId,x,ct), ct);

    [HttpGet("documents")]
    public Task<IActionResult> Documents([FromQuery] string projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? discipline,
        [FromQuery] string? document, [FromQuery] string? person, [FromQuery] string? status, [FromQuery] int page=1, [FromQuery] int pageSize=50, CancellationToken ct=default)
        => !string.IsNullOrWhiteSpace(status) && !ProductionAuditStatus.Todos.Contains(status.Trim())
            ? Task.FromResult<IActionResult>(BadRequest(new { message="Status inválido." }))
            : Read(projectId, new(from,to,discipline,document,person,page,pageSize,status?.Trim()), x => repository.ReadDocumentsAsync(projectId,x,ct), ct);

    [HttpGet("reconciliation")]
    public Task<IActionResult> Reconciliation([FromQuery] string projectId, CancellationToken ct=default)
        => Read(projectId, new(null,null,null,null,null), x => repository.ReadReconciliationAsync(projectId,x,ct), ct);

    [HttpGet("timesheet-unassigned")]
    public Task<IActionResult> TimesheetUnassigned([FromQuery] string projectId, CancellationToken ct=default)
        => Read(projectId, new(null,null,null,null,null), _ => repository.ReadUnassignedTimesheetAsync(projectId,ct), ct);

    [HttpGet("timesheet-outside-ldp")]
    public Task<IActionResult> TimesheetOutsideLdp([FromQuery] string projectId, CancellationToken ct=default)
        => Read(projectId, new(null,null,null,null,null), _ => repository.ReadOutsideLdpAsync(projectId,ct),ct);

    [HttpPost("timesheet-outside-ldp/direction")]
    public Task<IActionResult> SaveTimesheetOutsideLdpDirection(
        [FromQuery] string projectId,[FromBody] OutsideLdpDirectionInput input,CancellationToken ct=default)
        => Write(projectId,()=>repository.SaveOutsideLdpDirectionAsync(
            projectId,input,
            User.Identity?.Name ?? User.FindFirst("email")?.Value ?? User.FindFirst("preferred_username")?.Value,ct),
            ct,"direcionamento de HH fora da LDP");

    [HttpGet("timesheet-unassigned/routing")]
    public Task<IActionResult> TimesheetUnassignedRouting([FromQuery] string projectId, CancellationToken ct=default)
        => Read(projectId, new(null,null,null,null,null), _ => repository.ReadTimesheetRoutingConfigAsync(projectId,ct), ct);

    [HttpPut("timesheet-unassigned/routing")]
    public Task<IActionResult> SaveTimesheetUnassignedRouting(
        [FromQuery] string projectId,
        [FromBody] ProductionAuditTimesheetRoutingInput input,
        CancellationToken ct=default)
        => Write(projectId, () => repository.SaveTimesheetRoutingConfigAsync(
            projectId,
            input,
            User.Identity?.Name ?? User.FindFirst("email")?.Value ?? User.FindFirst("preferred_username")?.Value,
            ct), ct, "roteamento do Timesheet");

    [HttpPost("timesheet-unassigned/treatment")]
    public Task<IActionResult> SaveTimesheetUnassignedTreatment(
        [FromQuery] string projectId,
        [FromBody] ProductionAuditUnassignedTreatmentInput input,
        CancellationToken ct=default)
        => Write(projectId, () => repository.SaveUnassignedTimesheetTreatmentAsync(
            projectId,
            input,
            User.Identity?.Name ?? User.FindFirst("email")?.Value ?? User.FindFirst("preferred_username")?.Value,
            ct), ct, "tratamento de HH sem documento");

    [HttpGet("experimental-summary/access")]
    [RequirePermission("quality.comparison.view", "quality.divergences.view", "quality.professionals.view", AnyProject=true)]
    public async Task<IActionResult> ExperimentalSummaryAccess(CancellationToken ct=default)
        => Ok(new { allowed = permissions.Enforced || await IsResumoLabOwnerAsync(ct) });

    [HttpGet("engineering-professionals")]
    public async Task<IActionResult> EngineeringProfessionals([FromQuery] string projectId, CancellationToken ct=default)
    {
        if (!permissions.Enforced && !await IsResumoLabOwnerAsync(ct)) return NotFound();
        return await Read(projectId, new(null,null,null,null,null),
            _ => repository.ReadEngineeringProfessionalScopeAsync(projectId, ct), ct);
    }

    [HttpPut("engineering-professionals")]
    public async Task<IActionResult> SaveEngineeringProfessional(
        [FromQuery] string projectId, [FromBody] EngineeringProfessionalScopeInput input, CancellationToken ct=default)
    {
        if (!permissions.Enforced && !await IsResumoLabOwnerAsync(ct)) return NotFound();
        var current = await naCurrentUser.GetAsync(ct);
        return await Write(projectId, () => repository.SaveEngineeringProfessionalScopeAsync(
            projectId, input, current?.Email, ct), ct, "classificação de profissionais da engenharia");
    }

    [HttpGet("audit-differences")]
    public async Task<IActionResult> AuditDifferences([FromQuery] string projectId, CancellationToken ct=default)
    {
        if (!permissions.Enforced && !await IsResumoLabOwnerAsync(ct)) return NotFound();
        return await Read(projectId, new(null,null,null,null,null),
            _ => repository.ReadAuditDifferencesAsync(projectId, ct), ct);
    }

    [HttpGet("experimental-summary")]
    public async Task<IActionResult> ExperimentalSummary([FromQuery] string projectId, CancellationToken ct=default)
    {
        if (!permissions.Enforced && !await IsResumoLabOwnerAsync(ct)) return NotFound();
        return await Read(projectId, new(null,null,null,null,null), _ => repository.ReadExperimentalSummaryAsync(projectId,ct), ct);
    }

    private async Task<bool> IsResumoLabOwnerAsync(CancellationToken ct)
    {
        var internalUser = await naCurrentUser.GetAsync(ct);
        if (internalUser?.UserId == ResumoLabOwnerUserId) return true;
        if (internalUser?.UserId == ResumoComparativoGerenteUserId &&
            string.Equals(internalUser.Email, ResumoComparativoGerenteEmail, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var autodeskUser = await currentUser.GetAsync(ct);
            if (string.Equals(autodeskUser?.AutodeskUserId, ResumoLabOwnerAutodeskId, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(autodeskUser?.AutodeskUserId, ResumoComparativoGerenteAutodeskId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(autodeskUser.Email, ResumoComparativoGerenteEmail, StringComparison.OrdinalIgnoreCase);
        }
        catch (AutodeskUserInfoException)
        {
            return false;
        }
    }

    private async Task<IActionResult> Write<T>(string? projectId, Func<Task<T>> operation, CancellationToken ct, string operationName)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message="Projeto inválido." });
        try
        {
            var gate = await CheckAccessAsync(projectId!, ct);
            if (gate is not null) return gate;
            return Ok(await operation());
        }
        catch(ArgumentException ex){return BadRequest(new {message=ex.Message});}
        catch(InvalidOperationException ex){return Conflict(new {message=ex.Message});}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            logger.LogWarning(ex,"Falha ao salvar {Operation}. Tipo={Type}",operationName,ex.GetType().Name);
            return StatusCode(503,new {message="Não foi possível salvar agora."});
        }
    }

    private async Task<IActionResult> Read<T>(string? projectId, ProductionAuditFilters filters, Func<ProductionAuditFilters,Task<T>> read, CancellationToken ct)
    {
        if (!IsAccId(projectId) || filters.Page is < 1 or > 1000000 || filters.PageSize is < 1 or > 100)
            return BadRequest(new { message="Filtros inválidos." });
        try
        {
            var gate = await CheckAccessAsync(projectId!, ct);
            if (gate is not null) return gate;
            return Ok(await read(filters));
        }
        catch(ProductionAuditScopeException ex) when(ex.Scope.Status is ProductionAuditScopeStatus.CrNotConfigured or ProductionAuditScopeStatus.CrAmbiguous)
        {
            return Conflict(new {
                code=ex.Scope.Status==ProductionAuditScopeStatus.CrNotConfigured?"crOsNotConfigured":"crOsAmbiguous",
                message=ex.Scope.Message,
                crOs=ex.Scope.CrOs
            });
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            logger.LogWarning("Falha na auditoria de produção. Tipo={Type}",ex.GetType().Name);
            return StatusCode(503,new {message="Auditoria temporariamente indisponível."});
        }
    }

    private async Task<IActionResult?> CheckAccessAsync(string projectId, CancellationToken ct)
    {
        var access=await projectAccess.CheckProjectAccessAsync(projectId,ct);
        if(access==PermissionCheck.Unauthenticated) return Unauthorized(new { message="Sessão expirada." });
        if(access==PermissionCheck.Denied) return StatusCode(403,new { message="Projeto não autorizado." });
        if(access!=PermissionCheck.Allowed) return StatusCode(503,new { message="Não foi possível validar acesso." });

        var route = HttpContext.Request.Path.Value ?? "";
        var actionKey = route switch
        {
            var x when x.Contains("timesheet-outside-ldp/direction", StringComparison.OrdinalIgnoreCase) => "quality.outside-ldp.edit",
            var x when x.Contains("timesheet-outside-ldp", StringComparison.OrdinalIgnoreCase) => "quality.outside-ldp.view",
            var x when x.Contains("timesheet-unassigned/treatment", StringComparison.OrdinalIgnoreCase) => "quality.unassigned.edit",
            var x when x.Contains("timesheet-unassigned/routing", StringComparison.OrdinalIgnoreCase) => HttpMethods.IsGet(Request.Method) ? "quality.unassigned.view" : "quality.unassigned.edit",
            var x when x.Contains("timesheet-unassigned", StringComparison.OrdinalIgnoreCase) => "quality.unassigned.view",
            var x when x.Contains("engineering-professionals", StringComparison.OrdinalIgnoreCase) => HttpMethods.IsGet(Request.Method) ? "quality.professionals.view" : "quality.professionals.edit",
            var x when x.Contains("audit-differences", StringComparison.OrdinalIgnoreCase) => "quality.divergences.view",
            var x when x.Contains("experimental-summary", StringComparison.OrdinalIgnoreCase) => "quality.comparison.view",
            _ => null,
        };
        var permission = await permissions.CheckAsync(actionKey is null ? Keys : [actionKey], projectId, ct);
        if(permission==PermissionCheck.Unauthenticated) return Unauthorized(new { message="Sessão expirada." });
        if(permission==PermissionCheck.Denied) return NotFound();
        if(permission!=PermissionCheck.Allowed) return StatusCode(503,new { message="Não foi possível validar permissões." });
        return null;
    }

    private static bool IsAccId(string? value)=>
        value is {Length:38} &&
        value.StartsWith("b.",StringComparison.Ordinal) &&
        Guid.TryParseExact(value[2..],"D",out _);
}
