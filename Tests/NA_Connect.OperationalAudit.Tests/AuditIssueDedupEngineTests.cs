using System;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Dedup;

namespace NA_Connect.OperationalAudit.Tests
{
    public class AuditIssueDedupEngineTests
    {
        [Fact]
        public void DecidirParaOcorrenciaEncontrada_SemIssueExistente_CriaNovoAberto()
        {
            var agora = DateTime.UtcNow;
            var decisao = AuditIssueDedupEngine.DecidirParaOcorrenciaEncontrada(null, agora);

            Assert.True(decisao.EhNovo);
            Assert.Equal(OperationalIssueStatus.Aberto, decisao.NovoStatus);
            Assert.Equal(agora, decisao.FirstSeenAtUtc);
            Assert.Equal(agora, decisao.LastSeenAtUtc);
            Assert.Equal(1, decisao.OccurrenceCount);
        }

        [Fact]
        public void DecidirParaOcorrenciaEncontrada_IssueJaAberto_ContinuaAberto_IncrementaOcorrencia_PreservaFirstSeen()
        {
            var primeiraVez = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var agora = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            var existente = new AuditIssueSnapshot
            {
                Status = OperationalIssueStatus.Aberto,
                FirstSeenAtUtc = primeiraVez,
                LastSeenAtUtc = primeiraVez,
                OccurrenceCount = 3
            };

            var decisao = AuditIssueDedupEngine.DecidirParaOcorrenciaEncontrada(existente, agora);

            Assert.False(decisao.EhNovo);
            Assert.Equal(OperationalIssueStatus.Aberto, decisao.NovoStatus);
            Assert.Equal(primeiraVez, decisao.FirstSeenAtUtc);
            Assert.Equal(agora, decisao.LastSeenAtUtc);
            Assert.Equal(4, decisao.OccurrenceCount);
        }

        [Theory]
        [InlineData(OperationalIssueStatus.PossivelmenteResolvido)]
        [InlineData(OperationalIssueStatus.ResolvidoComEvidencia)]
        public void DecidirParaOcorrenciaEncontrada_ReapareceuDepoisDeResolvido_ViraRegressao(OperationalIssueStatus statusAnterior)
        {
            var existente = new AuditIssueSnapshot
            {
                Status = statusAnterior,
                FirstSeenAtUtc = DateTime.UtcNow.AddDays(-10),
                LastSeenAtUtc = DateTime.UtcNow.AddDays(-5),
                OccurrenceCount = 2
            };

            var decisao = AuditIssueDedupEngine.DecidirParaOcorrenciaEncontrada(existente, DateTime.UtcNow);

            Assert.Equal(OperationalIssueStatus.Regressao, decisao.NovoStatus);
            // Nunca volta silenciosamente para Aberto — regressao e uma informacao distinta.
            Assert.NotEqual(OperationalIssueStatus.Aberto, decisao.NovoStatus);
        }

        [Fact]
        public void DecidirParaAusenciaNestaExecucao_JaResolvidoComEvidencia_PermaneceInalterado_MesmoSemProva()
        {
            var resultado = AuditIssueDedupEngine.DecidirParaAusenciaNestaExecucao(
                OperationalIssueStatus.ResolvidoComEvidencia, requisitoPositivoComprovadoNestaExecucao: false);

            Assert.Equal(OperationalIssueStatus.ResolvidoComEvidencia, resultado);
        }

        [Fact]
        public void DecidirParaAusenciaNestaExecucao_AbertoSemProvaPositiva_NuncaResolvePorAusenciaSozinha()
        {
            var resultado = AuditIssueDedupEngine.DecidirParaAusenciaNestaExecucao(
                OperationalIssueStatus.Aberto, requisitoPositivoComprovadoNestaExecucao: false);

            Assert.Equal(OperationalIssueStatus.PossivelmenteResolvido, resultado);
            Assert.NotEqual(OperationalIssueStatus.ResolvidoComEvidencia, resultado);
        }

        [Fact]
        public void DecidirParaAusenciaNestaExecucao_AbertoComProvaPositivaExplicita_ResolveComEvidencia()
        {
            var resultado = AuditIssueDedupEngine.DecidirParaAusenciaNestaExecucao(
                OperationalIssueStatus.Aberto, requisitoPositivoComprovadoNestaExecucao: true);

            Assert.Equal(OperationalIssueStatus.ResolvidoComEvidencia, resultado);
        }

        [Fact]
        public void DecidirParaAusenciaNestaExecucao_RegressaoComProvaPositiva_ResolveComEvidencia()
        {
            var resultado = AuditIssueDedupEngine.DecidirParaAusenciaNestaExecucao(
                OperationalIssueStatus.Regressao, requisitoPositivoComprovadoNestaExecucao: true);

            Assert.Equal(OperationalIssueStatus.ResolvidoComEvidencia, resultado);
        }
    }
}
