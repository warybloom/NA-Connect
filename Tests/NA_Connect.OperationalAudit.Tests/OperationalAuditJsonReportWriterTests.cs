using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Reporting;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditJsonReportWriterTests : IDisposable
    {
        private readonly string _diretorioTemp = Path.Combine(Path.GetTempPath(), "NAConnect_AuditJsonTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_diretorioTemp)) Directory.Delete(_diretorioTemp, recursive: true);
        }

        [Fact]
        public async Task EscreverAsync_EnumsSerializadosComoTexto_NuncaComoNumero()
        {
            var resultado = new OperationalAuditResult
            {
                ExecutionId = Guid.NewGuid(),
                IniciadoEmUtc = DateTime.UtcNow,
                Status = OperationalAuditRunStatus.CompletedWithWarnings
            };

            var writer = new OperationalAuditJsonReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains("\"CompletedWithWarnings\"", conteudo);

            using var doc = JsonDocument.Parse(conteudo);
            Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("Status").ValueKind);
        }

        [Fact]
        public async Task EscreverAsync_PreservaAcentuacaoSemEscaparEmUnicode()
        {
            var resultado = new OperationalAuditResult { ExecutionId = Guid.NewGuid(), IniciadoEmUtc = DateTime.UtcNow };
            resultado.Findings.Add(new OperationalFinding
            {
                ProjectIdAcc = "P1",
                Etapa = "7",
                CodigoProblema = "COD1",
                Titulo = "Atributo não encontrado na versão mais recente"
            });

            var writer = new OperationalAuditJsonReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains("não encontrado na versão", conteudo);
            Assert.DoesNotContain("\\u00e3", conteudo);
        }

        [Fact]
        public async Task EscreverAsync_CamposNulos_SaoOmitidos()
        {
            var resultado = new OperationalAuditResult { ExecutionId = Guid.NewGuid(), IniciadoEmUtc = DateTime.UtcNow };
            // FinalizadoEmUtc e PersistenciaSqlErro permanecem nulos.

            var writer = new OperationalAuditJsonReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.DoesNotContain("FinalizadoEmUtc", conteudo);
            Assert.DoesNotContain("PersistenciaSqlErro", conteudo);
        }

        [Fact]
        public async Task EscreverAsync_JsonResultanteEhValidoEParseavel()
        {
            var resultado = new OperationalAuditResult { ExecutionId = Guid.NewGuid(), IniciadoEmUtc = DateTime.UtcNow };
            resultado.Findings.Add(new OperationalFinding { ProjectIdAcc = "P1", Etapa = "7", CodigoProblema = "COD1", Titulo = "T" });

            var writer = new OperationalAuditJsonReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            var exception = Record.Exception(() => JsonDocument.Parse(conteudo));
            Assert.Null(exception);
        }
    }
}
