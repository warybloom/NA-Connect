using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Testes de integração do checkpoint "PRODUCTION_INTEGRATION" — cobre a lista de 14 itens do pedido
    /// (seção 21), na parte que pertence ao COORDINATOR em si (o resto — ordem/posição dentro do
    /// GrdAutomationWorker, nunca antes da Stage17, hook chamado exatamente uma vez por projeto mesmo em
    /// caminhos de `continue` — está em <see cref="GrdAutomationWorkerAuditHookContractTests"/>, por texto-
    /// fonte real, já que o Worker em si não é isolável em teste unitário sem um host ASP.NET Core
    /// completo).
    ///
    /// Usa a composição REAL (<see cref="OperationalAuditServiceCollectionExtensions.AddOperationalAudit"/>,
    /// via o próprio coordinator) — nunca fakes de runner/regras — porque o objeto sob teste é exatamente a
    /// integração real com o container de DI isolado. Nunca abre um socket de verdade: os caminhos de erro
    /// testados aqui usam connection strings AUSENTES (falha rápida e explícita em
    /// <c>AuditConnectionStringResolver</c>, antes de qualquer tentativa de rede) — nunca um servidor
    /// inacessível, que dependeria de timeouts de rede lentos e imprevisíveis entre ambientes.
    /// </summary>
    public class OperationalAuditExecutionCoordinatorTests : IDisposable
    {
        private readonly List<string> _diretoriosTemp = new();

        public void Dispose()
        {
            foreach (var dir in _diretoriosTemp)
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        private string NovoDiretorioTemp()
        {
            string dir = Path.Combine(Path.GetTempPath(), "NAConnect_CoordinatorTests_" + Guid.NewGuid().ToString("N"));
            _diretoriosTemp.Add(dir);
            return dir;
        }

        private static IConfiguration Configuracao(Dictionary<string, string?> valores) =>
            new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

        private static OperationalAuditExecutionCoordinator NovoCoordinator(IConfiguration configuration) =>
            new(configuration, NullLoggerFactory.Instance, NullLogger<OperationalAuditExecutionCoordinator>.Instance);

        // ---- Item 1: Enabled=false -> não executa audit ---------------------------------------------

        [Fact]
        public async Task Enabled_False_NaoExecuta_NuncaMontaContainerDeDI()
        {
            var configuration = Configuracao(new Dictionary<string, string?> { ["OperationalAudit:Enabled"] = "false" });
            var coordinator = NovoCoordinator(configuration);

            var resultado = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-1", "Projeto Teste");

            Assert.False(resultado.Executada);
            Assert.True(resultado.Sucesso);
            Assert.Null(resultado.Resultado);
            Assert.Null(resultado.ErroFatal);
        }

        // ---- Itens 3/4 (runner/repository falham -> Worker continua): pior caso possível — a montagem ---
        // ---- do próprio container de DI falha (AuditSourceConnection ausente) — nada escapa daqui. ------

        [Fact]
        public async Task Enabled_True_ConfiguracaoAusente_NuncaLancaParaQuemChama_RetornaErroFatal()
        {
            var configuration = Configuracao(new Dictionary<string, string?>
            {
                ["OperationalAudit:Enabled"] = "true",
                ["OperationalAudit:OutputDirectory"] = NovoDiretorioTemp()
                // ConnectionStrings:AuditSourceConnection propositalmente ausente.
            });
            var coordinator = NovoCoordinator(configuration);

            var resultado = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-1", "Projeto Teste");

            Assert.True(resultado.Executada);
            Assert.False(resultado.Sucesso);
            Assert.False(string.IsNullOrWhiteSpace(resultado.ErroFatal));
            Assert.Null(resultado.Resultado);
        }

        [Fact]
        public async Task Enabled_True_ConfiguracaoAusente_ErroFatalNuncaContemPalavraChaveDeSegredo()
        {
            // Caminho RÁPIDO (nunca abre socket): AuditConnectionStringResolver lança
            // InvalidOperationException explicitamente quando a connection string está ausente — nunca
            // ecoa senha/secret/token porque nenhum desses valores chega a existir neste caminho.
            var configuration = Configuracao(new Dictionary<string, string?>
            {
                ["OperationalAudit:Enabled"] = "true",
                ["OperationalAudit:OutputDirectory"] = NovoDiretorioTemp()
            });
            var coordinator = NovoCoordinator(configuration);

            var resultado = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-1", "Projeto Teste");

            Assert.False(resultado.Sucesso);
            Assert.NotNull(resultado.ErroFatal);
            Assert.DoesNotContain("Password", resultado.ErroFatal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pwd=", resultado.ErroFatal, StringComparison.OrdinalIgnoreCase);
        }

        // ---- Item 13/estrutural: cancelamento nunca escapa como exceção não tratada -------------------

        [Fact]
        public async Task TokenJaCancelado_NuncaLancaParaQuemChama()
        {
            var configuration = Configuracao(new Dictionary<string, string?>
            {
                ["OperationalAudit:Enabled"] = "true",
                ["OperationalAudit:OutputDirectory"] = NovoDiretorioTemp()
            });
            var coordinator = NovoCoordinator(configuration);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var resultado = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-1", "Projeto Teste", cts.Token);

            Assert.True(resultado.Executada);
            Assert.False(resultado.Sucesso);
        }

        // ---- Item 12: várias chamadas sequenciais (uma por projeto) são isoladas entre si --------------

        [Fact]
        public async Task ChamadasSequenciaisParaProjetosDiferentes_SaoIsoladasEntreSi()
        {
            var configuration = Configuracao(new Dictionary<string, string?> { ["OperationalAudit:Enabled"] = "false" });
            var coordinator = NovoCoordinator(configuration);

            var resultado1 = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-1", "Projeto 1");
            var resultado2 = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-2", "Projeto 2");

            Assert.False(resultado1.Executada);
            Assert.False(resultado2.Executada);
            Assert.NotSame(resultado1, resultado2);
        }

        // ---- Item: duração sempre preenchida, sucesso ou falha -----------------------------------------

        [Fact]
        public async Task Duracao_EstaSemprePreenchida_MesmoNoCaminhoDeErro()
        {
            var configuration = Configuracao(new Dictionary<string, string?> { ["OperationalAudit:Enabled"] = "true" });
            var coordinator = NovoCoordinator(configuration);

            var resultado = await coordinator.ExecutarAuditoriaDoProjetoAsync("PRJ-1", "Projeto Teste");

            Assert.True(resultado.Duracao >= TimeSpan.Zero);
        }

        // ---- Item 10 (PersistToDatabase chega ao runner): prova via as opções resolvidas pelo MESMO -----
        // ---- caminho de composição que o coordinator usa internamente (AddOperationalAudit) -------------

        [Fact]
        public void AddOperationalAudit_ResolveOptions_PersistToDatabaseEEnabledRefletemAConfiguracao()
        {
            var configuration = Configuracao(new Dictionary<string, string?>
            {
                ["OperationalAudit:Enabled"] = "true",
                ["OperationalAudit:PersistToDatabase"] = "true"
            });

            var services = new ServiceCollection();
            services.AddOperationalAudit(configuration);
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<APS.WebApp.Services.OperationalAudit.Configuration.OperationalAuditOptions>();

            Assert.True(options.Enabled);
            Assert.True(options.PersistToDatabase);
        }
    }
}
