using System;
using System.IO;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Checkpoint 4, seção 12: subpasta por execução dentro de <c>AuditReports</c> — sem quebrar o contrato
    /// existente (cada writer continua recebendo só uma string <c>OutputDirectory</c> via
    /// <see cref="APS.WebApp.Services.OperationalAudit.Configuration.OperationalAuditOptions"/>; os nomes de
    /// arquivo dentro dela já incluem ExecutionId+timestamp via
    /// <c>ReportFileNaming.NomeArquivo</c> — nunca sobrescreve uma execução anterior).
    /// </summary>
    public static class AuditReportPathResolver
    {
        public const string PastaBaseNomeDefault = "AuditReports";

        public static string ResolverDiretorioBase(string raizDoRepositorio) =>
            Path.Combine(raizDoRepositorio, PastaBaseNomeDefault);

        /// <summary>Uma subpasta nova por execução (timestamp + AuditRunId curto) — nunca reaproveitada entre execuções.</summary>
        public static string ResolverSubpastaDaExecucao(string diretorioBase, Guid auditRunId, DateTime iniciadoEmUtc)
        {
            string auditRunIdCurto = auditRunId.ToString("N").Substring(0, 8);
            string nomeSubpasta = $"{iniciadoEmUtc:yyyyMMdd_HHmmss}_{auditRunIdCurto}";
            return Path.Combine(diretorioBase, nomeSubpasta);
        }

        /// <summary>
        /// Checkpoint 4.1, seção 1: local PERSISTENTE do baseline de hash dos 13 arquivos protegidos —
        /// deliberadamente FORA de <c>build_verify_output</c>/<c>bin</c>/<c>obj</c>/qualquer pasta de build
        /// (essas podem ser apagadas por build/clean a qualquer momento, o que já invalidou o baseline do
        /// Checkpoint 4).
        /// </summary>
        public static string ResolverDiretorioDeValidacao(string raizDoRepositorio) =>
            Path.Combine(ResolverDiretorioBase(raizDoRepositorio), "_validation");
    }
}
