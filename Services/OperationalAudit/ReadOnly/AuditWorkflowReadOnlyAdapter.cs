using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services;
using Newtonsoft.Json.Linq;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>
    /// Implementação real de <see cref="IAuditWorkflowReadOnlyAdapter"/> — cliente HTTP próprio (mesmo
    /// padrão de <see cref="AuditAccReadOnlyAdapter"/>: nunca reaproveita <c>AccReviewWorkflowService</c>,
    /// que tem métodos de escrita). Só GET contra a API de Reviews do ACC.
    /// </summary>
    public sealed class AuditWorkflowReadOnlyAdapter : IAuditWorkflowReadOnlyAdapter
    {
        private const string BaseUrl = "https://developer.api.autodesk.com";

        private readonly HttpClient _httpClient;
        private readonly TokenManager _tokenManager;

        public AuditWorkflowReadOnlyAdapter(HttpClient httpClient, TokenManager tokenManager)
        {
            _httpClient = httpClient;
            _tokenManager = tokenManager;
        }

        public async Task<IReadOnlyList<AuditReviewRef>> ObterHistoricoDeReviewsAsync(string projectId, string itemId, CancellationToken ct = default)
        {
            string token = await _tokenManager.GetTokenAsync();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/bim360/docs/v1/projects/{projectId}/items/{itemId}/reviews");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            var resultado = new List<AuditReviewRef>();
            if (json.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var review in results.EnumerateArray())
                {
                    var vereditos = new List<AuditReviewVersionVerdictRef>();
                    if (review.TryGetProperty("versions", out var versoes) && versoes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var v in versoes.EnumerateArray())
                        {
                            vereditos.Add(new AuditReviewVersionVerdictRef
                            {
                                VersionUrn = v.TryGetProperty("versionUrn", out var vu) ? vu.GetString() ?? string.Empty : string.Empty,
                                ApproveStatus = v.TryGetProperty("approveStatus", out var ap) && ap.ValueKind == JsonValueKind.String ? ap.GetString() : null
                            });
                        }
                    }

                    resultado.Add(new AuditReviewRef
                    {
                        ReviewId = review.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                        Status = review.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                        IniciadaEmUtc = LerData(review, "startedAt"),
                        FinalizadaEmUtc = LerData(review, "closedAt"),
                        CurrentStepId = review.TryGetProperty("currentStepId", out var stepEl) ? stepEl.GetString() : null,
                        // Checkpoint 3 — melhor esforço: preenchidos SE o endpoint de listagem os devolver
                        // junto do resumo da Review; quando não devolver, ficam null (WF-006/WF-007 tratam
                        // isso como evidência insuficiente, nunca como erro).
                        CurrentStepDueDateRaw = review.TryGetProperty("currentStepDueDate", out var dueEl) && dueEl.ValueKind == JsonValueKind.String ? dueEl.GetString() : null,
                        WorkflowId = review.TryGetProperty("workflowId", out var wfEl) && wfEl.ValueKind == JsonValueKind.String ? wfEl.GetString() : null,
                        Vereditos = vereditos
                    });
                }
            }
            return resultado;
        }

        /// <summary>
        /// Correção pós-Checkpoint 3 (WF-007): GET da definição BRUTA do workflow, exatamente o mesmo
        /// endpoint usado por <c>AccReviewWorkflowService.ResolverNomeEtapaAtualAsync</c> (linha 1103):
        /// <c>construction/reviews/v1/projects/{projectId}/workflows/{workflowId}</c> — nunca
        /// <c>bim360/docs/v1/...</c> (endpoint diferente, usado só para a LISTAGEM de reviews). Devolve o
        /// JSON bruto como <see cref="JObject"/> (Newtonsoft.Json — necessário para
        /// <see cref="Configuration.WorkflowStepNameResolver"/> navegar para nós pai) para que a resolução
        /// COMPLETA (4 níveis) rode sobre o dado real, nunca um subconjunto pré-resolvido aqui. Nunca lança:
        /// qualquer falha (rede, 404, JSON inválido) vira <c>null</c> — a regra chamadora trata isso como
        /// evidência insuficiente, nunca como "não resolvido". Nunca chama <c>AccReviewWorkflowService</c>
        /// (que tem a resolução completa e é um serviço protegido).
        /// </summary>
        public async Task<JObject?> ObterDefinicaoDoWorkflowAsync(string projectId, string workflowId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(workflowId)) return null;
            try
            {
                string token = await _tokenManager.GetTokenAsync();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/construction/reviews/v1/projects/{projectId}/workflows/{workflowId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return null;

                string corpo = await response.Content.ReadAsStringAsync(ct);
                return JObject.Parse(corpo);
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? LerData(JsonElement elemento, string propriedade) =>
            elemento.TryGetProperty(propriedade, out var el) && el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), out var data)
                ? data.ToUniversalTime()
                : null;
    }
}
