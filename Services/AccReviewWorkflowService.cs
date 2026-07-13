using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http.Headers;


namespace APS.WebApp.Services
{
    public class AccReviewWorkflowService
    {
        private readonly HttpClient _http;
        
        private readonly ILogger<AccReviewWorkflowService> _logger;

        // ✅ ID do Administrador (Pego do seu token manual que funcionou)
        private const string AdminAutodeskId = "WDV284PFQTJDQZEG"; 

        public AccReviewWorkflowService(HttpClient http, ILogger<AccReviewWorkflowService> logger)
        {
            _http = http;
            _logger = logger;
        }
        public AccReviewWorkflowService(HttpClient httpClient)
        {
            _http = httpClient;
        }

        public async Task<Dictionary<string, string>> GetProjectWorkflowMapAsync(string projectId, string accessToken)
{
    var workflowMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var cacheWorkflows = new Dictionary<string, JObject>();
    string projectIdShort = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;

    try
    {
        string listUrl = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/reviews";
        var reviewsResp = await SendGetAsync(listUrl, accessToken);
        var reviews = reviewsResp?["results"] as JArray;

        if (reviews == null) return workflowMap;

        foreach (var review in reviews)
        {
            string reviewId = review["id"]?.ToString() ?? "";
            var detail = await SendGetAsync($"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/reviews/{reviewId}", accessToken);
            if (detail == null) continue;

            string currentStepId = detail["currentStepId"]?.ToString() ?? "";
            string workflowId = detail["workflowId"]?.ToString() ?? "";
            
            // Resolve o nome da Etapa (EXECUTOR, etc.)
            string nomeEtapa = "EM REVISÃO";
            if (!string.IsNullOrEmpty(workflowId) && !string.IsNullOrEmpty(currentStepId))
            {
                if (!cacheWorkflows.TryGetValue(workflowId, out JObject? wfDef))
                {
                    wfDef = await SendGetAsync($"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/workflows/{workflowId}", accessToken);
                    if (wfDef != null) cacheWorkflows[workflowId] = wfDef;
                }
                nomeEtapa = wfDef?["steps"]?.FirstOrDefault(s => s["id"]?.ToString() == currentStepId)?["name"]?.ToString() ?? "REVISÃO";
            }

            // Pega os arquivos deste review
            var versionsResp = await SendGetAsync($"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/reviews/{reviewId}/versions", accessToken);
            var versions = versionsResp?["results"] as JArray;

            if (versions != null)
            {
                foreach (var v in versions)
                {
                    string fileName = v["name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        // IMPORTANTE: Remove .dwg, .pdf, .xlsx para bater com a LDP
                        string cleanName = Path.GetFileNameWithoutExtension(fileName).Trim();
                        workflowMap[cleanName] = nomeEtapa.ToUpper();
                    }
                }
            }
        }
    }
    catch (Exception ex) { _logger.LogError($"Erro no Workflow: {ex.Message}"); }
    
    _logger.LogInformation($"✅ Mapeamento concluído: {workflowMap.Count} arquivos em revisão encontrados.");
    return workflowMap;
}

// Método auxiliar de normalização (copie da sua LdpUpdateService se necessário)
private string NormalizeDoc(string s) => System.Text.RegularExpressions.Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");

        private async Task<JObject?> SendGetAsync(string url, string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            // ✅ ADICIONADO: Contexto de Administrador
            req.Headers.Add("x-user-id", AdminAutodeskId); 

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }

        // --- ADICIONE ESTES MÉTODOS PARA LIMPAR O ERRO CS1061 ---

// 🎯 Note o terceiro parâmetro: string userId
// 🎯 GARANTA QUE OS MÉTODOS ABAIXO TAMBÉM EXISTAM (Eles chamam o método acima)
        public async Task<List<JToken>> GetProjectReviewsAsync(string projectId, string accessToken, string userId)
        {
            string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            string url = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{cleanId}/reviews";
            var response = await SendGetWithUserContextAsync(url, accessToken, userId);
            return response?["results"]?.ToList() ?? new List<JToken>();
        }

        public async Task<List<JToken>> GetReviewVersionsAsync(string projectId, string reviewId, string accessToken, string userId)
        {
            string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            string url = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{cleanId}/reviews/{reviewId}/versions";
            var response = await SendGetWithUserContextAsync(url, accessToken, userId);
            return response?["results"]?.ToList() ?? new List<JToken>();
        }

// 🎯 AJUSTE NO MÉTODO PRIVADO: Mude de _httpClient para _http
        private async Task<JObject?> SendGetWithUserContextAsync(string url, string token, string userId)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", $"Bearer {token}");
            req.Headers.Add("x-user-id", userId); 

            // Estava _httpClient, mude para _http que é a variável que o construtor preencheu
            var res = await _http.SendAsync(req); 
            if (!res.IsSuccessStatusCode) return null;

            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }
    }
}
