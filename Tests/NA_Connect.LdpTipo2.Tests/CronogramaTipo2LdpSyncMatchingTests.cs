using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Cobre o algoritmo puro de matching Cronograma Tipo 2 x LDP Tipo 2
    /// (CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching): N&amp;A primeiro, Client Document
    /// Number como fallback com canonicalização automática para o N&amp;A da linha, e MATCH_AMBIGUO tanto
    /// por múltiplas tarefas do cronograma quanto por duplicidade de N&amp;A/Client dentro da própria LDP
    /// — itens 2, 3 e 7 do plano de finalização de LDP Tipo 2 / Cronograma Tipo 2.
    /// </summary>
    public class CronogramaTipo2LdpSyncMatchingTests
    {
        private static GateLdpParaSincronizacao Gate(long id, long ldpRegistroId, string na, string? cliente, byte percentual, bool ativo)
        {
            return new GateLdpParaSincronizacao
            {
                Id = id,
                LdpRegistroId = ldpRegistroId,
                ProjectIdAcc = "PROJ-TESTE",
                NumeroDocumentoNa = na,
                NumeroDocumentoCliente = cliente,
                GatePercentual = percentual,
                GateAtivo = ativo
            };
        }

        private static CronogramaTipo2Registro Candidato(string numeroNormalizado, byte percentual, string uid = "1")
        {
            return new CronogramaTipo2Registro
            {
                NumeroDocumentoNaOriginal = numeroNormalizado,
                NumeroDocumentoNaNormalizado = numeroNormalizado,
                GatePercentual = percentual,
                UidTarefaPersistido = uid,
                NomeTarefa = $"Tarefa {uid}"
            };
        }

        [Fact]
        public void CronogramaUsaNA_GeraMatchEncontradoComOrigemNA()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "DOC-001", "CLI-001", 30, ativo: true) };
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("DOC-001", 30) };

            var (decisoes, usados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            var decisao = Assert.Single(decisoes);
            Assert.Equal(StatusMatchingLdp.MatchEncontrado, decisao.Status);
            Assert.Equal(OrigemMatchLdp.Na, decisao.OrigemMatch);
            Assert.False(decisao.AmbiguoPorDuplicidadeLdp);
            Assert.Single(usados);
        }

        [Fact]
        public void CronogramaUsaClientDocumentNumber_GeraMatchEncontradoComOrigemClientECanonicalizaParaNA()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "CHS001-000-PC2-DR-003", "P-0602.1-310-PX-DR-0055", 60, ativo: true) };
            // O cronograma trouxe o número do CLIENTE — o normalizador não sabe disso, só compara texto.
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("P-0602.1-310-PX-DR-0055", 60) };

            var (decisoes, usados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            var decisao = Assert.Single(decisoes);
            Assert.Equal(StatusMatchingLdp.MatchEncontrado, decisao.Status);
            Assert.Equal(OrigemMatchLdp.Cliente, decisao.OrigemMatch);
            // Canonicalização: a decisão está associada ao Gate cujo NumeroDocumentoNa é a identidade
            // canônica da linha — nunca o Client.
            Assert.Equal("CHS001-000-PC2-DR-003", decisao.Gate.NumeroDocumentoNa);
            Assert.Single(usados);
        }

        [Fact]
        public void NumeroInexistente_GateAtivo_GeraCronogramaNaoEncontrado()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "DOC-001", "CLI-001", 30, ativo: true) };
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("DOC-999-NAO-EXISTE", 30) };

            var (decisoes, usados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            var decisao = Assert.Single(decisoes);
            Assert.Equal(StatusMatchingLdp.CronogramaNaoEncontrado, decisao.Status);
            Assert.Null(decisao.OrigemMatch);
            Assert.Empty(usados);
        }

        [Fact]
        public void NumeroInexistente_GateInativo_GeraGateInativoSemCronograma()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "DOC-001", "CLI-001", 30, ativo: false) };
            var candidatos = new List<CronogramaTipo2Registro>();

            var (decisoes, _) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            Assert.Equal(StatusMatchingLdp.GateInativoSemCronograma, Assert.Single(decisoes).Status);
        }

        [Fact]
        public void GateInativoNaLdp_ComCandidatoDoCronograma_GeraCronogramaParaGateInativo()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "DOC-001", "CLI-001", 30, ativo: false) };
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("DOC-001", 30) };

            var (decisoes, usados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            Assert.Equal(StatusMatchingLdp.CronogramaParaGateInativo, Assert.Single(decisoes).Status);
            Assert.Single(usados);
        }

        [Fact]
        public void NADuplicadoNaLDP_GeraMatchAmbiguoPorDuplicidadeLdpParaAmbasAsLinhas()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao>
            {
                Gate(1, 100, "DOC-001", "CLI-001", 30, ativo: true),
                Gate(2, 200, "DOC-001", "CLI-002", 30, ativo: true) // mesmo N&A, LdpRegistroId diferente = duas linhas
            };
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("DOC-001", 30) };

            var (decisoes, _) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            Assert.Equal(2, decisoes.Count);
            Assert.All(decisoes, d =>
            {
                Assert.Equal(StatusMatchingLdp.MatchAmbiguo, d.Status);
                Assert.True(d.AmbiguoPorDuplicidadeLdp);
            });
        }

        [Fact]
        public void ClientDuplicadoNaLDP_ComNADistintos_GeraMatchAmbiguoPorDuplicidadeLdp()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao>
            {
                Gate(1, 100, "DOC-001", "CLI-999", 60, ativo: true),
                Gate(2, 200, "DOC-002", "CLI-999", 60, ativo: true) // mesmo Client, N&A diferentes
            };
            // O candidato do cronograma bate com o Client "CLI-999" (nenhum candidato bate direto com
            // DOC-001/DOC-002 por N&A) — como o Client está duplicado, ambas as linhas ficam ambíguas.
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("CLI-999", 60) };

            var (decisoes, _) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            Assert.Equal(2, decisoes.Count);
            Assert.All(decisoes, d =>
            {
                Assert.Equal(StatusMatchingLdp.MatchAmbiguo, d.Status);
                Assert.Equal(OrigemMatchLdp.Cliente, d.OrigemMatch);
                Assert.True(d.AmbiguoPorDuplicidadeLdp);
            });
        }

        [Fact]
        public void MultiplasTarefasDoCronograma_ParaNAUnicoNaLDP_GeraMatchAmbiguoSemDuplicidadeLdp()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "DOC-001", "CLI-001", 30, ativo: true) };
            var candidatos = new List<CronogramaTipo2Registro>
            {
                Candidato("DOC-001", 30, uid: "1"),
                Candidato("DOC-001", 30, uid: "2")
            };

            var (decisoes, usados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            var decisao = Assert.Single(decisoes);
            Assert.Equal(StatusMatchingLdp.MatchAmbiguo, decisao.Status);
            Assert.False(decisao.AmbiguoPorDuplicidadeLdp);
            Assert.Equal(2, decisao.Candidatos.Count);
            Assert.Equal(2, usados.Count);
        }

        [Fact]
        public void CandidatoSemCorrespondencia_NaoEntraEmCandidatosUsados()
        {
            var gatesLdp = new List<GateLdpParaSincronizacao> { Gate(1, 100, "DOC-001", null, 30, ativo: true) };
            var candidatos = new List<CronogramaTipo2Registro> { Candidato("DOC-999", 30) };

            var (_, usados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            Assert.Empty(usados);
        }
    }
}
