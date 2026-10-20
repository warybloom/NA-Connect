using System;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seção 12: subpasta por AuditRunId/data — nunca sobrescreve execução anterior.</summary>
    public class AuditReportPathResolverTests
    {
        [Fact]
        public void DiretorioBase_ApontaParaAuditReportsNaRaiz()
        {
            string baseDir = AuditReportPathResolver.ResolverDiretorioBase(@"C:\Dev\N-A-Connect");

            Assert.Equal(System.IO.Path.Combine(@"C:\Dev\N-A-Connect", "AuditReports"), baseDir);
        }

        [Fact]
        public void SubpastasDeExecucoesDiferentes_SaoSempreDistintas()
        {
            string baseDir = @"C:\Dev\N-A-Connect\AuditReports";
            var run1 = Guid.NewGuid();
            var run2 = Guid.NewGuid();

            string pasta1 = AuditReportPathResolver.ResolverSubpastaDaExecucao(baseDir, run1, new DateTime(2026, 1, 1, 10, 0, 0));
            string pasta2 = AuditReportPathResolver.ResolverSubpastaDaExecucao(baseDir, run2, new DateTime(2026, 1, 1, 10, 0, 0));

            Assert.NotEqual(pasta1, pasta2); // mesmo timestamp, AuditRunId diferente -> nunca colide.
        }

        [Fact]
        public void MesmoAuditRunId_MomentosDiferentes_SubpastasDiferentes()
        {
            string baseDir = @"C:\Dev\N-A-Connect\AuditReports";
            var run = Guid.NewGuid();

            string pasta1 = AuditReportPathResolver.ResolverSubpastaDaExecucao(baseDir, run, new DateTime(2026, 1, 1, 10, 0, 0));
            string pasta2 = AuditReportPathResolver.ResolverSubpastaDaExecucao(baseDir, run, new DateTime(2026, 1, 1, 10, 0, 1));

            Assert.NotEqual(pasta1, pasta2);
        }
    }
}
