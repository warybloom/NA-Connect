using System.Net;

namespace APS.WebApp.Services.AutodeskAuth;

/// <summary>Executa uma chamada Autodesk 3-legged com no mÃ¡ximo uma renovaÃ§Ã£o apÃ³s 401.</summary>
public static class AutodeskUserRequestExecutor
{
    public static async Task<T> ExecuteAsync<T>(
        IAutodeskUserTokenProvider provider,
        Func<string, CancellationToken, Task<T>> request,
        CancellationToken cancellationToken = default)
    {
        var current = await provider.GetValidAccessTokenAsync(cancellationToken);
        if (!current.IsValid)
            throw new AutodeskUserReconnectRequiredException(current.Status);

        try
        {
            return await request(current.AccessToken!, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await provider.ForceRefreshAsync(current.AccessToken, cancellationToken);
            if (!refreshed.IsValid)
                throw new AutodeskUserReconnectRequiredException(refreshed.Status, ex);

            try
            {
                return await request(refreshed.AccessToken!, cancellationToken);
            }
            catch (HttpRequestException retry) when (retry.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new AutodeskUserReconnectRequiredException(AutodeskUserTokenStatus.ReconnectRequired, retry);
            }
        }
    }
}

public sealed class AutodeskUserReconnectRequiredException : Exception
{
    public AutodeskUserTokenStatus Status { get; }

    public AutodeskUserReconnectRequiredException(AutodeskUserTokenStatus status, Exception? inner = null)
        : base("A integraÃ§Ã£o Autodesk exige reconexÃ£o.", inner) => Status = status;
}
