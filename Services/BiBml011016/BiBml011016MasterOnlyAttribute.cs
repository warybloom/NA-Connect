using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace APS.WebApp.Services.BiBml011016;

/// <summary>
/// BI BML011.016 é EXCLUSIVO do Master Admin. A decisão vem só de IPermissionService.GetCurrentUserAsync (sessão do servidor),
/// a cada request, e NÃO depende de Permissions:Enforce: com Enforce=false o RequirePermission libera qualquer sessão, este
/// gate não. Admin com permissions.manage, Gestão, TV ou vínculo com o projeto: 403. Sem sessão: 401.
/// </summary>
public static class BiBml011016MasterPolicy
{
    public static async Task<IActionResult?> AvaliarAsync(IPermissionService permissions, ILogger? logger, CancellationToken ct)
    {
        CurrentPermissionUser? usuario;
        try { usuario = await permissions.GetCurrentUserAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning("BI BML011.016: falha ao validar a identidade. Tipo={Tipo}", ex.GetType().Name);
            return new ObjectResult(new { message = "Não foi possível validar permissões." }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
        if (usuario is null) return new UnauthorizedObjectResult(new { message = "Sessão expirada." });
        if (usuario.IsMasterAdmin != true)
        {
            logger?.LogWarning("BI BML011.016: acesso negado a usuário não Master Admin. UserId={UserId}", usuario.UserId);
            return new ObjectResult(new { message = "BI BML011.016 restrito ao Master Admin." }) { StatusCode = StatusCodes.Status403Forbidden };
        }
        return null;
    }
}

/// <summary>
/// Filtro de autorização que roda ANTES de qualquer outro (Order = int.MinValue), inclusive do [RequirePermission] —
/// assim o não-master recebe 403 explícito em vez do 404 genérico. As actions chamam a política de novo (defesa em profundidade).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class BiBml011016MasterOnlyAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var permissions = services.GetRequiredService<IPermissionService>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("BiBml011016.MasterGate");
        context.Result = await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, context.HttpContext.RequestAborted);
    }
}
