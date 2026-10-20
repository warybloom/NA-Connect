using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// CDE / Lista de Documentos — visão da LDP ESPELHO (Tipo 1 ou Tipo 2) lida do banco, somente leitura.
/// Gate interno de sessão N&A, UserProjects e capability antes do repository filtrado
/// por Project_ID_ACC. As colunas vêm do backend (CdeDocumentListDefinitions), nunca fixas no frontend.
/// </summary>
[ApiController]
[Route("api/reports/cde")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CdeReportsController(IProjectAccessService projectAccess, IPermissionService permissions, ICdeDocumentListRepository repository,
    ILogger<CdeReportsController> logger) : ControllerBase
{
    [HttpGet("document-list")]
    public async Task<IActionResult> DocumentList([FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] string? disciplina = null, [FromQuery] string? search = null, [FromQuery] string? sort = null,
        [FromQuery] string? dir = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (!IsAccId(projectId) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 ||
            disciplina is { Length: > 100 } || search is { Length: > 100 } || sort is { Length: > 50 } || dir is not (null or "" or "asc" or "desc"))
            return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, ["cde.document-list.view"], cancellationToken);
            if (authorization is not null) return authorization;
            return Ok(await repository.ReadAsync(projectId, Empty(disciplina), Empty(search), Empty(sort), dir == "desc",
                page, pageSize, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na consulta da Lista de Documentos (CDE). Tipo={ErrorType}", ex.GetType().Name);
            return StatusCode(503, new { message = "Lista de documentos temporariamente indisponível." });
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
