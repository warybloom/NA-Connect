using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.Reporting
{
    /// <summary>JSON estruturado (seção 22) — serializa a <see cref="OperationalAuditResult"/> inteira, enums como texto (nunca número, para o Power BI/consumo externo não depender da ordem de declaração do enum).</summary>
    public sealed class OperationalAuditJsonReportWriter : IOperationalAuditReportWriter
    {
        private static readonly JsonSerializerOptions Opcoes = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // preserva acentuação (ex.: "não encontrado") sem escapar em \uXXXX
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public async Task<string> EscreverAsync(OperationalAuditResult resultado, string outputDirectory, CancellationToken cancellationToken = default)
        {
            string caminho = ReportFileNaming.GarantirDiretorioECaminho(outputDirectory, resultado.ExecutionId, resultado.IniciadoEmUtc, "json");

            await using var stream = new FileStream(caminho, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, resultado, Opcoes, cancellationToken);

            return caminho;
        }
    }
}
