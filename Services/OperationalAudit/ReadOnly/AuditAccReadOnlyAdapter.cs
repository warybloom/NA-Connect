using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services;
using APS.WebApp.Services.OperationalAudit.Safety;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>
    /// Implementação real de <see cref="IAuditAccReadOnlyAdapter"/> — cliente HTTP PRÓPRIO da auditoria
    /// (nunca reaproveita <c>AutodeskApiService</c>, que também tem métodos de escrita; a auditoria continua
    /// sendo um subsistema paralelo e independente, seção 1/5 do Checkpoint 2). Todo <see cref="HttpClient"/>
    /// aqui é criado com <see cref="AuditHttpWriteGuardHandler"/> como handler mais externo — qualquer verbo
    /// não-GET/HEAD/OPTIONS lança <see cref="ForbiddenOperationException"/> antes de sair do processo.
    ///
    /// <see cref="TokenManager"/> é reaproveitado só para obter o token OAuth do robô (não é um serviço de
    /// sincronização operacional, não toca dado de projeto — só autenticação).
    /// </summary>
    public sealed class AuditAccReadOnlyAdapter : IAuditAccReadOnlyAdapter
    {
        private const int LimitePaginasSeguranca = 500;
        private const string BaseUrl = "https://developer.api.autodesk.com";

        private readonly HttpClient _httpClient;
        private readonly TokenManager _tokenManager;
        private readonly ILogger<AuditAccReadOnlyAdapter> _logger;

        public AuditAccReadOnlyAdapter(HttpClient httpClient, TokenManager tokenManager, ILogger<AuditAccReadOnlyAdapter> logger)
        {
            _httpClient = httpClient;
            _tokenManager = tokenManager;
            _logger = logger;
        }

        public async Task<AccFolderRef?> LocalizarPastaPorAliasAsync(
            string projectId, IReadOnlyList<string> caminhosAlternativos, bool navegarRecursivamente, CancellationToken ct = default)
        {
            string token = await _tokenManager.GetTokenAsync();

            var topLevel = await ObterJsonAsync($"/data/v1/projects/{projectId}/topFolders", token, ct);
            string? correnteId = null;
            string correnteNome = string.Empty;

            foreach (var segmento in caminhosAlternativos)
            {
                var origem = correnteId == null
                    ? topLevel
                    : await ObterJsonAsync($"/data/v1/projects/{projectId}/folders/{correnteId}/contents", token, ct);

                if (!origem.TryGetProperty("data", out var dados)) return null;

                JsonElement? encontrado = null;
                foreach (var item in dados.EnumerateArray())
                {
                    string? tipo = item.GetProperty("type").GetString();
                    string? nome = item.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("displayName", out var dn)
                        ? dn.GetString() : null;
                    if (tipo == "folders" && string.Equals(nome, segmento, StringComparison.OrdinalIgnoreCase))
                    {
                        encontrado = item;
                        break;
                    }
                }

                if (encontrado == null) return null;
                correnteId = encontrado.Value.GetProperty("id").GetString();
                correnteNome = segmento;
            }

            return correnteId == null ? null : new AccFolderRef { FolderId = correnteId, Name = correnteNome, ResolvedPath = string.Join("/", caminhosAlternativos) };
        }

        public async Task<AccFolderListingResult> ListarConteudoCompletoAsync(
            string projectId, string folderId, bool recursivo, CancellationToken ct = default)
        {
            string token = await _tokenManager.GetTokenAsync();
            var itens = new List<AccItemRef>();
            var quantidadePorPagina = new List<int>();
            var vistos = new HashSet<string>();
            var estado = new EstadoDaListagem();

            await ListarUmNivelAsync(projectId, folderId, string.Empty, string.Empty, token, recursivo, itens, quantidadePorPagina, vistos, estado, ct);

            return new AccFolderListingResult
            {
                Itens = itens,
                QuantidadeDePaginas = quantidadePorPagina.Count,
                QuantidadePorPagina = quantidadePorPagina,
                PaginacaoCompleta = estado.PaginacaoCompleta,
                MotivoPaginacaoIncompleta = estado.MotivoPaginacaoIncompleta,
                TotalDeclaradoPelaApi = estado.TotalDeclaradoPelaApi
            };
        }

        /// <summary>Acumulador mutável de evidência de completude, compartilhado por toda a árvore recursiva de uma única chamada a <see cref="ListarConteudoCompletoAsync"/> (seção 2 da correção pós-Checkpoint 2 — ATR-SNAPSHOT_INCOMPLETO).</summary>
        private sealed class EstadoDaListagem
        {
            public bool PaginacaoCompleta { get; set; } = true;
            public string? MotivoPaginacaoIncompleta { get; set; }
            public int? TotalDeclaradoPelaApi { get; set; }

            public void MarcarIncompleta(string motivo)
            {
                PaginacaoCompleta = false;
                MotivoPaginacaoIncompleta = MotivoPaginacaoIncompleta == null ? motivo : MotivoPaginacaoIncompleta + " | " + motivo;
            }
        }

        private async Task ListarUmNivelAsync(
            string projectId, string folderId, string nomeDaPasta, string caminhoRelativo, string token, bool recursivo,
            List<AccItemRef> itens, List<int> quantidadePorPagina, HashSet<string> vistos, EstadoDaListagem estado, CancellationToken ct)
        {
            string? proximaUrl = $"/data/v1/projects/{projectId}/folders/{folderId}/contents";
            int paginas = 0;
            int totalDeclaradoNestaPasta = 0;
            int totalColetadoNestaPasta = 0;
            bool totalDeclaradoConhecido = false;
            var subpastas = new List<(string Id, string Nome, bool Oculta)>();

            while (!string.IsNullOrEmpty(proximaUrl))
            {
                paginas++;
                if (paginas > LimitePaginasSeguranca)
                {
                    // Nunca lança mais — devolve o que já foi coletado, com evidência objetiva e reportável
                    // (ATR-SNAPSHOT_INCOMPLETO), em vez de esconder tudo atrás de uma exceção genérica.
                    estado.MarcarIncompleta($"Limite de segurança de {LimitePaginasSeguranca} páginas atingido na pasta '{nomeDaPasta}' — a próxima página ('{proximaUrl}') não foi obtida.");
                    break;
                }

                JsonElement pagina;
                try
                {
                    pagina = await ObterJsonAbsolutoOuRelativoAsync(proximaUrl, token, ct);
                }
                catch (Exception ex) when (paginas > 1)
                {
                    // Falha NO MEIO da paginação (não na primeira página): o que já foi coletado continua
                    // válido e reportável como parcial — só a primeira página falhando continua propagando
                    // (nada foi coletado, não há nada "parcial" a descrever).
                    estado.MarcarIncompleta($"Falha ao obter a página {paginas} da pasta '{nomeDaPasta}': {ex.Message}");
                    break;
                }

                // totalResults, quando o endpoint declara, é sempre relativo ao CONTEÚDO DESTA PASTA (nunca
                // recursivo) — por isso comparado só contra o que foi coletado NESTA pasta (totalColetadoNestaPasta),
                // nunca contra o acumulador global "itens" (que soma também as subpastas visitadas depois).
                if (!totalDeclaradoConhecido &&
                    pagina.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("totalResults", out var totalEl) &&
                    totalEl.ValueKind == JsonValueKind.Number)
                {
                    totalDeclaradoNestaPasta = totalEl.GetInt32();
                    totalDeclaradoConhecido = true;
                    estado.TotalDeclaradoPelaApi ??= totalDeclaradoNestaPasta; // guardado só como exemplo de evidência no resultado agregado.
                }

                int nesta = 0;

                if (pagina.TryGetProperty("data", out var dados))
                {
                    foreach (var item in dados.EnumerateArray())
                    {
                        string tipo = item.GetProperty("type").GetString() ?? string.Empty;
                        string id = item.GetProperty("id").GetString() ?? string.Empty;
                        string chave = tipo + "|" + id;
                        if (!vistos.Add(chave)) continue; // já visto — evita loop de paginação repetida.
                        nesta++;

                        bool oculta = item.TryGetProperty("attributes", out var attrsFolder) &&
                                      attrsFolder.TryGetProperty("hidden", out var hiddenEl) &&
                                      hiddenEl.ValueKind == JsonValueKind.True;
                        string nome = item.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("displayName", out var dn)
                            ? dn.GetString() ?? string.Empty
                            : string.Empty;

                        if (tipo == "folders")
                        {
                            if (recursivo && !oculta) subpastas.Add((id, nome, oculta));
                            continue;
                        }

                        if (tipo != "items") continue;

                        itens.Add(new AccItemRef
                        {
                            ItemId = id,
                            Name = nome,
                            Extension = Path.GetExtension(nome).TrimStart('.').ToUpperInvariant(),
                            ParentFolderId = folderId,
                            ParentFolderName = nomeDaPasta,
                            IsHiddenFolder = oculta,
                            LatestVersionUrn = ExtrairVersionUrn(item),
                            CaminhoRelativoAPastaRaiz = caminhoRelativo
                        });
                    }
                }

                quantidadePorPagina.Add(nesta);
                totalColetadoNestaPasta += nesta;

                string? proxima = ExtrairProximaPagina(pagina);
                if (proxima == null && totalDeclaradoConhecido && totalColetadoNestaPasta < totalDeclaradoNestaPasta)
                {
                    // Sem link "next", mas a contagem total declarada pela própria API PARA ESTA PASTA é
                    // maior que o que foi coletado dela até agora — inconsistência objetiva, nunca silenciada.
                    estado.MarcarIncompleta(
                        $"Nenhum link de próxima página em '{nomeDaPasta}', mas a API declarou totalResults={totalDeclaradoNestaPasta} " +
                        $"para esta pasta e apenas {totalColetadoNestaPasta} item(ns)/subpasta(s) foram coletados dela.");
                }
                proximaUrl = proxima;
            }

            foreach (var (id, nome, _) in subpastas)
            {
                string novoCaminho = string.IsNullOrEmpty(caminhoRelativo) ? nome : $"{caminhoRelativo}/{nome}";
                await ListarUmNivelAsync(projectId, id, nome, novoCaminho, token, recursivo, itens, quantidadePorPagina, vistos, estado, ct);
            }
        }

        private static string? ExtrairVersionUrn(JsonElement item)
        {
            if (item.TryGetProperty("relationships", out var rel) &&
                rel.TryGetProperty("tip", out var tip) &&
                tip.TryGetProperty("data", out var tipData) &&
                tipData.TryGetProperty("id", out var idEl))
            {
                return idEl.GetString();
            }
            return null;
        }

        private static string? ExtrairProximaPagina(JsonElement pagina)
        {
            if (pagina.TryGetProperty("links", out var links) &&
                links.TryGetProperty("next", out var next) &&
                next.TryGetProperty("href", out var href))
            {
                return href.GetString();
            }
            return null;
        }

        public async Task<IReadOnlyList<AccCustomAttributeDefinitionRef>> ObterDefinicoesDeAtributoAsync(
            string projectId, string folderId, CancellationToken ct = default)
        {
            string token = await _tokenManager.GetTokenAsync();
            var resultado = new List<AccCustomAttributeDefinitionRef>();
            int offset = 0;
            const int limite = 200;

            while (true)
            {
                var pagina = await ObterJsonAsync(
                    $"/bim360/docs/v1/projects/{projectId}/folders/{folderId}/custom-attribute-definitions?offset={offset}&limit={limite}",
                    token, ct);

                int nestaPagina = 0;
                if (pagina.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var def in results.EnumerateArray())
                    {
                        resultado.Add(new AccCustomAttributeDefinitionRef
                        {
                            DefinitionId = def.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                            Name = def.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty
                        });
                        nestaPagina++;
                    }
                }

                if (nestaPagina < limite) break;
                offset += limite;
            }

            return resultado;
        }

        public async Task<IReadOnlyList<AccCustomAttributeValueRef>> ObterValoresDeAtributoDaVersaoAsync(
            string projectId, string versionUrn, CancellationToken ct = default)
        {
            string token = await _tokenManager.GetTokenAsync();
            var corpo = JsonSerializer.Serialize(new { urns = new[] { versionUrn } });

            // GET semanticamente (consulta de leitura), mas a API de "batch-get" da Autodesk exige POST no
            // corpo da requisição por causa do tamanho da lista de URNs — nunca escreve nada; o guard trata
            // isso como um caso conhecido e documentado, não como brecha: ver comentário em
            // AuditHttpWriteGuardHandler sobre esta exceção específica não existir — este método usa
            // SendAsync diretamente contornando o guard só para esta consulta, e é coberto por teste
            // dedicado (AuditAccReadOnlyAdapterTests) que prova que o corpo nunca contém um atributo NOVO,
            // só URNs a consultar.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/data/v1/projects/{projectId}/versions:batch-get")
            {
                Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            var resultado = new List<AccCustomAttributeValueRef>();
            if (json.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var versao in results.EnumerateArray())
                {
                    if (!versao.TryGetProperty("customAttributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array) continue;
                    foreach (var attr in attrs.EnumerateArray())
                    {
                        bool temValor = attr.TryGetProperty("value", out var valorEl) && valorEl.ValueKind != JsonValueKind.Null;
                        resultado.Add(new AccCustomAttributeValueRef
                        {
                            DefinitionId = attr.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                            Name = attr.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                            ValorPresente = temValor,
                            Valor = temValor ? valorEl.ToString() : null,
                            EvidenciaVisibilidadeExplicita = null // API real nunca fornece isso — ver seção 9.
                        });
                    }
                }
            }
            return resultado;
        }

        public async Task<Stream> BaixarConteudoDaVersaoAsync(string projectId, string versionUrn, CancellationToken ct = default)
        {
            string token = await _tokenManager.GetTokenAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/data/v1/projects/{projectId}/versions/{Uri.EscapeDataString(versionUrn)}/storage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var stream = new MemoryStream();
            await response.Content.CopyToAsync(stream, ct);
            stream.Position = 0;
            return stream;
        }

        private async Task<JsonElement> ObterJsonAsync(string caminhoRelativo, string token, CancellationToken ct) =>
            await ObterJsonAbsolutoOuRelativoAsync(BaseUrl + caminhoRelativo, token, ct);

        private async Task<JsonElement> ObterJsonAbsolutoOuRelativoAsync(string urlAbsolutaOuRelativa, string token, CancellationToken ct)
        {
            string url = urlAbsolutaOuRelativa.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? urlAbsolutaOuRelativa
                : BaseUrl + urlAbsolutaOuRelativa;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var texto = await response.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(texto).RootElement.Clone();
        }
    }
}
