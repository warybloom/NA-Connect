using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a correção do limite de 200 itens da Etapa 7 (seção 6/7/10/24 do pedido de correção):
    /// AutodeskApiService.GetFolderContentsAllPagesAsync percorre TODAS as páginas via links.next.href,
    /// nunca depende de um tamanho de página fixo, nunca retorna lista parcial silenciosamente (lança
    /// exceção em qualquer falha de página) e protege contra loop (URL repetida) e itens duplicados entre
    /// páginas. Não acessa o ACC real — todas as respostas HTTP são simuladas por
    /// <see cref="SequenceHttpMessageHandler"/>.
    /// </summary>
    public class FolderContentsPaginationTests
    {
        private sealed class SequenceHttpMessageHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _respostas;
            public List<string> UrlsRequisitadas { get; } = new List<string>();

            public SequenceHttpMessageHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> respostas)
            {
                _respostas = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(respostas);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                UrlsRequisitadas.Add(request.RequestUri!.ToString());
                if (_respostas.Count == 0)
                {
                    throw new InvalidOperationException("Fake handler sem mais respostas configuradas — o SUT fez mais requisições do que o esperado pelo teste.");
                }
                return Task.FromResult(_respostas.Dequeue()(request));
            }
        }

        private static AutodeskApiService CriarServico(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> respostas, out SequenceHttpMessageHandler handler)
        {
            handler = new SequenceHttpMessageHandler(respostas);
            var httpClient = new HttpClient(handler);
            var tokenManager = new TokenManager(new HttpClient(), new ConfigurationBuilder().Build(), new MemoryCache(new MemoryCacheOptions()));
            return new AutodeskApiService(httpClient, tokenManager, new ConfigurationBuilder().Build(), NullLogger<AutodeskApiService>.Instance, new DocumentMatchingService());
        }

        private static HttpResponseMessage Pagina(int quantidade, string? nextHref, int idInicial = 0, bool comFolders = false, HttpStatusCode status = HttpStatusCode.OK)
        {
            var dados = new List<object>();
            for (int i = 0; i < quantidade; i++)
            {
                int idx = idInicial + i;
                dados.Add(new
                {
                    type = comFolders && i % 50 == 49 ? "folders" : "items",
                    id = $"urn:id-{idx}",
                    attributes = new { displayName = $"Documento-{idx}.pdf", lastModifiedTime = "2026-01-01T00:00:00Z", hidden = false },
                    relationships = new { tip = new { data = new { id = $"urn:adsk.wipprod:fs.file:vf.item-{idx}?version=1" } } }
                });
            }

            var corpo = new
            {
                data = dados,
                links = new { next = nextHref == null ? null : new { href = nextHref } }
            };
            var json = JsonSerializer.Serialize(corpo);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? json : "erro simulado", Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "text/plain")
            };
        }

        // 1 — pasta com 0 itens.
        [Fact]
        public async Task PastaComZeroItens_RetornaListaVazia()
        {
            var servico = CriarServico(new[] { (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Pagina(0, null)) }, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Empty(resultado);
        }

        // 2 — pasta com 199 itens (uma página, abaixo do limite antigo de 200).
        [Fact]
        public async Task PastaCom199Itens_RetornaTodosEmUmaPagina()
        {
            var servico = CriarServico(new[] { (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Pagina(199, null)) }, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(199, resultado.Count);
            Assert.Single(handler.UrlsRequisitadas);
        }

        // 3 — pasta com exatamente 200 itens (o limite antigo não pode truncar).
        [Fact]
        public async Task PastaCom200Itens_NaoTruncaEmUmaPagina()
        {
            var servico = CriarServico(new[] { (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Pagina(200, null)) }, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(200, resultado.Count);
        }

        // 4 — pasta com 201 itens em duas páginas (200 + 1) — o caso mínimo que provava o bug real.
        [Fact]
        public async Task PastaCom201Itens_PercorreDuasPaginas()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(200, "https://developer.api.autodesk.com/data/v1/projects/p/folders/f/contents?page=2", idInicial: 0),
                _ => Pagina(1, null, idInicial: 200)
            };
            var servico = CriarServico(respostas, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(201, resultado.Count);
            Assert.Equal(2, handler.UrlsRequisitadas.Count);
        }

        // 5 — pasta com 284 itens (04.02 real reportado com mais de 200).
        [Fact]
        public async Task PastaCom284Itens_AcumulaTodasAsPaginas()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(200, "https://developer.api.autodesk.com/next?page=2", idInicial: 0),
                _ => Pagina(84, null, idInicial: 200)
            };
            var servico = CriarServico(respostas, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(284, resultado.Count);
        }

        // 6 — pasta com 448 itens (10.01 real reportado como "175 de 448 selecionados").
        [Fact]
        public async Task PastaCom448Itens_AcumulaTresPaginas()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(200, "https://developer.api.autodesk.com/next?page=2", idInicial: 0),
                _ => Pagina(200, "https://developer.api.autodesk.com/next?page=3", idInicial: 200),
                _ => Pagina(48, null, idInicial: 400)
            };
            var servico = CriarServico(respostas, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(448, resultado.Count);
            Assert.Equal(3, handler.UrlsRequisitadas.Count);
        }

        // 7 — pasta com 500 itens.
        [Fact]
        public async Task PastaCom500Itens_AcumulaTodasAsPaginas()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(200, "https://developer.api.autodesk.com/next?page=2", idInicial: 0),
                _ => Pagina(200, "https://developer.api.autodesk.com/next?page=3", idInicial: 200),
                _ => Pagina(100, null, idInicial: 400)
            };
            var servico = CriarServico(respostas, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(500, resultado.Count);
        }

        // 8 — links.next inexistente encerra a paginação.
        [Fact]
        public async Task LinksNextInexistente_EncerraPaginacao()
        {
            var servico = CriarServico(new[] { (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Pagina(10, null)) }, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(10, resultado.Count);
            Assert.Single(handler.UrlsRequisitadas);
        }

        // 9 — links.next nulo (href/next explicitamente null) encerra a paginação.
        [Fact]
        public async Task LinksNextNulo_EncerraPaginacao()
        {
            HttpResponseMessage RespostaComNextNulo(HttpRequestMessage _)
            {
                var corpo = new
                {
                    data = new[]
                    {
                        new { type = "items", id = "urn:id-0", attributes = new { displayName = "Doc.pdf", lastModifiedTime = "2026-01-01", hidden = false }, relationships = new { tip = new { data = new { id = "urn:adsk.wipprod:fs.file:vf.a?version=1" } } } }
                    },
                    links = new { next = (object?)null }
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json") };
            }

            var servico = CriarServico(new Func<HttpRequestMessage, HttpResponseMessage>[] { RespostaComNextNulo }, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Single(resultado);
            Assert.Single(handler.UrlsRequisitadas);
        }

        // 10 — links.next.href absoluto funciona.
        [Fact]
        public async Task LinksNextHrefAbsoluto_Funciona()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(5, "https://developer.api.autodesk.com/data/v1/projects/p/folders/f/contents?page=2", idInicial: 0),
                _ => Pagina(3, null, idInicial: 5)
            };
            var servico = CriarServico(respostas, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(8, resultado.Count);
            Assert.StartsWith("https://developer.api.autodesk.com/data/v1/projects/p/folders/f/contents?page=2", handler.UrlsRequisitadas[1]);
        }

        // 11 — links.next.href relativo funciona (resolvido contra a URL da página atual).
        [Fact]
        public async Task LinksNextHrefRelativo_Funciona()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(5, "?page=2", idInicial: 0),
                _ => Pagina(3, null, idInicial: 5)
            };
            var servico = CriarServico(respostas, out var handler);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");
            Assert.Equal(8, resultado.Count);
            Assert.True(Uri.IsWellFormedUriString(handler.UrlsRequisitadas[1], UriKind.Absolute));
        }

        // 12 — URL repetida bloqueia loop (a mesma URL nunca é revisitada).
        [Fact]
        public async Task UrlRepetida_BloqueiaLoopInfinito()
        {
            const string urlInicial = "https://developer.api.autodesk.com/data/v1/projects/p/folders/f/contents";
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(1, urlInicial, idInicial: 0) // aponta para si mesma -> loop
            };
            var servico = CriarServico(respostas, out _);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token"));
        }

        // 13 — falha na segunda página nunca retorna snapshot parcial (lança exceção; página 1 não é
        // silenciosamente aceita como resultado final).
        [Fact]
        public async Task FalhaNaSegundaPagina_NaoRetornaListaParcial()
        {
            var respostas = new Func<HttpRequestMessage, HttpResponseMessage>[]
            {
                _ => Pagina(200, "https://developer.api.autodesk.com/next?page=2", idInicial: 0),
                _ => Pagina(0, null, status: HttpStatusCode.InternalServerError)
            };
            var servico = CriarServico(respostas, out _);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token"));
        }

        // 14 — item repetido em duas páginas (mesmo Type+Id) é deduplicado.
        [Fact]
        public async Task ItemRepetidoEmDuasPaginas_EhDeduplicadoPorTypeEId()
        {
            HttpResponseMessage Pagina1(HttpRequestMessage _) => Pagina(200, "https://developer.api.autodesk.com/next?page=2", idInicial: 0);
            HttpResponseMessage Pagina2(HttpRequestMessage _) => Pagina(50, null, idInicial: 150); // itens 150-199 repetidos da página 1

            var servico = CriarServico(new Func<HttpRequestMessage, HttpResponseMessage>[] { Pagina1, Pagina2 }, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");

            // 200 (página 1) + 50 (página 2, todos repetidos) deduplicados por Type+Id -> continuam 200.
            Assert.Equal(200, resultado.Count);
        }

        // 15 — arquivos (type=items) e subpastas (type=folders) continuam diferenciados na resposta.
        [Fact]
        public async Task ArquivosESubpastas_ContinuamDiferenciados()
        {
            var servico = CriarServico(new[] { (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Pagina(100, null, comFolders: true)) }, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");

            Assert.Contains(resultado, i => i.Type == "items");
            Assert.Contains(resultado, i => i.Type == "folders");
            Assert.Equal(100, resultado.Count);
        }

        // Item sem Id é ignorado e logado como erro (seção 7) — não interrompe a coleta dos demais itens.
        [Fact]
        public async Task ItemSemId_EhIgnoradoSemInterromperColeta()
        {
            HttpResponseMessage RespostaComItemSemId(HttpRequestMessage _)
            {
                var corpo = new
                {
                    data = new object[]
                    {
                        new { type = "items", id = "", attributes = new { displayName = "SemId.pdf", hidden = false } },
                        new { type = "items", id = "urn:id-1", attributes = new { displayName = "ComId.pdf", hidden = false }, relationships = new { tip = new { data = new { id = "urn:adsk.wipprod:fs.file:vf.a?version=1" } } } }
                    },
                    links = new { }
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json") };
            }

            var servico = CriarServico(new Func<HttpRequestMessage, HttpResponseMessage>[] { RespostaComItemSemId }, out _);
            var resultado = await servico.GetFolderContentsAllPagesAsync("b.proj", "folder1", "token");

            Assert.Single(resultado);
            Assert.Equal("ComId.pdf", resultado[0].Name);
        }
    }
}
