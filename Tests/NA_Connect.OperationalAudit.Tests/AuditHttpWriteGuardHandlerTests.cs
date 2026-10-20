using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Safety;

namespace NA_Connect.OperationalAudit.Tests
{
    public class AuditHttpWriteGuardHandlerTests
    {
        /// <summary>Handler interno espiao: registra se foi invocado e devolve uma resposta 200 fixa — nunca faz uma chamada de rede real.</summary>
        private sealed class InnerHandlerEspiao : HttpMessageHandler
        {
            public bool Invocado { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Invocado = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        private static HttpClient CriarCliente(out InnerHandlerEspiao espiao)
        {
            espiao = new InnerHandlerEspiao();
            var guard = new AuditHttpWriteGuardHandler(espiao);
            return new HttpClient(guard) { BaseAddress = new Uri("https://developer.api.autodesk.com") };
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        public async Task SendAsync_MetodosDeLeitura_PassamParaOInnerHandler(string metodo)
        {
            using var client = CriarCliente(out var espiao);
            var request = new HttpRequestMessage(new HttpMethod(metodo), "recurso");

            var response = await client.SendAsync(request);

            Assert.True(espiao.Invocado);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public async Task SendAsync_MetodosDeEscrita_LancamENuncaChegamAoInnerHandler(string metodo)
        {
            using var client = CriarCliente(out var espiao);
            var request = new HttpRequestMessage(new HttpMethod(metodo), "recurso");

            await Assert.ThrowsAsync<ForbiddenOperationException>(() => client.SendAsync(request));

            Assert.False(espiao.Invocado);
        }

        [Fact]
        public async Task SendAsync_MensagemDeErro_IncluiOMetodoBloqueado()
        {
            using var client = CriarCliente(out _);
            var request = new HttpRequestMessage(HttpMethod.Post, "recurso");

            var ex = await Assert.ThrowsAsync<ForbiddenOperationException>(() => client.SendAsync(request));

            Assert.Contains("POST", ex.Message);
        }
    }
}
