using System.Threading.Tasks;
using APS.WebApp.Services;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Abstração mínima sobre <see cref="TokenManager"/> — existe só para permitir testar
    /// <see cref="AccReadOnlyConnectivityValidator"/> com um fake (token disponível/indisponível) sem
    /// precisar de <c>IMemoryCache</c>/<c>HttpClient</c> reais nem de client id/secret configurados.
    /// </summary>
    public interface IAccTokenProvider
    {
        Task<string> GetTokenAsync();
    }

    /// <summary>Implementação real — delega 100% para o mesmo <see cref="TokenManager"/> já usado pelos adaptadores somente-leitura (seção 2: "não inventar outro fluxo de autenticação").</summary>
    public sealed class TokenManagerAccTokenProvider : IAccTokenProvider
    {
        private readonly TokenManager _tokenManager;

        public TokenManagerAccTokenProvider(TokenManager tokenManager)
        {
            _tokenManager = tokenManager;
        }

        public Task<string> GetTokenAsync() => _tokenManager.GetTokenAsync();
    }
}
