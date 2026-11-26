using System;
using System.IO;

namespace APS.WebApp.Services.OperationalAudit.Reporting
{
    /// <summary>Nome de arquivo padrão dos três relatórios (seção 22 da matriz da Fase 1) — um único ponto de verdade, para os três writers nunca divergirem.</summary>
    internal static class ReportFileNaming
    {
        public static string NomeArquivo(Guid executionId, DateTime momentoUtc, string extensao) =>
            $"Auditoria_Operacional_{executionId:N}_{momentoUtc:yyyyMMdd_HHmmss}.{extensao}";

        public static string GarantirDiretorioECaminho(string outputDirectory, Guid executionId, DateTime momentoUtc, string extensao)
        {
            Directory.CreateDirectory(outputDirectory);
            return Path.Combine(outputDirectory, NomeArquivo(executionId, momentoUtc, extensao));
        }
    }
}
