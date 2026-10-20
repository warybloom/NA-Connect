using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace APS.WebApp.Services.Permissions;

/// <summary>O ID permanece estável quando o administrador renomeia PERFIL GESTÃO.</summary>
public static class ManagementProfilePolicy
{
    public const int ProfileId = 9;
    public static bool IsRestricted(UserPermissionData data) =>
        !PermissionResolver.IsMasterAdmin(data)
        && data.Assignments.Any(a => a.ProfileId == ProfileId && a.ProjetoId is null);
}

/// <summary>
/// Auditoria de Planejamento herda keys também usadas em Lançamentos da Produção.
/// Bloquear somente seu endpoint próprio; people/documents/disciplines/entries continuam intactos.
/// A consulta ao vínculo real independe de Permissions:Enforce.
/// </summary>
public sealed class ManagementProfilePlanningFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!string.Equals(context.HttpContext.Request.Path.Value?.TrimEnd('/'),
                "/api/production-audit/reconciliation", StringComparison.OrdinalIgnoreCase)) return;
        var ct = context.HttpContext.RequestAborted;
        var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        var repository = context.HttpContext.RequestServices.GetRequiredService<IPermissionRepository>();
        try
        {
            var user = await permissions.GetCurrentUserAsync(ct);
            if (user is null) { context.Result = new UnauthorizedResult(); return; }
            if (user.IsMasterAdmin) return;
            if (user.UserId is not int id) return;
            var data = await repository.CarregarDadosAsync(id, false, ct);
            if (ManagementProfilePolicy.IsRestricted(data)) context.Result = new NotFoundResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
