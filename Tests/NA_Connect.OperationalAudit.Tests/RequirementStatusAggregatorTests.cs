using System;
using System.Collections.Generic;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Aggregation;

namespace NA_Connect.OperationalAudit.Tests
{
    public class RequirementStatusAggregatorTests
    {
        private static OperationalRequirementResult Requisito(OperationalRequirementStatus status) =>
            new OperationalRequirementResult { ProjectIdAcc = "P", Etapa = "7", RequirementCode = "REQ", Status = status };

        private static OperationalFinding Finding(
            bool bloqueiaEtapa,
            OperationalAuditErrorDomain dominio = OperationalAuditErrorDomain.Operacional,
            OperationalAuditClassification classificacao = OperationalAuditClassification.DadoInvalido) =>
            new OperationalFinding
            {
                ProjectIdAcc = "P",
                Etapa = "7",
                CodigoProblema = "COD",
                Titulo = "T",
                BloqueiaEtapa = bloqueiaEtapa,
                ErrorDomain = dominio,
                Classificacao = classificacao
            };

        private static OperationalStageResult Agregar(
            IReadOnlyList<OperationalRequirementResult>? requisitos = null,
            IReadOnlyList<OperationalFinding>? findings = null) =>
            RequirementStatusAggregator.Agregar(
                Guid.NewGuid(), "P", "7", "Etapa 7",
                requisitos ?? Array.Empty<OperationalRequirementResult>(),
                findings ?? Array.Empty<OperationalFinding>(),
                DateTime.UtcNow, DateTime.UtcNow);

        [Fact]
        public void SemRequisitos_ResultaEmNaoExecutada()
        {
            Assert.Equal(OperationalAuditStageStatus.NaoExecutada, Agregar().Resultado);
        }

        [Fact]
        public void BloqueioDeAutomacao_VenceQualquerOutroProblema_MesmoComFalhaDeDados()
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Failed) };
            var findings = new[]
            {
                Finding(bloqueiaEtapa: true, dominio: OperationalAuditErrorDomain.Automacao),
                Finding(bloqueiaEtapa: true, classificacao: OperationalAuditClassification.ConfiguracaoProjeto)
            };

            Assert.Equal(OperationalAuditStageStatus.ReprovadaPorAutomacao, Agregar(requisitos, findings).Resultado);
        }

        [Fact]
        public void BloqueioDeConfiguracao_VenceEstruturaEDados()
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Failed) };
            var findings = new[]
            {
                Finding(bloqueiaEtapa: true, classificacao: OperationalAuditClassification.ConfiguracaoProjeto),
                Finding(bloqueiaEtapa: true, classificacao: OperationalAuditClassification.EstruturaAcc)
            };

            Assert.Equal(OperationalAuditStageStatus.ReprovadaPorConfiguracao, Agregar(requisitos, findings).Resultado);
        }

        [Theory]
        [InlineData(OperationalAuditClassification.EstruturaAcc)]
        [InlineData(OperationalAuditClassification.EstruturaPlanilha)]
        public void BloqueioDeEstrutura_VenceDados(OperationalAuditClassification classificacao)
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Failed) };
            var findings = new[] { Finding(bloqueiaEtapa: true, classificacao: classificacao) };

            Assert.Equal(OperationalAuditStageStatus.ReprovadaPorEstrutura, Agregar(requisitos, findings).Resultado);
        }

        [Fact]
        public void RequisitoFalho_SemBloqueioEspecial_ResultaEmReprovadaPorDados()
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Approved), Requisito(OperationalRequirementStatus.Failed) };
            Assert.Equal(OperationalAuditStageStatus.ReprovadaPorDados, Agregar(requisitos).Resultado);
        }

        [Fact]
        public void SomenteInconclusivo_SemNadaReprovado_ResultaEmInconclusiva()
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Approved), Requisito(OperationalRequirementStatus.Inconclusive) };
            Assert.Equal(OperationalAuditStageStatus.Inconclusiva, Agregar(requisitos).Resultado);
        }

        [Fact]
        public void SomenteAviso_SemNadaReprovado_ResultaEmAprovadaComAlertas()
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Approved), Requisito(OperationalRequirementStatus.Warning) };
            Assert.Equal(OperationalAuditStageStatus.AprovadaComAlertas, Agregar(requisitos).Resultado);
        }

        [Fact]
        public void TodosAprovados_ResultaEmAprovada()
        {
            var requisitos = new[] { Requisito(OperationalRequirementStatus.Approved), Requisito(OperationalRequirementStatus.Approved) };
            Assert.Equal(OperationalAuditStageStatus.Aprovada, Agregar(requisitos).Resultado);
        }

        [Fact]
        public void PercentualConformidade_IgnoraNaoAplicavelENaoExecutado_NoDenominador()
        {
            var requisitos = new[]
            {
                Requisito(OperationalRequirementStatus.Approved),
                Requisito(OperationalRequirementStatus.Approved),
                Requisito(OperationalRequirementStatus.NotApplicable),
                Requisito(OperationalRequirementStatus.NotExecuted)
            };

            var resultado = Agregar(requisitos);

            Assert.Equal(100m, resultado.PercentualConformidade);
        }
    }
}
