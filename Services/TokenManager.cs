// Pasta: C:/.../AccIntegration.WebApp/Services/
// Ficheiro: TokenManager.cs
#nullable disable
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace APS.WebApp.Services // <-- O namespace correto!
{
    public class TokenManager
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public TokenManager(HttpClient httpClient, IConfiguration configuration, IMemoryCache cache)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _cache = cache;
        }

        public async Task<string> GetTokenAsync()
        {
            const string cacheKey = "TwoLeggedToken";
            if (!_cache.TryGetValue(cacheKey, out string token))
            {
                string clientId = _configuration["APS:ClientId"];
                string clientSecret = _configuration["APS:ClientSecret"];
                string scope = _configuration["APS:Scope"];
                
                var request = new HttpRequestMessage(HttpMethod.Post, "https://developer.api.autodesk.com/authentication/v2/token");
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = scope
                });

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                token = tokenData.GetProperty("access_token").GetString();
                var expiresIn = tokenData.GetProperty("expires_in").GetInt32();
                
                _cache.Set(cacheKey, token, TimeSpan.FromSeconds(expiresIn - 300)); // Cache com margem de segurança
            }
            return token;
        }
    }
}