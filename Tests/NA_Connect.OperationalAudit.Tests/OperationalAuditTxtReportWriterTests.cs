using System;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Reporting;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditTxtReportWriterTests : IDisposable
    {
        private readonly string _diretorioTemp = Path.Combine(Path.GetTempPath(), "NAConnect_AuditTxtTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_diretorioTemp)) Directory.Delete(_diretorioTemp, recursive: true);
        }

        [Fact]
        public async Task EscreverAsync_Cabecalho_ContemExecutionIdEStatus()
        {
            var resultado = new OperationalAuditResult
            {
                ExecutionId = Guid.NewGuid(),
                IniciadoEmUtc = DateTime.UtcNow,
                Status = OperationalAuditRunStatus.Completed
            };
            resultado.ProjetosProcessados.Add("P1");

            var writer = new OperationalAuditTxtReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains(resultado.ExecutionId.ToString("N"), conteudo);
            Assert.Contains("Completed", conteudo);
        }

        [Fact]
        public async Task EscreverAsync_PersistenciaSqlFalhou_AvisoExplicitoNoTexto_NuncaDizQueFoiSalvo()
        {
            var resultado = new OperationalAuditResult
            {
                ExecutionId = Guid.NewGuid(),
                IniciadoEmUtc = DateTime.UtcNow,
                PersistenciaSqlFalhou = true,
                PersistenciaSqlErro = "timeout ao conectar"
            };

            var writer = new OperationalAuditTxtReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains("FALHOU", conteudo);
            Assert.Contains("timeout ao conectar", conteudo);
        }

        [Fact]
        public async Task EscreverAsync_AchadosAgrupadosPorProjeto_TituloDoAchadoAparece()
        {
            var resultado = new OperationalAuditResult { ExecutionId = Guid.NewGuid(), IniciadoEmUtc = DateTime.UtcNow };
            resultado.Findings.Add(new OperationalFinding
            {
                ProjectIdAcc = "P1",
                Projeto = "Projeto Um",
                Etapa = "7",
                CodigoProblema = "COD1",
                Titulo = "Atributo obrigatorio ausente",
                Severidade = OperationalAuditSeverity.Alto
            });

            var writer = new OperationalAuditTxtReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains("P1", conteudo);
            Assert.Contains("Projeto Um", conteudo);
            Assert.Contains("Atributo obrigatorio ausente", conteudo);
        }

        [Fact]
        public async Task EscreverAsync_ErrosDeExecucao_AparecemEmSecaoPropria()
        {
            var resultado = new OperationalAuditResult { ExecutionId = Guid.NewGuid(), IniciadoEmUtc = DateTime.UtcNow };
            resultado.ErrosDeExecucao.Add("Falha ao ler pasta X do ACC");

            var writer = new OperationalAuditTxtReportWriter();
            string caminho = await writer.EscreverAsync(resultado, _diretorioTemp);
            string conteudo = await File.ReadAllTextAsync(caminho);

            Assert.Contains("ERROS DE EXECUCAO", conteudo);
            Assert.Contains("Falha ao ler pasta X do ACC", conteudo);
        }
    }
}
