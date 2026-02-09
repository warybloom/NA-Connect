// Ficheiro: ProjectCreationService.cs
// Pasta: Services/
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using APS.WebApp.Models;
using System.Text.Json.Serialization; // <-- CORREÇÃO: ADICIONADO ESTA LINHA

namespace APS.WebApp.Services
{
    public class ProjectCreationService
    {
        private readonly HttpClient _httpClient;
        private readonly TokenManager _tokenManager;

        public ProjectCreationService(HttpClient httpClient, TokenManager tokenManager)
        {
            _httpClient = httpClient;
            _tokenManager = tokenManager;
        }

        public async Task<string> CreateProjectAsync(string accountId, ProjectData projectData)
        {
            var accessToken = await _tokenManager.GetTokenAsync();
            var requestUrl = $"https://developer.api.autodesk.com/hq/v1/accounts/{accountId}/projects";

            var jsonPayload = JsonSerializer.Serialize(projectData, new JsonSerializerOptions
            {
                // Esta linha agora vai funcionar
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var createdProject = JsonSerializer.Deserialize<JsonElement>(responseContent);
                return createdProject.GetProperty("id").GetString();
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao criar o projeto. Status: {response.StatusCode}. Resposta: {errorContent}");
            }
        }
    }
}