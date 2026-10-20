using APS.WebApp.Services.Identity;

namespace APS.WebApp.Services.Tv.Portal;

/// <summary>
/// Roda depois de Session e ANTES de <see cref="TvPortalMiddleware"/>. Só atua em três casos:
/// (1) sessão marcada como de dispositivo TV → revalida cookie + perfil restrito/tv.view a CADA request;
/// (2) sem sessão, com cookie de dispositivo, em GET de leitura do portal → restaura a sessão (após restart/idle);
/// (3) Logout com cookie → revoga o dispositivo. Login comum (N&A ou Autodesk) passa intocado.
/// A sessão restaurada é exatamente a do usuário TV: a barreira do portal continua decidindo cada API.
/// </summary>
public sealed class TvDeviceSessionMiddleware(RequestDelegate next, ILogger<TvDeviceSessionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, TvDeviceSessionService devices, TvPortalIdentityService identities)
    {
        if (!devices.Enabled) { await next(context); return; }
        var path = context.Request.Path.Value ?? "/";
        var token = context.Request.Cookies[TvDeviceSessionService.CookieName];
        var ct = context.RequestAborted;

        if (path.Equals("/Authentication/OAuth/Logout", StringComparison.OrdinalIgnoreCase))
        {
            if (token is not null)
            {
                try { if (await devices.Store.FindAsync(token, ct) is { } r) await devices.RevokeAsync(r.Id, "logout", ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Falha ao revogar dispositivo TV no logout."); }
                devices.DeleteCookie(context.Response);
            }
            await next(context);
            return;
        }

        int? userId;
        try { userId = context.Session.GetInt32(NaSessionKeys.UserId); } catch (ArgumentException) { userId = null; }
        var deviceId = context.Session.GetString(TvDeviceSessionService.SessionDeviceKey);
        // Registro recém-feito só vira sessão de dispositivo quando o navegador comprovadamente devolve o cookie Secure.
        // Se recusar (http fora de localhost), a sessão continua a comum de antes, sem 401 surpresa.
        var promote = false;
        if (userId is not null && deviceId is null)
        {
            var pending = context.Session.GetString(TvDeviceSessionService.SessionPendingKey);
            if (pending is null || token is null) { await next(context); return; }
            deviceId = pending;
            promote = true;
        }
        if (userId is null && (token is null || !Restorable(context.Request) || !string.IsNullOrEmpty(context.Session.GetString("AccessToken"))))
        { await next(context); return; }

        var validation = await devices.ValidateAsync(token, ct);
        if (validation.Check == TvDeviceCheck.Unavailable) { await Deny(context, 503); return; }
        if (validation is not { Check: TvDeviceCheck.Valid, Record: { } record, User: { } user }
            || deviceId is not null && (record.Id != deviceId || record.UserId != userId))
        {
            devices.DeleteCookie(context.Response);
            if (deviceId is null) { await next(context); return; } // igual a não ter cookie: o portal responde 401
            context.Session.Clear();
            await Deny(context, 401);
            return;
        }

        if (deviceId is null)
        {
            context.Session.Clear();
            context.Session.SetInt32(NaSessionKeys.UserId, record.UserId);
            context.Session.SetString(NaSessionKeys.Email, user.Email);
            context.Session.SetString(NaSessionKeys.Nome, user.DisplayName ?? user.Email);
            context.Session.SetString(NaSessionKeys.Provider, APS.WebApp.Services.LocalAuth.NaLocalAuthPolicy.Provider);
            context.Session.SetString(TvDeviceSessionService.SessionDeviceKey, record.Id);
            logger.LogInformation("Sessão TV restaurada pelo dispositivo {DeviceId} (UserId={UserId}).", record.Id, record.UserId);
        }
        else if (promote)
        {
            context.Session.SetString(TvDeviceSessionService.SessionDeviceKey, record.Id);
            context.Session.Remove(TvDeviceSessionService.SessionPendingKey);
        }

        TvPortalIdentity? identity;
        try { identity = await identities.ReadAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { context.Session.Clear(); await Deny(context, 503); return; }
        // Sem elevação: se a conta deixou de ser TV restrita (virou admin/comum) ou perdeu tv.view, o dispositivo morre.
        if (identity is null || identity.UserId != record.UserId || !identity.Restricted || !identity.CanView)
        {
            context.Session.Clear();
            try { await devices.RevokeAsync(record.Id, "perfil-nao-tv-restrito", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Falha ao revogar dispositivo TV {DeviceId}.", record.Id); }
            devices.DeleteCookie(context.Response);
            await Deny(context, 401);
            return;
        }

        try
        {
            if (await devices.Store.RenewIfDueAsync(record, devices.Options, ct) is { } renewed)
                context.Response.Cookies.Append(TvDeviceSessionService.CookieName, token!, devices.Cookie(renewed.ExpiresAt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Falha ao renovar dispositivo TV {DeviceId}.", record.Id); }
        await next(context);
    }

    /// <summary>Só leituras do próprio portal restauram a sessão. Telas/APIs administrativas nunca.</summary>
    public static bool Restorable(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var path = request.Path.Value ?? "";
        return path is "/api/tv-portal/session" or "/api/tv-portal/presentations" or "/api/tv-portal/device"
            || path.StartsWith("/api/tv-portal/presentations/", StringComparison.Ordinal)
            || path.StartsWith("/api/tv-portal/data/", StringComparison.Ordinal);
    }

    private static Task Deny(HttpContext c, int status)
    {
        c.Response.StatusCode = status;
        c.Response.Headers.CacheControl = "no-store";
        return c.Response.WriteAsJsonAsync(new { message = status == 503 ? "Validação do dispositivo TV indisponível." : "Sessão do dispositivo TV encerrada. Entre novamente." });
    }
}
