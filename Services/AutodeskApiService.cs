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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AutodeskApiService> _logger;
        private readonly TokenManager _tokenManager;
        

        public AutodeskApiService(HttpClient httpClient, TokenManager tokenManager, IConfiguration configuration, ILogger<AutodeskApiService> logger) // <-- ADICIONE ', ILogger<AutodeskApiService> logger' AQUI
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger; 
            _tokenManager = tokenManager;
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

        // Ficheiro: Services/AutodeskApiService.cs
        // Dentro da classe AutodeskApiService

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
                    // Lançar ou retornar lista parcial dependendo do requisito
                    throw new HttpRequestException($"Erro ao buscar templates: {response.StatusCode}. Detalhes: {errorContent}");
                }

                var responseStream = await response.Content.ReadAsStreamAsync();
                var pageJson = await JsonDocument.ParseAsync(responseStream);
                var root = pageJson.RootElement;

                // Process results on the current page
                if (root.TryGetProperty("results", out var resultsProperty) && resultsProperty.ValueKind == JsonValueKind.Array)
                {
                    var templatesOnPage = JsonSerializer.Deserialize<List<AccProjectTemplate>>(resultsProperty.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (templatesOnPage != null)
                    {
                        allTemplates.AddRange(templatesOnPage);
                        _logger.LogDebug("Adicionados {Count} templates desta página.", templatesOnPage.Count);
                    }
                }

                // Check for the next page URL
                if (root.TryGetProperty("pagination", out var pagination) &&
                    pagination.TryGetProperty("nextUrl", out var nextUrlProperty) &&
                    nextUrlProperty.ValueKind == JsonValueKind.String)
                {
                    nextUrl = nextUrlProperty.GetString();
                    _logger.LogDebug("Encontrada próxima página: {NextUrl}", nextUrl);
                }
                else
                {
                    nextUrl = null; // No more pages
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

        #region MÉTODOS DE ADMINISTRAÇÃO DA CONTA
        // Todos os seus métodos de administração de conta
        // GetAccountRolesAsync, AssignAccRolesToUserAsync, etc.
        #endregion

        #region MÉTODOS DE GESTÃO DE DADOS (DATA MANAGEMENT & OSS API)

        private string ToDataManagementProjectId(string projectId)
        {
            return projectId.StartsWith("b.") ? projectId : $"b.{projectId}";
        }

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

        public async Task<JsonElement?> GetProjectRootFolderAsync(string hubId, string projectId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var requestUrl = $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects/{dataManagementProjectId}/topFolders";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var jsonResponse = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
            foreach (var folder in jsonResponse.GetProperty("data").EnumerateArray())
            {
                if (folder.GetProperty("attributes").TryGetProperty("name", out var nameProperty) && nameProperty.GetString() == "Project Files")
                {
                    return folder;
                }
            }
            return null;
        }

        // Ficheiro: Services/AutodeskApiService.cs
        // Dentro da classe AutodeskApiService

        public async Task<string?> CreateFolderAsync(string projectId, string parentFolderId, string folderName, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var requestUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders";

            // Definição do payload (sem alteração)
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
                            type = "folders:autodesk.bim360:Folder", // Ou o tipo apropriado se não for BIM360/ACC
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

            // --- INÍCIO DA MODIFICAÇÃO ---
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict) // 409 Conflict
                {
                    _logger.LogWarning("Pasta '{FolderName}' já existe (409 Conflict). Retornando null.", folderName);
                    return null; // Indica que já existe, não é erro fatal aqui.
                }
                // Verifica especificamente pelo status code 423
                else if ((int)response.StatusCode == 423) // 423 Locked
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Erro 423 (Locked) ao tentar criar pasta '{FolderName}'. Detalhe API: {ErrorBody}. Retornando null para retry.", folderName, errorBody);
                    // Retorna null para sinalizar ao DataController que precisa tentar novamente.
                    return null;
                }
                else // Outros erros (400, 401, 403, 500, etc.)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Erro {StatusCode} inesperado ao criar pasta '{FolderName}': {ErrorBody}", response.StatusCode, folderName, error);
                    // Lança exceção para outros erros que não são de conflito ou bloqueio temporário.
                    // Usar HttpRequestException inclui o StatusCode.
                    throw new HttpRequestException($"Erro ao criar a pasta '{folderName}': Status {response.StatusCode} - {error}", null, response.StatusCode);
                }
            }
            // --- FIM DA MODIFICAÇÃO ---

            // Processamento de sucesso (201 Created) continua igual
            var responseStream = await response.Content.ReadAsStreamAsync();
            var createdFolder = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            // Adiciona verificação segura para propriedades
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
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        }

        public async Task<string?> GetSignedDownloadUrlAsync(string storageUrn, string accessToken)
        {
            var idParts = storageUrn.Split(':').Last().Split('/');
            var bucketKey = Uri.EscapeDataString(idParts[0].Split('/').Last());
            var objectKey = Uri.EscapeDataString(idParts[1]);
            var url = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3download";

            var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(requestMessage);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var signedUrlData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return signedUrlData.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        }

        public async Task<JsonElement?> CreateNewVersionAsync(string projectId, string itemId, string folderId, string fileName, Stream fileStream, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);

            // ETAPAS 1 a 4 (Permanecem iguais e estão a funcionar)
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

            // ETAPA 5: Criar a nova "versão"
            // ▼▼▼ A CORREÇÃO FINAL ESTÁ AQUI ▼▼▼
            // Alinhando o endpoint EXATAMENTE com o Passo 8 da documentação
            var createVersionUrl = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/versions";
            // ▲▲▲ FIM DA CORREÇÃO ▲▲▲

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

        // Em AutodeskApiService.cs

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
                    var name = attributes.GetProperty("displayName").GetString() ?? "Sem Nome";
                    
                    string? lastModified = null;
                    if (attributes.TryGetProperty("lastModifiedTime", out var lmProp)) 
                        lastModified = lmProp.GetString();
                    
                    bool isHidden = false;
                    if (attributes.TryGetProperty("hidden", out var hiddenProp)) 
                        isHidden = hiddenProp.GetBoolean();

                    string? versionUrn = null;
                    if (type == "items" && 
                        item.TryGetProperty("relationships", out var rels) && 
                        rels.TryGetProperty("tip", out var tip) && 
                        tip.TryGetProperty("data", out var tipData))
                    {
                        versionUrn = tipData.GetProperty("id").GetString();
                    }

                    items.Add(new AccFolderItem 
                    { 
                        Name = name, 
                        Type = type,
                        Id = id,
                        VersionUrn = versionUrn,
                        LastModifiedTime = lastModified,
                        IsHidden = isHidden
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

// --- ARQUIVO: AutodeskApiService.cs ---

// --- ARQUIVO: AutodeskApiService.cs ---

        public async Task<Stream> DownloadFileStreamAsync(string projectId, string urn, string accessToken)
        {
            if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // 1. RESOLVER ITEM -> VERSÃO
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

                // 2. OBTER DETALHES DA VERSÃO (Storage ID)
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

                // 3. OBTER URL DE DOWNLOAD S3 (Signed URL)
                // O storageId vem como: urn:adsk.objects:os.object:wip.dm.prod/bucket-key/object-key
                // Exemplo real: urn:adsk.objects:os.object:wip.dm.prod/9a3d4.../meu-arquivo.dwg
                
                // CORREÇÃO: Limpeza robusta do prefixo
                var cleanStorageId = storageId.Replace("urn:adsk.objects:os.object:", "");
                var parts = cleanStorageId.Split('/');
                
                if (parts.Length < 2) throw new Exception($"Formato de StorageID inválido: {storageId}");

                var bucketKey = parts[0]; // Agora pegamos o primeiro pedaço limpo (ex: wip.dm.prod)
                var objectKey = string.Join("/", parts.Skip(1)); // O resto é a chave do objeto (pode conter barras)

                // Codifica apenas a objectKey
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

                // 4. BAIXAR BYTES
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

        /// <summary>
        /// Baixa o arquivo para uma pasta temporária local e retorna o caminho físico.
        /// Isso resolve problemas de corrupção de Stream no EPPlus.
        /// </summary>
        public async Task<string> DownloadFileToTempPathAsync(string projectId, string versionUrn, string accessToken)
        {
            // 1. Obtém a URL assinada (Reutilizando sua lógica existente)
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var versionDetailsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/versions/{System.Web.HttpUtility.UrlEncode(versionUrn)}");
            versionDetailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            var versionResponse = await _httpClient.SendAsync(versionDetailsRequest);
            versionResponse.EnsureSuccessStatusCode();
            
            var versionJson = await JsonDocument.ParseAsync(await versionResponse.Content.ReadAsStreamAsync());
            var storageUrn = versionJson.RootElement.GetProperty("data").GetProperty("relationships").GetProperty("storage").GetProperty("data").GetProperty("id").GetString();
            
            var downloadUrl = await GetSignedDownloadUrlAsync(storageUrn, accessToken);

            // 2. Baixa os bytes e salva no disco
            var fileResponse = await _httpClient.GetAsync(downloadUrl);
            fileResponse.EnsureSuccessStatusCode();
            var fileBytes = await fileResponse.Content.ReadAsByteArrayAsync();

            // Cria um nome único na pasta temporária
            string tempFileName = $"LDP_{Guid.NewGuid()}.xlsx";
            string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);

            await File.WriteAllBytesAsync(tempPath, fileBytes);

            return tempPath; // Retorna o caminho C:\Users\...\AppData\Local\Temp\LDP_GUID.xlsx
        }


        // Certifique-se de que este método está assim na sua classe:
        private async Task FindAllFilesRecursiveAsync(string projectId, string folderId, Dictionary<string, List<string>> fileMap, string accessToken)
{
    var contents = await GetFolderContentsSimpleAsync(projectId, folderId, accessToken);

    foreach (var item in contents)
    {
        if (item.Type == "items")
        {
            if (!string.IsNullOrEmpty(item.Name) && !string.IsNullOrEmpty(item.VersionUrn))
            {
                // Ignora arquivos temporários do Excel (~$)
                if (item.Name.StartsWith("~$")) continue;

                // Normaliza o nome (Sem extensão, Trim, Maiúsculo)
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(item.Name).Trim();
                
                // Adiciona ao mapa
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
        // 2. Substitua o método GetAllProjectFilesAsync
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
       private async Task<string?> FindFileByNameRecursiveAsync(string projectId, string folderId, string fileNameSubstring, string accessToken, bool isRecursive = true)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, folderId, accessToken);
            
            var candidates = new List<(string Id, DateTime Date, string Name)>();

            // Filtra arquivos
            foreach (var item in contents.Where(c => c.Type == "items"))
            {
                if (string.IsNullOrEmpty(item.Name) || item.Name.StartsWith("~$")) continue;

                string nameNoExt = Path.GetFileNameWithoutExtension(item.Name);
                
                if (nameNoExt.Equals(fileNameSubstring, StringComparison.OrdinalIgnoreCase) && 
                    item.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime lastModified = DateTime.MinValue;
                    if (!string.IsNullOrEmpty(item.LastModifiedTime))
                    {
                        DateTime.TryParse(item.LastModifiedTime, out lastModified);
                    }

                    if (item.VersionUrn != null)
                    {
                        candidates.Add((item.VersionUrn, lastModified, item.Name));
                    }
                }
            }

            // Retorna o mais recente
            if (candidates.Any())
            {
                var winner = candidates.OrderByDescending(x => x.Date).First();
                return winner.Id;
            }

            // Busca nas subpastas
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



        public async Task<string> FindLdpFileUrnAsync(string hubId, string projectId, string accessToken)
{
    // Hierarquia de pastas a seguir
    var folderPath = new List<string> 
    { 
        "01 - PLANEJAMENTO E CONTROLE", 
        "01.06 - LISTA DE DOCUMENTOS PROJETO" 
    };

    // 1. OBTÉM O ID DA PASTA RAIZ ('Project Files')
    var rootFolder = await GetProjectRootFolderAsync(hubId, projectId, accessToken);
    if (!rootFolder.HasValue)
        throw new Exception("A pasta raiz 'Project Files' não foi encontrada.");

    string? currentFolderId = rootFolder.Value.GetProperty("id").GetString();
    if (string.IsNullOrEmpty(currentFolderId)) 
        throw new Exception("Não foi possível obter o ID da pasta raiz.");

    // 2. BUSCA SEQUENCIAL NA HIERARQUIA
    Console.WriteLine($">>> Navegando para a pasta LDP: {string.Join(" -> ", folderPath)}");
    foreach (var folderName in folderPath)
    {
        // Busca a subpasta usando busca NÃO-RECURSIVA (isRecursive: false)
        string? nextFolderId = await FindFolderByNameRecursiveAsync(
            projectId, 
            currentFolderId, 
            folderName, 
            accessToken,
            isRecursive: false); 

        if (string.IsNullOrEmpty(nextFolderId))
        {
            throw new Exception($"A subpasta '{folderName}' não foi encontrada no caminho esperado.");
        }
        currentFolderId = nextFolderId;
    }

    // O currentFolderId agora é o ID da pasta final.
    string targetFolderId = currentFolderId;

    // 3. BUSCA O ARQUIVO APENAS NA PASTA FINAL
    string ldpFileNameIdentifier = GetProjectSettingValue(projectId, "LdpFileName");
    Console.WriteLine($">>> Buscando arquivo '{ldpFileNameIdentifier}.xlsx' APENAS na pasta final: {targetFolderId}");

    // Usa FindFileByNameRecursiveAsync, mas com isRecursive: false para restringir a busca.
    var urn = await FindFileByNameRecursiveAsync(
        projectId, 
        targetFolderId, 
        ldpFileNameIdentifier, 
        accessToken,
        isRecursive: false); 

    if (string.IsNullOrEmpty(urn))
    {
        throw new Exception($"Nenhum ficheiro Excel contendo '{ldpFileNameIdentifier}' no nome foi encontrado na pasta específica.");
    }
    return urn;
}


        public async Task UpdateCustomAttributesForVersionAsync(string projectId, string versionUrn, List<object> customAttributes, string accessToken)
        {
            string projectIdWithoutPrefix = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            string encodedVersionUrn = HttpUtility.UrlEncode(versionUrn);
            var requestUrl = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectIdWithoutPrefix}/versions/{encodedVersionUrn}/custom-attributes:batch-update";

            var jsonPayload = JsonSerializer.Serialize(customAttributes);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao atualizar atributos para a versão {versionUrn}: {error}");
            }
        }

        private string GetProjectSettingValue(string projectId, string settingKey)
        {
            // Tenta obter a configuração específica do projeto
            string settingValue = _configuration[$"ProjectSettings:{projectId}:{settingKey}"];

            // Se não encontrar, busca a configuração "Default"
            if (string.IsNullOrEmpty(settingValue))
            {
                settingValue = _configuration[$"ProjectSettings:Default:{settingKey}"];
            }

            // Se ainda assim não encontrar, lança um erro
            if (string.IsNullOrEmpty(settingValue))
            {
                throw new Exception($"A configuração '{settingKey}' não foi encontrada para o projeto '{projectId}' nem no 'Default' em appsettings.json.");
            }

            return settingValue;
        }

        // 3. Substitua o método SyncLdpAttributesAsync (Aqui está a lógica de SMART MATCH)
public async Task<SyncResult> SyncLdpAttributesAsync(string hubId, string projectId, List<Dictionary<string, string>> ldpData, string accessToken)
{
    string keyColumnName = GetProjectSettingValue(projectId, "KeyColumn");
    _logger.LogInformation($">>> Iniciando Sync. Chave usada: '{keyColumnName}'");

    // 1. Busca definições de atributos
    var rootFolder = await GetProjectRootFolderAsync(hubId, projectId, accessToken);
    if (!rootFolder.HasValue) throw new Exception("Pasta raiz não encontrada.");
    var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
    
    var attributeDefinitions = await GetFolderAttributeDefinitionsAsync(projectId, rootFolderId, accessToken);
    var dynamicAttributeMap = attributeDefinitions.ToDictionary(def => def.Name, def => def.Id.ToString(), StringComparer.OrdinalIgnoreCase);
    
    // 2. Mapeia todos os arquivos do ACC
    var fileMap = await GetAllProjectFilesAsync(hubId, projectId, accessToken); 
    
    var result = new SyncResult { TotalLdpFiles = ldpData.Count };

    // 3. Itera sobre a LDP
    foreach (var row in ldpData)
    {
        // Pega o código do documento da linha do Excel (ex: "BML-ARQ-01")
        if (row.TryGetValue("_DOC_NUMBER_KEY_", out var docCode) || 
            row.TryGetValue(keyColumnName, out docCode))
        {
            if (string.IsNullOrEmpty(docCode)) continue;

            result.AllLdpFiles.Add(docCode); 
            var codeToSearch = docCode.Trim();

            // === LÓGICA DE BUSCA INTELIGENTE (SMART MATCH) ===
            List<string> targetUrns = new List<string>();

            // Tentativa A: Match Exato (Sem Extensão)
            // Ex: Excel "DOC-01" == ACC "DOC-01.pdf"
            if (fileMap.TryGetValue(codeToSearch, out var exactMatches))
            {
                targetUrns.AddRange(exactMatches);
            }
            else
            {
                // Tentativa B: Match Parcial (StartsWith)
                // Ex: Excel "DOC-01" encontra ACC "DOC-01_R00.pdf"
                // Isso é um pouco mais lento, mas resolve o problema de sufixos.
                var partialMatches = fileMap.Keys
                    .Where(k => k.StartsWith(codeToSearch, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in partialMatches)
                {
                    targetUrns.AddRange(fileMap[key]);
                }
            }

            // === AÇÃO ===
            if (targetUrns.Any())
            {
                _logger.LogInformation($"✅ ENCONTRADO: '{docCode}' corresponde a {targetUrns.Count} arquivo(s) no ACC.");
                
                // Prepara os atributos para envio
                var customAttributesForFile = new List<object>(); 
                foreach (var attribute in row)
                {
                    // Ignora chaves internas
                    if (attribute.Key == "_DOC_NUMBER_KEY_") continue;

                    if (dynamicAttributeMap.TryGetValue(attribute.Key, out var attributeId))
                    {
                        customAttributesForFile.Add(new { id = int.Parse(attributeId), value = attribute.Value });
                    }
                }

                if (customAttributesForFile.Any())
                {
                    foreach (var versionUrn in targetUrns.Distinct())
                    {
                        try {
                            await UpdateCustomAttributesForVersionAsync(projectId, versionUrn, customAttributesForFile, accessToken);
                            result.FilesUpdated++;
                        } catch (Exception ex) {
                            _logger.LogError($"Erro ao atualizar '{docCode}' (URN: {versionUrn}): {ex.Message}");
                        }
                    }
                }
            }
            else 
            {
                // Loga apenas um aviso para não poluir, mas conta como não encontrado
                // _logger.LogWarning($"❌ NÃO ENCONTRADO: '{docCode}' não tem correspondência no ACC.");
                result.FilesNotFound.Add(docCode); 
            }
        }
    } 

    result.SummaryMessage = $"Sincronização concluída. {result.FilesUpdated} atualizações de atributos enviadas. {result.FilesNotFound.Count} códigos da LDP não encontrados no ACC.";
    
    // Log de amostra dos não encontrados para ajudar no debug
    if (result.FilesNotFound.Any())
    {
        _logger.LogWarning("Exemplos de arquivos NÃO encontrados (Verifique se o nome no ACC bate com estes códigos):");
        foreach(var missing in result.FilesNotFound.Take(5)) _logger.LogWarning($" - {missing}");
    }

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

        #endregion

        // EM: AutodeskApiService.cs
        // ADICIONE ESTE NOVO MÉTODO DENTRO DA CLASSE AutodeskApiService

        /// <summary>
        /// Procura recursivamente por uma pasta com um nome específico a partir de uma pasta pai.
        /// </summary>
        /// <param name="projectId">ID do projeto.</param>
        /// <param name="parentFolderId">ID da pasta onde a busca começará (ex: pasta raiz).</param>
        /// <param name="folderName">Nome da pasta a ser encontrada.</param>
        /// <param name="accessToken">Token de acesso.</param>
        /// <returns>O ID da pasta encontrada ou null se não for encontrada.</returns>
      public async Task<string?> FindFolderByNameRecursiveAsync(string projectId, string parentFolderId, string folderName, string accessToken, bool isRecursive = true)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, parentFolderId, accessToken);

            foreach (var item in contents.Where(c => c.Type == "folders"))
            {
                if (item.IsHidden) continue;

                // Verifica nome
                if (item.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Id;
                }

                // Recursividade
                if (isRecursive)
                {
                    var result = await FindFolderByNameRecursiveAsync(projectId, item.Id, folderName, accessToken, true);
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }
                }
            }
            return null;
        }
/// <summary>
/// Encontra o URN de um item de template dentro de uma pasta específica.
/// </summary>
/// <param name="projectId">ID do projeto.</param>
/// <param name="templateFolderId">ID da pasta que contém os arquivos de template.</param>
/// <param name="templateFileName">Nome do arquivo de template (ex: "template.dwg").</param>
/// <param name="accessToken">Token de acesso.</param>
/// <returns>O URN do item encontrado ou null.</returns>
        public async Task<string?> FindTemplateItemUrnAsync(string projectId, string templateFolderId, string templateFileName, string accessToken)
        {
            var contents = await GetFolderContentsSimpleAsync(projectId, templateFolderId, accessToken);
            
            foreach (var item in contents)
            {
                if (item.Type == "items")
                {
                    if (item.Name.Equals(templateFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Retorna VersionUrn se tiver, senão o Id do item
                        return item.VersionUrn ?? item.Id; 
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Cria um placeholder copiando um item de template para uma nova pasta com um novo nome.
        /// </summary>
        /// <param name="projectId">ID do projeto.</param>
        /// <param name="sourceItemUrn">URN do item de template a ser copiado.</param>
        /// <param name="targetFolderId">ID da pasta de destino.</param>
        /// <param name="newFileName">O novo nome do arquivo na pasta de destino.</param>
        /// <param name="accessToken">Token de acesso.</param>
        // EM: AutodeskApiService.cs
        // SUBSTITUA O MÉTODO INTEIRO PELA VERSÃO CORRIGIDA ABAIXO

        // EM: AutodeskApiService.cs
        // SUBSTITUA O MÉTODO DE CÓPIA ANTERIOR POR ESTE

        /// <summary>
        /// Cria um placeholder copiando a versão de um item de template para uma nova pasta com um novo nome.
        /// Este método usa o endpoint POST /items com o parâmetro 'copyFrom'.
        /// </summary>
        // EM: AutodeskApiService.cs
        // SUBSTITUA O MÉTODO DE CÓPIA PELA VERSÃO CORRIGIDA ABAIXO

        public async Task CopyPlaceholderAsync(string projectId, string sourceVersionUrn, string targetFolderId, string newFileName, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var encodedSourceUrn = HttpUtility.UrlEncode(sourceVersionUrn);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/items?copyFrom={encodedSourceUrn}";

            // ================== INÍCIO DA CORREÇÃO DO PAYLOAD ==================
            var payload = new
            {
                jsonapi = new { version = "1.0" },
                data = new
                {
                    type = "items",
                    relationships = new
                    {
                        // ---- BLOCO ADICIONADO ----
                        // Mesmo em uma cópia, o novo item precisa saber qual é sua versão principal (tip).
                        // Apontamos para a versão "1" que está definida no bloco 'included'.
                        tip = new
                        {
                            data = new { type = "versions", id = "1" }
                        },
                        // -------------------------
                        parent = new
                        {
                            data = new { type = "folders", id = targetFolderId }
                        }
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
                    name = newFileName
                }
            }
                }
            };
            // =================== FIM DA CORREÇÃO DO PAYLOAD ===================

            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(">>> [DEBUG] Payload de CÓPIA (via copyFrom) a ser enviado:");
            Console.WriteLine(jsonPayload);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            request.Content = new StringContent(jsonPayload, Encoding.UTF8);
            request.Content.Headers.ContentType = null;
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/vnd.api+json");

            Console.WriteLine($">>> [DEBUG] Verificando Cabeçalho Content-Type FINAL: {request.Content.Headers.ContentType}");

            using var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($">>> [DEBUG] Response Status (Copy Item): {response.StatusCode}");
            Console.WriteLine($">>> [DEBUG] Response Body (Copy Item): {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erro ao copiar item para criar placeholder: {response.StatusCode} - {responseContent}");
            }
        }


        // EM: AutodeskApiService.cs
        // ADICIONE ESTE NOVO MÉTODO
        public async Task<string?> GetItemTipVersionUrnAsync(string projectId, string itemId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/items/{itemId}/tip";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($">>> [ERRO] Falha ao obter a versão TIP do item {itemId}: {content}");
                return null;
            }

            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.GetProperty("data").GetProperty("id").GetString();
        }



        // EM: AutodeskApiService.cs
        // SUBSTITUA O MÉTODO INTEIRO PELA VERSÃO FINAL E SIMPLIFICADA ABAIXO

        public async Task UploadNewFileAsync(string projectId, string folderId, string fileName, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/items";

            // --- CORREÇÃO FINAL: Usar o tipo GENÉRICO para TODOS os placeholders ---
            // A API provou ser inconsistente com tipos específicos para esta operação.
            // O tipo 'items:autodesk.core:File' é o padrão para criação de itens vazios.
            const string GENERIC_ITEM_TYPE = "items:autodesk.core:File";
            const string GENERIC_VERSION_TYPE = "versions:autodesk.core:File";
            // --- FIM DA CORREÇÃO ---

            var payload = new
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
                            type = GENERIC_ITEM_TYPE,
                            version = "1.0" // A versão é necessária para o tipo genérico
                        }
                    },
                    relationships = new
                    {
                        tip = new { data = new { type = "versions", id = "1" } },
                        parent = new { data = new { type = "folders", id = folderId } }
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
                    name = fileName,
                    extension = new
                    {
                        type = GENERIC_VERSION_TYPE,
                        version = "1.0" // A versão é necessária para o tipo genérico
                    }
                }
            }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(">>> [DEBUG] Payload FINAL a ser enviado:");
            Console.WriteLine(jsonPayload);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            request.Content = new StringContent(jsonPayload, Encoding.UTF8);
            request.Content.Headers.ContentType = null;
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/vnd.api+json");

            Console.WriteLine($">>> [DEBUG] Verificando Cabeçalho Content-Type FINAL: {request.Content.Headers.ContentType}");

            using var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($">>> [DEBUG] Response Status: {response.StatusCode}");
            Console.WriteLine($">>> [DEBUG] Response Body: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erro ao criar placeholder: {response.StatusCode} - {responseContent}");
            }
        }

        public async Task<string> ResolveDisciplineFolderIdAsync(string projectId, string rootFolderId, string disciplinaCodigo, string accessToken)
        {
            // 1. Encontrar a pasta "04" (Engenharia/Engineering)
            // Tenta os nomes possíveis para a pasta pai
            var parentFolderNames = new[] { "04 - ENGENHARIA", "04 - ENGINEERING" };
            string parentEngineeringId = null;

            // Busca a pasta 04 dentro da Raiz
            var rootContents = await GetFolderContentsSimpleAsync(projectId, rootFolderId, accessToken);
            foreach (var folder in rootContents.Where(c => c.Type == "folders"))
            {
                if (parentFolderNames.Any(p => folder.Name.Trim().Equals(p, StringComparison.OrdinalIgnoreCase)))
                {
                    parentEngineeringId = folder.Id;
                    break;
                }
            }

            if (string.IsNullOrEmpty(parentEngineeringId))
                throw new Exception("Pasta '04 - ENGENHARIA' (ou '04 - ENGINEERING') não encontrada na raiz do projeto.");

            // 2. Mapeamento Bilíngue das Disciplinas
            // Formato: CodigoLDP -> { NomePT, NomeEN }
            // IMPORTANTE: Confirme se os códigos (Case "PR", "MC") batem com sua LDP!
            string[] possibleNames = disciplinaCodigo.ToUpper() switch
            {
                "PR" => new[] { "04.01 - PROCESSO", "04.01 - PROCESS" },
                "MC" => new[] { "04.02 - MECÂNICA", "04.02 - MECHANICAL" },
                "TL" => new[] { "04.03 - TELECOMUNICAÇÕES", "04.03 - TELECOMUNICATIONS" },
                "AR" => new[] { "04.04 - ARQUITETURA", "04.04 - ARCHITECTURE" },
                "IA" => new[] { "04.05 - INSTRUMENTAÇÃO E AUTOMAÇÃO", "04.05 - INTRUMENTATION AND AUTOMATION" }, // "IN" ou "IA"
                "IN" => new[] { "04.05 - INSTRUMENTAÇÃO E AUTOMAÇÃO", "04.05 - INTRUMENTATION AND AUTOMATION" },
                "TB" => new[] { "04.06 - TUBULAÇÃO", "04.06 - PIPING" },
                "IF" => new[] { "04.07 - INFRAESTRUTURA", "04.07 - INFRASTRUCTURE" },
                "PE" => new[] { "04.08 - ESTRUTURA METÁLICA", "04.08 - STRUCTURAL ENGINEERING" }, // "PE" ou "ES"
                "ES" => new[] { "04.08 - ESTRUTURA METÁLICA", "04.08 - STRUCTURAL ENGINEERING" },
                "EQ" => new[] { "04.09 - EQUIPAMENTOS", "04.09 - EQUIPMENT" },
                "EL" => new[] { "04.10 - ELÉTRICA", "04.10 - ELECTRICAL" },
                "CV" => new[] { "04.11 - CIVIL", "04.11 - CIVIL" },
                "AC" => new[] { "04.12 - AVAC", "04.12 - HVAC" },
                "SD" => new[] { "04.13 - SISTEMAS DIGITAIS", "04.13 - DIGITAL SYSTEMS" },
                _ => null
            };

            if (possibleNames == null)
                throw new Exception($"Código de disciplina '{disciplinaCodigo}' não mapeado no sistema.");

            // 3. Buscar a subpasta correta dentro de "04"
            var engineeringContents = await GetFolderContentsSimpleAsync(projectId, parentEngineeringId, accessToken);
            
            foreach (var folder in engineeringContents.Where(c => c.Type == "folders"))
            {
                // Verifica se o nome da pasta bate com alguma das opções (PT ou EN)
                if (possibleNames.Any(name => folder.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    return folder.Id; // Encontrou!
                }
            }

            throw new Exception($"Pasta da disciplina '{disciplinaCodigo}' não encontrada dentro de 04 - ENGINEERING. (Procurado por: {string.Join(" ou ", possibleNames)})");
        }


        public async Task<bool> FolderAcceptsFilesAsync(string projectId, string folderId, string accessToken)
        {
            string dataManagementProjectId = ToDataManagementProjectId(projectId);
            var url = $"https://developer.api.autodesk.com/data/v1/projects/{dataManagementProjectId}/folders/{folderId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Erro ao verificar pasta: {response.StatusCode} - {content}");

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
        // Adicione este novo método dentro da classe em Services/AutodeskApiService.cs

        /// <summary>
        /// Obtém os detalhes completos de uma versão específica de um ficheiro.
        /// </summary>
        /// <param name="projectId">ID do projeto.</param>
        /// <param name="versionUrn">URN da versão.</param>
        /// <param name="accessToken">Token de acesso.</param>
        /// <returns>Um JsonElement com os detalhes da versão.</returns>
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


        // EM: Services/AutodeskApiService.cs
        // ADICIONE ESTE NOVO MÉTODO COMPLETO DENTRO DA CLASSE

        /// <summary>
        /// Obtém o URN da pasta-pai de um item específico.
        /// </summary>
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

            if (string.IsNullOrEmpty(parentFolderId))
            {
                throw new Exception($"Não foi possível encontrar a pasta-pai para o item {itemId}.");
            }

            return parentFolderId;
        }

        public async Task<string> GetInternalTokenAsync()
        {
            // Escopos necessários para ler arquivos e pastas
            // IMPORTANTE: Para o robô, geralmente usamos 'data:read bucket:read'
            // Se ele precisar escrever na LDP, adicione 'data:write data:create'
            var scope = "data:read data:write data:create bucket:read account:read"; 
            
            // Usa as credenciais que já estão no appsettings.json
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

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("O Robô falhou ao tentar pegar o crachá (Token). Verifique ClientId/Secret.");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("access_token").GetString();
            }
        }

        /// <summary>
