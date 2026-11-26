using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>Estado de uma checagem individual de conectividade — <c>NotTested</c> nunca é confundido com <c>Ok</c> (Checkpoint 4.1, seção 2).</summary>
    public enum ConnectivityCheckStatus
    {
        Ok,
        Failed,
        NotTested
    }

    public sealed class AccConnectivityResult
    {
        public ConnectivityCheckStatus ApsAuth { get; init; } = ConnectivityCheckStatus.NotTested;
        public ConnectivityCheckStatus AccProjectRead { get; init; } = ConnectivityCheckStatus.NotTested;

        /// <summary>Só um resumo seguro (nunca token/segredo) — ex.: código HTTP, "timeout", "404 projeto não encontrado".</summary>
        public string? Detalhe { get; init; }
    }

    public interface IAccReadOnlyConnectivityValidator
    {
        Task<AccConnectivityResult> ValidarAsync(string projectId, CancellationToken ct = default);
    }

    /// <summary>
    /// Checkpoint 4.1, seção 2: valida conectividade ACC SOMENTE LEITURA — autenticação APS (token) e um GET
    /// mínimo e barato contra o projeto selecionado. Reaproveita EXATAMENTE o mesmo endpoint já usado por
    /// <c>AuditAccReadOnlyAdapter.LocalizarPastaPorAliasAsync</c>
    /// (<c>/data/v1/projects/{projectId}/topFolders</c>) — nunca inventa outro fluxo. O <see cref="HttpClient"/>
    /// injetado deve sempre vir com <c>AuditHttpWriteGuardHandler</c> como handler mais externo (mesma
    /// composição de <c>AddOperationalAudit</c> para <see cref="APS.WebApp.Services.OperationalAudit.ReadOnly.IAuditAccReadOnlyAdapter"/>)
    /// — nunca um POST/PUT/PATCH/DELETE pode sair daqui.
    ///
    /// Contrato de status HTTP (nunca ambíguo):
    ///   2xx            → AccProjectRead = Ok
    ///   401/403        → AccProjectRead = Failed (auth/permissão)
    ///   404             → AccProjectRead = Failed (projeto não encontrado/sem acesso)
    ///   5xx/timeout/etc → AccProjectRead = Failed (evidência objetiva de falha do teste, ainda que a causa
    ///                     possa ser transitória do lado do servidor — o teste É factível e a resposta
    ///                     negativa é evidência, não ausência de evidência; por isso nunca INCONCLUSIVE aqui)
    ///   token indisponível → ApsAuth = Failed, AccProjectRead = NotTested (nunca tentamos o GET sem token)
    ///
    /// Nunca imprime/loga token, refresh token, client secret ou connection string — <see cref="Detalhe"/>
    /// só contém código HTTP/motivo textual curto.
    /// </summary>
    public sealed class AccReadOnlyConnectivityValidator : IAccReadOnlyConnectivityValidator
    {
        private const string BaseUrl = "https://developer.api.autodesk.com";

        private readonly HttpClient _httpClient;
        private readonly IAccTokenProvider _tokenProvider;

        public AccReadOnlyConnectivityValidator(HttpClient httpClient, IAccTokenProvider tokenProvider)
        {
            _httpClient = httpClient;
            _tokenProvider = tokenProvider;
        }

        public async Task<AccConnectivityResult> ValidarAsync(string projectId, CancellationToken ct = default)
        {
            string token;
            try
            {
                token = await _tokenProvider.GetTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new AccConnectivityResult { ApsAuth = ConnectivityCheckStatus.Failed, AccProjectRead = ConnectivityCheckStatus.NotTested, Detalhe = "Token vazio." };
                }
            }
            catch (Exception ex)
            {
                // Nunca ecoa ex.Message bruto — pode conter fragmentos de URL/credencial dependendo da causa
                // real da falha; só o TIPO da exceção é reportado, nunca o texto completo.
                return new AccConnectivityResult
                {
                    ApsAuth = ConnectivityCheckStatus.Failed,
                    AccProjectRead = ConnectivityCheckStatus.NotTested,
                    Detalhe = $"Falha ao obter token: {ex.GetType().Name}"
                };
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/data/v1/projects/{projectId}/topFolders");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    return new AccConnectivityResult { ApsAuth = ConnectivityCheckStatus.Ok, AccProjectRead = ConnectivityCheckStatus.Ok, Detalhe = $"HTTP {(int)response.StatusCode}" };
                }

                string motivo = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "401 não autorizado",
                    System.Net.HttpStatusCode.Forbidden => "403 sem permissão",
                    System.Net.HttpStatusCode.NotFound => "404 projeto não encontrado",
                    _ => $"HTTP {(int)response.StatusCode}"
                };
                return new AccConnectivityResult { ApsAuth = ConnectivityCheckStatus.Ok, AccProjectRead = ConnectivityCheckStatus.Failed, Detalhe = motivo };
            }
            catch (Exception ex)
            {
                return new AccConnectivityResult { ApsAuth = ConnectivityCheckStatus.Ok, AccProjectRead = ConnectivityCheckStatus.Failed, Detalhe = ex.GetType().Name };
            }
        }
    }
}
