using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace APS.WebApp.Services.Permissions;

/// <summary>
/// [RequirePermission("robot.configure")] — valida no BACKEND (ocultar menu é só UX). Aceita várias keys (qualquer uma
/// basta: endpoint compartilhado por duas telas). O projeto do contexto vem, nesta ordem, da rota ou da query:
/// "projectId", "projectIdAcc". Toda resolução passa por IPermissionService (sem SQL aqui, sem connection string).
/// Camada ADICIONAL: IProjectReportAccess continua validando o acesso ao projeto dentro das actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute(params string[] anyOfKeys) : Attribute, IAsyncAuthorizationFilter
{
    public IReadOnlyList<string> Keys { get; } = anyOfKeys;
    /// <summary>
    /// true = basta a key em QUALQUER projeto (ou global). Só para endpoints de listagem/metadado SEM projeto no request
    /// (ex.: lista de projetos cadastrados que alimenta as telas); o dado de cada projeto continua protegido por
    /// IProjectReportAccess e pelos endpoints com projectId.
    /// </summary>
    public bool AnyProject { get; init; }
    private static readonly string[] ParametrosProjeto = { "projectId", "projectIdAcc" };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var service = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        // Não aceitar projeto A para autorizar uma rota que efetivamente manipula projeto B.
        // Conferir TODOS os valores da rota, query e formulário, inclusive repetidos.
        var candidatos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string Canonico(string value)
        {
            var text = value.Trim();
            return text.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        }
        void Adicionar(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var valor = raw.Trim();
            candidatos.TryAdd(Canonico(valor), valor);
        }
        foreach (var nome in ParametrosProjeto)
        {
            if (context.RouteData.Values.TryGetValue(nome, out var route)) Adicionar(route?.ToString());
            if (context.HttpContext.Request.Query.TryGetValue(nome, out var query))
                foreach (var item in query) Adicionar(item);
        }
        if (context.HttpContext.Request.HasFormContentType)
        {
            var form = await context.HttpContext.Request.ReadFormAsync(context.HttpContext.RequestAborted);
            foreach (var nome in ParametrosProjeto)
                foreach (var item in form[nome]) Adicionar(item);
        }
        if (candidatos.Count > 1)
        {
            context.Result = new BadRequestObjectResult(new { message = "Projeto divergente na requisição." });
            return;
        }
        string? projeto = candidatos.Count == 0 ? null : candidatos.Values.First();
        var resultado = await service.CheckAsync(Keys, projeto, context.HttpContext.RequestAborted, AnyProject && projeto is null);
        context.Result = resultado switch
        {
            PermissionCheck.Allowed => null,
            PermissionCheck.Unauthenticated => new UnauthorizedObjectResult(new { message = "Sessão expirada." }),
            // Não revelar a existência nem as chaves de uma funcionalidade sem autorização.
            PermissionCheck.Denied => new NotFoundResult(),
            _ => new ObjectResult(new { message = "Não foi possível validar permissões." }) { StatusCode = 503 },
        };
    }
}

/// <summary>Endpoint sem permissão de funcionalidade (login, callbacks externos, listas básicas do ACC). O motivo é obrigatório.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PermissionExemptAttribute(string motivo) : Attribute
{
    public string Motivo { get; } = motivo;
}
