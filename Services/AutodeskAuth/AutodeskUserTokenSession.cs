using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace APS.WebApp.Services.AutodeskAuth;

/// <summary>
/// Estado da INTEGRAÇÃO Autodesk (token 3-legged do usuário) na sessão. Não é a identidade N&A
/// (Auth:UserId/Email/Nome/Provider — ver NaSessionKeys) e não é o token 2-legged do TokenManager.
/// </summary>
public static class AutodeskSessionKeys
{
    public const string Connected = "Autodesk:Connected";
    public const string AccessToken = "Autodesk:AccessToken";
    public const string RefreshToken = "Autodesk:RefreshToken";
    /// <summary>UTC, formato round-trip ("o"). É o instante real informado pela Autodesk (agora + expires_in), sem margem.</summary>
    public const string ExpiresAt = "Autodesk:ExpiresAt";
    public const string UserId = "Autodesk:UserId";

    // Chaves legadas mantidas em dual-write até a migração dos consumidores (ETAPA 07B/08).
    public const string LegacyAccessToken = "AccessToken";
    public const string LegacyRefreshToken = "RefreshToken";
    public const string LegacyAutodeskUserId = "Auth:AutodeskUserId";
}

/// <summary>Parâmetros centralizados do token 3-legged do usuário.</summary>
public static class AutodeskUserTokenPolicy
{
    /// <summary>Endpoint OAuth v2 já usado pelo callback (authorization_code) e pelo TokenManager (client_credentials).</summary>
    public const string TokenEndpoint = "https://developer.api.autodesk.com/authentication/v2/token";

    /// <summary>
    /// HttpClient nomeado do POST grant_type=refresh_token: SEM AutodeskRetryHandler nem qualquer DelegatingHandler.
    /// Repetir o POST automaticamente reutilizaria um refresh_token já rotacionado/consumido.
    /// </summary>
    public const string RefreshHttpClientName = "AutodeskUserTokenRefresh";

    /// <summary>Token com menos que isto de validade é renovado antes do uso (mesma folga do 2-legged).</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Quanto tempo o resultado de um refresh fica disponível para outras requisições da MESMA sessão que ainda
    /// carregam o refresh_token já consumido (snapshot de sessão anterior ao commit). Uma vida de access token.
    /// </summary>
    public static readonly TimeSpan RefreshResultRetention = TimeSpan.FromMinutes(60);
}

/// <summary>Conjunto de tokens 3-legged válido para gravação na sessão.</summary>
public sealed record AutodeskUserTokenSet(string AccessToken, string RefreshToken, DateTimeOffset? ExpiresAt);

/// <summary>Leitura/gravação do estado Autodesk na sessão, com dual-write das chaves legadas.</summary>
public static class AutodeskUserTokenSession
{
    internal sealed record State(bool ExplicitlyDisconnected, string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

    /// <summary>ExpiresAt real = agora + expires_in. Sem expires_in válido, a expiração fica desconhecida (null).</summary>
    public static DateTimeOffset? ExpiresAtFrom(DateTimeOffset nowUtc, long? expiresInSeconds) =>
        expiresInSeconds is > 0 ? nowUtc.ToUniversalTime().AddSeconds(expiresInSeconds.Value) : null;

    /// <summary>
    /// Grava um token recém-obtido (authorization_code ou refresh_token) nas chaves Autodesk:* e nas legadas.
    /// refresh_token vazio NÃO sobrescreve o anterior (a Autodesk pode não rotacionar).
    /// </summary>
    public static void Save(ISession session, string accessToken, string? refreshToken, DateTimeOffset? expiresAt)
    {
        session.SetString(AutodeskSessionKeys.AccessToken, accessToken);
        session.SetString(AutodeskSessionKeys.LegacyAccessToken, accessToken);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            session.SetString(AutodeskSessionKeys.RefreshToken, refreshToken);
            session.SetString(AutodeskSessionKeys.LegacyRefreshToken, refreshToken);
        }
        if (expiresAt is { } exp)
            session.SetString(AutodeskSessionKeys.ExpiresAt, exp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        else
            session.Remove(AutodeskSessionKeys.ExpiresAt);
        session.SetString(AutodeskSessionKeys.Connected, "true");
    }

    /// <summary>
    /// Desconecta SOMENTE a integração Autodesk: tokens Autodesk:* e o RefreshToken legado (já inutilizável) são removidos.
    /// Nunca toca em Auth:UserId/Email/Nome/Provider nem chama Session.Clear(). O AccessToken legado é mantido para
    /// não alterar o comportamento dos consumidores ainda não migrados (é o mesmo estado de um token vencido hoje);
    /// o provider ignora-o porque Autodesk:Connected=false tem precedência.
    /// </summary>
    public static void MarkDisconnected(ISession session)
    {
        session.SetString(AutodeskSessionKeys.Connected, "false");
        session.Remove(AutodeskSessionKeys.AccessToken);
        session.Remove(AutodeskSessionKeys.RefreshToken);
        session.Remove(AutodeskSessionKeys.ExpiresAt);
        session.Remove(AutodeskSessionKeys.LegacyRefreshToken);
    }

    /// <summary>
    /// Lê o estado atual. Sessões anteriores à 07A (sem Autodesk:Connected) caem nas chaves legadas.
    /// </summary>
    internal static State Read(ISession session)
    {
        if (session.GetString(AutodeskSessionKeys.Connected) == "false")
            return new State(true, null, null, null);

        var access = NonEmpty(session.GetString(AutodeskSessionKeys.AccessToken))
                     ?? NonEmpty(session.GetString(AutodeskSessionKeys.LegacyAccessToken));
        var refresh = NonEmpty(session.GetString(AutodeskSessionKeys.RefreshToken))
                      ?? NonEmpty(session.GetString(AutodeskSessionKeys.LegacyRefreshToken));
        DateTimeOffset? expiresAt = DateTimeOffset.TryParse(session.GetString(AutodeskSessionKeys.ExpiresAt),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exp) ? exp : null;
        return new State(false, access, refresh, expiresAt);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
