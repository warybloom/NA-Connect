using APS.WebApp.Services.Identity;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Areas.Authentication.Controllers;

public sealed record NaLoginRequest(string? Email, string? Password);

public sealed record NaLoginResponse(int UserId, string Email, string Nome, string Provider, bool MustChangePassword);

/// <summary>Troca obrigatória da própria senha. Sem userId: o usuário vem da sessão.</summary>
public sealed record NaChangePasswordRequest(string? NewPassword, string? ConfirmPassword);

/// <summary>
/// Login N&A local (e-mail + senha), independente da Autodesk. Termina na mesma sessão N&A do login Autodesk
/// (Auth:UserId/Email/Nome/Provider); não grava nenhum token nem chave Autodesk.
/// </summary>
[Area("Authentication")]
[Route("[area]/Na/[action]")]
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[PermissionExempt("Login N&A local: ocorre antes de existir identidade.")]
public sealed class NaAuthController(INaLocalAuthService auth, ILogger<NaAuthController> logger) : ControllerBase
{
    /// <summary>Mesma mensagem para e-mail inexistente, senha errada, usuário inativo, sem senha local ou bloqueado.</summary>
    public const string MensagemGenerica =
        "E-mail ou senha inválidos. Após várias tentativas o acesso fica temporariamente bloqueado.";

    [HttpPost]
    public async Task<IActionResult> Login([FromBody] NaLoginRequest? req, CancellationToken ct)
    {
        var resultado = await auth.AuthenticateAsync(req?.Email, req?.Password, ct);
        switch (resultado.Status)
        {
            case NaLocalAuthStatus.Success when resultado.User is { } usuario:
                // Nova identidade: descarta qualquer estado anterior da sessão (outro usuário, tokens Autodesk antigos).
                HttpContext.Session.Clear();
                HttpContext.Session.SetInt32(NaSessionKeys.UserId, usuario.UserId);
                HttpContext.Session.SetString(NaSessionKeys.Email, usuario.Email);
                HttpContext.Session.SetString(NaSessionKeys.Nome, usuario.Nome);
                HttpContext.Session.SetString(NaSessionKeys.Provider, usuario.AuthenticationProvider);
                if (resultado.MustChangePassword)
                    HttpContext.Session.SetString(NaLocalAuthPolicy.MustChangePasswordSessionKey, "true");
                return Ok(new NaLoginResponse(usuario.UserId, usuario.Email, usuario.Nome, usuario.AuthenticationProvider,
                    resultado.MustChangePassword));

            case NaLocalAuthStatus.Unavailable:
                return StatusCode(503, new { message = "Login indisponível no momento." });

            default:
                logger.LogInformation("Login N&A local recusado: {Motivo}", resultado.MotivoInterno);
                return Unauthorized(new { message = MensagemGenerica });
        }
    }

    /// <summary>
    /// POST /Authentication/Na/ChangePassword — primeiro acesso: troca obrigatória da PRÓPRIA senha.
    /// O usuário é sempre o da sessão (Auth:UserId); não exige permissions.manage. Sucesso mantém a sessão e libera a entrada.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ChangePassword([FromBody] NaChangePasswordRequest? req, CancellationToken ct)
    {
        int? userId;
        try { userId = HttpContext.Session.GetInt32(NaSessionKeys.UserId); }
        catch (ArgumentException) { userId = null; }
        if (userId is not > 0) return Unauthorized(new { message = "Sessão expirada. Entre novamente." });

        if (req?.NewPassword != req?.ConfirmPassword)
            return BadRequest(new { message = "A confirmação não confere com a nova senha." });

        var resultado = await auth.ChangeOwnPasswordAsync(userId.Value, req?.NewPassword, ct);
        switch (resultado.Status)
        {
            case NaSetPasswordStatus.Success:
                HttpContext.Session.Remove(NaLocalAuthPolicy.MustChangePasswordSessionKey);
                return NoContent();
            case NaSetPasswordStatus.InvalidPassword:
                return BadRequest(new { message = resultado.Message });
            case NaSetPasswordStatus.NotRequired:
                HttpContext.Session.Remove(NaLocalAuthPolicy.MustChangePasswordSessionKey);
                return Conflict(new { message = resultado.Message });
            case NaSetPasswordStatus.UserNotFound:
                return Unauthorized(new { message = "Sessão expirada. Entre novamente." });
            default:
                return StatusCode(503, new { message = "Não foi possível alterar a senha agora. Tente novamente." });
        }
    }
}
