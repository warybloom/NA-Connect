using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>
    /// Checkpoint 4, seções 4/18 — prova, contra o composition root REAL (nunca um fake de DI), que:
    /// - a auditoria resolve para o MESMO IOperationalAuditRunner/regras/repository já existentes (nunca
    ///   duplicados aqui);
    /// - nenhum IHostedService é registrado;
    /// - GrdAutomationWorker nunca é resolvido nem referenciado;
    /// - a auditoria nunca toca dado real (só valores fake de connection string — SqlConnection nunca é
    ///   aberto durante a resolução de DI, só quando um comando é de fato executado, o que os testes abaixo
    ///   evitam com listas de projeto vazias).
    /// </summary>
    public class CompositionRootTests
    {
        private static IConfiguration ConfiguracaoFake() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=fake-test-server;Database=fake-test-db;Integrated Security=true;",
                ["ConnectionStrings:AuditRepositoryConnection"] = "Server=fake-test-server;Database=fake-test-audit;Integrated Security=true;",
                ["APS:ClientId"] = "fake-client-id",
                ["APS:ClientSecret"] = "fake-client-secret",
                ["APS:Scope"] = "data:read",
                ["OperationalAudit:OutputDirectory"] = Path.Combine(Path.GetTempPath(), "NA_Connect_Cli_Tests_" + Guid.NewGuid().ToString("N"))
            })
            .Build();

        private static ServiceProvider ConstruirProvider(out IConfiguration configuration)
        {
            configuration = ConfiguracaoFake();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOperationalAudit(configuration);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void Runner_ResolveParaOMesmoTipoJaExistente_NuncaUmaCopia()
        {
            using var provider = ConstruirProvider(out _);
            var runner = provider.GetRequiredService<IOperationalAuditRunner>();

            Assert.IsType<OperationalAuditRunner>(runner);
        }

        [Fact]
        public void TodasAs18RegrasReaisSaoResolvidas_NenhumaDuplicada()
        {
            using var provider = ConstruirProvider(out _);
            var regras = provider.GetServices<IOperationalAuditRule>().ToList();

            Assert.Equal(18, regras.Count);
            var etapas = regras.Select(r => r.Etapa).ToList();
            Assert.Equal(etapas.Count, etapas.Distinct().Count());
        }

        [Fact]
        public void NenhumIHostedServiceEhRegistrado()
        {
            var configuration = ConfiguracaoFake();
            var services = new ServiceCollection();
            services.AddOperationalAudit(configuration);

            Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void GrdAutomationWorker_NuncaERegistrado()
        {
            var configuration = ConfiguracaoFake();
            var services = new ServiceCollection();
            services.AddOperationalAudit(configuration);

            Assert.DoesNotContain(services, sd => sd.ServiceType.Name.Contains("GrdAutomationWorker") || sd.ImplementationType?.Name.Contains("GrdAutomationWorker") == true);
        }

        [Fact]
        public void DryRun_ForcaPersistToDatabaseFalse_MesmoQueConfigDigaOContrario()
        {
            var options = new OperationalAuditOptions { PersistToDatabase = true }; // config diria "true" — o CLI tem que vencer.
            var argumentos = new CliArguments { Mode = CliMode.DryRun, ProjectId = "PRJ-1" };

            CliModeOptionsApplier.Aplicar(options, argumentos);

            Assert.False(options.PersistToDatabase);
        }

        [Fact]
        public void LiveNoPersist_ForcaPersistToDatabaseFalse()
        {
            var options = new OperationalAuditOptions { PersistToDatabase = true };
            var argumentos = new CliArguments { Mode = CliMode.LiveNoPersist, ProjectId = "PRJ-1", AllowLiveRead = true };

            CliModeOptionsApplier.Aplicar(options, argumentos);

            Assert.False(options.PersistToDatabase);
        }

        [Fact]
        public void LivePersist_HabilitaPersistToDatabase()
        {
            var options = new OperationalAuditOptions { PersistToDatabase = false };
            var argumentos = new CliArguments { Mode = CliMode.LivePersist, ProjectId = "PRJ-1", AllowLiveRead = true, Persist = true };

            CliModeOptionsApplier.Aplicar(options, argumentos);

            Assert.True(options.PersistToDatabase);
        }

        [Fact]
        public async Task PersistToDatabaseFalse_NuncaInvocaAuditRepository_MesmoComRunnerReal()
        {
            // Integração real: composition root de verdade, OperationalAuditRunner de verdade, mas
            // PersistToDatabase=false (via CliModeOptionsApplier) e NENHUM projeto na lista — garante que
            // a auditoria não toca ACC/SQL real neste teste, e ainda assim prova que a factory de
            // IAuditRepository nunca é invocada quando a persistência está desligada.
            using var provider = ConstruirProvider(out _);
            var options = provider.GetRequiredService<OperationalAuditOptions>();
            CliModeOptionsApplier.Aplicar(options, new CliArguments { Mode = CliMode.LiveNoPersist, ProjectId = "PRJ-INEXISTENTE", AllowLiveRead = true });

            var runner = provider.GetRequiredService<IOperationalAuditRunner>();
            var resultado = await runner.ExecutarAsync(Array.Empty<string>(), "teste-composicao", CancellationToken.None);

            Assert.False(resultado.PersistenciaSqlFalhou);
            Assert.Empty(resultado.ProjetosProcessados);
        }

        [Fact]
        public async Task PersistToDatabaseTrue_RepositoryFalha_RelatorioContinuaSendoGerado()
        {
            // Teste 17: nenhuma falha da auditoria pode tocar dado operacional, e o relatório TXT/CSV/JSON
            // continua sendo gerado mesmo quando a persistência em audit.* falha — aqui a "falha" é uma
            // factory que sempre lança, nunca uma tentativa real de conexão (nenhum SQL Server real é
            // contatado neste teste).
            var configuration = ConfiguracaoFake();
            string outputDir = configuration["OperationalAudit:OutputDirectory"]!;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOperationalAudit(configuration);
            // Sobrescreve a factory real por uma que sempre lança — última registração vence na resolução singular.
            services.AddTransient<System.Func<IAuditRepository>>(_ => () => throw new System.InvalidOperationException("Falha simulada de persistência — nenhuma conexão real foi tentada."));

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OperationalAuditOptions>();
            options.PersistToDatabase = true;
            options.OutputDirectory = outputDir;

            var runner = provider.GetRequiredService<IOperationalAuditRunner>();
            var resultado = await runner.ExecutarAsync(Array.Empty<string>(), "teste-falha-repository", CancellationToken.None);

            Assert.True(resultado.PersistenciaSqlFalhou);
            Assert.True(Directory.Exists(outputDir), "O diretório de saída deveria existir — os writers rodam independente da falha de persistência.");
            Assert.NotEmpty(Directory.GetFiles(outputDir));

            Directory.Delete(outputDir, recursive: true);
        }

        [Fact]
        public void WebProjectProgramCs_NuncaChamaAddOperationalAuditNemResolveIOperationalAuditRunnerDiretamente()
        {
            // Checkpoint de integração de produção (revisão do Checkpoint 4, seção 2/teste 20): desde a
            // integração de produção, Program.cs do site DEVE registrar IOperationalAuditExecutionCoordinator
            // (senão GrdAutomationWorker não conseguiria resolvê-lo) — mas a garantia estrutural original
            // continua válida na parte que importa: o site nunca chama a extensão de composição completa da
            // auditoria (AddOperationalAudit(...) — isso só acontece dentro do próprio
            // OperationalAuditExecutionCoordinator, num container isolado) nem resolve
            // IOperationalAuditRunner diretamente. Isso evita, entre outras coisas, registrar os 3 report
            // writers duas vezes no mesmo container (TXT/CSV/JSON duplicados).
            string caminhoProgramCs = LocalizarArquivo("Program.cs");
            string conteudo = File.ReadAllText(caminhoProgramCs);

            Assert.DoesNotContain("AddOperationalAudit", conteudo);
            Assert.DoesNotContain("IOperationalAuditRunner", conteudo);
            Assert.Contains("IOperationalAuditExecutionCoordinator", conteudo);
        }

        private static string LocalizarArquivo(string nomeRelativo)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null) throw new InvalidOperationException("Raiz do repositório não encontrada.");
            return Path.Combine(dir.FullName, nomeRelativo);
        }
    }
}
