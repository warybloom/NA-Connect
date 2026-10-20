using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;

namespace APS.WebApp.Services.LocalAuth;

/// <summary>Parâmetros centralizados do login N&A local (email + senha).</summary>
public static class NaLocalAuthPolicy
{
    /// <summary>Valor gravado em Auth:Provider por um login local.</summary>
    public const string Provider = "N&A";

    /// <summary>
    /// "true" enquanto a troca obrigatória de senha (primeiro acesso) não foi concluída. Auth:UserId permanece na sessão,
    /// mas CheckStatus não considera a sessão autenticada até a troca terminar.
    /// </summary>
    public const string MustChangePasswordSessionKey = "Auth:MustChangePassword";

    /// <summary>Falhas consecutivas que bloqueiam a credencial.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Duração do bloqueio após <see cref="MaxFailedAttempts"/> falhas.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const int MinPasswordLength = 8;

    /// <summary>Teto de tamanho (evita custo de hash arbitrário em requisições anônimas).</summary>
    public const int MaxPasswordLength = 256;

    public const int MaxEmailLength = 256;

    public static string NormalizarEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Novo estado após uma senha incorreta. Bloqueio já vencido recomeça a contagem em 1; ao atingir o máximo,
    /// bloqueia por <see cref="LockoutDuration"/>. A versão SQL (SqlNaLocalCredentialRepository) aplica a mesma regra
    /// atomicamente.
    /// </summary>
    public static (int FailedAttempts, DateTime? LockoutUntil) AposFalha(int failedAttempts, DateTime? lockoutUntil, DateTime agoraUtc)
    {
        var bloqueioVencido = lockoutUntil is { } ate && ate <= agoraUtc;
        var falhas = bloqueioVencido ? 1 : failedAttempts + 1;
        DateTime? bloqueio = falhas >= MaxFailedAttempts ? agoraUtc + LockoutDuration : bloqueioVencido ? null : lockoutUntil;
        return (falhas, bloqueio);
    }

    private static readonly HashSet<string> Triviais = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "87654321", "password", "password1", "senha123", "senha1234",
        "qwertyui", "qwerty123", "abcdefgh", "abc12345", "11111111", "00000000", "naconnect", "na@connect",
    };

    /// <summary>Política mínima: 8+ caracteres, não só espaços, não um único caractere repetido, fora de uma lista curta de triviais.</summary>
    public static string? ValidarSenha(string? senha)
    {
        if (string.IsNullOrWhiteSpace(senha)) return "Informe a senha.";
        if (senha.Length < MinPasswordLength) return $"A senha deve ter pelo menos {MinPasswordLength} caracteres.";
        if (senha.Length > MaxPasswordLength) return $"A senha deve ter no máximo {MaxPasswordLength} caracteres.";
        if (senha.Distinct().Count() == 1 || Triviais.Contains(senha.Trim())) return "Senha muito simples. Escolha outra.";
        return null;
    }
}

/// <summary>Credencial local (auth.UserCredentials). PasswordHash nunca sai do backend.</summary>
public sealed record NaLocalCredential(int UserId, string PasswordHash, int FailedAttempts, DateTime? LockoutUntil,
    DateTime PasswordUpdatedAt, bool MustChangePassword);

public interface INaLocalCredentialRepository
{
    /// <summary>Usuário de perm.Users pelo e-mail (comparação case-insensitive do banco). Não cria usuário.</summary>
    Task<PermUser?> ObterUsuarioPorEmailAsync(string email, CancellationToken ct);
    Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct);
    Task<NaLocalCredential?> ObterCredencialAsync(int userId, CancellationToken ct);
    /// <summary>Incrementa falhas e aplica o bloqueio de forma atômica (regra de <see cref="NaLocalAuthPolicy.AposFalha"/>).</summary>
    Task<(int FailedAttempts, DateTime? LockoutUntil)> RegistrarFalhaAsync(int userId, DateTime agoraUtc, CancellationToken ct);
    /// <summary>Login correto: FailedAttempts = 0, LockoutUntil = NULL.</summary>
    Task RegistrarSucessoAsync(int userId, CancellationToken ct);
    /// <summary>Cria/substitui a credencial (zera falhas e bloqueio).</summary>
    Task DefinirSenhaAsync(int userId, string passwordHash, bool mustChangePassword, DateTime agoraUtc, CancellationToken ct);
}

public enum NaLocalAuthStatus
{
    Success,
    /// <summary>E-mail inexistente, usuário inativo, sem credencial, senha errada OU bloqueado — indistinguíveis externamente.</summary>
    InvalidCredentials,
    /// <summary>Banco/schema indisponível.</summary>
    Unavailable,
}

public sealed record NaLocalAuthResult(NaLocalAuthStatus Status, NaCurrentUser? User, bool MustChangePassword)
{
    /// <summary>Só para log/diagnóstico interno — nunca devolvido ao cliente.</summary>
    public string MotivoInterno { get; init; } = "";
}

public enum NaSetPasswordStatus { Success, UserNotFound, InvalidPassword, Unavailable, NotRequired }

public sealed record NaSetPasswordResult(NaSetPasswordStatus Status, string? Message = null);

public interface INaLocalAuthService
{
    Task<NaLocalAuthResult> AuthenticateAsync(string? email, string? password, CancellationToken ct);
    Task<NaSetPasswordResult> SetPasswordAsync(int userId, string? password, bool mustChangePassword, CancellationToken ct);

    /// <summary>
    /// Troca obrigatória da PRÓPRIA senha (primeiro acesso). userId vem sempre da sessão (Auth:UserId), nunca do cliente.
    /// Exige usuário ativo, credencial existente e MustChangePassword = true; grava o novo hash com MustChangePassword = false.
    /// </summary>
    Task<NaSetPasswordResult> ChangeOwnPasswordAsync(int sessionUserId, string? newPassword, CancellationToken ct);
}
