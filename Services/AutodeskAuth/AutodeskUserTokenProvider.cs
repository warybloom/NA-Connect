using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APS.WebApp.Models;
using Microsoft.AspNetCore.Http;

namespace APS.WebApp.Services.AutodeskAuth;

public enum AutodeskUserTokenStatus
{
    /// <summary>AccessToken pronto para uso.</summary>
    Valid,
    /// <summary>Sessão sem integração Autodesk (usuário N&A sem login Autodesk, ou sem sessão).</summary>
    NotConnected,
    /// <summary>Integração existia, mas não há como renovar (refresh ausente/rejeitado): exige novo login Autodesk.</summary>
    ReconnectRequired,
    /// <summary>Falha transitória ao renovar (rede/5xx). A integração continua conectada; tentar depois.</summary>
    Unavailable,
}

public sealed record AutodeskUserTokenResult(AutodeskUserTokenStatus Status, string? AccessToken, DateTimeOffset? ExpiresAt)
{
    public bool IsValid => Status == AutodeskUserTokenStatus.Valid && !string.IsNullOrEmpty(AccessToken);
    public bool RequiresReconnect => Status is AutodeskUserTokenStatus.NotConnected or AutodeskUserTokenStatus.ReconnectRequired;

    internal static AutodeskUserTokenResult Valid(string token, DateTimeOffset? expiresAt) => new(AutodeskUserTokenStatus.Valid, token, expiresAt);
    internal static readonly AutodeskUserTokenResult NotConnected = new(AutodeskUserTokenStatus.NotConnected, null, null);
    internal static readonly AutodeskUserTokenResult ReconnectRequired = new(AutodeskUserTokenStatus.ReconnectRequired, null, null);
    internal static readonly AutodeskUserTokenResult Unavailable = new(AutodeskUserTokenStatus.Unavailable, null, null);
}

/// <summary>
/// Token Autodesk 3-legged do USUÁRIO da sessão atual. Não é autenticação N&A, não é CurrentUser e não é o
/// TokenManager (2-legged, client_credentials). Nenhum resultado deste provider encerra a sessão N&A.
/// </summary>
public interface IAutodeskUserTokenProvider
{
    /// <summary>Token válido sem HTTP quando possível; renova (refresh_token) se ausente, vencido ou dentro da margem.</summary>
    Task<AutodeskUserTokenResult> GetValidAccessTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Renovação forçada para o fluxo futuro "401 → refresh uma vez → retry uma vez" (ETAPA 07B).
    /// Informe o token que a Autodesk recusou: se outra requisição já o substituiu, o token atual é devolvido sem
    /// nova chamada HTTP. Com <paramref name="rejectedAccessToken"/> null, renova incondicionalmente.
    /// </summary>
    Task<AutodeskUserTokenResult> ForceRefreshAsync(string? rejectedAccessToken, CancellationToken ct = default);
}

