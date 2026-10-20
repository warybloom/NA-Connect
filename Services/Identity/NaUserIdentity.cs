using Microsoft.AspNetCore.Http;

namespace APS.WebApp.Services.Identity;

/// <summary>Identidade interna do usuário autenticado no N&A Connect.</summary>
public sealed record NaCurrentUser(int UserId, string Email, string Nome, string AuthenticationProvider);

/// <summary>Chaves da identidade N&A, separadas do token e da identidade Autodesk legados.</summary>
public static class NaSessionKeys
{
    public const string UserId = "Auth:UserId";
    public const string Email = "Auth:Email";
    public const string Nome = "Auth:Nome";
    public const string Provider = "Auth:Provider";
}

public interface INaCurrentUserAccessor
{
    Task<NaCurrentUser?> GetAsync(CancellationToken ct = default);
}

/// <summary>Lê somente a identidade interna já materializada na sessão; nunca chama Autodesk.</summary>
public sealed class SessionNaCurrentUserAccessor(IHttpContextAccessor http) : INaCurrentUserAccessor
{
    public Task<NaCurrentUser?> GetAsync(CancellationToken ct = default)
    {
        var session = http.HttpContext?.Session;
        int? userId;
        try
        {
            userId = session?.GetInt32(NaSessionKeys.UserId);
        }
        catch (ArgumentException)
        {
            userId = null;
        }
        if (userId is not > 0) return Task.FromResult<NaCurrentUser?>(null);

        var email = session?.GetString(NaSessionKeys.Email) ?? "";
        var nome = session?.GetString(NaSessionKeys.Nome) ?? "";
        var provider = session?.GetString(NaSessionKeys.Provider) ?? "";
        return Task.FromResult<NaCurrentUser?>(new NaCurrentUser(userId.Value, email, nome, provider));
    }
}
