using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Aggregation;

namespace NA_Connect.OperationalAudit.Tests
{
    public class AuditRunStatusResolverTests
    {
        [Fact]
        public void NenhumProjetoProcessado_ResultaEmFailed()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 0, projetosComFalhaDePersistencia: 0, projetosComErroDeExecucao: 0, persistenciaSqlFalhouGlobalmente: false);
            Assert.Equal(OperationalAuditRunStatus.Failed, status);
        }

        [Fact]
        public void TodosOsProjetosComErroDeExecucao_ResultaEmFailed()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 3, projetosComFalhaDePersistencia: 0, projetosComErroDeExecucao: 3, persistenciaSqlFalhouGlobalmente: false);
            Assert.Equal(OperationalAuditRunStatus.Failed, status);
        }

        [Fact]
        public void AlgunsProjetosComErroDeExecucao_ResultaEmPartiallyCompleted()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 3, projetosComFalhaDePersistencia: 0, projetosComErroDeExecucao: 1, persistenciaSqlFalhouGlobalmente: false);
            Assert.Equal(OperationalAuditRunStatus.PartiallyCompleted, status);
        }

        [Fact]
        public void AlgunsProjetosComFalhaDePersistencia_ResultaEmPartiallyCompleted()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 3, projetosComFalhaDePersistencia: 1, projetosComErroDeExecucao: 0, persistenciaSqlFalhouGlobalmente: false);
            Assert.Equal(OperationalAuditRunStatus.PartiallyCompleted, status);
        }

        [Fact]
        public void PersistenciaFalhouGlobalmente_SemFalhaPorProjeto_ResultaEmCompletedWithWarnings()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 3, projetosComFalhaDePersistencia: 0, projetosComErroDeExecucao: 0, persistenciaSqlFalhouGlobalmente: true);
            Assert.Equal(OperationalAuditRunStatus.CompletedWithWarnings, status);
        }

        [Fact]
        public void TudoOk_ResultaEmCompleted()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 3, projetosComFalhaDePersistencia: 0, projetosComErroDeExecucao: 0, persistenciaSqlFalhouGlobalmente: false);
            Assert.Equal(OperationalAuditRunStatus.Completed, status);
        }

        [Fact]
        public void FalhaPorProjeto_TemPrioridadeSobreCompletedWithWarnings()
        {
            var status = AuditRunStatusResolver.Resolver(totalProjetos: 3, projetosComFalhaDePersistencia: 1, projetosComErroDeExecucao: 0, persistenciaSqlFalhouGlobalmente: true);
            Assert.Equal(OperationalAuditRunStatus.PartiallyCompleted, status);
        }
    }
}