public sealed class AutodeskUserTokenProvider(
    IHttpContextAccessor http,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    AutodeskUserTokenRefreshCoordinator coordinator,
    ILogger<AutodeskUserTokenProvider> logger,
    TimeProvider? timeProvider = null) : IAutodeskUserTokenProvider
{
    // Cache de identidade do SessionCurrentUserAccessor (amarrado ao hash do AccessToken).
    private const string UserInfoTokenHashKey = "Auth:TokenHash";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<AutodeskUserTokenResult> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        var session = http.HttpContext?.Session;
        if (session is null) return AutodeskUserTokenResult.NotConnected;

        var state = AutodeskUserTokenSession.Read(session);
        if (state.ExplicitlyDisconnected) return AutodeskUserTokenResult.ReconnectRequired;
        if (state.AccessToken is null && state.RefreshToken is null) return AutodeskUserTokenResult.NotConnected;
        if (state.AccessToken is not null && !NeedsRefresh(state.ExpiresAt))
            return AutodeskUserTokenResult.Valid(state.AccessToken, state.ExpiresAt);

        return await RefreshUnderLockAsync(session, force: false, rejectedAccessToken: null, ct);
    }

    public async Task<AutodeskUserTokenResult> ForceRefreshAsync(string? rejectedAccessToken, CancellationToken ct = default)
    {
        var session = http.HttpContext?.Session;
        if (session is null) return AutodeskUserTokenResult.NotConnected;

        var state = AutodeskUserTokenSession.Read(session);
        if (state.ExplicitlyDisconnected) return AutodeskUserTokenResult.ReconnectRequired;
        if (state.AccessToken is null && state.RefreshToken is null) return AutodeskUserTokenResult.NotConnected;

        return await RefreshUnderLockAsync(session, force: true, rejectedAccessToken, ct);
    }

    private async Task<AutodeskUserTokenResult> RefreshUnderLockAsync(ISession session, bool force, string? rejectedAccessToken, CancellationToken ct)
    {
        using var _ = await coordinator.AcquireAsync(session.Id, ct);

        // Relê dentro do lock: outra requisição pode ter renovado neste meio-tempo.
        var state = AutodeskUserTokenSession.Read(session);
        if (state.ExplicitlyDisconnected) return AutodeskUserTokenResult.ReconnectRequired;
        state = AdoptRefreshDoneElsewhere(session, state);

        if (state.AccessToken is not null && !NeedsRefresh(state.ExpiresAt)
            && (!force || (rejectedAccessToken is not null && state.AccessToken != rejectedAccessToken)))
            return AutodeskUserTokenResult.Valid(state.AccessToken, state.ExpiresAt);

        if (state.RefreshToken is null)
        {
            // Sem refresh_token, um token ainda não vencido continua utilizável (exceto se acabou de ser recusado).
            if (!force && state.AccessToken is not null && !IsExpired(state.ExpiresAt))
                return AutodeskUserTokenResult.Valid(state.AccessToken, state.ExpiresAt);
            logger.LogWarning("Token Autodesk do usuário sem refresh_token: integração marcada como desconectada (reconexão necessária).");
            AutodeskUserTokenSession.MarkDisconnected(session);
            return AutodeskUserTokenResult.ReconnectRequired;
        }

        var outcome = await RequestRefreshAsync(state.RefreshToken, ct);
        switch (outcome.Kind)
        {
            case RefreshKind.Success:
                // Sem refresh_token novo na resposta, o anterior continua sendo o vigente (nunca grava vazio por cima).
                var set = new AutodeskUserTokenSet(outcome.Response!.access_token,
                    string.IsNullOrWhiteSpace(outcome.Response.refresh_token) ? state.RefreshToken : outcome.Response.refresh_token,
                    AutodeskUserTokenSession.ExpiresAtFrom(_clock.GetUtcNow(), outcome.Response.expires_in));
                Apply(session, state.AccessToken, set);
                coordinator.RememberRefresh(session.Id, state.RefreshToken, set);
                logger.LogInformation("Token Autodesk do usuário renovado (refresh_token rotacionado: {Rotacionado}).",
                    set.RefreshToken != state.RefreshToken);
                return AutodeskUserTokenResult.Valid(set.AccessToken, set.ExpiresAt);

            case RefreshKind.Rejected:
                logger.LogWarning("Refresh Autodesk recusado (HTTP {Status}): integração marcada como desconectada; sessão N&A preservada.",
                    outcome.StatusCode);
                AutodeskUserTokenSession.MarkDisconnected(session);
                return AutodeskUserTokenResult.ReconnectRequired;

            default:
                logger.LogWarning("Refresh Autodesk indisponível ({Detalhe}); integração mantida.", outcome.Detail);
                if (!force && state.AccessToken is not null && !IsExpired(state.ExpiresAt))
                    return AutodeskUserTokenResult.Valid(state.AccessToken, state.ExpiresAt);
                return AutodeskUserTokenResult.Unavailable;
        }
    }

    /// <summary>
    /// Se o refresh_token desta sessão já foi consumido por outra requisição (snapshot de sessão antigo), adota os
    /// tokens resultantes. Segue a cadeia caso tenha havido mais de um refresh desde o snapshot.
    /// </summary>
    private AutodeskUserTokenSession.State AdoptRefreshDoneElsewhere(ISession session, AutodeskUserTokenSession.State state)
    {
        AutodeskUserTokenSet? latest = null;
        var consumed = state.RefreshToken;
        for (var i = 0; i < 8 && consumed is not null && coordinator.TryGetRefresh(session.Id, consumed, out var next); i++)
        {
            latest = next;
            if (next.RefreshToken == consumed) break;
            consumed = next.RefreshToken;
        }
        if (latest is null || latest.AccessToken == state.AccessToken) return state;

        Apply(session, state.AccessToken, latest);
        return AutodeskUserTokenSession.Read(session);
    }

    private void Apply(ISession session, string? previousAccessToken, AutodeskUserTokenSet set)
    {
        AutodeskUserTokenSession.Save(session, set.AccessToken, set.RefreshToken, set.ExpiresAt);

        // O refresh mantém o mesmo usuário: atualiza o vínculo do cache de UserInfo para o token novo em vez de
        // forçar uma chamada UserInfo. Se o cache não pertencia ao token anterior, fica como está (inválido).
        if (previousAccessToken is not null && session.GetString(UserInfoTokenHashKey) == UserInfoHash(previousAccessToken))
            session.SetString(UserInfoTokenHashKey, UserInfoHash(set.AccessToken));
    }

    private enum RefreshKind { Success, Rejected, Transient }

    private sealed record RefreshOutcome(RefreshKind Kind, TokenResponse? Response, int? StatusCode, string Detail);

    private async Task<RefreshOutcome> RequestRefreshAsync(string refreshToken, CancellationToken ct)
    {
        var clientId = config["APS:ClientId"];
        var clientSecret = config["APS:ClientSecret"];
        // Sem "scope": a renovação mantém os scopes concedidos no login (APS:Scope + APS:UserScope).
        using var request = new HttpRequestMessage(HttpMethod.Post, AutodeskUserTokenPolicy.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            })
        };
        // Mesma autenticação de cliente do callback authorization_code.
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));

        try
        {
            // Cliente nomeado SEM retry: exatamente um POST por tentativa (ver AutodeskUserTokenPolicy.RefreshHttpClientName).
            var httpClient = httpClientFactory.CreateClient(AutodeskUserTokenPolicy.RefreshHttpClientName);
            using var response = await httpClient.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new RefreshOutcome(RefreshKind.Rejected, null, status, "rejeitado");
            if (!response.IsSuccessStatusCode)
                return new RefreshOutcome(RefreshKind.Transient, null, status, $"HTTP {status}");

            var body = JsonSerializer.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync(ct));
            if (body is null || string.IsNullOrWhiteSpace(body.access_token))
                return new RefreshOutcome(RefreshKind.Transient, null, status, "resposta sem access_token");
            return new RefreshOutcome(RefreshKind.Success, body, status, "ok");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new RefreshOutcome(RefreshKind.Transient, null, null, ex.GetType().Name);
        }
    }

    private bool NeedsRefresh(DateTimeOffset? expiresAt) =>
        // Expiração desconhecida (sessão anterior à 07A): mantém o comportamento legado e usa o token;
        // a recusa real (401) será tratada por ForceRefreshAsync na 07B.
        expiresAt is { } exp && _clock.GetUtcNow() >= exp - AutodeskUserTokenPolicy.RefreshMargin;

    private bool IsExpired(DateTimeOffset? expiresAt) => expiresAt is { } exp && _clock.GetUtcNow() >= exp;

    // Mesmo formato de SessionCurrentUserAccessor.Hash (Services/Identity/AutodeskUserIdentity.cs).
    private static string UserInfoHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];
}
