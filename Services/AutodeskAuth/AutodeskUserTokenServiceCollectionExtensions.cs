using Microsoft.Extensions.DependencyInjection;

namespace APS.WebApp.Services.AutodeskAuth;

public static class AutodeskUserTokenServiceCollectionExtensions
{
    /// <summary>
    /// Provider 3-legged + coordinator singleton (single-flight) + HttpClient de refresh sem retry.
    /// O retry global (ConfigureHttpClientDefaults → AutodeskRetryHandler) continua valendo para todos os outros clientes;
    /// aqui ele é retirado somente do cliente nomeado de refresh, para que cada refresh do provider seja UM único POST.
    /// </summary>
    public static IServiceCollection AddAutodeskUserTokenProvider(this IServiceCollection services)
    {
        services.AddHttpClient(AutodeskUserTokenPolicy.RefreshHttpClientName)
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
        services.AddSingleton<AutodeskUserTokenRefreshCoordinator>();
        services.AddScoped<IAutodeskUserTokenProvider, AutodeskUserTokenProvider>();
        return services;
    }
}
