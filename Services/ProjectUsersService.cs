// Ficheiro: ProjectUsersService.cs
// Pasta: Services/
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace APS.WebApp.Services
{
    public class ProjectUsersService
    {
        private readonly HttpClient _httpClient;
        private readonly TokenManager _tokenManager;

        public ProjectUsersService(HttpClient httpClient, TokenManager tokenManager)
        {
            _httpClient = httpClient;
            _tokenManager = tokenManager;
        }

        /// <summary>
        /// Adiciona um utilizador a um projeto e ativa os seus serviços, definindo-o como administrador.
        /// </summary>
        public async Task AssignProjectAdminAsync(string accountId, string projectId, string userId)
        {
            var accessToken = await _tokenManager.GetTokenAsync();
            var requestUrl = $"https://developer.api.autodesk.com/hq/v2/accounts/{accountId}/projects/{projectId}/users/import";

            // CORREÇÃO: O payload deve ser uma LISTA de objetos, e não um objeto contendo uma lista.
            var payload = new List<object>
            {
                new
                {
                    userId = userId,
                    services = new
                    {
                        documentManagement = new { accessLevel = "admin" },
                        projectAdministration = new { accessLevel = "admin" }
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("x-user-id", "cdd414c9-fe1b-4999-a3b7-3ceabedcc567");
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao adicionar administrador ao projeto. Status: {response.StatusCode}. Resposta: {errorContent}");
            }
        }
    }
}