using Microsoft.AspNetCore.WebUtilities;

namespace APS.WebApp.Services.Tv.Portal;

/// <summary>Runs after Session but before Routing. Denies every non-portal API/action for a restricted TV identity.</summary>
public sealed class TvPortalMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TvPortalIdentityService identityService, TvConfigurationStore configurations,
        TvPortalAssignmentStore assignments, APS.WebApp.Services.Permissions.IPermissionRepository repository)
    {
        var path = context.Request.Path.Value ?? "/";
        // Login endpoints must also work before a local session exists.
        var authenticated = context.Session.GetInt32("Auth:UserId") is not null || !string.IsNullOrEmpty(context.Session.GetString("AccessToken"));
        if (!authenticated) { if (path.StartsWith("/api/tv-portal/", StringComparison.OrdinalIgnoreCase)) { await Deny(context, 401); return; } await next(context); return; }
        TvPortalIdentity? identity;
        try { identity = await identityService.ReadAsync(context.RequestAborted); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch { await Deny(context, 503); return; } // identity unknown is never a compatibility bypass
        if (identity is null) { await Deny(context, 401); return; }
        if (!identity.Restricted) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        if (path.StartsWith("/api/tv-portal/data/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var parts = path["/api/tv-portal/data/".Length..].Split('/');
                if (!HttpMethods.IsGet(context.Request.Method) || !identity.CanView || parts.Length != 2
                    || !int.TryParse(parts[1], out var index) || index < 0 || !TvPortalResources.ValidQuery(context.Request.Query))
                { await Deny(context, 403); return; }
                var config = await configurations.ReadAsync(context.RequestAborted);
                var assigned = await assignments.ReadAsync(context.RequestAborted);
                var p = config.Presentations.SingleOrDefault(p => p.Id == parts[0] && p.EstaAtiva);
                if (p is null || !TvPortalResources.Supported(p) || !TvPortalPolicy.Assigned(assigned, p.Id, identity.UserId) || index >= p.Slides.Count)
                { await Deny(context, 403); return; }
                var slide = p.Slides[index];
                var resource = context.Request.Query["resource"].ToString();
                var project = context.Request.Query["projectId"].ToString();
                var projects = slide.Projetos is { Count: > 0 } ps ? ps : [new TvSlideProjeto(slide.HubId, slide.ProjectId, slide.ProjectName)];
                var saved = projects.SingleOrDefault(x => x.ProjectId == project);
                var requestedProjects = new[] { project }.Concat(context.Request.Query["projetos"].Select(x => x ?? "")).ToHashSet(StringComparer.Ordinal);
                if (saved is null || !TvPortalResources.ForScreen(slide.ScreenId).Contains(resource, StringComparer.Ordinal)
                    || !TvPortalResources.PeriodoPermitido(slide, resource, context.Request.Query)
                    || context.Request.Query.TryGetValue("hubId", out var hub) && hub.ToString() != saved.HubId
                    || requestedProjects.Any(id => !projects.Any(p => p.ProjectId == id)))
                { await Deny(context, 403); return; }
                // Somente recursos READ-ONLY dos projetos gravados NESTE slide/ apresentação:
                // o perfil TV não recebe vínculos administrativos com os projetos.
                foreach (var id in requestedProjects)
                    if (!await identityService.CanViewProjectAsync(identity.UserId, id, context.RequestAborted)) { await Deny(context, 403); return; }
                context.Items[TvPortalPolicy.ScopeItem] = new TvReadScope(requestedProjects, TvPortalResources.ReadKeys(slide));
                var oldPath = context.Request.Path; var oldQuery = context.Request.QueryString;
                context.Request.Path = resource;
                context.Request.QueryString = QueryString.Create(context.Request.Query.Where(q => q.Key != "resource")
                    .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v))));
                try { await next(context); }
                finally { context.Items.Remove(TvPortalPolicy.ScopeItem); context.Request.Path = oldPath; context.Request.QueryString = oldQuery; }
                return;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch { if (!context.Response.HasStarted) await Deny(context, 503); return; }
        }
        var allowed = HttpMethods.IsGet(context.Request.Method) && (path == "/api/tv-portal/session" || path == "/api/tv-portal/presentations"
            || path.StartsWith("/api/tv-portal/presentations/", StringComparison.Ordinal)
            || path.Equals("/Authentication/OAuth/CheckStatus", StringComparison.OrdinalIgnoreCase))
            // Sessão de dispositivo da própria TV (status/registrar/esquecer). Lista e revogação administrativas ficam fora.
            || path == "/api/tv-portal/device" && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsPost(context.Request.Method)
                || HttpMethods.IsDelete(context.Request.Method));
        var logout = path.Equals("/Authentication/OAuth/Logout", StringComparison.OrdinalIgnoreCase);
        var spa = HttpMethods.IsGet(context.Request.Method) && !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && (path == "/" || path == "/tv/iniciar" || path == "/gestao-a-vista" || path == "/login");
        if (allowed || logout || spa) { await next(context); return; }
        if (HttpMethods.IsGet(context.Request.Method) && !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        { context.Response.Redirect("/tv/iniciar"); return; }
        await Deny(context, 403);
    }
    private static Task Deny(HttpContext c, int status) { c.Response.StatusCode = status; return c.Response.WriteAsJsonAsync(new { message = "Acesso TV não autorizado." }); }
}
