using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Identity;

namespace APS.WebApp.Services.LocalAuth;

/// <summary>
/// Login N&A local por e-mail + senha. Hash via PasswordHasher&lt;T&gt; do ASP.NET Core Identity (PBKDF2 com salt
/// embutido no próprio hash). Não cria usuário, não concede projeto, não toca na integração Autodesk.
/// </summary>
public sealed class NaLocalAuthService(
    INaLocalCredentialRepository repository,
    ILogger<NaLocalAuthService> logger,
    IPasswordHasher<PermUser>? passwordHasher = null,
    TimeProvider? timeProvider = null) : INaLocalAuthService
{
    private readonly IPasswordHasher<PermUser> _hasher = passwordHasher ?? new PasswordHasher<PermUser>();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Hash de uma senha aleatória: verificado quando o usuário/credencial não existe, para o tempo de resposta
    // não revelar se o e-mail está cadastrado.
    private static readonly Lazy<string> HashFicticio = new(() =>
        new PasswordHasher<PermUser>().HashPassword(null!, Guid.NewGuid().ToString("N")));

    public async Task<NaLocalAuthResult> AuthenticateAsync(string? email, string? password, CancellationToken ct)
    {
        var normalizado = NaLocalAuthPolicy.NormalizarEmail(email);
        if (normalizado.Length is 0 or > NaLocalAuthPolicy.MaxEmailLength || string.IsNullOrEmpty(password)
            || password.Length > NaLocalAuthPolicy.MaxPasswordLength)
            return Invalido("entrada inválida");

        try
        {
            var usuario = await repository.ObterUsuarioPorEmailAsync(normalizado, ct);
            if (usuario is null) { VerificarFicticio(password); return Invalido("e-mail inexistente"); }
            if (!usuario.Ativo) { VerificarFicticio(password); return Invalido($"usuário inativo ({usuario.UserId})"); }

            var credencial = await repository.ObterCredencialAsync(usuario.UserId, ct);
            if (credencial is null) { VerificarFicticio(password); return Invalido($"sem credencial local ({usuario.UserId})"); }

            var agora = _clock.GetUtcNow().UtcDateTime;
            if (credencial.LockoutUntil is { } ate && ate > agora)
                return Invalido($"bloqueado até {ate:o} ({usuario.UserId})");

            var verificacao = _hasher.VerifyHashedPassword(usuario, credencial.PasswordHash, password);
            if (verificacao == PasswordVerificationResult.Failed)
            {
                var (falhas, bloqueio) = await repository.RegistrarFalhaAsync(usuario.UserId, agora, ct);
                logger.LogWarning("Login N&A local recusado. UserId={UserId} Falhas={Falhas} Bloqueado={Bloqueado}",
                    usuario.UserId, falhas, bloqueio is { } b && b > agora);
                return Invalido($"senha incorreta ({usuario.UserId})");
            }

            await repository.RegistrarSucessoAsync(usuario.UserId, ct);
            if (verificacao == PasswordVerificationResult.SuccessRehashNeeded)
                await repository.DefinirSenhaAsync(usuario.UserId, _hasher.HashPassword(usuario, password),
                    credencial.MustChangePassword, agora, ct);

            var nome = string.IsNullOrWhiteSpace(usuario.DisplayName) ? usuario.Email : usuario.DisplayName;
            logger.LogInformation("Login N&A local. UserId={UserId}", usuario.UserId);
            return new NaLocalAuthResult(NaLocalAuthStatus.Success,
                new NaCurrentUser(usuario.UserId, usuario.Email, nome, NaLocalAuthPolicy.Provider), credencial.MustChangePassword);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Login N&A local indisponível. Tipo={Tipo}", ex.GetType().Name);
            return new NaLocalAuthResult(NaLocalAuthStatus.Unavailable, null, false) { MotivoInterno = ex.GetType().Name };
        }
    }

    public async Task<NaSetPasswordResult> SetPasswordAsync(int userId, string? password, bool mustChangePassword, CancellationToken ct)
    {
        if (NaLocalAuthPolicy.ValidarSenha(password) is { } erro)
            return new NaSetPasswordResult(NaSetPasswordStatus.InvalidPassword, erro);
        try
        {
            var usuario = await repository.ObterUsuarioAsync(userId, ct);
            if (usuario is null) return new NaSetPasswordResult(NaSetPasswordStatus.UserNotFound, "Usuário inexistente.");
            await repository.DefinirSenhaAsync(userId, _hasher.HashPassword(usuario, password!), mustChangePassword,
                _clock.GetUtcNow().UtcDateTime, ct);
            return new NaSetPasswordResult(NaSetPasswordStatus.Success);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Definição de senha N&A indisponível. Tipo={Tipo}", ex.GetType().Name);
            return new NaSetPasswordResult(NaSetPasswordStatus.Unavailable, "Operação indisponível.");
        }
    }

    public async Task<NaSetPasswordResult> ChangeOwnPasswordAsync(int sessionUserId, string? newPassword, CancellationToken ct)
    {
        if (NaLocalAuthPolicy.ValidarSenha(newPassword) is { } erro)
            return new NaSetPasswordResult(NaSetPasswordStatus.InvalidPassword, erro);
        try
        {
            var usuario = await repository.ObterUsuarioAsync(sessionUserId, ct);
            if (usuario is not { Ativo: true }) return new NaSetPasswordResult(NaSetPasswordStatus.UserNotFound, "Sessão inválida.");
            var credencial = await repository.ObterCredencialAsync(sessionUserId, ct);
            if (credencial is not { MustChangePassword: true })
                return new NaSetPasswordResult(NaSetPasswordStatus.NotRequired, "Não há troca de senha pendente para este usuário.");
            if (_hasher.VerifyHashedPassword(usuario, credencial.PasswordHash, newPassword!) != PasswordVerificationResult.Failed)
                return new NaSetPasswordResult(NaSetPasswordStatus.InvalidPassword, "A nova senha deve ser diferente da senha atual.");

            // DefinirSenhaAsync: novo hash, PasswordUpdatedAt = agora (UTC), MustChangePassword = false, falhas/bloqueio zerados.
            await repository.DefinirSenhaAsync(sessionUserId, _hasher.HashPassword(usuario, newPassword!), false,
                _clock.GetUtcNow().UtcDateTime, ct);
            logger.LogInformation("Senha N&A trocada no primeiro acesso. UserId={UserId}", sessionUserId);
            return new NaSetPasswordResult(NaSetPasswordStatus.Success);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Troca de senha N&A indisponível. Tipo={Tipo}", ex.GetType().Name);
            return new NaSetPasswordResult(NaSetPasswordStatus.Unavailable, "Operação indisponível.");
        }
    }

    private void VerificarFicticio(string password) => _hasher.VerifyHashedPassword(null!, HashFicticio.Value, password);

    private static NaLocalAuthResult Invalido(string motivo) =>
        new(NaLocalAuthStatus.InvalidCredentials, null, false) { MotivoInterno = motivo };
}
