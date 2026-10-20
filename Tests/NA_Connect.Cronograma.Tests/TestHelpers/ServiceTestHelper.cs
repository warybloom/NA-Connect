using APS.WebApp.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services;

namespace NA_Connect.Cronograma.Tests.TestHelpers
{
    /// <summary>
    /// Constrói uma instância REAL de AccScheduleSyncService para os testes chamarem seus métodos
    /// internal diretamente — nunca faz rede/SQL (os testes só exercitam os métodos puros de
    /// resolução/normalização, que não chamam nenhuma das dependências construídas aqui).
    /// </summary>
    internal static class ServiceTestHelper
    {
        public static AccScheduleSyncService NovoAccScheduleSyncService()
        {
            var configuration = new ConfigurationBuilder().Build();
            var httpClient = new HttpClient();
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var tokenManager = new TokenManager(httpClient, configuration, memoryCache);
            var docMatching = new DocumentMatchingService();
            var autodeskApi = new AutodeskApiService(httpClient, tokenManager, configuration, NullLogger<AutodeskApiService>.Instance, docMatching);
            var dbService = new DatabaseService(configuration, NullLogger<DatabaseService>.Instance);
            var discoveryService = new ProjectAutoDiscoveryService(autodeskApi, NullLogger<ProjectAutoDiscoveryService>.Instance);

            return new AccScheduleSyncService(autodeskApi, dbService, NullLogger<AccScheduleSyncService>.Instance, configuration, discoveryService);
        }
    }
}
