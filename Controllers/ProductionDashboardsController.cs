using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>
/// Dashboards interativos do Relatório de Produção: GET api/reports/production/dashboard/{id}.
/// Filtros de cross-filter chegam como "f.&lt;dimensão&gt;=valor" (repetível). Resposta única com KPIs, gráficos e
/// tabela calculados sobre o MESMO conjunto filtrado. Mesmo gate dos ReportsControllers.
/// </summary>
[ApiController]
[Route("api/reports/production/dashboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductionDashboardsController(IProjectAccessService projectAccess, IProductionDashboardRepository repository,
    IPermissionService permissions, ILogger<ProductionDashboardsController> logger) : ControllerBase
{
    public const string PrefixoFiltro = "f.";
    /// <summary>Máximo de projetos consolidados num dashboard (TV multiprojeto).</summary>
    public const int MaxProjetos = 200;

    [HttpGet("{dashboardId}")]
    public async Task<IActionResult> Dashboard(string dashboardId, [FromQuery] string? hubId, [FromQuery] string projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15,
        // MULTIPROJETO (30/09/2026, TV): projetos ADICIONAIS ao principal. O dashboard é avaliado pela MESMA definição sobre a
        // união das linhas de cada projeto (cada um lido pela sua própria consulta/gates, com permissão e acesso validados
        // projeto a projeto) — nenhuma fórmula muda; 1 projeto = comportamento anterior.
        [FromQuery(Name = "projetos")] string[]? projetos = null,
        CancellationToken cancellationToken = default)
    {
        if (!ProductionDashboards.Todos.TryGetValue(dashboardId ?? "", out var definicao)) return NotFound(new { message = "Dashboard inexistente." });
        // production.view (classe) libera o módulo; cada dashboard exige a SUA key no projeto do contexto.
        // Aceita a key do dashboard em QUALQUER variante (Gerencial ou Operacional) — mesma tela, mesmos dados.
        if (!PermissionCatalog.DashboardsProducao.TryGetValue(definicao.Id, out var chavesDashboard))
            return StatusCode(403, new { message = "Dashboard sem permissão cadastrada." });
        if (!IsAccId(projectId) || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return BadRequest(new { message = "Filtros inválidos." });
        var todosProjetos = new[] { projectId }.Concat(projetos ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        if (todosProjetos.Length > MaxProjetos || todosProjetos.Any(p => !IsAccId(p))) return BadRequest(new { message = "Filtros inválidos." });

        var acesso = await projectAccess.CheckProjectAccessAsync(projectId, cancellationToken);
        if (acesso == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (acesso == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (acesso != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });

        var permissaoModulo = await permissions.CheckAsync(["production.view"], projectId, cancellationToken);
        if (permissaoModulo == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (permissaoModulo == PermissionCheck.Denied) return StatusCode(403, new { message = "Você não tem permissão para este dashboard.", permission = "production.view" });
        if (permissaoModulo != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar permissões." });

        var permissao = await permissions.CheckAsync(chavesDashboard.ToArray(), projectId, cancellationToken);
        if (permissao == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (permissao == PermissionCheck.Denied) return StatusCode(403, new { message = "Você não tem permissão para este dashboard.", permission = chavesDashboard });
        if (permissao != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar permissões." });
        foreach (var extra in todosProjetos.Skip(1))
        {
            var acessoExtra = await projectAccess.CheckProjectAccessAsync(extra, cancellationToken);
            if (acessoExtra == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (acessoExtra == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
            if (acessoExtra != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
            var p = await permissions.CheckAsync(chavesDashboard.ToArray(), extra, cancellationToken);
            if (p == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
            if (p == PermissionCheck.Denied) return StatusCode(403, new { message = "Você não tem permissão para este dashboard em um dos projetos.", permission = chavesDashboard });
            if (p != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar permissões." });
        }

        IReadOnlyDictionary<string, IReadOnlySet<string>> filtros;
        try
        {
            var brutos = Request.Query.Where(q => q.Key.StartsWith(PrefixoFiltro, StringComparison.Ordinal))
                .ToDictionary(q => q.Key[PrefixoFiltro.Length..], q => (IReadOnlyList<string>)q.Value.Select(v => v ?? "").ToList(), StringComparer.Ordinal);
            filtros = DashboardEngine.ValidarFiltros(definicao, brutos);
        }
        catch (ArgumentException) { return BadRequest(new { message = "Filtros inválidos." }); }

        try
        {
            var linhas = new List<DashboardRow>();
            foreach (var pid in todosProjetos)
                linhas.AddRange(await repository.ReadRowsAsync(definicao, pid, cancellationToken));
            return Ok(DashboardEngine.Avaliar(definicao, linhas, filtros, page, pageSize));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            // Mesmo padrão de GestaoVistaReportsController: só número/mensagem do erro SQL, só no log do servidor.
            // Nunca connection string, senha ou token; o frontend recebe apenas a mensagem genérica.
            logger.LogWarning("Falha no dashboard {Dashboard} do Relatório de Produção. Tipo=SqlException Number={SqlErrorNumber} Message={SqlErrorMessage}",
                dashboardId, ex.Number, ex.Message);
            return StatusCode(503, new { message = "Dashboard temporariamente indisponível." });
        }
        catch (Exception ex)
        {
            logger.LogWarning("Falha no dashboard {Dashboard} do Relatório de Produção. Tipo={ErrorType}", dashboardId, ex.GetType().Name);
            return StatusCode(503, new { message = "Dashboard temporariamente indisponível." });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