/// Navega recursivamente pelas pastas. AGORA ACEITA HUBID.
/// </summary>
public async Task<string> FindFolderIdByPathAsync(string hubId, string projectId, string[] folderNames, string accessToken)
{
    // Passamos o HubId corretamente agora
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
/// <summary>
/// Localiza um arquivo dentro de uma pasta específica buscando por parte do nome (ex: "LDP").
/// </summary>
public async Task<(string FileId, string FileName)> FindItemIdByNamePartAsync(string projectId, string folderId, string namePart, string accessToken)
{
    var contents = await GetFolderContentsAsync(projectId, folderId, accessToken);
    
    // CORREÇÃO: Usamos c.Name e c.LastModifiedTime diretamente
    var file = contents
        .Where(c => c.Type == "items" && c.Name.ToUpper().Contains(namePart.ToUpper()))
        .OrderByDescending(c => c.LastModifiedTime)
        .FirstOrDefault();

    if (file == null) return (null, null);

    return (file.Id, file.Name);
}

/// <summary>
/// Localiza um arquivo pelo nome EXATO (usado para sobrescrever/versionar o espelho).
/// </summary>
public async Task<string> FindItemIdByNameExactAsync(string projectId, string folderId, string exactName, string accessToken)
{
    var contents = await GetFolderContentsAsync(projectId, folderId, accessToken);
    
    // CORREÇÃO: Usamos c.Name diretamente
    var file = contents.FirstOrDefault(c => c.Type == "items" && c.Name.Equals(exactName, StringComparison.OrdinalIgnoreCase));
    
    return file?.Id;
}

/// <summary>
/// Busca as pastas raiz usando a API de PROJETOS (que funciona no seu ambiente).
/// </summary>
public async Task<List<AccFolderItem>> GetTopFoldersAsync(string hubId, string projectId, string accessToken)
{
    // Garante prefixos "b."
    if (!hubId.StartsWith("b.")) hubId = "b." + hubId;
    if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

    // MUDANÇA: Usando a rota project/v1 que sabemos que funciona
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

/// <summary>
/// Lista o conteúdo (arquivos e subpastas) de uma pasta específica.
/// Método essencial para o robô entrar na pasta "09 - DESENVOLVIMENTO", depois "09.10...", etc.
/// </summary>
public async Task<List<AccFolderItem>> GetFolderContentsAsync(string projectId, string folderId, string accessToken)
{
    // Garante que o projectId tem o prefixo "b."
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

/// <summary>
/// Método auxiliar para ler o JSON da Autodesk e transformar na nossa classe AccFolderItem.
/// </summary>
// --- SUBSTITUA O MÉTODO ParseFolderItems POR ESTE NOVO "BLINDADO" ---

private List<AccFolderItem> ParseFolderItems(string jsonResponse)
{
    var list = new List<AccFolderItem>();
    try
    {
        using (var doc = JsonDocument.Parse(jsonResponse))
        {
            // Verifica se existe a propriedade "data"
            if (!doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                // Se não tiver data, retorna lista vazia sem dar erro
                return list;
            }

            // Se for Array (vários itens)
            if (dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataElement.EnumerateArray())
                {
                    var folderItem = ExtractItemSafe(item);
                    if (folderItem != null) list.Add(folderItem);
                }
            }
            // Se for Objeto único (alguns endpoints retornam apenas um item)
            else if (dataElement.ValueKind == JsonValueKind.Object)
            {
                var folderItem = ExtractItemSafe(dataElement);
                if (folderItem != null) list.Add(folderItem);
            }
        }
    }
    catch (Exception ex)
    {
        // Loga o erro mas não quebra a execução
        _logger.LogError($"Erro ao fazer parse seguro dos itens: {ex.Message}");
    }
    return list;
}

/// <summary>
/// Método auxiliar que tenta extrair propriedades sem dar erro se faltar alguma.
/// </summary>
private AccFolderItem ExtractItemSafe(JsonElement item)
{
    try
    {
        // Tenta pegar ID e Type de forma segura
        string id = item.TryGetProperty("id", out var pId) ? pId.GetString() : null;
        string type = item.TryGetProperty("type", out var pType) ? pType.GetString() : "unknown";

        if (string.IsNullOrEmpty(id)) return null; // Sem ID não serve

        // Tenta pegar o Nome (pode estar em 'name' ou 'displayName')
        string name = "Sem Nome";
        if (item.TryGetProperty("attributes", out var attrs))
        {
            if (attrs.TryGetProperty("name", out var pName) && pName.ValueKind == JsonValueKind.String)
            {
                name = pName.GetString();
            }
            else if (attrs.TryGetProperty("displayName", out var pDisp) && pDisp.ValueKind == JsonValueKind.String)
            {
                name = pDisp.GetString();
            }
        }

        // Tenta pegar Data de Modificação
        string lastModified = DateTime.Now.ToString();
        if (item.TryGetProperty("attributes", out var a) && 
            a.TryGetProperty("lastModifiedTime", out var t))
        {
            lastModified = t.GetString();
        }

        // Tenta pegar Versão (se houver)
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
    catch
    {
        return null; // Se der qualquer erro num item específico, pula ele
    }
}
    
// --- ARQUIVO: AutodeskApiService.cs ---
// Adicione este método dentro da classe

/// <summary>
/// Faz o upload de um arquivo NOVO para uma pasta (Cria o Item).
/// </summary>
// --- ARQUIVO: AutodeskApiService.cs ---

/// <summary>
/// Faz o upload de um arquivo NOVO para uma pasta (Cria o Item).
/// </summary>
public async Task<string> UploadFileToFolderAsync(string projectId, string folderId, string fileName, Stream fileStream, string accessToken)
{
    // Garante prefixos
    if (!projectId.StartsWith("b.")) projectId = "b." + projectId;

    using (var client = new HttpClient())
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // PASSO 1: Criar um local de armazenamento (Storage Location)
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

        // PASSO 2: Obter URL assinada para Upload (S3)
        // Precisamos extrair bucketKey e objectKey do objectId
        // Formato: urn:adsk.objects:os.object:wip.dm.prod/bucketKey/objectKey
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

        // PASSO 3: Fazer o Upload dos bytes para o S3
        // IMPORTANTE: Usar um cliente novo SEM headers de autorização da Autodesk, pois o S3 usa a assinatura na URL
        using (var s3Client = new HttpClient()) 
        {
            // Garante que o stream está no início
            if (fileStream.Position != 0 && fileStream.CanSeek) fileStream.Position = 0;

            var streamContent = new StreamContent(fileStream);
            // S3 às vezes rejeita se não tiver content-type ou se for incorreto. "application/octet-stream" é seguro.
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); 

            var uploadResponse = await s3Client.PutAsync(uploadUrl, streamContent);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                var err = await uploadResponse.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao fazer Upload dos bytes S3 ({uploadResponse.StatusCode}): {err}");
            }
        }

        // PASSO 4: Completar o Upload no OSS
        var completeUrl = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{encodedObjectKey}/signeds3upload";
        var completeBody = new { uploadKey = uploadKey };
        var completeContent = new StringContent(JsonSerializer.Serialize(completeBody), Encoding.UTF8, "application/json");
        
        var completeResponse = await client.PostAsync(completeUrl, completeContent);
        if (!completeResponse.IsSuccessStatusCode)
        {
             var err = await completeResponse.Content.ReadAsStringAsync();
             throw new Exception($"Erro ao completar upload OSS ({completeResponse.StatusCode}): {err}");
        }

        // PASSO 5: Criar o Item no Data Management (Linkar o arquivo ao Folder)
        // Isso cria a PRIMEIRA versão do arquivo
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
    // Tenta pegar do projeto
    string? fileName = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];

    // Se for nulo, tenta pegar o default
    if (string.IsNullOrEmpty(fileName))
    {
        fileName = _configuration["ProjectSettings:Default:LdpFileName"];
    }

    // Se AINDA for nulo, retorna o fallback padrão
    // O operador '??' garante que se fileName for null, retornamos ".xlsx"
    return fileName ?? ".xlsx"; 
}
// ... (Seus outros métodos como UploadNewFileAsync, etc...)

        /// <summary>
        /// Obtém um Token 2-Legged (Credenciais de Cliente) para uso interno do Robô.
        /// Este token tem permissões de superusuário.
        /// </summary>
        public async Task<string> Get2LeggedTokenAsync()
        {
            // Define o escopo: O Robô precisa de poderes para ler, escrever e criar arquivos/pastas
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
                
                // Chama o endpoint de autenticação
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

    // --- ARQUIVO: AutodeskApiService.cs ---
// Adicione ou atualize este método

public async Task<Dictionary<string, Dictionary<string, string>>> GetBatchAttributesAsync(string projectId, List<string> versionUrns, string accessToken)
{
    var result = new Dictionary<string, Dictionary<string, string>>();
    
    // Divide em lotes de 40 para não sobrecarregar a API
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
                    
                    // IMPORTANTE: StringComparer.OrdinalIgnoreCase faz "REV." ser igual a "Rev." ou "rev."
                    var attrsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // 1. Pega Atributos Customizados
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

// 1. Método para listar todos os arquivos do projeto (Indexação)
// --- DENTRO DE AutodeskApiService.cs ---

public async Task<List<AccFileInfo>> IndexarArquivosProjetoAsync(string hubId, string projectId, string accessToken)
{
    _logger.LogInformation(">>> Indexando arquivos do projeto para validar existências...");
    var allFiles = new List<AccFileInfo>();
    
    // Agora usamos o hubId que vem do Worker, sem risco de ser null
    var topFolders = await GetTopFoldersAsync(hubId, projectId, accessToken);

    foreach (var folder in topFolders)
    {
        await GetFilesRecursiveAsync(projectId, folder.Id, allFiles, accessToken);
    }

    _logger.LogInformation($">>> Indexação concluída. {allFiles.Count} arquivos ativos encontrados no ACC.");
    return allFiles;
}

// Auxiliar para a indexação recursiva
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

// 2. Método para atualizar atributos no ACC
public async Task UpdateFileAttributesAsync(string projectId, string versionUrn, object attributes, string accessToken)
{
    // A URL do ACC para batch-update de atributos customizados
    var url = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectId.Replace("b.", "")}/versions/{Uri.EscapeDataString(versionUrn)}/custom-attributes:batch-update";

    var payload = new[] { attributes }; // O ACC espera um array de alterações

    var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await _httpClient.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        throw new Exception($"Erro ao atualizar atributos no ACC: {error}");
    }
}
// Variável global para rastreio de custo de performance
public static int TotalApiCallsInCurrentCycle { get; private set; } = 0;

