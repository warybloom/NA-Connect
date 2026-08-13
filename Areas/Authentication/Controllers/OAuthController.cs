// Ficheiro: Areas/Authentication/Controllers/OAuthController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using APS.WebApp.Services;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System;
using Microsoft.AspNetCore.Http;

namespace APS.WebApp.Areas.Authentication.Controllers
{
    [Area("Authentication")]
    [Route("[area]/[controller]/[action]")] // Rota a nível de controller para clareza
    public class OAuthController : Controller
    {
        private readonly IConfiguration _config;
        private readonly AutodeskApiService _autodeskApi;
        private readonly HttpClient _httpClient;

        public OAuthController(IConfiguration config, AutodeskApiService autodeskApi, HttpClient httpClient)
        {
            _config = config;
            _autodeskApi = autodeskApi;
            _httpClient = httpClient;
        }

        [HttpGet]
        public IActionResult GetLoginUrl()
        {
            var clientId = _config["APS:ClientId"];
            var callbackUrl = _config["APS:CallbackUrl"];
            var scopes = _config["APS:Scope"];
            
            var authorizationUrl = $"https://developer.api.autodesk.com/authentication/v2/authorize?response_type=code&client_id={clientId}&redirect_uri={HttpUtility.UrlEncode(callbackUrl)}&scope={HttpUtility.UrlEncode(scopes)}";
            
            return Ok(new { loginUrl = authorizationUrl });
        }

        [HttpGet]
        public async Task<IActionResult> Callback(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest("Código de autorização ausente.");
            }

            var clientId = _config["APS:ClientId"];
            var clientSecret = _config["APS:ClientSecret"];
            var callbackUrl = _config["APS:CallbackUrl"];
            
            var tokenRequestParams = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", callbackUrl }
            };
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://developer.api.autodesk.com/authentication/v2/token")
            {
                Content = new FormUrlEncodedContent(tokenRequestParams)
            };
            var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                return BadRequest("Erro ao obter o token.");
            }

            var tokenData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var accessToken = tokenData.GetProperty("access_token").GetString();
            var refreshToken = tokenData.GetProperty("refresh_token").GetString();
            
            HttpContext.Session.SetString("AccessToken", accessToken);
            if (!string.IsNullOrEmpty(refreshToken))
            {
                HttpContext.Session.SetString("RefreshToken", refreshToken);
            }

            // Redireciona para a raiz do site. O HomeController irá então
            // redirecionar para a aplicação React.
            return Redirect("/"); 
        }

        [HttpGet]
        public IActionResult CheckStatus()
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            return Ok(new { isAuthenticated = !string.IsNullOrEmpty(accessToken) });
        }

        [HttpGet]
        public async Task<IActionResult> GetUserName()
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized();
            }
            
            try
            {
                var userName = await _autodeskApi.GetUserNameAsync(accessToken);
                return Ok(new { userName });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Não foi possível obter o nome do utilizador." });
            }
        }


[HttpGet]
public async Task<IActionResult> GetProfile()
{
    var accessToken = HttpContext.Session.GetString("AccessToken");
    if (string.IsNullOrEmpty(accessToken))
    {
        Console.WriteLine("AccessToken is null or empty.");
        return Unauthorized(new { message = "Não autenticado." });
    }

    try
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://developer.api.autodesk.com/userprofile/v1/users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

var responseBody = await response.Content.ReadAsStringAsync();

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(responseBody);

if (!response.IsSuccessStatusCode)
{
    return StatusCode((int)response.StatusCode, responseBody);
}

        // 1. Lê a resposta como string
        var content = await response.Content.ReadAsStringAsync();
        var userJson = JsonDocument.Parse(content).RootElement;

        // 2. Mapeamento correto dos campos
        string name = userJson.TryGetProperty("name", out var nameProp) && !string.IsNullOrEmpty(nameProp.GetString())
            ? nameProp.GetString()
            : userJson.TryGetProperty("userName", out var userNameProp) ? userNameProp.GetString() : "Utilizador";

        // Corrigido: a Autodesk usa 'emailId'
        string email = userJson.TryGetProperty("emailId", out var emailProp) 
            ? emailProp.GetString() 
            : string.Empty;

        // Corrigido: 'profileImages' em camelCase
        string avatar = userJson.TryGetProperty("profileImages", out var profileImagesProp) && 
                        profileImagesProp.TryGetProperty("sizeX80", out var avatarProp) 
            ? avatarProp.GetString() 
            : string.Empty;

        return Ok(new { name, email, avatar });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = "Não foi possível obter o perfil do utilizador.", details = ex.Message });
    }
}
[HttpGet]
public async Task<IActionResult> GetLoggedInUserName()
{
    var accessToken = HttpContext.Session.GetString("AccessToken");
    if (string.IsNullOrEmpty(accessToken))
    {
        return Unauthorized(new { message = "Não autenticado." });
    }

    try
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.userprofile.autodesk.com/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, responseBody);
        }

        var userJson = JsonDocument.Parse(responseBody).RootElement;

        string name = userJson.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString()
            : "Utilizador";

        return Ok(new { name });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = "Não foi possível obter o nome do utilizador.", details = ex.Message });
    }
}

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return Redirect("/"); // Redireciona para a página principal, que por sua vez redirecionará para a página de login do React.
        }
    }
}