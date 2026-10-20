using APS.WebApp.Services.Identity;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record TvDeviceRegisterRequest(string? Label);

/// <summary>
/// Sessão de dispositivo da conta TV restrita. Registro só pela própria TV logada (perfil restrito + tv.view + senha local
/// sem troca pendente + ao menos uma apresentação ativa atribuída). Lista/revogação: quem configura TV (não restrito).
/// </summary>
[ApiController, Route("api/tv-portal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[PermissionExempt("Portal TV: autorização estrita por TvPortalIdentityService em cada action (perfis reais, mesmo com Enforce=false).")]
public sealed class TvDeviceSessionController(TvDeviceSessionService devices, TvPortalIdentityService identities,
    INaLocalCredentialRepository credentials, IPermissionRepository repository, TvConfigurationStore configurations,
    TvPortalAssignmentStore assignments) : ControllerBase
{
    private bool CrossSite => Request.Headers["Sec-Fetch-Site"].ToString() is "cross-site" or "same-site";

    [HttpGet("device")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        var available = devices.Enabled && identity.Restricted && identity.CanView;
        var id = HttpContext.Session.GetString(TvDeviceSessionService.SessionDeviceKey);
        var record = available && id is not null ? (await devices.Store.ReadAsync(ct)).Devices.FirstOrDefault(d => d.Id == id) : null;
        // Ainda pendente aqui = este GET chegou sem o cookie: o navegador recusou o cookie Secure (acesso sem HTTPS).
        var cookieRejected = available && HttpContext.Session.GetString(TvDeviceSessionService.SessionPendingKey) is not null;
        return Ok(new { available, registered = record is not null, cookieRejected, expiresAt = record?.ExpiresAt, idleDays = devices.Options.IdleDays });
    }

    [HttpPost("device")]
    public async Task<IActionResult> Register([FromBody] TvDeviceRegisterRequest? request, CancellationToken ct)
    {
        if (!devices.Enabled) return NotFound();
        if (CrossSite) return StatusCode(403);
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.Restricted || !identity.CanView)
            return StatusCode(403, new { message = "Somente a conta TV restrita pode manter uma TV conectada." });
        if (HttpContext.Session.GetString(NaLocalAuthPolicy.MustChangePasswordSessionKey) == "true"
            || HttpContext.Session.GetString(NaSessionKeys.Provider) != NaLocalAuthPolicy.Provider)
            return StatusCode(403, new { message = "Entre com o e-mail e a senha N&A da conta TV." });
        var user = await repository.ObterUsuarioAsync(identity.UserId, ct);
        var credential = await credentials.ObterCredencialAsync(identity.UserId, ct);
        if (user is not { Ativo: true } || credential is null || credential.MustChangePassword)
            return StatusCode(403, new { message = "Conta TV sem senha local válida." });
        var config = await configurations.ReadAsync(ct);
        var grants = await assignments.ReadAsync(ct);
        if (!config.Presentations.Any(p => p.EstaAtiva && TvPortalPolicy.Assigned(grants, p.Id, identity.UserId)))
            return StatusCode(403, new { message = "Nenhuma apresentação ativa atribuída a esta conta." });

        // Re-registro na mesma TV substitui o anterior (rotação do token).
        if (Request.Cookies[TvDeviceSessionService.CookieName] is { } old && await devices.Store.FindAsync(old, ct) is { } previous)
            await devices.RevokeAsync(previous.Id, "substituido", ct);
        var label = string.Concat((request?.Label ?? "").Trim().Where(c => !char.IsControl(c)).Take(60));
        var (record, token) = await devices.Store.CreateAsync(identity.UserId, credential.PasswordUpdatedAt.Ticks,
            label.Length > 0 ? label : "TV", devices.Options, ct);
        Response.Cookies.Append(TvDeviceSessionService.CookieName, token, devices.Cookie(record.ExpiresAt));
        // Pendente até o navegador devolver o cookie (promoção no middleware); ver Status.cookieRejected.
        HttpContext.Session.Remove(TvDeviceSessionService.SessionDeviceKey);
        HttpContext.Session.SetString(TvDeviceSessionService.SessionPendingKey, record.Id);
        return Ok(new { registered = true, expiresAt = record.ExpiresAt });
    }

    [HttpDelete("device")]
    public async Task<IActionResult> Forget(CancellationToken ct)
    {
        if (CrossSite) return StatusCode(403);
        if (await identities.ReadAsync(ct) is null) return Unauthorized();
        if (Request.Cookies[TvDeviceSessionService.CookieName] is { } token && await devices.Store.FindAsync(token, ct) is { } record)
            await devices.RevokeAsync(record.Id, "esquecido-na-tv", ct);
        devices.DeleteCookie(Response);
        HttpContext.Session.Remove(TvDeviceSessionService.SessionDeviceKey);
        HttpContext.Session.Remove(TvDeviceSessionService.SessionPendingKey);
        return NoContent();
    }

    [HttpGet("devices")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.CanConfigure) return StatusCode(403);
        var now = DateTimeOffset.UtcNow;
        var names = new Dictionary<int, string>();
        var result = new List<object>();
        foreach (var d in (await devices.Store.ReadAsync(ct)).Devices.OrderByDescending(d => d.LastSeenAt))
        {
            if (!names.TryGetValue(d.UserId, out var name))
                names[d.UserId] = name = (await repository.ObterUsuarioAsync(d.UserId, ct)) is { } u ? u.DisplayName ?? u.Email : $"#{d.UserId}";
            result.Add(new { d.Id, d.UserId, userName = name, d.Label, d.CreatedAt, d.LastSeenAt, d.ExpiresAt, d.RevokedAt, d.RevokedReason,
                active = d.Active(now, devices.Options) });
        }
        return Ok(new { enabled = devices.Enabled, devices = result });
    }

    [HttpDelete("devices/{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        if (CrossSite) return StatusCode(403);
        var identity = await identities.ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.CanConfigure) return StatusCode(403);
        return await devices.RevokeAsync(id, $"revogado-admin:{identity.UserId}", ct) ? NoContent() : NotFound();
    }
}
