using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>
    /// Checkpoint 4.1, seção 4 — os 12 testes de gate pedidos explicitamente, numerados na mesma ordem do
    /// pedido (alguns reafirmam, com um nome dedicado e rastreável, garantias já cobertas em
    /// <c>CompositionRootTests</c>/<c>ArgumentParserTests</c> — nunca duplicando a implementação, só
    /// tornando a cobertura de cada item explícita e fácil de auditar).
    /// </summary>
    public class GateTests
    {
        private static IConfiguration ConfiguracaoFakeDoCli() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=fake-test-server;Database=fake-test-db;Integrated Security=true;",
                ["ConnectionStrings:AuditRepositoryConnection"] = "Server=fake-test-server;Database=fake-test-audit;Integrated Security=true;",
                ["APS:ClientId"] = "fake-client-id",
                ["APS:ClientSecret"] = "fake-client-secret",
                ["APS:Scope"] = "data:read",
                ["OperationalAudit:OutputDirectory"] = Path.Combine(Path.GetTempPath(), "NA_Connect_Gate_Tests_" + Guid.NewGuid().ToString("N"))
            })
            .Build();

        private static string LocalizarRaizDoRepositorio()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null) throw new InvalidOperationException("Raiz do repositório não encontrada.");
            return dir.FullName;
        }

        [Fact]
        public void Teste1_WebProgramCs_SoConheceOCoordinator_NuncaOAddOperationalAuditNemIOperationalAuditRunnerDiretamente()
        {
            // Revisado no checkpoint de integração de produção: o site agora registra
            // IOperationalAuditExecutionCoordinator (para GrdAutomationWorker poder resolvê-lo) — mas
            // continua nunca chamando a extensão de composição completa da auditoria
            // (AddOperationalAudit(...), que só roda dentro do próprio coordinator, em container isolado)
            // nem resolvendo IOperationalAuditRunner/as 18 regras diretamente.
            string conteudo = File.ReadAllText(Path.Combine(LocalizarRaizDoRepositorio(), "Program.cs"));

            Assert.DoesNotContain("AddOperationalAudit", conteudo);
            Assert.DoesNotContain("IOperationalAuditRunner", conteudo);
            Assert.Contains("IOperationalAuditExecutionCoordinator", conteudo);
        }

        [Fact]
        public void Teste2_EnabledFalse_ConfiguracaoDoSite_NenhumaAuditoria()
        {
            // A configuração REAL do site (sem OperationalAudit:Enabled definido) produz Enabled=false por
            // padrão — e como Program.cs (teste 1) nunca chama o runner de qualquer forma, isso nunca tem
            // efeito operante hoje; este teste prova só a metade "options" da garantia.
            var configVazia = new ConfigurationBuilder().Build();
            var options = OperationalAuditOptions.LerConfiguracao(configVazia);

            Assert.False(options.Enabled);
        }

        [Fact]
        public void Teste3_CliSemArgumentos_NenhumaAuditoria()
        {
            var resultado = ArgumentParser.Parse(Array.Empty<string>());

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.Help, resultado.Argumentos!.Mode);
            // Modo Help nunca chega a CliModeOptionsApplier/composition root — Program.cs retorna antes.
        }

        [Fact]
        public void Teste4_CliRunSemAllowLiveRead_NenhumaAuditoria()
        {
            var resultado = ArgumentParser.Parse(new[] { "--run", "--project-id", "PRJ-1" });

            Assert.False(resultado.Sucesso);
        }

        [Fact]
        public void Teste5_CliValidateConfig_NenhumaAuditoria()
        {
            var resultado = ArgumentParser.Parse(new[] { "--validate-config" });
            Assert.Equal(CliMode.ValidateConfig, resultado.Argumentos!.Mode);

            // Prova textual: o case ValidateConfig em Program.cs só chama ExecutarValidateConfig, nunca o runner.
            string programCs = File.ReadAllText(Path.Combine(LocalizarRaizDoRepositorio(), "Tools", "NA_Connect.OperationalAudit.Cli", "Program.cs"));
            int indiceCase = programCs.IndexOf("case CliMode.ValidateConfig:", StringComparison.Ordinal);
            int indiceProximoCase = programCs.IndexOf("case CliMode.ValidateConnections:", indiceCase, StringComparison.Ordinal);
            Assert.True(indiceCase >= 0 && indiceProximoCase > indiceCase);
            string blocoDoCase = programCs.Substring(indiceCase, indiceProximoCase - indiceCase);
            Assert.DoesNotContain("ExecutarAsync", blocoDoCase);
            Assert.DoesNotContain("IOperationalAuditRunner", blocoDoCase);
        }

        [Fact]
        public async Task Teste6_CliAutorizadoManualmente_RunnerPodeSerChamado()
        {
            var configuration = ConfiguracaoFakeDoCli();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOperationalAudit(configuration);
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<OperationalAuditOptions>();
            var argumentos = new CliArguments { Mode = CliMode.LiveNoPersist, ProjectId = "PRJ-INEXISTENTE", AllowLiveRead = true };
            CliModeOptionsApplier.Aplicar(options, argumentos);
            CliModeOptionsApplier.GarantirHabilitadoParaExecucao(options); // nunca lança quando autorizado.

            Assert.True(options.Enabled);

            var runner = provider.GetRequiredService<IOperationalAuditRunner>();
            var resultado = await runner.ExecutarAsync(Array.Empty<string>(), "teste-gate-6", CancellationToken.None);

            Assert.NotNull(resultado);
        }

        [Fact]
        public void Teste6b_GarantirHabilitadoParaExecucao_SemAplicar_LancaAoInvesDeExecutarSilenciosamente()
        {
            var options = new OperationalAuditOptions(); // Enabled=false, nunca passou por CliModeOptionsApplier.Aplicar.

            Assert.Throws<InvalidOperationException>(() => CliModeOptionsApplier.GarantirHabilitadoParaExecucao(options));
        }

        [Fact]
        public void Teste7_ExecucaoCli_NuncaAlteraConfiguracaoGlobalDoSite()
        {
            // O CLI lê o PRÓPRIO appsettings.json (Tools\NA_Connect.OperationalAudit.Cli\appsettings.json),
            // nunca o do site (raiz do repositório). CliModeOptionsApplier só muta uma instância EM MEMÓRIA
            // (singleton do container do processo CLI) — nunca grava em disco.
            string raiz = LocalizarRaizDoRepositorio();
            string appsettingsDoSite = Path.Combine(raiz, "appsettings.json");
            string appsettingsDoCli = Path.Combine(raiz, "Tools", "NA_Connect.OperationalAudit.Cli", "appsettings.json");

            Assert.True(File.Exists(appsettingsDoCli));
            Assert.NotEqual(appsettingsDoSite, appsettingsDoCli);

            // CliModeOptionsApplier nunca escreve em disco (só muta a instância de OperationalAudit
            // já em memória) — nenhuma chamada de escrita de arquivo no código-fonte real do método.
            string codigoFonte = File.ReadAllText(Path.Combine(raiz, "Tools", "NA_Connect.OperationalAudit.Cli", "CliModeOptionsApplier.cs"));
            Assert.DoesNotContain("File.Write", codigoFonte);
            Assert.DoesNotContain("File.AppendAllText", codigoFonte);
            Assert.DoesNotContain("StreamWriter", codigoFonte);
        }

        [Fact]
        public void Teste8_DryRun_PersistToDatabaseFalse()
        {
            var options = new OperationalAuditOptions { PersistToDatabase = true };
            CliModeOptionsApplier.Aplicar(options, new CliArguments { Mode = CliMode.DryRun, ProjectId = "PRJ-1", AllowLiveRead = true });

            Assert.False(options.PersistToDatabase);
        }

        [Fact]
        public void Teste9_LiveSemPersist_PersistToDatabaseFalse()
        {
            var options = new OperationalAuditOptions { PersistToDatabase = true };
            CliModeOptionsApplier.Aplicar(options, new CliArguments { Mode = CliMode.LiveNoPersist, ProjectId = "PRJ-1", AllowLiveRead = true });

            Assert.False(options.PersistToDatabase);
        }

        [Fact]
        public void Teste10_LiveComPersist_PersistToDatabaseTrue()
        {
            var options = new OperationalAuditOptions { PersistToDatabase = false };
            CliModeOptionsApplier.Aplicar(options, new CliArguments { Mode = CliMode.LivePersist, ProjectId = "PRJ-1", AllowLiveRead = true, Persist = true });

            Assert.True(options.PersistToDatabase);
        }

        [Fact]
        public void Teste11_PersistSozinho_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--persist" });

            Assert.False(resultado.Sucesso);
        }

        [Fact]
        public void Teste12_NenhumHostedServiceOuWorkerIniciado()
        {
            var configuration = ConfiguracaoFakeDoCli();
            var services = new ServiceCollection();
            services.AddOperationalAudit(configuration);

            Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IHostedService));
            Assert.DoesNotContain(services, sd =>
                sd.ServiceType.Name.Contains("GrdAutomationWorker") ||
                sd.ImplementationType?.Name.Contains("GrdAutomationWorker") == true);
        }
    }
}
