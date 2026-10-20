using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditPersistenceFailureTests : IDisposable
    {
        private readonly string _diretorioTemp = Path.Combine(Path.GetTempPath(), "NAConnect_PersistFailTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_diretorioTemp)) Directory.Delete(_diretorioTemp, recursive: true);
        }

        private static OperationalAuditRunner NovoRunner(FakeAuditRepository repository, FakeReportWriter writer, OperationalAuditOptions options, bool repositorioNuncaDeveSerResolvido = false)
        {
            var regra = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult() };
            Func<APS.WebApp.Services.OperationalAudit.Persistence.IAuditRepository> fabrica = () =>
            {
                if (repositorioNuncaDeveSerResolvido)
                {
                    throw new InvalidOperationException("A fábrica do repository NUNCA deveria ser chamada quando PersistToDatabase=false.");
                }
                return repository;
            };
            return new OperationalAuditRunner(options, new[] { regra }, fabrica, new[] { writer }, NullLogger<OperationalAuditRunner>.Instance);
        }

        [Fact]
        public async Task PersistToDatabaseFalse_NuncaResolveORepository()
        {
            var repository = new FakeAuditRepository();
            var writer = new FakeReportWriter();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = false };
            var runner = NovoRunner(repository, writer, options, repositorioNuncaDeveSerResolvido: true);

            var exception = await Record.ExceptionAsync(() => runner.ExecutarAsync(new[] { "PRJ-1" }));

            Assert.Null(exception); // a fábrica lançaria se fosse chamada — não sendo chamada, nada lança.
        }

        [Fact]
        public async Task PersistToDatabaseFalse_AindaAssimGeraArquivos()
        {
            var repository = new FakeAuditRepository();
            var writer = new FakeReportWriter();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = false };
            var runner = NovoRunner(repository, writer, options);

            await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(1, writer.VezesChamado);
        }

        [Fact]
        public async Task FalhaAoIniciarAuditRun_AindaAssimGeraArquivos_ENuncaAfirmaQueBancoFoiAtualizado()
        {
            var repository = new FakeAuditRepository { FalharAoIniciar = true };
            var writer = new FakeReportWriter();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = true };
            var runner = NovoRunner(repository, writer, options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(1, writer.VezesChamado);
            Assert.True(resultado.PersistenciaSqlFalhou);
            Assert.False(repository.FinalizarChamado); // nunca finaliza um AuditRun que nem começou.
        }

        [Fact]
        public async Task FalhaAoPersistirProjeto_NenhumFindingDesaparece_ContinuaProjetosSeguintes()
        {
            var repository = new FakeAuditRepository { LancarAoPersistirProjeto = true };
            var writer = new FakeReportWriter();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = true };
            var runner = NovoRunner(repository, writer, options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1", "PRJ-2" });

            Assert.Equal(2, resultado.ProjetosProcessados.Count); // ambos processados apesar da falha de persistência.
            Assert.True(resultado.PersistenciaSqlFalhou);
            Assert.Equal(1, writer.VezesChamado); // writer ainda roda mesmo com falha de persistência.
        }

        [Fact]
        public async Task PersistirProjetoRetornaFalha_MarcaFalhaMasContinua()
        {
            var repository = new FakeAuditRepository { FalharAoPersistirProjeto = true };
            var writer = new FakeReportWriter();
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = true };
            var runner = NovoRunner(repository, writer, options);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.True(resultado.PersistenciaSqlFalhou);
            Assert.NotNull(resultado.PersistenciaSqlErro);
        }

        [Fact]
        public async Task WriterFalha_OutrosWritersContinuamENenhumFindingDesaparece()
        {
            var repository = new FakeAuditRepository();
            var writerQuebrado = new FakeReportWriter { LancarExcecao = true };
            var writerOk = new FakeReportWriter { ExtensaoFake = "ok" };
            var options = new OperationalAuditOptions { OutputDirectory = _diretorioTemp, PersistToDatabase = false };
            var regra = new FakeOperationalAuditRule("0") { Executar = ctx => new OperationalRuleExecutionResult() };
            var runner = new OperationalAuditRunner(options, new[] { regra }, () => repository, new APS.WebApp.Services.OperationalAudit.Reporting.IOperationalAuditReportWriter[] { writerQuebrado, writerOk }, NullLogger<OperationalAuditRunner>.Instance);

            var resultado = await runner.ExecutarAsync(new[] { "PRJ-1" });

            Assert.Equal(1, writerOk.VezesChamado);
            Assert.Contains(resultado.ErrosDeExecucao, e => e.Contains("FakeReportWriter"));
        }
    }
}
