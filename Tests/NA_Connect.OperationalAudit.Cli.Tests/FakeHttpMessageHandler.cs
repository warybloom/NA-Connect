using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Handler HTTP fake — nunca sai da máquina de teste, nunca faz rede real (Checkpoint 4.1, seção 2).</summary>
    public sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode RespostaStatusCode { get; set; } = HttpStatusCode.OK;
        public string RespostaCorpo { get; set; } = "{}";
        public HttpRequestMessage? UltimaRequisicao { get; private set; }
        public int QuantidadeDeChamadas { get; private set; }
        public bool LancarExcecao { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaRequisicao = request;
            QuantidadeDeChamadas++;

            if (LancarExcecao) throw new HttpRequestException("Falha de rede simulada.");

            return Task.FromResult(new HttpResponseMessage(RespostaStatusCode)
            {
                Content = new StringContent(RespostaCorpo)
            });
        }
    }

    public sealed class FakeAccTokenProvider : IAccTokenProvider
    {
        public string? Token { get; set; } = "fake-token-nunca-real";
        public bool LancarExcecao { get; set; }

        public Task<string> GetTokenAsync()
        {
            if (LancarExcecao) throw new InvalidOperationException("Falha simulada ao obter token.");
            return Task.FromResult(Token ?? string.Empty);
        }
    }
}