// Método para incrementar o contador (Módulo de Telemetria)
private void IncrementApiCounter()
{
    TotalApiCallsInCurrentCycle++;
}

// Método para resetar ao fim do ciclo
public static void ResetCounter() 
{
    TotalApiCallsInCurrentCycle = 0;
}

// EXEMPLO de como aplicar: 
// Procure no seu código onde você faz: await _httpClient.SendAsync(request);
// E adicione a chamada do contador logo antes:
private async Task<HttpResponseMessage> ExecuteHttpRequest(HttpRequestMessage request)
{
    IncrementApiCounter(); // Rastreia a chamada antes de enviar [cite: 2025-07-28]
    return await _httpClient.SendAsync(request);
}
// Modelo auxiliar para a lista
public class AccFileInfo
{
    public string FileName { get; set; }
    public string FileUrn { get; set; }
    public string VersionUrn { get; set; }
}

// Dentro de AutodeskApiService.cs
public async Task<List<dynamic>> GetProjectsAsync(string hubId)
        {
            // Agora usando o nome correto conforme o seu arquivo TokenManager.cs
            string token = await _tokenManager.GetTokenAsync();
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<dynamic>();

            var content = await response.Content.ReadAsStringAsync();
            
            // ✅ Uso do nome completo para evitar conflitos com o Newtonsoft
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



    }
}