using APS.WebApp.Services.Identity;
using APS.WebApp.Services.AutodeskAuth;
using APS.WebApp.Services.Permissions;
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
    [PermissionExempt("Login/logout OAuth Autodesk: ocorre antes de existir identidade.")]
    public class OAuthController : Controller
    {
        private readonly IConfiguration _config;
        private readonly AutodeskApiService _autodeskApi;
        private readonly HttpClient _httpClient;
        private readonly IPermissionRepository _permissionRepository;
        private readonly ILogger<OAuthController> _logger;

        public OAuthController(IConfiguration config, AutodeskApiService autodeskApi, HttpClient httpClient,
            IPermissionRepository permissionRepository, ILogger<OAuthController> logger)
        {
            _config = config;
            _autodeskApi = autodeskApi;
            _httpClient = httpClient;
            _permissionRepository = permissionRepository;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult GetLoginUrl()
        {
            var clientId = _config["APS:ClientId"];
            var callbackUrl = _config["APS:CallbackUrl"];
            // Login 3-legged = scopes do sistema (APS:Scope, também usados no token 2-legged do TokenManager) +
            // APS:UserScope (user-profile:read, exigido pelo UserInfo). O 2-legged NÃO recebe APS:UserScope.
            var scopes = string.Join(' ', ($"{_config["APS:Scope"]} {_config["APS:UserScope"]}")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal));
            
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
            long? expiresIn = tokenData.TryGetProperty("expires_in", out var expiresInJson)
                && expiresInJson.ValueKind == JsonValueKind.Number && expiresInJson.TryGetInt64(out var segundos) ? segundos : null;

            // Dual-write: chaves Autodesk:* (integração, ExpiresAt real em UTC, Connected=true) + AccessToken/RefreshToken legados.
            AutodeskUserTokenSession.Save(HttpContext.Session, accessToken!, refreshToken,
                AutodeskUserTokenSession.ExpiresAtFrom(DateTimeOffset.UtcNow, expiresIn));

            // Login = sincronização do PRÓPRIO usuário (Users + projetos ACC + perfil padrão ausente), em segundo plano.
            // Nunca bloqueia nem derruba o login: falha aqui só fica no log.
            try
            {
                var currentUser = HttpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>();
                if (await currentUser.GetAsync(HttpContext.RequestAborted) is { Email.Length: > 0 } usuario)
                {
                    var userId = await _permissionRepository.RegistrarAcessoAsync(
                        string.IsNullOrWhiteSpace(usuario.AutodeskUserId) ? null : usuario.AutodeskUserId,
                        usuario.Email, usuario.Nome, HttpContext.RequestAborted);
                    HttpContext.Session.SetInt32(NaSessionKeys.UserId, userId);
                    HttpContext.Session.SetString(NaSessionKeys.Email, usuario.Email);
                    HttpContext.Session.SetString(NaSessionKeys.Nome, usuario.Nome);
                    HttpContext.Session.SetString(NaSessionKeys.Provider, "Autodesk");
                    if (!string.IsNullOrWhiteSpace(usuario.AutodeskUserId))
                        HttpContext.Session.SetString(AutodeskSessionKeys.UserId, usuario.AutodeskUserId);
                    // Compatibilidade com consumidores que ainda usam o cache de permissões legado.
                    HttpContext.Session.SetInt32("Perm:UserId", userId);
                    HttpContext.Session.SetString("Perm:UserKey",
                        $"{usuario.AutodeskUserId}|{usuario.Email.ToLowerInvariant()}");
                    HttpContext.RequestServices.GetRequiredService<APS.WebApp.Services.Permissions.Sync.PermissionLoginSync>().Disparar(usuario, accessToken!);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sessão N&A/sincronização de permissões no login não iniciada ({Tipo}).", ex.GetType().Name);
            }

            // Redireciona para a raiz do site. O HomeController irá então
            // redirecionar para a aplicação React.
            return Redirect("/"); 
        }

        [HttpGet]
        public IActionResult CheckStatus()
        {
            // Primeiro acesso N&A com troca de senha obrigatória pendente: a sessão existe, mas ainda não libera o sistema.
            if (HttpContext.Session.GetString(APS.WebApp.Services.LocalAuth.NaLocalAuthPolicy.MustChangePasswordSessionKey) == "true")
                return Ok(new { isAuthenticated = false });

            if (HttpContext.Session.GetInt32(NaSessionKeys.UserId) is > 0)
                return Ok(new { isAuthenticated = true });

            var accessToken = HttpContext.Session.GetString("AccessToken");
            return Ok(new { isAuthenticated = !string.IsNullOrEmpty(accessToken) });
        }

        [HttpGet]
        public async Task<IActionResult> GetUserName([FromServices] ICurrentUserAccessor currentUser,
            [FromServices] INaCurrentUserAccessor naCurrentUser, CancellationToken ct)
        {
            var naUser = await naCurrentUser.GetAsync(ct);
            if (naUser is not null)
            {
                var userName = naUser.Nome.Length > 0 ? naUser.Nome : naUser.Email;
                return Ok(new { userName, email = naUser.Email, autodeskUserId = HttpContext.Session.GetString("Auth:AutodeskUserId") });
            }

            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized();
            }

            // Fonte única da identidade (UserInfo OIDC, cacheado na sessão) — a mesma usada pelas permissões.
            try
            {
                var usuario = await currentUser.GetAsync(ct);
                if (usuario is null) return Unauthorized();
                var userName = usuario.Nome.Length > 0 ? usuario.Nome : usuario.Email;
                return Ok(new { userName, email = usuario.Email, autodeskUserId = usuario.AutodeskUserId });
            }
            catch (AutodeskUserInfoException ex) when (ex.ExigeNovoLogin)
            {
                // 409 (não 401): o Layout redireciona para /login em 401; aqui a sessão existe, falta o scope no token.
                return StatusCode(409, new { message = AutodeskUserInfoException.MensagemNovoLogin, reloginRequired = true });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Não foi possível obter o nome do utilizador." });
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
