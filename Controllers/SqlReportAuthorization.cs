using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>Gate comum dos relatórios SQL legados: projeto permitido antes da capability funcional.</summary>
internal static class SqlReportAuthorization
{
    public static async Task<IActionResult?> CheckAsync(ControllerBase controller, IProjectAccessService projectAccess,
        IPermissionService permissions, string projectId, IReadOnlyCollection<string> permissionKeys, CancellationToken cancellationToken)
    {
        var projectDecision = await projectAccess.CheckProjectAccessAsync(projectId, cancellationToken);
        if (projectDecision == PermissionCheck.Unauthenticated)
            return controller.Unauthorized(new { message = "Sessão expirada." });
        if (projectDecision == PermissionCheck.Denied)
            return controller.StatusCode(403, new { message = "Projeto não autorizado." });
        if (projectDecision != PermissionCheck.Allowed)
            return controller.StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });

        var permissionDecision = await permissions.CheckAsync(permissionKeys, projectId, cancellationToken);
        if (permissionDecision == PermissionCheck.Unauthenticated)
            return controller.Unauthorized(new { message = "Sessão expirada." });
        if (permissionDecision == PermissionCheck.Denied)
            return controller.StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = permissionKeys });
        return permissionDecision == PermissionCheck.Allowed
            ? null
            : controller.StatusCode(503, new { message = "Não foi possível validar permissões." });
    }
}
