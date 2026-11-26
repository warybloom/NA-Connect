using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FinanceiroHorasReportsController(IProjectAccessService projectAccess, IPermissionService permissions, IFinanceiroHorasReportRepository repository,
    ILogger<FinanceiroHorasReportsController> logger) : ControllerBase
{
    [HttpGet("financeiro-horas")]
    public async Task<IActionResult> FinanceiroHoras([FromQuery] string? hubId, [FromQuery] string projectId,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["reports.timesheet.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (SqlException ex)
        {
            // Apenas número/mensagem do erro SQL, só no log do servidor. Nunca connection string, token
            // ou corpo HTTP. Mensagem nunca é enviada ao frontend.
            logger.LogWarning("Falha na consulta de Financeiro/Horas. Tipo=SqlException Number={SqlErrorNumber} Message={SqlErrorMessage}",
                ex.Number, ex.Message);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
        catch (Exception ex)
        {
            // Nunca registrar corpo HTTP, token, diagnóstico SQL ou connection string.
            logger.LogWarning("Falha na consulta de Financeiro/Horas. Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
