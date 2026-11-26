using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.Reporting
{
    /// <summary>
    /// Um dos três formatos de saída (seção 22 da matriz da Fase 1 / seção 16 da correção da Fase 2).
    /// Todos os writers leem da MESMA <see cref="OperationalAuditResult"/> — nenhum tem lógica de
    /// classificação própria.
    /// </summary>
    public interface IOperationalAuditReportWriter
    {
        /// <summary>Escreve o relatório em <paramref name="outputDirectory"/> e devolve o caminho completo do arquivo gravado.</summary>
        Task<string> EscreverAsync(OperationalAuditResult resultado, string outputDirectory, CancellationToken cancellationToken = default);
    }
}
