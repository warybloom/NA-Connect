using System.Globalization;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Controllers;

/// <summary>Endpoints exclusivos das quatro telas Gestão à Vista (Jamile). Somente leitura.</summary>
[ApiController]
[Route("api/reports/gestao-vista-jamile")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GestaoVistaJamileController(IProjectAccessService projectAccess, IPermissionService permissions,
    IConfiguration configuration, CoordinationStandardRepository coordination, DisciplineStandardRepository standards,
    ILogger<GestaoVistaJamileController> logger) : ControllerBase
{
    private static readonly string[] PermissoesGestao = ["reports.gestao.view", "reports.ldp.view", "reports.desvios.view",
        "reports.timesheet.view", "production.managerial.hh-custo.view", "production.financial.project.view", "production.financial.document.view"];

    private GestaoVistaJamileRepository Repository => new(configuration, coordination, standards);

    [HttpGet("disciplinas")]
    public Task<IActionResult> Disciplinas([FromQuery] string projectId, CancellationToken ct) =>
        Executar(projectId, PermissoesGestao, () => Repository.DisciplinasAsync(projectId, ct), ct);

    [HttpGet("programacao")]
    public Task<IActionResult> Programacao([FromQuery] string projectId, [FromQuery] string inicio, [FromQuery] string fim, CancellationToken ct)
    {
        if (!Data(inicio, out var di) || !Data(fim, out var df) || df < di || (df - di).TotalDays > 62)
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Período inválido." }));
        return Executar(projectId, ["reports.weekly.view", .. PermissoesGestao], () => Repository.ProgramacaoAsync(projectId, di, df, ct), ct);
    }

    private async Task<IActionResult> Executar<T>(string projectId, IReadOnlyCollection<string> chaves, Func<Task<T>> ler, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, chaves, ct);
            if (authorization is not null) return authorization;
            return Ok(await ler());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta Gestão à Vista (Jamile). Ref={Ref} Detalhe={Detalhe}", HttpContext.TraceIdentifier, ReportErrors.Describe(ex));
            return StatusCode(503, new { message = $"Relatório temporariamente indisponível. (ref: {HttpContext.TraceIdentifier})" });
        }
    }

    private static bool Data(string? value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
