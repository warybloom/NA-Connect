using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4.1, seção 2: validador de conectividade ACC somente leitura — sempre com HttpMessageHandler/token provider fake, nunca ACC real.</summary>
    public class AccReadOnlyConnectivityValidatorTests
    {
        private static (AccReadOnlyConnectivityValidator Validador, FakeHttpMessageHandler Handler, FakeAccTokenProvider TokenProvider) NovoValidador()
        {
            var handler = new FakeHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var tokenProvider = new FakeAccTokenProvider();
            return (new AccReadOnlyConnectivityValidator(httpClient, tokenProvider), handler, tokenProvider);
        }

        [Fact]
        public async Task TokenDisponivel_Get200_RetornaOk()
        {
            var (validador, handler, _) = NovoValidador();
            handler.RespostaStatusCode = HttpStatusCode.OK;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Ok, resultado.ApsAuth);
            Assert.Equal(ConnectivityCheckStatus.Ok, resultado.AccProjectRead);
        }

        [Fact]
        public async Task TokenIndisponivel_RetornaFailed_NuncaTentaOGet()
        {
            var (validador, handler, tokenProvider) = NovoValidador();
            tokenProvider.LancarExcecao = true;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.ApsAuth);
            Assert.Equal(ConnectivityCheckStatus.NotTested, resultado.AccProjectRead);
            Assert.Equal(0, handler.QuantidadeDeChamadas); // nunca tenta o GET sem token.
        }

        [Fact]
        public async Task TokenVazio_RetornaFailed_NuncaTentaOGet()
        {
            var (validador, handler, tokenProvider) = NovoValidador();
            tokenProvider.Token = "";

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.ApsAuth);
            Assert.Equal(0, handler.QuantidadeDeChamadas);
        }

        [Fact]
        public async Task Get401_RetornaFailed()
        {
            var (validador, handler, _) = NovoValidador();
            handler.RespostaStatusCode = HttpStatusCode.Unauthorized;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Ok, resultado.ApsAuth); // token foi obtido normalmente.
            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.AccProjectRead);
        }

        [Fact]
        public async Task Get403_RetornaFailed()
        {
            var (validador, handler, _) = NovoValidador();
            handler.RespostaStatusCode = HttpStatusCode.Forbidden;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.AccProjectRead);
        }

        [Fact]
        public async Task Get404Projeto_RetornaFailed()
        {
            var (validador, handler, _) = NovoValidador();
            handler.RespostaStatusCode = HttpStatusCode.NotFound;

            var resultado = await validador.ValidarAsync("PRJ-INEXISTENTE");

            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.AccProjectRead);
            Assert.Contains("404", resultado.Detalhe);
        }

        [Fact]
        public async Task Get500_RetornaFailed_ConformeContrato()
        {
            var (validador, handler, _) = NovoValidador();
            handler.RespostaStatusCode = HttpStatusCode.InternalServerError;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.AccProjectRead);
        }

        [Fact]
        public async Task FalhaDeRede_NuncaLanca_RetornaFailed()
        {
            var (validador, handler, _) = NovoValidador();
            handler.LancarExcecao = true;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.Equal(ConnectivityCheckStatus.Failed, resultado.AccProjectRead);
        }

        [Fact]
        public async Task NuncaEnviaVerboDiferenteDeGet()
        {
            var (validador, handler, _) = NovoValidador();

            await validador.ValidarAsync("PRJ-1");

            Assert.NotNull(handler.UltimaRequisicao);
            Assert.Equal(HttpMethod.Get, handler.UltimaRequisicao!.Method);
        }

        [Fact]
        public async Task TokenNuncaApareceNaUrlOuNoDetalhe()
        {
            var (validador, handler, tokenProvider) = NovoValidador();
            tokenProvider.Token = "TOKEN-SUPER-SECRETO-XYZ";
            handler.RespostaStatusCode = HttpStatusCode.OK;

            var resultado = await validador.ValidarAsync("PRJ-1");

            Assert.DoesNotContain("TOKEN-SUPER-SECRETO-XYZ", handler.UltimaRequisicao!.RequestUri!.ToString());
            Assert.DoesNotContain("TOKEN-SUPER-SECRETO-XYZ", resultado.Detalhe ?? string.Empty);
            // O token vai só no header Authorization (nunca na URL/corpo) — checagem estrutural adicional.
            Assert.NotNull(handler.UltimaRequisicao.Headers.Authorization);
            Assert.Equal("Bearer", handler.UltimaRequisicao.Headers.Authorization!.Scheme);
        }

        [Fact]
        public async Task UsaOMesmoEndpointJaUsadoPeloAdaptadorReal_TopFolders()
        {
            var (validador, handler, _) = NovoValidador();

            await validador.ValidarAsync("PRJ-XYZ");

            Assert.Contains("/data/v1/projects/PRJ-XYZ/topFolders", handler.UltimaRequisicao!.RequestUri!.ToString());
        }
    }
}
