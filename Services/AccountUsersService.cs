// Ficheiro: APS.WebApp/Services/AccountUsersService.cs (VERSÃO COMPLETA E CORRIGIDA)
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using APS.WebApp.Models;

namespace APS.WebApp.Services
{
    public class AccountUsersService
    {
        private readonly HttpClient _httpClient;
        private readonly TokenManager _tokenManager;
        private const int PageLimit = 100;

        public AccountUsersService(HttpClient httpClient, TokenManager tokenManager)
        {
            _httpClient = httpClient;
            _tokenManager = tokenManager;
        }

        public async Task<List<AccountUser>> GetAccountUsersAsync(string accountId)
        {
            var allUsers = new List<AccountUser>();
            var accessToken = await _tokenManager.GetTokenAsync();
            int currentOffset = 0;
            bool keepGoing = true;

            try
            {
                while (keepGoing)
                {
                    // USANDO A API LEGADA /hq/v1/ QUE É COMPATÍVEL COM O SEU HUB ID
                    var requestUrl = $"https://developer.api.autodesk.com/hq/v1/accounts/{accountId}/users?limit={PageLimit}&offset={currentOffset}";

                    var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    request.Headers.Add("Authorization", $"Bearer {accessToken}");

                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode(); 

                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    var usersOnPage = JsonSerializer.Deserialize<List<AccountUser>>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (usersOnPage != null && usersOnPage.Count > 0)
                    {
                        allUsers.AddRange(usersOnPage);

                        if (usersOnPage.Count < PageLimit)
                        {
                            keepGoing = false;
                        }
                        else
                        {
                            currentOffset += PageLimit;
                        }
                    }
                    else
                    {
                        keepGoing = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Uma exceção ocorreu em GetAccountUsersAsync: {ex.ToString()}");
                throw new HttpRequestException($"Falha ao buscar usuários da conta. Status: {ex.Source}. Detalhes: {ex.Message}", ex);
            }
            return allUsers;
        }
    }
}