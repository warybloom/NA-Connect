using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers.WeeklyPlanning;

public abstract class PlanningReadControllerBase(IProjectReportAccess access) : ControllerBase
{
    protected async Task<IActionResult?> Guard(string hubId, string projectId, DateOnly start, DateOnly end,
        string? discipline, int page, int pageSize, CancellationToken ct)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized(new { message = "Sessão expirada." });
        if (!AccId(hubId) || !AccId(projectId) || start == default || end < start ||
            end.DayNumber - start.DayNumber > 6 || end == DateOnly.MaxValue ||
            discipline is { Length: > 200 } || page is < 1 or > 1000000 || pageSize is < 1 or > 100)
            return BadRequest(new { message = "Projeto, período ou paginação inválidos." });
        var result = await access.CheckAsync(hubId, projectId, token, ct);
        return result switch
        {
            ProjectReportAccess.Allowed => null,
            ProjectReportAccess.Unauthenticated => Unauthorized(new { message = "Sessão expirada." }),
            ProjectReportAccess.Denied => StatusCode(403, new { message = "Projeto não autorizado." }),
            _ => StatusCode(503, new { message = "Não foi possível validar acesso ao projeto." })
        };
    }
    private static bool AccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
