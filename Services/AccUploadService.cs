// Services/AccUploadService.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using APS.WebApp.Models;

namespace APS.WebApp.Services
{
    public class AccUploadService
    {
        private readonly HttpClient _http;
        private readonly IHttpClientFactory _httpClientFactory;

        public AccUploadService(HttpClient http, IHttpClientFactory httpClientFactory)
        {
            _http = http;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<AccUploadResult> UploadArquivoAsync(
            string projectId,
            string folderId,
            string fileName,
            Stream conteudoArquivo,
            string accessToken)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // A Data Management API (ACC/BIM 360) exige o prefixo "b." no projectId
            string pid = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";

            try
            {
                // --- 1. Criar storage ---
                var storagePayload = new
                {
                    jsonapi = new { version = "1.0" },
                    data = new
                    {
                        type = "objects",
                        attributes = new { name = fileName },
                        relationships = new
                        {
                            target = new
                            {
                                data = new { type = "folders", id = folderId }
                            }
                        }
                    }
                };

                var storageResp = await PostJsonApiAsync(
                    $"https://developer.api.autodesk.com/data/v1/projects/{pid}/storage",
                    storagePayload);

                if (!storageResp.RootElement.TryGetProperty("data", out var storageData))
                    return Falha("Falha ao criar storage.");

                string storageId = storageData.GetProperty("id").GetString()!;
                var partes = storageId.Replace("urn:adsk.objects:os.object:", "").Split('/');
                string bucketKey = partes[0];
                string objectKey = partes[1];

                // --- 2. Pegar URL assinada do S3 ---
                var signedResp = await GetJsonAsync(
                    $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload?parts=1");

                string uploadKey = signedResp.RootElement.GetProperty("uploadKey").GetString()!;
                string uploadUrl = signedResp.RootElement.GetProperty("urls")[0].GetString()!;

                // --- 3. Enviar o arquivo pro S3 (HttpClient dedicado, sem header Authorization) ---
                using (var s3Client = _httpClientFactory.CreateClient())
                using (var content = new StreamContent(conteudoArquivo))
                {
                    var putResp = await s3Client.PutAsync(uploadUrl, content);
                    if (!putResp.IsSuccessStatusCode)
                        return Falha($"Falha ao enviar arquivo pro S3: {putResp.StatusCode}");
                }

                // --- 4. Finalizar upload no OSS ---
                var finalizePayload = new { uploadKey };
                var finalizeResp = await PostJsonAsync(
                    $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload",
                    finalizePayload);

                if (!finalizeResp.RootElement.TryGetProperty("objectId", out _))
                    return Falha("Falha ao finalizar upload no OSS.");

                // --- 5. Criar item (primeira versão do documento) ---
                var itemPayload = new
                {
                    jsonapi = new { version = "1.0" },
                    data = new
                    {
                        type = "items",
                        attributes = new
                        {
                            displayName = fileName,
                            extension = new { type = "items:autodesk.bim360:File", version = "1.0" }
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
                                storage = new { data = new { type = "objects", id = storageId } }
                            }
                        }
                    }
                };

                var itemResp = await PostJsonApiAsync(
                    $"https://developer.api.autodesk.com/data/v1/projects/{pid}/items",
                    itemPayload);

                if (!itemResp.RootElement.TryGetProperty("data", out var itemData))
                    return Falha("Falha ao criar o item na pasta do ACC.");

                string? versionId = null;
                if (itemData.TryGetProperty("relationships", out var relationships) &&
                    relationships.TryGetProperty("tip", out var tip) &&
                    tip.TryGetProperty("data", out var tipData))
                {
                    versionId = tipData.GetProperty("id").GetString();
                }

                return new AccUploadResult
                {
                    Sucesso = true,
                    ItemId = itemData.GetProperty("id").GetString(),
                    VersionId = versionId,
                    Mensagem = "Documento enviado com sucesso."
                };
            }
            catch (HttpRequestException ex)
            {
                return Falha($"Erro de comunicação com a APS: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Falha($"Erro inesperado no upload: {ex.Message}");
            }
        }

        // Usado nos endpoints de Data Management (storage, items) — exigem JSON:API
        private async Task<JsonDocument> PostJsonApiAsync(string url, object payload)
        {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/vnd.api+json");
                content.Headers.ContentType!.CharSet = null;   // 🎯 remove o "; charset=utf-8" que a Autodesk rejeita

                var resp = await _http.PostAsync(url, content);
                var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Erro {resp.StatusCode} em {url}: {body}");

            return JsonDocument.Parse(body);
        }

        // Usado nos endpoints OSS (signeds3upload) — aceitam application/json
        private async Task<JsonDocument> PostJsonAsync(string url, object payload)
        {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                content.Headers.ContentType!.CharSet = null;

                var resp = await _http.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Erro {resp.StatusCode} em {url}: {body}");

            return JsonDocument.Parse(body);
        }

        private async Task<JsonDocument> GetJsonAsync(string url)
        {
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Erro {resp.StatusCode} em {url}: {body}");

            return JsonDocument.Parse(body);
        }

        private static AccUploadResult Falha(string mensagem) =>
            new() { Sucesso = false, Mensagem = mensagem };
    }
}