using System;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Reporting;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditCsvReportWriterTests : IDisposable
    {
        private readonly string _diretorioTemp = Path.Combine(Path.GetTempPath(), "NAConnect_AuditCsvTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_diretorioTemp)) Directory.Delete(_diretorioTemp, recursive: true);
        }

        private static OperationalAuditResult NovoResultado(params OperationalFinding[] findings)
        {
            var resultado = new OperationalAuditResult
            {
                ExecutionId = Guid.NewGuid(),
                IniciadoEmUtc = DateTime.UtcNow
            };
            foreach (var f in findings) resultado.Findings.Add(f);
            return resultado;
        }

        [Fact]
        public async Task EscreverAsync_UmaLinhaPorFinding_MaisOCabecalho()
        {
            var resultado = NovoResultado(
                new OperationalFinding { ProjectIdAcc = "P1", Etapa = "7", CodigoProblema = "COD1", Titulo = "Titulo 1" },
                new OperationalFinding { ProjectIdAcc = "P2", Etapa = "9", CodigoProblema = "COD2", Titulo = "Titulo 2" });

            var writer = new OperationalAuditCsvReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);

            string[] linhas = await File.ReadAllLinesAsync(caminho);
            Assert.Equal(3, linhas.Length); // cabecalho + 2 findings
            Assert.StartsWith("ExecutionId,", linhas[0]);
        }

        [Fact]
        public async Task EscreverAsync_CampoComVirgulaAspaEQuebraDeLinha_EscapadoConformeRfc4180()
        {
            var resultado = NovoResultado(new OperationalFinding
            {
                ProjectIdAcc = "P1",
                Etapa = "7",
                CodigoProblema = "COD1",
                Titulo = "Titulo com, virgula \"aspas\" e\nquebra de linha"
            });

            var writer = new OperationalAuditCsvReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains("\"Titulo com, virgula \"\"aspas\"\" e\nquebra de linha\"", conteudo);
        }

        [Fact]
        public async Task EscreverAsync_NomeDoArquivo_SeguePadraoDeReportFileNaming()
        {
            var resultado = NovoResultado();
            var writer = new OperationalAuditCsvReportWriter();

            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);

            Assert.EndsWith(".csv", caminho);
            Assert.Contains(resultado.ExecutionId.ToString("N"), caminho);
        }
    }
}
