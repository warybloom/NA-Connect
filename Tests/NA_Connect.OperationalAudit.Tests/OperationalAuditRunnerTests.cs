using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditRunnerTests : IDisposable
    {
        private readonly string _diretorioTemp = Path.Combine(Path.GetTempPath(), "NAConnect_RunnerTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_diretorioTemp)) Directory.Delete(_diretorioTemp, recursive: true);
        }

        private static OperationalAuditRunner NovoRunner(
            IEnumerable<IOperationalAuditRule> regras, FakeAuditRepository repository, IEnumerable<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter> writers, OperationalAuditOptions options) =>
            new OperationalAuditRunner(options, regras, () => repository, writers, NullLogger<OperationalAuditRunner>.Instance);

        [Fact]
        public async Task ExecutarAsync_AgregaFindingsERequisitosDeTodasAsRegras()
        {
            var regra1 = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "0", "F1") } } };
            var regra2 = new FakeOperationalAuditRule("7") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "7", "F2") } } };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regra1, regra2 }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(2, resultado.Findings.Count);
            Assert.Contains(resultado.Findings, f => f.RegraId == "F1");
            Assert.Contains(resultado.Findings, f => f.RegraId == "F2");
            Assert.Equal(2, resultado.StageResults.Count); // uma por regra.
        }

        [Fact]
        public async Task ExecutarAsync_FalhaDeUmaRegra_NuncaInterrompeAsDemais()
        {
            var regraOk = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "0", "OK") } } };
            var regraQuebra = new FakeOperationalAuditRule("7") { LancarExcecao = true };
            var regraDepois = new FakeOperationalAuditRule("9") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "9", "DEPOIS") } } };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regraOk, regraQuebra, regraDepois }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.True(regraDepois.VezesExecutada > 0, "A regra seguinte precisa ter rodado mesmo depois de uma regra anterior falhar.");
            Assert.Contains(resultado.Findings, f => f.RegraId == "OK");
            Assert.Contains(resultado.Findings, f => f.RegraId == "DEPOIS");
            Assert.Contains(resultado.Findings, f => f.CodigoProblema == "AUT-RULE-EXCEPTION" && f.Etapa == "7");
        }

        [Fact]
        public async Task ExecutarAsync_RegraLancaExcecaoParaUmProjeto_ProjetoContinuaComFindingSintetico_DemaisProjetosNaoSaoAfetados()
        {
            // Uma exceção de REGRA é absorvida no nível da regra (vira um finding de automação — ver
            // ExecutarAsync_FalhaDeUmaRegra_NuncaInterrompeAsDemais) — o projeto em si NUNCA é abortado por
            // isso, e os demais projetos seguem intocados. A cobertura de "falha de PROJETO" (persistência)
            // fica em OperationalAuditPersistenceFailureTests.FalhaAoPersistirProjeto_*.
            var regra = new FakeOperationalAuditRule("0")
            {
                Executar = ctx =>
                {
                    if (ctx.ProjectIdAcc == "PRJ-QUEBRA") throw new InvalidOperationException("Falha simulada na regra para este projeto.");
                    return new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "0", "OK") } };
                }
            };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regra }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1", "PRJ-QUEBRA", "PRJ-2" });

            Assert.Equal(3, resultado.ProjetosProcessados.Count);
            Assert.Contains(resultado.Findings, f => f.ProjectIdAcc == "PRJ-QUEBRA" && f.CodigoProblema == "AUT-RULE-EXCEPTION");
            Assert.Contains(resultado.Findings, f => f.ProjectIdAcc == "PRJ-1" && f.RegraId == "OK");
            Assert.Contains(resultado.Findings, f => f.ProjectIdAcc == "PRJ-2" && f.RegraId == "OK");
        }

        [Fact]
        public async Task ExecutarAsync_GeraRelatoriosMesmoSemPersistencia()
        {
            var regra = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult() };
            var repository = new FakeAuditRepository();
            var writer = new FakeReportWriter();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = false };
            var runner = NovoRunner(new[] { regra }, repository, new[] { writer }, options);

            await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(1, writer.VezesChamado);
            Assert.False(repository.IniciarChamado);
        }

        [Fact]
        public async Task ExecutarAsync_PersistenciaHabilitada_ChamaIniciarPersistirEFinalizar()
        {
            var regra = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "0", "F1") } } };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = true, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regra }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.True(repository.IniciarChamado);
            Assert.True(repository.FinalizarChamado);
            Assert.Single(repository.ProjetosPersistidos);
        }

        [Fact]
        public async Task ExecutarAsync_FinalizaComStatusCompletedQuandoTudoOk()
        {
            var regra = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult() };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regra }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(OperationalAuditRunStatus.Completed, resultado.Status);
            Assert.NotNull(resultado.FinalizadoEmUtc);
        }

        [Fact]
        public async Task ExecutarAsync_RespeitaFiltroDeEtapasConfigurado()
        {
            var regra0 = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "0", "F0") } } };
            var regra7 = new FakeOperationalAuditRule("7") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "7", "F7") } } };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, Stages = new List<string> { "7" }, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regra0, regra7 }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(0, regra0.VezesExecutada);
            Assert.Equal(1, regra7.VezesExecutada);
        }

        [Fact]
        public async Task ExecutarAsync_FindingsRecebemDedupHashEOccurrenceHash()
        {
            var regra = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult { Findings = new[] { NovoFinding(ctx, "0", "F1") } } };
            var repository = new FakeAuditRepository();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, GenerateTxt = false, GenerateCsv = false, GenerateJson = false };
            var runner = NovoRunner(new[] { regra }, repository, Array.Empty<APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter>(), options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            var finding = Assert.Single(resultado.Findings);
            Assert.False(string.IsNullOrEmpty(finding.DedupHash));
            Assert.False(string.IsNullOrEmpty(finding.OccurrenceHash));
        }

        private static OperationalFinding NovoFinding(OperationalAuditContext ctx, string etapa, string regraId) =>
            new OperationalFinding
            {
                ExecutionId = ctx.ExecutionId,
                DataHoraUtc = DateTime.UtcNow,
                ProjectIdAcc = ctx.ProjectIdAcc,
                Etapa = etapa,
                RegraId = regraId,
                CodigoProblema = regraId,
                Titulo = "T",
                ErrorDomain = OperationalAuditErrorDomain.Operacional,
                Classificacao = OperationalAuditClassification.NaoClassificado,
                Severidade = OperationalAuditSeverity.Baixo
            };
    }
}
