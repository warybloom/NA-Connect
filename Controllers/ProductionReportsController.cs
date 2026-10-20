using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// Relatório de Produção — endpoints SOMENTE LEITURA para as telas que ainda não tinham endpoint
/// (Change Order, Desvio de Qualidade, RNC detalhado, Controle Documental, Nivelamento e Controle da
/// Programação). O gate interno valida sessão N&A, UserProjects e permission key antes da consulta SQL.
/// O gate é interno: sessão N&A → UserProjects → permissão funcional → repository filtrado por Project_ID_ACC.
/// </summary>
[ApiController]
[Route("api/reports/production")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductionReportsController(IProjectAccessService projectAccess, IPermissionService permissions,
    IChangeOrderReportRepository changeOrders, IQualityDeviationReportRepository qualityDeviations,
    IRncReportRepository rnc, IDocumentControlReportRepository documentControl,
    IScheduleLevelingReportRepository scheduleLeveling, IScheduleControlReportRepository scheduleControl,
    ILogger<ProductionReportsController> logger) : ControllerBase
{
    [HttpGet("change-orders")]
    public Task<IActionResult> ChangeOrders([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? changeOrder = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Read(projectId, page, pageSize, Ok(changeOrder, 4000) && Ok(search, 100), "Change Order",
            ["production.managerial.change-order.view", "production.operational.change-order.view"],
            ct => changeOrders.ReadAsync(projectId, Empty(changeOrder), Empty(search), page, pageSize, ct), cancellationToken);

    [HttpGet("quality-deviations")]
    public Task<IActionResult> QualityDeviations([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? disciplina = null, [FromQuery] string? tipo = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Read(projectId, page, pageSize, Ok(disciplina, 150) && Ok(tipo, 250) && Ok(search, 100), "Desvio de Qualidade",
            ["production.managerial.desvio-qualidade.view", "production.operational.desvio-qualidade.view"],
            ct => qualityDeviations.ReadAsync(projectId, Empty(disciplina), Empty(tipo), Empty(search), page, pageSize, ct), cancellationToken);

    [HttpGet("rnc")]
    public Task<IActionResult> Rnc([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? status = null, [FromQuery] string? disciplina = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Read(projectId, page, pageSize, Ok(status, 100) && Ok(disciplina, 150) && Ok(search, 100), "RNC",
            ["production.managerial.rnc.view", "production.operational.rnc.view"],
            ct => rnc.ReadAsync(projectId, Empty(status), Empty(disciplina), Empty(search), page, pageSize, ct), cancellationToken);

    [HttpGet("document-control")]
    public Task<IActionResult> DocumentControl([FromQuery] string? hubId, [FromQuery] string projectId, CancellationToken cancellationToken = default)
        => Read(projectId, 1, 1, true, "Controle Documental",
            ["production.managerial.controle-documental.view", "production.operational.controle-documental.view"],
            ct => documentControl.ReadAsync(projectId, ct), cancellationToken);

    [HttpGet("schedule-leveling")]
    public Task<IActionResult> ScheduleLeveling([FromQuery] string? hubId, [FromQuery] string projectId, CancellationToken cancellationToken = default)
        => Read(projectId, 1, 1, true, "Nivelamento Programação",
            ["production.managerial.nivelamento.view", "production.operational.nivelamento.view"],
            ct => scheduleLeveling.ReadAsync(projectId, ct), cancellationToken);

    [HttpGet("schedule-control")]
    public Task<IActionResult> ScheduleControl([FromQuery] string? hubId, [FromQuery] string projectId, CancellationToken cancellationToken = default)
        => Read(projectId, 1, 1, true, "Controle Programação",
            ["production.managerial.controle-programacao.view", "production.operational.controle-programacao.view"],
            ct => scheduleControl.ReadAsync(projectId, ct), cancellationToken);

    private async Task<IActionResult> Read<T>(string projectId, int page, int pageSize, bool filtrosValidos,
        string relatorio, IReadOnlyCollection<string> permissionKeys, Func<CancellationToken, Task<T>> ler, CancellationToken cancellationToken)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 || !filtrosValidos)
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var acesso = await projectAccess.CheckProjectAccessAsync(projectId, cancellationToken);
            if (acesso == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (acesso == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
            if (acesso != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
            var permissao = await permissions.CheckAsync(permissionKeys, projectId, cancellationToken);
            if (permissao == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (permissao == PermissionCheck.Denied) return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = permissionKeys });
            if (permissao != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar permissões." });
            return Ok(await ler(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Nunca registrar corpo HTTP, token, diagnóstico SQL ou connection string.
            logger.LogWarning("Falha na consulta do Relatório de Produção ({Relatorio}). Tipo={ErrorType}", relatorio, ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static bool Ok(string? value, int max) => value is null || value.Length <= max;
    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
