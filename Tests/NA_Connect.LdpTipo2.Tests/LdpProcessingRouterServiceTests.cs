using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;
using APS.WebApp.Services.LdpRouting;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Cobre a orquestração das 4 etapas do Tipo 2 em LdpProcessingRouterService (item 4 do checkpoint
    /// pré-canário) com fakes — sem SQL/ACC real. Prova: sequência correta de chamadas; parada imediata
    /// quando uma etapa falha (etapas seguintes NUNCA são chamadas); exceção de qualquer etapa é
    /// capturada e vira Sucesso=false sem propagar; caminho Tipo1 nunca chama nenhum fake.
    /// </summary>
    public class LdpProcessingRouterServiceTests
    {
        private sealed class FakeLdpTipo2Service : ILdpTipo2Service
        {
            public int Chamadas;
            public LdpTipo2Resultado Resultado = new LdpTipo2Resultado { DocumentosEncontrados = 1 };
            public Exception LancarExcecao;

            public Task<LdpTipo2Resultado> ProcessarAsync(string hubId, string projectId, string accessToken, string ldpFileId, CancellationToken cancellationToken = default)
            {
                Chamadas++;
                if (LancarExcecao != null) throw LancarExcecao;
                return Task.FromResult(Resultado);
            }
        }

        private sealed class FakeCronogramaTipo2Service : ICronogramaTipo2Service
        {
            public int Chamadas;
            public CronogramaTipo2Resultado Resultado = new CronogramaTipo2Resultado { RegistrosValidos = 1, TarefasAnalisadas = 1 };

            public Task<CronogramaTipo2Resultado> ProcessarAsync(string hubId, string projectId, string accessToken, CancellationToken cancellationToken = default)
            {
                Chamadas++;
                return Task.FromResult(Resultado);
            }
        }

        private sealed class FakeCronogramaTipo2LdpSyncService : ICronogramaTipo2LdpSyncService
        {
            public int Chamadas;
            public CronogramaTipo2LdpSyncResult Resultado = new CronogramaTipo2LdpSyncResult { Sucesso = true };

            public Task<CronogramaTipo2LdpSyncResult> SincronizarAsync(string projectIdAcc, CancellationToken cancellationToken = default)
            {
                Chamadas++;
                return Task.FromResult(Resultado);
            }
        }

        private sealed class FakeCronogramaTipo2LdpPublicacaoService : ICronogramaTipo2LdpPublicacaoService
        {
            public int Chamadas;
            public CronogramaTipo2LdpPublicacaoResultado Resultado = new CronogramaTipo2LdpPublicacaoResultado { Sucesso = true, Mensagem = "ok" };

            public Task<CronogramaTipo2LdpPublicacaoResultado> PublicarSeHouverAlteracaoAsync(
                string hubId, string projectId, string accessToken, string ldpFileId,
                IReadOnlyList<CronogramaTipo2LdpPlanilhaAtualizacaoItem> atualizacoes, CancellationToken cancellationToken = default)
            {
                Chamadas++;
                return Task.FromResult(Resultado);
            }
        }

        private static (LdpProcessingRouterService Router, FakeLdpTipo2Service Ldp, FakeCronogramaTipo2Service Cronograma, FakeCronogramaTipo2LdpSyncService Sync, FakeCronogramaTipo2LdpPublicacaoService Publicacao) NovoRouter()
        {
            var ldp = new FakeLdpTipo2Service();
            var cronograma = new FakeCronogramaTipo2Service();
            var sync = new FakeCronogramaTipo2LdpSyncService();
            var publicacao = new FakeCronogramaTipo2LdpPublicacaoService();
            var router = new LdpProcessingRouterService(ldp, cronograma, sync, publicacao, NullLogger<LdpProcessingRouterService>.Instance);
            return (router, ldp, cronograma, sync, publicacao);
        }

        private static Projeto NovoProjetoTipo2() => new Projeto { ProjectIdAcc = "PROJ-TESTE", NomeProjeto = "Projeto Teste", TipoLdp = TipoLdp.Tipo2 };
        private static Projeto NovoProjetoTipo1() => new Projeto { ProjectIdAcc = "PROJ-TESTE", NomeProjeto = "Projeto Teste", TipoLdp = TipoLdp.Tipo1 };

        [Fact]
        public async Task Tipo2_CaminhoFeliz_ChamaAsQuatroEtapasNaOrdem()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();

            var resultado = await router.ProcessarAsync(NovoProjetoTipo2(), "hub", "token", "ldp-file-id");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, ldp.Chamadas);
            Assert.Equal(1, cronograma.Chamadas);
            Assert.Equal(1, sync.Chamadas);
            Assert.Equal(1, publicacao.Chamadas);
        }

        // Correção 16/09/2026 (fase 2): campos granulares que GrdAutomationWorker usa para reconhecer as
        // Etapas 09/11 legadas como "executadas pelo equivalente Tipo2" — nunca uma segunda execução.
        [Fact]
        public async Task Tipo2_CaminhoFeliz_ExpoeCamposGranularesLdpECronograma()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();
            ldp.Resultado = new LdpTipo2Resultado { DocumentosEncontrados = 456 };
            cronograma.Resultado = new CronogramaTipo2Resultado { RegistrosValidos = 612, TarefasAnalisadas = 761 };
            sync.Resultado = new CronogramaTipo2LdpSyncResult { Sucesso = true, MatchesEncontrados = 533 };

            var resultado = await router.ProcessarAsync(NovoProjetoTipo2(), "hub", "token", "ldp-file-id");

            Assert.True(resultado.LdpPersistidoComSucesso);
            Assert.Equal(456, resultado.LdpDocumentosEncontrados);
            Assert.True(resultado.CronogramaSincronizadoComSucesso);
            Assert.Equal(612, resultado.CronogramaTarefasValidas);
            Assert.Equal(761, resultado.CronogramaTarefasAnalisadas);
            Assert.Equal(533, resultado.CronogramaMatchesEncontrados);
        }

        [Fact]
        public async Task Tipo2_LdpComErro_InterrompeAntesDoCronograma()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();
            ldp.Resultado = new LdpTipo2Resultado { Erros = new List<string> { "layout inválido" } };

            var resultado = await router.ProcessarAsync(NovoProjetoTipo2(), "hub", "token", "ldp-file-id");

            Assert.False(resultado.Sucesso);
            Assert.Contains("layout inválido", resultado.Mensagem);
            Assert.Equal(1, ldp.Chamadas);
            Assert.Equal(0, cronograma.Chamadas);
            Assert.Equal(0, sync.Chamadas);
            Assert.Equal(0, publicacao.Chamadas);
            Assert.False(resultado.LdpPersistidoComSucesso);
            Assert.Null(resultado.CronogramaSincronizadoComSucesso);
        }

        [Fact]
        public async Task Tipo2_CronogramaComErro_InterrompeAntesDoMatching()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();
            cronograma.Resultado = new CronogramaTipo2Resultado { Erros = new List<string> { "falha ao baixar mpp" } };

            var resultado = await router.ProcessarAsync(NovoProjetoTipo2(), "hub", "token", "ldp-file-id");

            Assert.False(resultado.Sucesso);
            Assert.Equal(1, ldp.Chamadas);
            Assert.Equal(1, cronograma.Chamadas);
            Assert.Equal(0, sync.Chamadas);
            Assert.Equal(0, publicacao.Chamadas);
            Assert.True(resultado.LdpPersistidoComSucesso);
            Assert.False(resultado.CronogramaSincronizadoComSucesso);
        }

        [Fact]
        public async Task Tipo2_MatchingComFalha_InterrompeAntesDaPublicacao()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();
            sync.Resultado = new CronogramaTipo2LdpSyncResult { Sucesso = false, Erros = new List<string> { "falha sql" } };

            var resultado = await router.ProcessarAsync(NovoProjetoTipo2(), "hub", "token", "ldp-file-id");

            Assert.False(resultado.Sucesso);
            Assert.Equal(1, ldp.Chamadas);
            Assert.Equal(1, cronograma.Chamadas);
            Assert.Equal(1, sync.Chamadas);
            Assert.Equal(0, publicacao.Chamadas);
            Assert.True(resultado.LdpPersistidoComSucesso);
            Assert.False(resultado.CronogramaSincronizadoComSucesso);
        }

        [Fact]
        public async Task Tipo2_ExcecaoNaoTratadaEmQualquerEtapa_NuncaPropagaEViraFalha()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();
            ldp.LancarExcecao = new InvalidOperationException("bug inesperado");

            var resultado = await router.ProcessarAsync(NovoProjetoTipo2(), "hub", "token", "ldp-file-id");

            Assert.False(resultado.Sucesso);
            Assert.Contains("bug inesperado", resultado.Mensagem);
            Assert.Equal(0, cronograma.Chamadas);
        }

        [Fact]
        public async Task Tipo1_NuncaChamaNenhumFakeDoTipo2()
        {
            var (router, ldp, cronograma, sync, publicacao) = NovoRouter();

            var resultado = await router.ProcessarAsync(NovoProjetoTipo1(), "hub", "token", "ldp-file-id");

            Assert.True(resultado.Sucesso);
            Assert.Equal(TipoLdp.Tipo1, resultado.TipoLdp);
            Assert.Equal(0, ldp.Chamadas);
            Assert.Equal(0, cronograma.Chamadas);
            Assert.Equal(0, sync.Chamadas);
            Assert.Equal(0, publicacao.Chamadas);
        }
    }
}
