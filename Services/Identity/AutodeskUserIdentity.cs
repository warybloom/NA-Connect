using System.Text.Json;

namespace APS.WebApp.Services.Identity;

/// <summary>Usuário Autodesk autenticado na sessão. AutodeskUserId (= "sub" do OIDC) é a identidade externa estável.</summary>
public sealed record AutodeskUserInfo(string AutodeskUserId, string Email, string Nome);

/// <summary>UserInfo respondeu com erro HTTP (401/403 = token sem o scope user-profile:read ou expirado).</summary>
public sealed class AutodeskUserInfoException(int statusCode)
    : Exception($"UserInfo Autodesk respondeu HTTP {statusCode}.")
{
    public int StatusCode { get; } = statusCode;

    /// <summary>401/403: o token atual não permite ler o usuário — resolve com um novo login.</summary>
    public bool ExigeNovoLogin => StatusCode is 401 or 403;

    public const string MensagemNovoLogin =
        "É necessário sair e entrar UMA vez para obter token com o novo scope (user-profile:read).";
}

public static class AutodeskUserInfoParser
{
    /// <summary>Endpoint UserInfo atual da Autodesk Authentication API (OIDC).</summary>
    public const string Endpoint = "https://api.userprofile.autodesk.com/userinfo";

    /// <summary>
    /// Campos OIDC: sub → AutodeskUserId, email → Email, name → Nome. Campo ausente fica vazio (não é inventado);
    /// se "name" não vier, usa os campos OIDC given_name/family_name quando presentes.
    /// </summary>
    public static AutodeskUserInfo Parse(JsonElement p)
    {
        string Texto(string nome) =>
            p.ValueKind == JsonValueKind.Object && p.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        var nome = Texto("name");
        if (nome.Length == 0) nome = $"{Texto("given_name")} {Texto("family_name")}".Trim();
        return new AutodeskUserInfo(Texto("sub"), Texto("email"), nome);
    }
}

/// <summary>
/// FONTE ÚNICA da identidade do usuário logado (nome no Layout, permissões, auditoria e, futuramente, refresh de token).
/// Nenhum controller deve chamar a Autodesk para saber quem é o usuário: use este serviço.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>null = sem sessão. Lança AutodeskUserInfoException se a Autodesk recusar (ex.: token sem o scope).</summary>
    Task<AutodeskUserInfo?> GetAsync(CancellationToken ct);
}

public sealed class SessionCurrentUserAccessor(IHttpContextAccessor http, AutodeskApiService autodesk, ILogger<SessionCurrentUserAccessor> logger)
    : ICurrentUserAccessor
{
    private const string KId = "Auth:AutodeskUserId", KEmail = "Auth:Email", KNome = "Auth:Nome", KToken = "Auth:TokenHash";
    private const string KFalhaToken = "Auth:FalhaTokenHash", KFalhaStatus = "Auth:FalhaStatus";

    public async Task<AutodeskUserInfo?> GetAsync(CancellationToken ct)
    {
        var session = http.HttpContext?.Session;
        var token = session?.GetString("AccessToken");
        if (session is null || string.IsNullOrWhiteSpace(token)) return null;
        var hash = Hash(token);

        // Cache amarrado ao token: novo login (outro usuário/novo scope) invalida a identidade guardada.
        if (session.GetString(KToken) == hash && (session.GetString(KId) is { Length: > 0 } || session.GetString(KEmail) is { Length: > 0 }))
            return new AutodeskUserInfo(session.GetString(KId) ?? "", session.GetString(KEmail) ?? "", session.GetString(KNome) ?? "");

        // Recusa definitiva para ESTE token (401/403): não repete a chamada a cada requisição — só um novo login resolve.
        if (session.GetString(KFalhaToken) == hash && session.GetInt32(KFalhaStatus) is int status)
            throw new AutodeskUserInfoException(status);

        AutodeskUserInfo info;
        try
        {
            info = await autodesk.GetUserInfoAsync(token, ct);
        }
        catch (AutodeskUserInfoException ex)
        {
            logger.LogWarning("UserInfo Autodesk recusado. Status={Status}{Dica}", ex.StatusCode,
                ex.ExigeNovoLogin ? " — token sem user-profile:read ou expirado: novo login necessário." : "");
            if (ex.ExigeNovoLogin) { session.SetString(KFalhaToken, hash); session.SetInt32(KFalhaStatus, ex.StatusCode); }
            throw;
        }
        if (info.AutodeskUserId.Length == 0 && info.Email.Length == 0) return null;
        session.SetString(KId, info.AutodeskUserId); session.SetString(KEmail, info.Email); session.SetString(KNome, info.Nome);
        session.SetString(KToken, hash);
        session.Remove(KFalhaToken); session.Remove(KFalhaStatus);
        return info;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))[..16];
}
