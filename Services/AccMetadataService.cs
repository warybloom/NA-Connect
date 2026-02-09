using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using APS.WebApp.Services;

namespace APS.WebApp.Services
{
    public class AccMetadataService
    {
        private readonly HttpClient _http;
        private readonly TokenManager _tokenManager;
        private readonly ILogger<AccMetadataService> _logger;

        public AccMetadataService(HttpClient http, TokenManager tokenManager, ILogger<AccMetadataService> logger)
        {
            _http = http;
            _tokenManager = tokenManager;
            _logger = logger;
        }

        public async Task<string> GetAttributeValueAsync(string projectId, string versionUrn, string attributeName)
        {
            try
            {
                var accessToken = await _tokenManager.GetTokenAsync();
                var projectIdShort = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;

                // 1. Descobrir o ID técnico do atributo (ex: Status ou REV.)
                string? attrId = await DiscoverAttributeIdAsync(projectIdShort, accessToken, attributeName);
                if (string.IsNullOrEmpty(attrId)) return "N/A";

                // 2. Buscar os metadados da versão do arquivo
                var url = $"https://developer.api.autodesk.com/data/v1/projects/b.{projectIdShort}/versions/{Uri.EscapeDataString(versionUrn)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return "-";

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var customAttrs = json["data"]?["attributes"]?["extension"]?["data"]?["customAttributes"] as JArray;

                if (customAttrs == null) return "Vazio";

                // Procura o valor correspondente ao ID encontrado
                foreach (var attr in customAttrs)
                {
                    if (attr["id"]?.ToString() == attrId)
                    {
                        return attr["displayValue"]?.ToString() ?? attr["value"]?.ToString() ?? "Pendente";
                    }
                }

                return "Pendente";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao buscar atributo {attributeName}: {ex.Message}");
                return "Erro";
            }
        }

        private async Task<string?> DiscoverAttributeIdAsync(string projectIdShort, string token, string attrName)
        {
            var url = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectIdShort}/custom-attribute-definitions";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(request);
            if (!resp.IsSuccessStatusCode) return null;

            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var results = json["results"] as JArray;

            // Busca o ID que bate com o nome (ignora maiúsculas/minúsculas)
            var definition = results?.FirstOrDefault(x => 
                x["name"]?.ToString().Trim().Replace(".", "").Equals(attrName.Replace(".", ""), StringComparison.OrdinalIgnoreCase) == true);

            return definition?["id"]?.ToString();
        }
    }
}