#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using APS.WebApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services
{
    #region Modelos de Suporte para a API
    public class AttributeDefinitionsResponse
    {
        [JsonPropertyName("results")]
        public List<AttributeDefinition> Results { get; set; } = new List<AttributeDefinition>();
    }

    public class AttributeDefinition
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class AccFolderItem
    {
        public string Name { get; set; }
        public string Type { get; set; } // "items" ou "folders"
        public string Id { get; set; }   // ID da pasta ou do item
        public string VersionUrn { get; set; } // Apenas para arquivos
        public string LastModifiedTime { get; set; }
        public string LastModifiedUserName { get; set; } // 👈 Adicione este campo
        public int VersionNumber { get; set; }
        public bool IsHidden { get; set; } // Para filtrar pastas ocultas
    }

    public class SyncResult
    {
        public int TotalLdpFiles { get; set; }
        public int FilesUpdated { get; set; }
        public List<string> FilesNotFound { get; set; } = new List<string>();
        public List<string> FilesUpdatedSuccess { get; set; } = new List<string>();
        public List<string> AllLdpFiles { get; set; } = new List<string>();
        public string SummaryMessage { get; set; } = string.Empty;
    }
    #endregion

    public class AutodeskApiService
    {
        private static int _totalCalls = 0;
        private static string _lastRateLimit = "N/A";       
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AutodeskApiService> _logger;
        private readonly TokenManager _tokenManager;
        
        // 🛡️ A NOVA CATRACA DE ESCRITA NO ACC (Protege todo o sistema contra o Erro 429) 🛡️
        private static readonly SemaphoreSlim _accUpdateLock = new SemaphoreSlim(2, 2);

        public AutodeskApiService(HttpClient httpClient, TokenManager tokenManager, IConfiguration configuration, ILogger<AutodeskApiService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger; 
            _tokenManager = tokenManager;
        }

        // Método para expor os dados para o log final
        public (int TotalCalls, string RateLimit) GetApiStats() => (_totalCalls, _lastRateLimit);
        public void ResetStats() { _totalCalls = 0; }
        
        private void TraceApiHeaders(HttpResponseMessage response)
        {
            _totalCalls++;
            if (response.Headers.TryGetValues("X-Ads-RateLimit-Remaining", out var values))
            {
                _lastRateLimit = values.FirstOrDefault() ?? "N/A";
            }
        }
        
        #region MÉTODOS DE ADMINISTRAÇÃO DA CONTA
        
        public async Task<List<AccAccountRole>> GetAccountRolesAsync(string accountId, string accessToken)
        {
            var requestUrl = $"https://developer.api.autodesk.com/construction/admin/v1/accounts/{accountId}/roles";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseStream = await response.Content.ReadAsStreamAsync();
            var rolesResponse = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            if (rolesResponse.TryGetProperty("results", out var resultsProperty) && resultsProperty.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<AccAccountRole>>(resultsProperty.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new List<AccAccountRole>();
            }
            return new List<AccAccountRole>();
        }

        public async Task AssignAccRolesToUserAsync(string projectId, string userId, List<string> roleIds, string accessToken)
        {
            var requestUrl = $"https://developer.api.autodesk.com/construction/admin/v1/projects/{projectId}/users/{userId}";
            var payload = new { roleIds };
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao atribuir funções ACC ao utilizador: {error}");
            }
        }

        public async Task<List<AccProjectTemplate>> GetAccProjectTemplatesAsync(string accountId, string accessToken)
        {
            var allTemplates = new List<AccProjectTemplate>();
            string? nextUrl = $"https://developer.api.autodesk.com/construction/admin/v1/accounts/{accountId}/projects?filter[classification]=template&limit=100"; // Start with limit=100

            _logger.LogInformation("Buscando templates de projeto para a conta {AccountId}...", accountId);

            while (!string.IsNullOrEmpty(nextUrl))
            {
                _logger.LogDebug("Buscando página de templates: {Url}", nextUrl);
                var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Erro ao buscar página de templates ({StatusCode}): {ErrorContent}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Erro ao buscar templates: {response.StatusCode}. Detalhes: {errorContent}");
                }

                var responseStream = await response.Content.ReadAsStreamAsync();
                var pageJson = await JsonDocument.ParseAsync(responseStream);
                var root = pageJson.RootElement;

                if (root.TryGetProperty("results", out var resultsProperty) && resultsProperty.ValueKind == JsonValueKind.Array)
                {
                    var templatesOnPage = JsonSerializer.Deserialize<List<AccProjectTemplate>>(resultsProperty.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (templatesOnPage != null)
                    {
                        allTemplates.AddRange(templatesOnPage);
                        _logger.LogDebug("Adicionados {Count} templates desta página.", templatesOnPage.Count);
                    }
                }

                if (root.TryGetProperty("pagination", out var pagination) &&
                    pagination.TryGetProperty("nextUrl", out var nextUrlProperty) &&
                    nextUrlProperty.ValueKind == JsonValueKind.String)
                {
                    nextUrl = nextUrlProperty.GetString();
                    _logger.LogDebug("Encontrada próxima página: {NextUrl}", nextUrl);
                }
                else
                {
                    nextUrl = null; 
                    _logger.LogDebug("Última página de templates alcançada.");
                }
            }

            _logger.LogInformation("Total de {Count} templates encontrados para a conta {AccountId}.", allTemplates.Count, accountId);
            return allTemplates;
        }


        public async Task<string> CreateAccProjectFromTemplateAsync(string accountId, AccProjectData projectData, string accessToken)
        {
            var requestUrl = $"https://developer.api.autodesk.com/construction/admin/v1/accounts/{accountId}/projects";
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            var jsonPayload = JsonSerializer.Serialize(projectData, options);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao criar projeto a partir de template: {error}");
            }
            var responseStream = await response.Content.ReadAsStreamAsync();
            var createdProject = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            return createdProject.GetProperty("id").GetString() ?? "";
        }

        public async Task ImportUsersToAccProjectAsync(string projectId, AccUserImport usersToImport, string accessToken)
        {
            var requestUrl = $"https://developer.api.autodesk.com/construction/admin/v1/projects/{projectId}/users:import";
            var jsonPayload = JsonSerializer.Serialize(usersToImport);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao importar utilizadores para o projeto: {error}");
            }
        }

        public async Task<AccProjectUser?> GetProjectUserByEmailAsync(string projectId, string userEmail, string accessToken)
        {
            var requestUrl = $"https://developer.api.autodesk.com/construction/admin/v1/projects/{projectId}/users?filter[email]={Uri.EscapeDataString(userEmail)}";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseStream = await response.Content.ReadAsStreamAsync();
            var jsonResponse = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            if (jsonResponse.TryGetProperty("results", out var resultsProperty) && resultsProperty.ValueKind == JsonValueKind.Array)
            {
                var users = JsonSerializer.Deserialize<List<AccProjectUser>>(resultsProperty.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return users?.FirstOrDefault();
            }
            return null;
        }

        #endregion

        #region MÉTODOS DE GESTÃO DE DADOS (DATA MANAGEMENT & OSS API)

        private string ToDataManagementProjectId(string projectId) => projectId.StartsWith("b.") ? projectId : $"b.{projectId}";

        public async Task<string> GetUserNameAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://developer.api.autodesk.com/userprofile/v1/users/@me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var userProfile = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
            return $"{userProfile.GetProperty("firstName").GetString()} {userProfile.GetProperty("lastName").GetString()}";
        }

        public async Task<JsonElement> GetHubsAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://developer.api.autodesk.com/project/v1/hubs");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        }

        public async Task<JsonElement> GetHubProjectsAsync(string hubId, string accessToken)
        {
            var allProjects = new List<JsonElement>();
            string? nextUrl = $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects";
            while (!string.IsNullOrEmpty(nextUrl))
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await _httpClient.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();
                var pageData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                if (pageData.TryGetProperty("data", out var projectsOnPage) && projectsOnPage.ValueKind == JsonValueKind.Array)
                {
                    allProjects.AddRange(projectsOnPage.EnumerateArray());
                }
                nextUrl = pageData.TryGetProperty("links", out var links) &&
                          links.TryGetProperty("next", out var next) &&
                          next.TryGetProperty("href", out var href)
                          ? href.GetString() : null;
            }
            return JsonDocument.Parse(JsonSerializer.Serialize(new { data = allProjects })).RootElement;
        }

        public async Task<JsonElement?> GetProjectRootFolderAsync(string hubId, string projectId, string token)
        {
            try
            {
                string url = $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects/{projectId}/topFolders";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {token}");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erro ao buscar pastas de topo: {response.StatusCode} - {errorContent}");
                    return null;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                
                var folders = doc.RootElement.GetProperty("data").EnumerateArray();
                
                foreach (var folder in folders)
                {
                    string folderName = folder.GetProperty("attributes").GetProperty("displayName").GetString();
                    
                    if (folderName.Equals("Project Files", StringComparison.OrdinalIgnoreCase))
                    {
                        return folder.Clone();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Falha catastrófica ao obter pasta raiz: {ex.Message}");
                return null;
            }
        }

        public async Task<(string FileId, string FileName)> SearchFileInProjectAsync(string projectId, string namePart, string token)
        {
            try
            {
                string url = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/filters?filter[attributes.displayName]-contains={namePart}";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {token}");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode) return (null, null);

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0)
                {
                    var item = data[0];
                    string fileId = item.GetProperty("id").GetString();
                    string fileName = item.GetProperty("attributes").GetProperty("displayName").GetString();
                    
                    return (fileId, fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro na busca profunda: {ex.Message}");
            }

            return (null, null);
        }

       // Adicionamos o "namePattern" para ele saber quem é a LDP de verdade
        public async Task<(string FileId, string FileName)> GetFirstExcelInFolderAsync(string projectId, string folderId, string namePattern, string token)
        {
            try
            {
                string url = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/folders/{folderId}/contents";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {token}");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (null, null);

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                var items = doc.RootElement.GetProperty("data").EnumerateArray();

                foreach (var item in items)
                {
                    var attributes = item.GetProperty("attributes");
                    string fileName = attributes.GetProperty("displayName").GetString();
                    
                    // 🎯 O SEGREDO AQUI: Ele continua olhando SÓ na pasta configurada,
                    // mas agora ele exige que seja Excel E que tenha a sigla da LDP (Ex: "-LD-" ou "LDP")
                    if ((fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || 
                         fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)) &&
                        fileName.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
                    {
                        string fileId = item.GetProperty("id").GetString();
                        return (fileId, fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro ao listar arquivos da pasta: {ex.Message}");
            }
            return (null, null);
        }



        public class ItemDetails
        {
            public string ItemId { get; set; }
            public string DisplayName { get; set; }
            public string TipVersionUrn { get; set; }
            public string ParentFolderId { get; set; } 
        }

        public async Task<string?> CreateFolderAsync(string projectId, string parentFolderId, string folderName, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var requestUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders";

            var payload = new
            {
                data = new
                {
                    type = "folders",
                    attributes = new
                    {
                        name = folderName,
                        extension = new
                        {
                            type = "folders:autodesk.bim360:Folder", 
                            version = "1.0"
                        }
                    },
                    relationships = new
                    {
                        parent = new
                        {
                            data = new
                            {
                                type = "folders",
                                id = parentFolderId
                            }
                        }
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict) 
                {
                    _logger.LogWarning("Pasta '{FolderName}' já existe (409 Conflict). Retornando null.", folderName);
                    return null; 
                }
                else if ((int)response.StatusCode == 423) 
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Erro 423 (Locked) ao tentar criar pasta '{FolderName}'. Detalhe API: {ErrorBody}. Retornando null para retry.", folderName, errorBody);
                    return null;
                }
                else 
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Erro {StatusCode} inesperado ao criar pasta '{FolderName}': {ErrorBody}", response.StatusCode, folderName, error);
                    throw new HttpRequestException($"Erro ao criar a pasta '{folderName}': Status {response.StatusCode} - {error}", null, response.StatusCode);
                }
            }

            var responseStream = await response.Content.ReadAsStreamAsync();
            var createdFolder = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            if (createdFolder.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String)
            {
                var newFolderId = idElement.GetString();
                _logger.LogInformation("Pasta '{FolderName}' criada com sucesso (ID: {FolderId})", folderName, newFolderId);
                return newFolderId;
            }
            else
            {
                _logger.LogError("Resposta 201 inesperada ao criar pasta '{FolderName}'. Corpo: {ResponseBody}", folderName, await response.Content.ReadAsStringAsync());
                throw new Exception($"Resposta inesperada da API após criar a pasta '{folderName}'.");
            }
        }

        public async Task<JsonElement?> GetFolderDetailsAsync(string projectId, string folderId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders/{folderId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        }

        public async Task<JsonElement?> GetItemVersionsAsync(string projectId, string itemId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/items/{itemId}/versions";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        }

        public async Task<string?> GetSignedDownloadUrlAsync(string storageUrn, string accessToken)
        {
            var idParts = storageUrn.Split(':').Last().Split('/');
            var bucketKey = Uri.EscapeDataString(idParts[0].Split('/').Last());
            var objectKey = Uri.EscapeDataString(idParts[1]);
            var url = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3download";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return data.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        }

        public async Task<JsonElement?> CreateNewVersionAsync(string projectId, string itemId, string folderId, string fileName, Stream fileStream, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);

            var createStorageUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/storage";
            var storagePayload = new { jsonapi = new { version = "1.0" }, data = new { type = "objects", attributes = new { name = fileName }, relationships = new { target = new { data = new { type = "folders", id = folderId } } } } };
            var storageJsonPayload = JsonSerializer.Serialize(storagePayload);
            using var storageRequest = new HttpRequestMessage(HttpMethod.Post, createStorageUrl);
            storageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            storageRequest.Content = new StringContent(storageJsonPayload, Encoding.UTF8);
            storageRequest.Content.Headers.ContentType = null;
            storageRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/vnd.api+json");
            var storageResponse = await _httpClient.SendAsync(storageRequest);
            if (!storageResponse.IsSuccessStatusCode) { var err = await storageResponse.Content.ReadAsStringAsync(); throw new HttpRequestException($"Erro ao criar storage: {err}"); }
            
            var storageData = JsonDocument.Parse(await storageResponse.Content.ReadAsStringAsync()).RootElement;
            var objectId = storageData.GetProperty("data").GetProperty("id").GetString();
            if (objectId == null) throw new Exception("Não foi possível obter o objectId do armazenamento.");

            const string urnPrefix = "urn:adsk.objects:os.object:";
            if (!objectId.StartsWith(urnPrefix)) { throw new Exception($"Formato de URN do objeto inesperado: {objectId}"); }
            string urnWithoutPrefix = objectId.Substring(urnPrefix.Length);
            var keyParts = urnWithoutPrefix.Split('/');
            if (keyParts.Length < 2) { throw new Exception($"Não foi possível extrair bucketKey e objectKey da URN: {objectId}"); }
            var bucketKey = keyParts[0];
            var objectKey = HttpUtility.UrlEncode(keyParts[1]);
            
            var signedUrlEndpoint = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload";
            using var signedUrlRequest = new HttpRequestMessage(HttpMethod.Get, signedUrlEndpoint);
            signedUrlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var signedUrlResponse = await _httpClient.SendAsync(signedUrlRequest);
            if (!signedUrlResponse.IsSuccessStatusCode) { var err = await signedUrlResponse.Content.ReadAsStringAsync(); throw new HttpRequestException($"Erro ao obter signed URL: {err}"); }
            var signedUrlData = JsonDocument.Parse(await signedUrlResponse.Content.ReadAsStringAsync()).RootElement;
            var uploadUrl = signedUrlData.GetProperty("urls").EnumerateArray().First().GetString();
            var uploadKey = signedUrlData.GetProperty("uploadKey").GetString();

            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new StreamContent(fileStream) };
            var uploadResponse = await _httpClient.SendAsync(uploadRequest);
            if (!uploadResponse.IsSuccessStatusCode) { var err = await uploadResponse.Content.ReadAsStringAsync(); throw new HttpRequestException($"Erro no upload para S3: {err}"); }
            
            var completeUploadEndpoint = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload";
            var completePayload = new { uploadKey = uploadKey };
            var completeJsonPayload = JsonSerializer.Serialize(completePayload);
            using var completeRequest = new HttpRequestMessage(HttpMethod.Post, completeUploadEndpoint);
            completeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            completeRequest.Content = new StringContent(completeJsonPayload, Encoding.UTF8, "application/json");
            var completeResponse = await _httpClient.SendAsync(completeRequest);
            if (!completeResponse.IsSuccessStatusCode) { var err = await completeResponse.Content.ReadAsStringAsync(); throw new HttpRequestException($"Erro ao completar upload: {err}"); }

            var createVersionUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/versions";

            var versionPayload = new { jsonapi = new { version = "1.0" }, data = new { type = "versions", attributes = new { name = fileName, extension = new { type = "versions:autodesk.bim360:File", version = "1.0" } }, relationships = new { item = new { data = new { type = "items", id = itemId } }, storage = new { data = new { type = "objects", id = objectId } } } } };
            var versionJsonPayload = JsonSerializer.Serialize(versionPayload);
            using var versionRequest = new HttpRequestMessage(HttpMethod.Post, createVersionUrl);
            versionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            versionRequest.Content = new StringContent(versionJsonPayload, Encoding.UTF8);
            versionRequest.Content.Headers.ContentType = null;
            versionRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/vnd.api+json");
            
            var versionResponse = await _httpClient.SendAsync(versionRequest);
            if (!versionResponse.IsSuccessStatusCode)
            {
                var errorContent = await versionResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Erro ao criar a versão final: {errorContent}");
            }

            return JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync()).RootElement;
        }

        #endregion

        #region MÉTODOS DE CONTEÚDO DE PASTAS E ATRIBUTOS

        public async Task<List<AccFolderItem>> GetFolderContentsSimpleAsync(string projectId, string folderId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            string url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders/{folderId}/contents";
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            var items = new List<AccFolderItem>();

            if (root.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    var type = item.GetProperty("type").GetString() ?? "";
                    var id = item.GetProperty("id").GetString() ?? "";
                    var attributes = item.GetProperty("attributes");
                    
                    var name = attributes.TryGetProperty("displayName", out var n) ? n.GetString() : "Sem Nome";
                    var lastModified = attributes.TryGetProperty("lastModifiedTime", out var lm) ? lm.GetString() : null;
                    var userName = attributes.TryGetProperty("lastModifiedUserName", out var un) ? un.GetString() : "Sistema";

                    string? versionUrn = null;
                    int versionNumber = 1;

                    if (type == "items" && 
                        item.TryGetProperty("relationships", out var rels) && 
                        rels.TryGetProperty("tip", out var tip) && 
                        tip.TryGetProperty("data", out var tipData))
                    {
                        versionUrn = tipData.GetProperty("id").GetString();
                        if (versionUrn != null && versionUrn.Contains("version="))
                        {
                            int.TryParse(versionUrn.Split("version=").Last(), out versionNumber);
                        }
                    }

                    items.Add(new AccFolderItem 
                    { 
                        Name = name, 
                        Type = type,
                        Id = id,
                        VersionUrn = versionUrn,
                        LastModifiedTime = lastModified
                    });
                }
            }
            return items;
        }

        public async Task<List<AttributeDefinition>> GetFolderAttributeDefinitionsAsync(string projectId, string folderId, string accessToken)
        {
            string projectIdWithoutPrefix = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            var allDefinitions = new List<AttributeDefinition>();
            int offset = 0;
            const int limit = 200;

            while (true)
            {
                var requestUrl = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectIdWithoutPrefix}/folders/{folderId}/custom-attribute-definitions?limit={limit}&offset={offset}";
                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseStream = await response.Content.ReadAsStreamAsync();
                var definitionsResponse = await JsonSerializer.DeserializeAsync<AttributeDefinitionsResponse>(responseStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (definitionsResponse?.Results != null && definitionsResponse.Results.Any())
                {
                    allDefinitions.AddRange(definitionsResponse.Results);
                    if (definitionsResponse.Results.Count < limit) break;
                    offset += limit;
                }
                else
                {
                    break;
                }
            }
            return allDefinitions;
        }

        public async Task<JsonElement> GetVersionsWithCustomAttributesAsync(string projectId, List<string> versionUrns, string accessToken)
        {
            string projectIdWithoutPrefix = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            var requestUrl = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectIdWithoutPrefix}/versions:batch-get";
            var payload = new { urns = versionUrns };
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        }

        public async Task<JsonElement?> GetFolderContentsWithAttributesAsync(string projectId, string folderId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            string contentsUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders/{folderId}/contents?include=versions";
            var contentsRequest = new HttpRequestMessage(HttpMethod.Get, contentsUrl);
            contentsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var contentsResponse = await _httpClient.SendAsync(contentsRequest);
            contentsResponse.EnsureSuccessStatusCode();
            var folderContents = JsonDocument.Parse(await contentsResponse.Content.ReadAsStringAsync()).RootElement;

            var allAttributeDefinitions = await GetFolderAttributeDefinitionsAsync(projectId, folderId, accessToken);

            var versionUrns = new List<string>();
            if (folderContents.TryGetProperty("data", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.GetProperty("type").GetString() == "items" &&
                        item.TryGetProperty("relationships", out var rels) && rels.TryGetProperty("tip", out var tip) &&
                        tip.TryGetProperty("data", out var tipData) && tipData.TryGetProperty("id", out var id))
                    {
                        var idString = id.GetString();
                        if (idString != null) versionUrns.Add(idString);
                    }
                }
            }

            var attributesByUrn = new Dictionary<string, JsonElement>();
            if (versionUrns.Any())
            {
                var attributesValuesResponse = await GetVersionsWithCustomAttributesAsync(projectId, versionUrns, accessToken);
                if (attributesValuesResponse.TryGetProperty("results", out var attributeResults))
                {
                    foreach (var result in attributeResults.EnumerateArray())
                    {
                        attributesByUrn[result.GetProperty("urn").GetString()!] = result.Clone();
                    }
                }
            }

            var newIncluded = new List<JsonElement>();
            if (folderContents.TryGetProperty("included", out var originalIncluded))
            {
                foreach (var includedItem in originalIncluded.EnumerateArray())
                {
                    var urn = includedItem.GetProperty("id").GetString();
                    if (urn != null && attributesByUrn.TryGetValue(urn, out var versionWithAttributes))
                    {
                        newIncluded.Add(versionWithAttributes);
                    }
                    else
                    {
                        var customAttributesArray = allAttributeDefinitions
                            .Select(def => new { id = def.Id, name = def.Name, type = "string", value = (string?)null })
                            .ToList();

                        var extendedItem = new Dictionary<string, object>();
                        foreach (var prop in includedItem.EnumerateObject()) { extendedItem.Add(prop.Name, prop.Value.Clone()); }
                        if (urn != null) extendedItem["urn"] = urn;
                        extendedItem["customAttributes"] = customAttributesArray;
                        newIncluded.Add(JsonSerializer.SerializeToElement(extendedItem));
                    }
                }
            }

            var newRoot = new Dictionary<string, object>();
            foreach (var prop in folderContents.EnumerateObject())
            {
                if (prop.NameEquals("included")) { newRoot[prop.Name] = newIncluded; }
                else { newRoot[prop.Name] = prop.Value.Clone(); }
            }
            return JsonSerializer.SerializeToElement(newRoot);
        }

        #endregion

        #region MÉTODOS DE AUTOMAÇÃO E SINCRONIZAÇÃO

        public async Task<Stream> DownloadFileStreamAsync(string projectId, string urn, string accessToken)
        {
            if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                if (urn.Contains("dm.lineage"))
                {
                    var itemDetailsUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/items/{urn}";
                    var itemResponse = await client.GetAsync(itemDetailsUrl);

                    if (itemResponse.IsSuccessStatusCode)
                    {
                        var jsonItem = await itemResponse.Content.ReadAsStringAsync();
                        using (var doc = JsonDocument.Parse(jsonItem))
                        {
                            if (doc.RootElement.TryGetProperty("data", out var data) &&
                                data.TryGetProperty("relationships", out var rels) &&
                                rels.TryGetProperty("tip", out var tip) &&
                                tip.TryGetProperty("data", out var tipData) &&
                                tipData.TryGetProperty("id", out var versionId))
                            {
                                urn = versionId.GetString();
                            }
                        }
                    }
                }

                var encodedVersionUrn = System.Net.WebUtility.UrlEncode(urn);
                var versionDetailsUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/versions/{encodedVersionUrn}";
                var versionResponse = await client.GetAsync(versionDetailsUrl);

                if (!versionResponse.IsSuccessStatusCode)
                {
                    var err = await versionResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Erro ao pegar detalhes da versão ({versionResponse.StatusCode}): {err}");
                }

                string storageId = null;
                var jsonVersion = await versionResponse.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(jsonVersion))
                {
                    if (doc.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("relationships", out var rels) &&
                        rels.TryGetProperty("storage", out var storage) &&
                        storage.TryGetProperty("data", out var storageData))
                    {
                        storageId = storageData.GetProperty("id").GetString();
                    }
                }

                if (string.IsNullOrEmpty(storageId))
                    throw new Exception("Storage ID não encontrado.");

                var cleanStorageId = storageId.Replace("urn:adsk.objects:os.object:", "");
                var parts = cleanStorageId.Split('/');
                
                if (parts.Length < 2) throw new Exception($"Formato de StorageID inválido: {storageId}");

                var bucketKey = parts[0]; 
                var objectKey = string.Join("/", parts.Skip(1)); 
                objectKey = System.Net.WebUtility.UrlEncode(objectKey);

                var s3UrlEndpoint = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3download";
                var s3Response = await client.GetAsync(s3UrlEndpoint);

                if (!s3Response.IsSuccessStatusCode)
                {
                    var s3Err = await s3Response.Content.ReadAsStringAsync();
                    throw new Exception($"Erro ao obter link S3 ({s3Response.StatusCode}): {s3Err}");
                }

                string downloadUrl = null;
                var jsonS3 = await s3Response.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(jsonS3))
                {
                    if (doc.RootElement.TryGetProperty("url", out var urlProp))
                    {
                        downloadUrl = urlProp.GetString();
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    throw new Exception("URL S3 vazia.");

                using (var s3Client = new HttpClient())
                {
                    var fileResponse = await s3Client.GetAsync(downloadUrl);
                    
                    if (!fileResponse.IsSuccessStatusCode)
                        throw new Exception($"Erro ao baixar bytes S3 ({fileResponse.StatusCode})");

                    var memoryStream = new MemoryStream();
                    await fileResponse.Content.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                    return memoryStream;
                }
            }
        }

        public async Task<string> DownloadFileToTempPathAsync(string projectId, string versionUrn, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var versionDetailsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/versions/{System.Web.HttpUtility.UrlEncode(versionUrn)}");
            versionDetailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            var versionResponse = await _httpClient.SendAsync(versionDetailsRequest);
            versionResponse.EnsureSuccessStatusCode();
            
            var versionJson = await JsonDocument.ParseAsync(await versionResponse.Content.ReadAsStreamAsync());
            var storageUrn = versionJson.RootElement.GetProperty("data").GetProperty("relationships").GetProperty("storage").GetProperty("data").GetProperty("id").GetString();
            
            var downloadUrl = await GetSignedDownloadUrlAsync(storageUrn, accessToken);

            var fileResponse = await _httpClient.GetAsync(downloadUrl);
            fileResponse.EnsureSuccessStatusCode();
            var fileBytes = await fileResponse.Content.ReadAsByteArrayAsync();

            string tempFileName = $"LDP_{Guid.NewGuid()}.xlsx";
            string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);

            await File.WriteAllBytesAsync(tempPath, fileBytes);

            return tempPath; 
        }

        private async Task FindAllFilesRecursiveAsync(string projectId, string folderId, Dictionary<string, List<string>> fileMap, string accessToken)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, folderId, accessToken);

            foreach (var item in contents)
            {
                if (item.Type == "items")
                {
                    if (!string.IsNullOrEmpty(item.Name) && !string.IsNullOrEmpty(item.VersionUrn))
                    {
                        if (item.Name.StartsWith("~$")) continue;

                        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(item.Name).Trim();
                        
                        if (!fileMap.ContainsKey(fileNameWithoutExt))
                        {
                            fileMap[fileNameWithoutExt] = new List<string>();
                        }
                        fileMap[fileNameWithoutExt].Add(item.VersionUrn!);
                    }
                }
                else if (item.Type == "folders")
                {
                    if (!item.IsHidden)
                    {
                        await FindAllFilesRecursiveAsync(projectId, item.Id, fileMap, accessToken);
                    }
                }
            }
        }

        public async Task<Dictionary<string, List<string>>> GetAllProjectFilesAsync(string hubId, string projectId, string accessToken)
        {
            var fileMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            
            var rootFolder = await GetProjectRootFolderAsync(hubId, projectId, accessToken);
            if (rootFolder.HasValue)
            {
                var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
                if (rootFolderId != null)
                {
                    _logger.LogInformation(">>> Indexando TODOS os arquivos do projeto para busca...");
                    await FindAllFilesRecursiveAsync(projectId, rootFolderId, fileMap, accessToken);
                    _logger.LogInformation($">>> Indexação concluída. {fileMap.Count} arquivos únicos encontrados no ACC.");
                }
            }
            return fileMap;
        }

        public async Task<string?> FindFileByNameRecursiveAsync(string projectId, string folderId, string fileNameSubstring, string accessToken, bool isRecursive = true)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, folderId, accessToken);
            
            var candidates = new List<(string Id, DateTime Date, string Name)>();

            foreach (var item in contents.Where(c => c.Type == "items"))
            {
                if (string.IsNullOrEmpty(item.Name) || item.Name.StartsWith("~$")) continue;

                string nameNoExt = Path.GetFileNameWithoutExtension(item.Name);
                
                // Filtro flexível com Contains para garantir aderência à regra de negócio
                if (nameNoExt.Contains(fileNameSubstring, StringComparison.OrdinalIgnoreCase) && 
                    item.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime lastModified = DateTime.MinValue;
                    if (!string.IsNullOrEmpty(item.LastModifiedTime))
                    {
                        DateTime.TryParse(item.LastModifiedTime, out lastModified);
                    }

                    // 🎯 CORREÇÃO CRÍTICA: Trocamos item.VersionUrn por item.Id para retornar o Item URN legítimo
                    if (item.Id != null)
                    {
                        candidates.Add((item.Id, lastModified, item.Name));
                    }
                }
            }

            if (candidates.Any())
            {
                // Organiza para garantir que pegamos o arquivo mais recente caso haja duplicidade
                var winner = candidates.OrderByDescending(x => x.Date).First();
                return winner.Id;
            }

            if (isRecursive)
            {
                foreach (var folder in contents.Where(c => c.Type == "folders"))
                {
                    if (folder.IsHidden) continue;

                    var result = await FindFileByNameRecursiveAsync(projectId, folder.Id, fileNameSubstring, accessToken, isRecursive);
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }
                }
            }

            return null;
        }



        /// <summary>
        /// Varre as pastas do ACC Docs procurando pelo arquivo da LDP baseado no padrão dinâmico do banco de dados.
        /// Suporta escalabilidade futura eliminando strings hardcoded no motor.
        /// </summary>
        public async Task<string> FindLdpFileUrnAsync(string hubId, string projectId, string accessToken, string folderIdLdp = null, string pattern = null)
        {
            // Se não passar a pasta mapeada, tenta capturar a partir da raiz global de Project Files
            if (string.IsNullOrEmpty(folderIdLdp))
            {
                var rootFolder = await GetProjectRootFolderAsync(hubId, projectId, accessToken);
                if (!rootFolder.HasValue) throw new Exception("A pasta raiz 'Project Files' não foi encontrada.");
                folderIdLdp = rootFolder.Value.GetProperty("id").GetString();
            }

            // 🎯 ARQUITETURA DE ESCALA: Se o robô passou o parâmetro vindo do banco, usamos ele. 
            // Caso contrário, mantemos o comportamento e fallback original do sistema.
            string ldpFileNameIdentifier = !string.IsNullOrEmpty(pattern) 
                ? pattern 
                : GetProjectSettingValue(projectId, "LdpFileName");

            Console.WriteLine($">>> [API AUTODESK] Efetuando varredura recursiva do padrão '{ldpFileNameIdentifier}' na pasta: {folderIdLdp}");

            // Executa a busca recursiva nativa do seu serviço APS usando a string dinâmica
            var urn = await FindFileByNameRecursiveAsync(projectId, folderIdLdp, ldpFileNameIdentifier, accessToken, true); 

            if (string.IsNullOrEmpty(urn))
                throw new Exception($"Nenhum ficheiro Excel contendo '{ldpFileNameIdentifier}' no nome foi encontrado.");
            
            return urn;
        }

       




        // 🛡️ A CATRACA DE ATRIBUTOS CUSTOMIZADOS (Resolve o Erro 429)
        public async Task UpdateCustomAttributesForVersionAsync(string projectId, string versionUrn, List<object> customAttributes, string accessToken)
        {
            await _accUpdateLock.WaitAsync(); // Segura o fluxo (só 2 threads por vez)
            try
            {
                string projectIdWithoutPrefix = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
                string encodedVersionUrn = HttpUtility.UrlEncode(versionUrn);
                var requestUrl = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectIdWithoutPrefix}/versions/{encodedVersionUrn}/custom-attributes:batch-update";

                var jsonPayload = JsonSerializer.Serialize(customAttributes);

                int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    // A RequestMessage e o Content precisam ser criados dentro do loop
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    
                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        await Task.Delay(400); // Respiro de 400ms obrigatório para a API da Autodesk
                        return; // Sucesso!
                    }

                    var error = await response.Content.ReadAsStringAsync();
                    
                    // Se a Autodesk reclamar de excesso de requisições, nós esperamos e tentamos de novo
                    if ((int)response.StatusCode == 429 || error.Contains("Quota limit") || error.Contains("Too Many Requests"))
                    {
                        if (attempt == maxRetries) throw new Exception($"Erro 429 persistente: {error}");
                        _logger.LogWarning($"⏳ API da Autodesk sobrecarregada (429). Aguardando {attempt * 2}s antes de tentar novamente o URN: {versionUrn}...");
                        await Task.Delay(2000 * attempt);
                    }
                    else
                    {
                        throw new Exception($"Erro ao atualizar atributos para a versão {versionUrn}: {response.StatusCode} - {error}");
                    }
                }
            }
            finally
            {
                _accUpdateLock.Release(); // Libera a catraca
            }
        }

        private string GetProjectSettingValue(string projectId, string settingKey)
        {
            string settingValue = _configuration[$"ProjectSettings:{projectId}:{settingKey}"];
            if (string.IsNullOrEmpty(settingValue)) settingValue = _configuration[$"ProjectSettings:Default:{settingKey}"];
            
            // 🛡️ SEGURANÇA: Se a chave LdpFileName não estiver no appsettings.json, usa o fallback da N&A
            if (string.IsNullOrEmpty(settingValue)) 
            {
                if (settingKey == "LdpFileName") return "-LD-"; 
                throw new Exception($"A configuração '{settingKey}' não foi encontrada no arquivo de configurações.");
            }
            return settingValue;
        }

        public async Task<SyncResult> SyncLdpAttributesAsync(string hubId, string projectId, List<Dictionary<string, string>> ldpData, string accessToken, string keyColumnName) 
        {
            if (string.IsNullOrEmpty(keyColumnName)) throw new ArgumentException("Nome da coluna chave não fornecido.");

            _logger.LogInformation($">>> Iniciando Sync. Chave usada: '{keyColumnName}'");

            var rootFolder = await GetProjectRootFolderAsync(hubId, projectId, accessToken);
            if (!rootFolder.HasValue) throw new Exception("Pasta raiz não encontrada.");
            var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
            
            var attributeDefinitions = await GetFolderAttributeDefinitionsAsync(projectId, rootFolderId, accessToken);
            
            var accAttributes = attributeDefinitions.ToDictionary(
                def => def.Name.Trim().ToUpper(), 
                def => def.Id.ToString()
            );

            _logger.LogInformation($"[DEBUG] Atributos Disponíveis no ACC: {string.Join(" | ", accAttributes.Keys)}");

            var translationMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "N&A DOCUMENT NUMBER", "N&A DOCUMENT NUMBER" },
                { "NÚMERO DOCUMENTO N&A", "NÚMERO DOCUMENTO N&A" },
                { "N&A DOCUMENT", "N&A DOCUMENT NUMBER" }, 
                { "REV.", "REV." },
                { "REV", "REV." }, 
                { "REVISÃO", "REV." },
                { "REVISION", "REV." },
                { "CLIENT DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER" },
                { "NÚMERO DOCUMENTO CLIENTE", "NÚMERO DOCUMENTO CLIENTE" },
                { "DOCUMENT TITLE", "DOCUMENT TITLE" },
                { "TÍTULO DO DOCUMENTO", "TÍTULO DO DOCUMENTO" },
                { "TITLE", "DOCUMENT TITLE" },
                { "EMISSION DATA", "EMISSION DATA" },
                { "DATA DE EMISSÃO", "DATA DE EMISSÃO" },
                { "TRANMISTAL NUMBER", "TRANSMITTAL NUMBER" },
                { "TRANSMITTAL NUMBER", "TRANSMITTAL NUMBER" },
                { "NUMERO DA GRD", "NUMERO DA GRD" },
                { "ISSUED POUPURSE", "ISSUED PURPOSE" },
                { "PROPÓSITO DE EMISSÃO", "PROPÓSITO DE EMISSÃO" }
            };

            var fileMap = await GetAllProjectFilesAsync(hubId, projectId, accessToken); 
            
            var result = new SyncResult { 
                TotalLdpFiles = ldpData.Count,
                FilesUpdatedSuccess = new List<string>(), 
                FilesNotFound = new List<string>(),
                AllLdpFiles = new List<string>()
            };

            foreach (var row in ldpData)
            {
                if (row.TryGetValue("_DOC_NUMBER_KEY_", out var docCode) || 
                    row.TryGetValue(keyColumnName, out docCode))
                {
                    if (string.IsNullOrEmpty(docCode)) continue;

                    result.AllLdpFiles.Add(docCode); 
                    var codeToSearch = docCode.Trim();
                    List<string> targetUrns = new List<string>();

                    if (fileMap.TryGetValue(codeToSearch, out var exactMatches)) targetUrns.AddRange(exactMatches);
                    else {
                        var partialMatches = fileMap.Keys.Where(k => k.StartsWith(codeToSearch, StringComparison.OrdinalIgnoreCase));
                        foreach (var key in partialMatches) targetUrns.AddRange(fileMap[key]);
                    }

                    if (targetUrns.Any())
                    {
                        var customAttributesForFile = new List<object>(); 
                        var debugMatched = new List<string>();
                        var debugFailed = new List<string>();

                        foreach (var excelColumn in row)
                        {
                            if (excelColumn.Key == "_DOC_NUMBER_KEY_") continue;
                            if (string.IsNullOrWhiteSpace(excelColumn.Value)) continue;

                            string accAttributeId = null;
                            string excelHeaderUpper = excelColumn.Key.Trim().ToUpper();

                            if (accAttributes.ContainsKey(excelHeaderUpper)) 
                            {
                                accAttributeId = accAttributes[excelHeaderUpper];
                            }
                            else if (translationMap.TryGetValue(excelHeaderUpper, out var mappedName))
                            {
                                var mappedUpper = mappedName.Trim().ToUpper();
                                if (accAttributes.ContainsKey(mappedUpper)) 
                                {
                                    accAttributeId = accAttributes[mappedUpper];
                                }
                                else if (mappedUpper.Contains("TITLE") && accAttributes.ContainsKey("TITLE")) accAttributeId = accAttributes["TITLE"];
                                else if (mappedUpper.Contains("REV") && accAttributes.ContainsKey("REVISION")) accAttributeId = accAttributes["REVISION"];
                                else if (mappedUpper.Contains("REV") && accAttributes.ContainsKey("REVISÃO")) accAttributeId = accAttributes["REVISÃO"];
                                else if (mappedUpper.Contains("TRAN") && accAttributes.ContainsKey("TRANSMITTAL NUMBER")) accAttributeId = accAttributes["TRANSMITTAL NUMBER"];
                            }

                            if (accAttributeId != null)
                            {
                                object finalValue = excelColumn.Value;
                                if (excelHeaderUpper.Contains("DATA") || excelHeaderUpper.Contains("DATE"))
                                {
                                    if (DateTime.TryParse(excelColumn.Value, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out var dt) ||
                                        DateTime.TryParse(excelColumn.Value, out dt))
                                    {
                                        finalValue = dt.ToString("yyyy-MM-dd"); 
                                    }
                                }
                                customAttributesForFile.Add(new { id = int.Parse(accAttributeId), value = finalValue });
                                debugMatched.Add(excelColumn.Key);
                            }
                            else
                            {
                                debugFailed.Add(excelColumn.Key);
                            }
                        }

                        if (customAttributesForFile.Any())
                        {
                            foreach (var versionUrn in targetUrns.Distinct())
                            {
                                try {
                                    await UpdateCustomAttributesForVersionAsync(projectId, versionUrn, customAttributesForFile, accessToken);
                                    
                                    result.FilesUpdated++;
                                    if (!result.FilesUpdatedSuccess.Contains(docCode)) result.FilesUpdatedSuccess.Add(docCode);

                                    _logger.LogInformation($"   -> '{docCode}' Atualizado. Campos: {string.Join(", ", debugMatched)}");
                                    if(debugFailed.Any()) _logger.LogWarning($"   -> Campos ignorados (Não achou no ACC): {string.Join(", ", debugFailed)}");

                                } catch (Exception ex) {
                                    _logger.LogError($"   -> Erro ao atualizar ACC '{docCode}': {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"   -> ⚠️ '{docCode}' encontrado, mas NENHUM atributo bateu. Verifique os nomes no ACC.");
                            _logger.LogWarning($"      Excel enviou: {string.Join(", ", row.Keys)}");
                        }
                    }
                    else 
                    {
                        result.FilesNotFound.Add(docCode); 
                    }
                }
            } 

            result.SummaryMessage = $"Sincronização concluída. {result.FilesUpdated} atualizações enviadas. {result.FilesNotFound.Count} não encontrados.";
            return result; 
        }   
        
        public async Task CreateWebhookAsync(string folderUrn, string callbackUrl, string accessToken)
        {
            var requestUrl = "https://developer.api.autodesk.com/webhooks/v1/systems/data/events/dm.version.added/hooks";
            var payload = new
            {
                callbackUrl = callbackUrl,
                scope = new { folder = folderUrn }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao criar o Webhook: {error}");
            }
        }

        public async Task<string?> FindFolderByNameRecursiveAsync(string projectId, string parentFolderId, string folderName, string accessToken, bool isRecursive = true)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, parentFolderId, accessToken);

            foreach (var item in contents.Where(c => c.Type == "folders"))
            {
                if (item.IsHidden) continue;
                if (item.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)) return item.Id;

                if (isRecursive)
                {
                    var result = await FindFolderByNameRecursiveAsync(projectId, item.Id, folderName, accessToken, true);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
            return null;
        }

        public async Task<string> GetVersionExtensionTypeAsync(string projectId, string versionUrn, string accessToken)
        {
            string dmProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
            string cleanVersionUrn = versionUrn?.Split('?')[0];
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/versions/{cleanVersionUrn}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return "versions:autodesk.core:File"; 

            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);
            
            try 
            {
                return doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("extension").GetProperty("type").GetString();
            }
            catch
            {
                return "versions:autodesk.core:File";
            }
        }

        public async Task<string?> FindTemplateItemUrnAsync(string projectId, string templateFolderId, string templateFileName, string accessToken)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, templateFolderId, accessToken);
            foreach (var item in contents)
            {
                if (item.Type == "items" && item.Name.Equals(templateFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return item.VersionUrn ?? item.Id; 
                }
            }
            return null;
        }

        public async Task CopyPlaceholderAsync(string projectId, string sourceVersionUrn, string targetFolderId, string newFileName, string versionTypeFromTemplate, string accessToken)
        {
            string dmProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
            string encodedUrn = HttpUtility.UrlEncode(sourceVersionUrn);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/items?copyFrom={encodedUrn}";

            string itemType = versionTypeFromTemplate.Replace("versions:", "items:");

            var payload = new
            {
                jsonapi = new { version = "1.0" },
                data = new
                {
                    type = "items",
                    attributes = new
                    {
                        displayName = newFileName,
                        extension = new { type = itemType, version = "1.0" }
                    },
                    relationships = new
                    {
                        parent = new { data = new { type = "folders", id = targetFolderId } },
                        tip = new { data = new { type = "versions", id = "1" } }
                    }
                },
                included = new object[]
                {
                    new
                    {
                        type = "versions",
                        id = "1",
                        attributes = new
                        {
                            name = newFileName,
                            extension = new { type = versionTypeFromTemplate, version = "1.0" }
                        }
                    }
                }
            };
            
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao copiar arquivo: {error}");
            }
        }

        public async Task<string?> GetItemTipVersionUrnAsync(string projectId, string itemId, string accessToken)
        {
            try
            {
                string cleanItemId = itemId.Trim().Split('?')[0];
                string dmProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
                var url = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/items/{cleanItemId}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

                using var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        var retryUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/items/{Uri.EscapeDataString(cleanItemId)}";
                        using var retryRequest = new HttpRequestMessage(HttpMethod.Get, retryUrl);
                        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                        retryRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                        
                        var retryResponse = await _httpClient.SendAsync(retryRequest);
                        if (retryResponse.IsSuccessStatusCode) content = await retryResponse.Content.ReadAsStringAsync();
                        else return null;
                    }
                    else return null;
                }

                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("data", out var dataNode) &&
                    dataNode.TryGetProperty("relationships", out var relNode) &&
                    relNode.TryGetProperty("tip", out var tipNode) &&
                    tipNode.TryGetProperty("data", out var tipData))
                {
                    return tipData.GetProperty("id").GetString();
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar TIP do item {Id}", itemId);
                return null;
            }
        }

        public async Task<string> CopyFileViaCommandAsync(string projectId, string sourceVersionUrn, string targetFolderId, string accessToken)
        {
            string dmProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/commands";

            var payload = new
            {
                jsonapi = new { version = "1.0" },
                data = new
                {
                    type = "commands",
                    attributes = new
                    {
                        extension = new { type = "commands:autodesk.core:Copy", version = "1.0" }
                    },
                    relationships = new
                    {
                        resources = new { data = new[] { new { type = "versions", id = sourceVersionUrn } } },
                        target = new { data = new { type = "folders", id = targetFolderId } }
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/vnd.api+json"); 

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro no comando de cópia: {response.StatusCode} - {error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            
            try 
            {
                var resources = doc.RootElement.GetProperty("data").GetProperty("relationships").GetProperty("resources").GetProperty("data");
                if (resources.GetArrayLength() > 0) return resources[0].GetProperty("id").GetString();
            }
            catch { }

            throw new Exception("Cópia realizada, mas não foi possível obter o ID do novo item na resposta.");
        }

        public async Task RenameItemAsync(string projectId, string itemId, string newDisplayName, string accessToken)
        {
            string dmProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/items/{itemId}";

            var payload = new
            {
                jsonapi = new { version = "1.0" },
                data = new
                {
                    type = "items",
                    id = itemId,
                    attributes = new { displayName = newDisplayName }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/vnd.api+json");

            using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao renomear item {itemId}: {response.StatusCode} - {error}");
            }
        }

        public async Task<string> UploadNewFileAsync(string projectId, string folderId, string fileName, Stream fileStream, string itemType, string accessToken)
        {
            string dataManagementProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var storageUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/storage";
                var storageBody = new
                {
                    jsonapi = new { version = "1.0" },
                    data = new
                    {
                        type = "objects",
                        attributes = new { name = fileName },
                        relationships = new { target = new { data = new { type = "folders", id = folderId } } }
                    }
                };

                var storageContent = new StringContent(JsonSerializer.Serialize(storageBody), Encoding.UTF8, "application/json");
                var storageResponse = await client.PostAsync(storageUrl, storageContent);
                
                if (!storageResponse.IsSuccessStatusCode) throw new Exception($"Erro Storage: {await storageResponse.Content.ReadAsStringAsync()}");

                var storageJson = await storageResponse.Content.ReadAsStringAsync();
                string objectId = JsonDocument.Parse(storageJson).RootElement.GetProperty("data").GetProperty("id").GetString();

                var cleanId = objectId.Replace("urn:adsk.objects:os.object:", "");
                var parts = cleanId.Split('/');
                var bucketKey = parts[0];
                var objectKey = string.Join("/", parts.Skip(1));
                var encodedKey = System.Net.WebUtility.UrlEncode(objectKey);

                var signedUrlEndpoint = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{encodedKey}/signeds3upload";
                var signedResponse = await client.GetAsync(signedUrlEndpoint);
                
                if (!signedResponse.IsSuccessStatusCode) throw new Exception($"Erro Signed URL: {await signedResponse.Content.ReadAsStringAsync()}");

                var signedJson = await signedResponse.Content.ReadAsStringAsync();
                string uploadUrl = JsonDocument.Parse(signedJson).RootElement.GetProperty("urls").EnumerateArray().First().GetString();
                string uploadKey = JsonDocument.Parse(signedJson).RootElement.GetProperty("uploadKey").GetString();

                using (var s3Client = new HttpClient())
                {
                    if (fileStream.Position != 0 && fileStream.CanSeek) fileStream.Position = 0;
                    var streamContent = new StreamContent(fileStream);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    
                    var uploadRes = await s3Client.PutAsync(uploadUrl, streamContent);
                    if (!uploadRes.IsSuccessStatusCode) throw new Exception($"Erro Upload S3: {uploadRes.StatusCode}");
                }

                var completeBody = new { uploadKey = uploadKey };
                var completeContent = new StringContent(JsonSerializer.Serialize(completeBody), Encoding.UTF8, "application/json");
                var completeResponse = await client.PostAsync(signedUrlEndpoint, completeContent);
                
                if (!completeResponse.IsSuccessStatusCode) throw new Exception("Erro ao completar upload no OSS");

                string versionType = itemType.Replace("items:", "versions:");

                var itemUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/items";
                var itemBody = new
                {
                    jsonapi = new { version = "1.0" },
                    data = new
                    {
                        type = "items",
                        attributes = new
                        {
                            displayName = fileName,
                            extension = new { type = itemType, version = "1.0" }
                        },
                        relationships = new
                        {
                            tip = new { data = new { type = "versions", id = "1" } },
                            parent = new { data = new { type = "folders", id = folderId } }
                        }
                    },
                    included = new[]
                    {
                        new {
                            type = "versions",
                            id = "1",
                            attributes = new { name = fileName, extension = new { type = versionType, version = "1.0" } },
                            relationships = new { storage = new { data = new { type = "objects", id = objectId } } }
                        }
                    }
                };

                var itemContent = new StringContent(JsonSerializer.Serialize(itemBody), Encoding.UTF8);
                itemContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");

                var itemResponse = await client.PostAsync(itemUrl, itemContent);

                if (!itemResponse.IsSuccessStatusCode) throw new Exception($"Erro Create Item: {await itemResponse.Content.ReadAsStringAsync()}");

                return "Created";
            }
        }

        public async Task<string> ResolveDisciplineFolderIdAsync(string projectId, string rootFolderId, string disciplinaCodigo, string accessToken)
        {
            var parentFolderNames = new[] { "04 - ENGENHARIA", "04 - ENGINEERING" };
            string parentEngineeringId = null;

            var rootContents = await GetFolderContentsSimpleAsync(projectId, rootFolderId, accessToken);
            foreach (var folder in rootContents.Where(c => c.Type == "folders"))
            {
                if (parentFolderNames.Any(p => folder.Name.Trim().Equals(p, StringComparison.OrdinalIgnoreCase)))
                {
                    parentEngineeringId = folder.Id;
                    break;
                }
            }

            if (string.IsNullOrEmpty(parentEngineeringId)) throw new Exception("Pasta '04 - ENGENHARIA' não encontrada.");

            string[] possibleNames = disciplinaCodigo.ToUpper() switch
            {
                "PR" => new[] { "04.01 - PROCESSO", "04.01 - PROCESS" },
                "MC" => new[] { "04.02 - MECÂNICA", "04.02 - MECHANICAL" },
                "TL" => new[] { "04.03 - TELECOMUNICAÇÕES", "04.03 - TELECOMUNICATIONS" },
                "AR" => new[] { "04.04 - ARQUITETURA", "04.04 - ARCHITECTURE" },
                "IA" => new[] { "04.05 - INSTRUMENTAÇÃO E AUTOMAÇÃO", "04.05 - INTRUMENTATION AND AUTOMATION" }, 
                "IN" => new[] { "04.05 - INSTRUMENTAÇÃO E AUTOMAÇÃO", "04.05 - INTRUMENTATION AND AUTOMATION" },
                "TB" => new[] { "04.06 - TUBULAÇÃO", "04.06 - PIPING" },
                "IF" => new[] { "04.07 - INFRAESTRUTURA", "04.07 - INFRASTRUCTURE" },
                "PE" => new[] { "04.08 - ESTRUTURA METÁLICA", "04.08 - STRUCTURAL ENGINEERING" }, 
                "ES" => new[] { "04.08 - ESTRUTURA METÁLICA", "04.08 - STRUCTURAL ENGINEERING" },
                "EQ" => new[] { "04.09 - EQUIPAMENTOS", "04.09 - EQUIPMENT" },
                "EL" => new[] { "04.10 - ELÉTRICA", "04.10 - ELECTRICAL" },
                "CV" => new[] { "04.11 - CIVIL", "04.11 - CIVIL" },
                "AC" => new[] { "04.12 - AVAC", "04.12 - HVAC" },
                "SD" => new[] { "04.13 - SISTEMAS DIGITAIS", "04.13 - DIGITAL SYSTEMS" },
                _ => null
            };

            if (possibleNames == null) throw new Exception($"Código de disciplina '{disciplinaCodigo}' não mapeado.");

            var engineeringContents = await GetFolderContentsSimpleAsync(projectId, parentEngineeringId, accessToken);
            
            foreach (var folder in engineeringContents.Where(c => c.Type == "folders"))
            {
                if (possibleNames.Any(name => folder.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))) return folder.Id;
            }

            throw new Exception($"Pasta da disciplina '{disciplinaCodigo}' não encontrada.");
        }

        public async Task<bool> FolderAcceptsFilesAsync(string projectId, string folderId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders/{folderId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) throw new Exception($"Erro ao verificar pasta: {response.StatusCode} - {content}");

            using var doc = JsonDocument.Parse(content);
            var allowedTypes = doc.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("allowedTypes")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToList();

            return allowedTypes.Contains("items:autodesk.core:File");
        }

        public async Task<JsonElement?> GetVersionDetailsAsync(string projectId, string versionUrn, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var encodedVersionUrn = System.Web.HttpUtility.UrlEncode(versionUrn);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/versions/{encodedVersionUrn}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        }

        public async Task<string> GetItemParentFolderAsync(string projectId, string itemId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/items/{itemId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Erro ao obter detalhes do item {itemId}: {response.StatusCode} - {errorContent}");
            }

            var itemDetailsDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var parentFolderId = itemDetailsDoc.RootElement
                .GetProperty("data")
                .GetProperty("relationships")
                .GetProperty("parent")
                .GetProperty("data")
                .GetProperty("id")
                .GetString();

            if (string.IsNullOrEmpty(parentFolderId)) throw new Exception($"Não foi possível encontrar a pasta-pai para o item {itemId}.");

            return parentFolderId;
        }

        public async Task<string> GetInternalTokenAsync()
        {
            var scope = "data:read data:write data:create bucket:read account:read"; 
            
            var clientId = _configuration["APS:ClientId"];
            var clientSecret = _configuration["APS:ClientSecret"];

            using (var client = new HttpClient())
            {
                var body = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "grant_type", "client_credentials" },
                    { "scope", scope }
                };

                var content = new FormUrlEncodedContent(body);
                var response = await client.PostAsync("https://developer.api.autodesk.com/authentication/v2/token", content);

                if (!response.IsSuccessStatusCode) throw new Exception("O Robô falhou ao tentar pegar o crachá (Token).");

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("access_token").GetString();
            }
        }

        public async Task<string> FindFolderIdByPathAsync(string hubId, string projectId, string[] folderNames, string accessToken)
        {
            var topFolders = await GetTopFoldersAsync(hubId, projectId, accessToken);
            
            var rootFolder = topFolders.FirstOrDefault(f => f.Name == "Project Files");
            if (rootFolder == null) 
            {
                _logger.LogError("Pasta 'Project Files' não encontrada na raiz.");
                return null;
            }

            string currentFolderId = rootFolder.Id;

            foreach (var folderName in folderNames)
            {
                var contents = await GetFolderContentsAsync(projectId, currentFolderId, accessToken);
                
                var subFolder = contents.FirstOrDefault(c => 
                    c.Type == "folders" && 
                    c.Name.Trim().Equals(folderName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (subFolder == null)
                {
                    _logger.LogError($"Caminho quebrado: Pasta '{folderName}' não encontrada.");
                    return null;
                }
                currentFolderId = subFolder.Id;
            }
            return currentFolderId;
        }

        public async Task<(string FileId, string FileName)> FindItemIdByNamePartAsync(string projectId, string folderId, string namePart, string accessToken)
        {
            var contents = await GetFolderContentsAsync(projectId, folderId, accessToken);
            
            var file = contents
                .Where(c => c.Type == "items" && c.Name.ToUpper().Contains(namePart.ToUpper()))
                .OrderByDescending(c => c.LastModifiedTime)
                .FirstOrDefault();

            if (file == null) return (null, null);

            return (file.Id, file.Name);
        }

        public async Task<string> FindItemIdByNameExactAsync(string projectId, string folderId, string exactName, string accessToken)
        {
            var contents = await GetFolderContentsAsync(projectId, folderId, accessToken);
            var file = contents.FirstOrDefault(c => c.Type == "items" && c.Name.Equals(exactName, StringComparison.OrdinalIgnoreCase));
            return file?.Id;
        }

        public async Task<List<AccFolderItem>> GetTopFoldersAsync(string hubId, string projectId, string accessToken)
        {
            if (!hubId.StartsWith("b.")) hubId = "b." + hubId;
            if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

            var requestUrl = $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects/{projectId}/topFolders";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await client.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Erro ao buscar TopFolders (Hub: {hubId}): {response.StatusCode}");
                    return new List<AccFolderItem>();
                }

                var json = await response.Content.ReadAsStringAsync();
                return ParseFolderItems(json);
            }
        }

        public async Task<List<AccFolderItem>> GetFolderContentsAsync(string projectId, string folderId, string accessToken)
        {
            if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

            var requestUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/folders/{folderId}/contents";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await client.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Erro ao buscar conteúdo da pasta {folderId}: {response.StatusCode}");
                    return new List<AccFolderItem>();
                }

                var json = await response.Content.ReadAsStringAsync();
                return ParseFolderItems(json);
            }
        }

        private List<AccFolderItem> ParseFolderItems(string jsonResponse)
        {
            var list = new List<AccFolderItem>();
            try
            {
                using (var doc = JsonDocument.Parse(jsonResponse))
                {
                    if (!doc.RootElement.TryGetProperty("data", out var dataElement)) return list;

                    if (dataElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataElement.EnumerateArray())
                        {
                            var folderItem = ExtractItemSafe(item);
                            if (folderItem != null) list.Add(folderItem);
                        }
                    }
                    else if (dataElement.ValueKind == JsonValueKind.Object)
                    {
                        var folderItem = ExtractItemSafe(dataElement);
                        if (folderItem != null) list.Add(folderItem);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao fazer parse seguro dos itens: {ex.Message}");
            }
            return list;
        }

        private AccFolderItem ExtractItemSafe(JsonElement item)
        {
            try
            {
                string id = item.TryGetProperty("id", out var pId) ? pId.GetString() : null;
                string type = item.TryGetProperty("type", out var pType) ? pType.GetString() : "unknown";

                if (string.IsNullOrEmpty(id)) return null; 

                string name = "Sem Nome";
                if (item.TryGetProperty("attributes", out var attrs))
                {
                    if (attrs.TryGetProperty("name", out var pName) && pName.ValueKind == JsonValueKind.String)
                        name = pName.GetString();
                    else if (attrs.TryGetProperty("displayName", out var pDisp) && pDisp.ValueKind == JsonValueKind.String)
                        name = pDisp.GetString();
                }

                string lastModified = DateTime.Now.ToString();
                if (item.TryGetProperty("attributes", out var a) && a.TryGetProperty("lastModifiedTime", out var t))
                    lastModified = t.GetString();

                string versionUrn = null;
                if (item.TryGetProperty("relationships", out var rels) &&
                    rels.TryGetProperty("tip", out var tip) &&
                    tip.TryGetProperty("data", out var tipData) &&
                    tipData.TryGetProperty("id", out var tipId))
                {
                    versionUrn = tipId.GetString();
                }

                return new AccFolderItem
                {
                    Id = id,
                    Name = name,
                    Type = type,
                    LastModifiedTime = lastModified,
                    VersionUrn = versionUrn
                };
            }
            catch { return null; }
        }
    
        public async Task<string> UploadFileToFolderAsync(string projectId, string folderId, string fileName, Stream fileStream, string accessToken)
        {
            if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var storageUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/storage";
                var storageBody = new
                {
                    jsonapi = new { version = "1.0" },
                    data = new
                    {
                        type = "objects",
                        attributes = new { name = fileName },
                        relationships = new { target = new { data = new { type = "folders", id = folderId } } }
                    }
                };

                var storageContent = new StringContent(JsonSerializer.Serialize(storageBody), Encoding.UTF8, "application/json");
                var storageResponse = await client.PostAsync(storageUrl, storageContent);
                
                if (!storageResponse.IsSuccessStatusCode)
                {
                    var err = await storageResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Erro ao criar Storage ({storageResponse.StatusCode}): {err}");
                }

                var storageJson = await storageResponse.Content.ReadAsStringAsync();
                string objectId;
                using (var doc = JsonDocument.Parse(storageJson))
                {
                    objectId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
                }

                var cleanId = objectId.Replace("urn:adsk.objects:os.object:", "");
                var parts = cleanId.Split('/');
                var bucketKey = parts[0];
                var objectKey = string.Join("/", parts.Skip(1));
                var encodedObjectKey = System.Net.WebUtility.UrlEncode(objectKey);

                var signedUrlEndpoint = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{encodedObjectKey}/signeds3upload";
                var signedResponse = await client.GetAsync(signedUrlEndpoint);

                if (!signedResponse.IsSuccessStatusCode)
                {
                     var err = await signedResponse.Content.ReadAsStringAsync();
                     throw new Exception($"Erro ao obter URL S3 ({signedResponse.StatusCode}): {err}");
                }

                string uploadUrl, uploadKey;
                var signedJson = await signedResponse.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(signedJson))
                {
                    uploadUrl = doc.RootElement.GetProperty("urls").EnumerateArray().First().GetString();
                    uploadKey = doc.RootElement.GetProperty("uploadKey").GetString();
                }

                using (var s3Client = new HttpClient()) 
                {
                    if (fileStream.Position != 0 && fileStream.CanSeek) fileStream.Position = 0;
                    var streamContent = new StreamContent(fileStream);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); 
                    var uploadResponse = await s3Client.PutAsync(uploadUrl, streamContent);

                    if (!uploadResponse.IsSuccessStatusCode)
                    {
                        var err = await uploadResponse.Content.ReadAsStringAsync();
                        throw new Exception($"Erro ao fazer Upload dos bytes S3 ({uploadResponse.StatusCode}): {err}");
                    }
                }

                var completeUrl = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{encodedObjectKey}/signeds3upload";
                var completeBody = new { uploadKey = uploadKey };
                var completeContent = new StringContent(JsonSerializer.Serialize(completeBody), Encoding.UTF8, "application/json");
                
                var completeResponse = await client.PostAsync(completeUrl, completeContent);
                if (!completeResponse.IsSuccessStatusCode)
                {
                     var err = await completeResponse.Content.ReadAsStringAsync();
                     throw new Exception($"Erro ao completar upload OSS ({completeResponse.StatusCode}): {err}");
                }

                var itemUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/items";
                var itemBody = new
                {
                    jsonapi = new { version = "1.0" },
                    data = new
                    {
                        type = "items",
                        attributes = new
                        {
                            displayName = fileName,
                            extension = new
                            {
                                type = "items:autodesk.bim360:File",
                                version = "1.0"
                            }
                        },
                        relationships = new
                        {
                            tip = new { data = new { type = "versions", id = "1" } },
                            parent = new { data = new { type = "folders", id = folderId } }
                        }
                    },
                    included = new[]
                    {
                        new
                        {
                            type = "versions",
                            id = "1",
                            attributes = new
                            {
                                name = fileName,
                                extension = new { type = "versions:autodesk.bim360:File", version = "1.0" }
                            },
                            relationships = new
                            {
                                storage = new { data = new { type = "objects", id = objectId } }
                            }
                        }
                    }
                };

                var itemContent = new StringContent(JsonSerializer.Serialize(itemBody), Encoding.UTF8, "application/json");
                var itemResponse = await client.PostAsync(itemUrl, itemContent);

                if (!itemResponse.IsSuccessStatusCode)
                {
                    var err = await itemResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Erro ao criar Item no ACC ({itemResponse.StatusCode}): {err}");
                }

                var itemJson = await itemResponse.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(itemJson))
                {
                    return doc.RootElement.GetProperty("data").GetProperty("id").GetString();
                }
            }
        }

        private string GetLdpFileNameFromConfig(string projectId)
        {
            string? fileName = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];
            if (string.IsNullOrEmpty(fileName)) fileName = _configuration["ProjectSettings:Default:LdpFileName"];
            return fileName ?? ".xlsx"; 
        }

        public async Task<string> Get2LeggedTokenAsync()
        {
            var scope = "data:read data:write data:create data:search bucket:read account:read";

            var clientId = _configuration["APS:ClientId"];
            var clientSecret = _configuration["APS:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new Exception("As configurações APS:ClientId e APS:ClientSecret não foram encontradas no appsettings.json.");
            }

            using (var client = new HttpClient())
            {
                var body = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "grant_type", "client_credentials" },
                    { "scope", scope }
                };

                var content = new FormUrlEncodedContent(body);
                var response = await client.PostAsync("https://developer.api.autodesk.com/authentication/v2/token", content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[AutodeskApiService] Falha ao obter Token do Robô: {response.StatusCode} - {error}");
                    throw new Exception($"Não foi possível autenticar o Robô no Autodesk APS. Erro: {error}");
                }

                var json = await response.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.GetProperty("access_token").GetString();
                }
            }
        }

        public async Task<Dictionary<string, Dictionary<string, string>>> GetBatchAttributesAsync(string projectId, List<string> versionUrns, string accessToken)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            var chunks = versionUrns.Chunk(40).ToList(); 

            foreach (var batch in chunks)
            {
                try
                {
                    var jsonElement = await GetVersionsWithCustomAttributesAsync(projectId, batch.ToList(), accessToken);
                    
                    if (jsonElement.TryGetProperty("results", out var results))
                    {
                        foreach (var item in results.EnumerateArray())
                        {
                            var urn = item.GetProperty("urn").GetString();
                            var attrsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                            if (item.TryGetProperty("customAttributes", out var customAttrs) && customAttrs.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var attr in customAttrs.EnumerateArray())
                                {
                                    var name = attr.GetProperty("name").GetString();
                                    var val = attr.GetProperty("value").ToString();
                                    if (!string.IsNullOrEmpty(name)) 
                                    {
                                        attrsMap[name] = val;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(urn))
                            {
                                result[urn] = attrsMap;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao baixar lote de atributos: {ex.Message}");
                }
            }

            return result;
        }

        public async Task<List<AccFileInfo>> IndexarArquivosProjetoAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation(">>> Indexando arquivos do projeto para validar existências...");
            var allFiles = new List<AccFileInfo>();
            var topFolders = await GetTopFoldersAsync(hubId, projectId, accessToken);

            foreach (var folder in topFolders)
            {
                await GetFilesRecursiveAsync(projectId, folder.Id, allFiles, accessToken);
            }

            _logger.LogInformation($">>> Indexação concluída. {allFiles.Count} arquivos ativos encontrados no ACC.");
            return allFiles;
        }

        private async Task GetFilesRecursiveAsync(string projectId, string folderId, List<AccFileInfo> fileList, string accessToken)
        {
            var contents = await GetFolderContentsAsync(projectId, folderId, accessToken);
            
            foreach (var item in contents)
            {
                if (item.Type == "folders")
                {
                    await GetFilesRecursiveAsync(projectId, item.Id, fileList, accessToken);
                }
                else if (item.Type == "items")
                {
                    fileList.Add(new AccFileInfo { 
                        FileName = item.Name, 
                        FileUrn = item.Id, 
                        VersionUrn = item.VersionUrn 
                    });
                }
            }
        }

        // 🛡️ NOVO MÉTODO BLINDADO PARA ATUALIZAR ATRIBUTOS DE FICHEIROS 🛡️
        public async Task UpdateFileAttributesAsync(string projectId, string versionUrn, object attributes, string accessToken)
        {
            await _accUpdateLock.WaitAsync(); // Catraca ativa
            try
            {
                var url = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectId.Replace("b.", "")}/versions/{Uri.EscapeDataString(versionUrn)}/custom-attributes:batch-update";
                var payload = new[] { attributes }; 
                var jsonPayload = JsonSerializer.Serialize(payload);

                int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        await Task.Delay(400); // Respiro
                        return;
                    }

                    var error = await response.Content.ReadAsStringAsync();
                    if ((int)response.StatusCode == 429 || error.Contains("Quota limit") || error.Contains("Too Many Requests"))
                    {
                        if (attempt == maxRetries) throw new Exception($"Erro 429 persistente após {maxRetries} tentativas: {error}");
                        _logger.LogWarning($"⏳ [UpdateFileAttributes] Autodesk bloqueou (429). Aguardando {attempt * 2}s...");
                        await Task.Delay(2000 * attempt); // Tempo exponencial de espera
                    }
                    else
                    {
                        throw new Exception($"Erro ao atualizar atributos no ACC: {error}");
                    }
                }
            }
            finally
            {
                _accUpdateLock.Release();
            }
        }

        public static int TotalApiCallsInCurrentCycle { get; private set; } = 0;

        private void IncrementApiCounter()
        {
            TotalApiCallsInCurrentCycle++;
        }

        public static void ResetCounter() { _totalCalls = 0; _lastRateLimit = "N/A"; }

        public static (int Calls, string Limit) GetTelemetry() => (_totalCalls, _lastRateLimit);

        private void TraceApiTelemetry(HttpResponseMessage response)
        {
            _totalCalls++;
            if (response.Headers.TryGetValues("X-Ads-RateLimit-Remaining", out var values))
            {
                _lastRateLimit = values.FirstOrDefault() ?? "N/A";
            }
        }

        private async Task<HttpResponseMessage> ExecuteHttpRequest(HttpRequestMessage request)
        {
            IncrementApiCounter(); 
            return await _httpClient.SendAsync(request);
        }

        public class AccFileInfo
        {
            public string FileName { get; set; }
            public string FileUrn { get; set; }
            public string VersionUrn { get; set; }
        }

        public async Task<List<dynamic>> GetProjectsAsync(string hubId)
        {
            string token = await _tokenManager.GetTokenAsync();
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<dynamic>();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            var projectList = new List<dynamic>();
            
            if (root.TryGetProperty("data", out var dataArray))
            {
                foreach (var project in dataArray.EnumerateArray())
                {
                    projectList.Add(new { 
                        id = project.GetProperty("id").GetString(), 
                        name = project.GetProperty("attributes").GetProperty("name").GetString() 
                    });
                }
            }
            
            return projectList;
        }

        public async Task<string> GetItemExtensionTypeAsync(string projectId, string itemUrn, string accessToken)
        {
            string cleanId = itemUrn?.Split('?')[0]; 
            if (string.IsNullOrEmpty(cleanId)) return "items:autodesk.core:File";

            string dmProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dmProjectId}/items/{cleanId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[AVISO API] Não foi possível ler tipo do Item {cleanId}. Status: {response.StatusCode}");
                return "items:autodesk.core:File"; 
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);

            try
            {
                return doc.RootElement
                    .GetProperty("data")
                    .GetProperty("attributes")
                    .GetProperty("extension")
                    .GetProperty("type")
                    .GetString();
            }
            catch
            {
                return "items:autodesk.core:File";
            }
        }

        public async Task<ItemDetails> GetItemDetailsAsync(string projectId, string itemId, string accessToken)
        {
            try
            {
                string formattedProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
                string url = $"https://developer.api.autodesk.com/data/v1/projects/{formattedProjectId}/items/{itemId}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {accessToken}");

                var response = await _httpClient.SendAsync(request);
                TraceApiTelemetry(response);
                if (!response.IsSuccessStatusCode) return null;

                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(jsonString);
                var root = doc.RootElement.GetProperty("data");

                var details = new ItemDetails
                {
                    ItemId = root.GetProperty("id").GetString(),
                    DisplayName = root.GetProperty("attributes").GetProperty("displayName").GetString(),
                    TipVersionUrn = root.GetProperty("relationships").GetProperty("tip").GetProperty("data").GetProperty("id").GetString(),
                    ParentFolderId = root.GetProperty("relationships").GetProperty("parent").GetProperty("data").GetProperty("id").GetString()
                };

                return details;
            }
            catch { return null; }
        }

        public async Task<List<FileItem>> GetProjectFilesAsync(string projectId, string accessToken)
        {
            try
            {
                var folderId = "urn:adsk.wipprod:fs.folder:co.y5QkRLctRQmRnJ41VxSIOw"; 
                var contents = await GetFolderContentsAsync(projectId, folderId, accessToken);

                return contents
                        .Where(c => c.Type == "items")
                        .Select(c => new FileItem 
                        { 
                            FileId = c.Id,
                            Name = c.Name, 
                            Type = c.Type 
                        })
                        .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AutodeskApi] Erro ao listar arquivos: {ex.Message}");
                return new List<FileItem>();
            }
        }
    }
     #endregion
}