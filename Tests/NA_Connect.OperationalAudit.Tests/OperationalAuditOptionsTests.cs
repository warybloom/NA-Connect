using System;
using System.Collections.Generic;
using APS.WebApp.Services.OperationalAudit.Configuration;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditOptionsTests
    {
        private static IConfiguration ConfigVazia() =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        [Fact]
        public void LerConfiguracao_SemNenhumaChaveConfigurada_UsaDefaultsSeguros()
        {
            var options = OperationalAuditOptions.LerConfiguracao(ConfigVazia());

            Assert.False(options.Enabled);
            Assert.False(options.RunAfterAutomation);
            Assert.True(options.GenerateTxt);
            Assert.True(options.GenerateJson);
            Assert.True(options.GenerateCsv);
            Assert.False(options.PersistToDatabase);
            Assert.Equal("AuditSourceConnection", options.SourceConnectionStringName);
            Assert.Equal("AuditRepositoryConnection", options.RepositoryConnectionStringName);
            Assert.Equal(10, options.MaxEvidencePerFinding);
            Assert.Empty(options.Stages);
        }

        [Fact]
        public void LerConfiguracao_SemNenhumaChaveConfigurada_NuncaLancaNemValidaConnectionStrings()
        {
            // Enabled=false e' o default — LerConfiguracao nunca deve tentar resolver/validar
            // ConnectionStrings:AuditSourceConnection ou AuditRepositoryConnection so' por ler a secao.
            var exception = Record.Exception(() => OperationalAuditOptions.LerConfiguracao(ConfigVazia()));
            Assert.Null(exception);
        }

        [Fact]
        public void LerConfiguracao_ComValoresExplicitos_RespeitaCadaUm()
        {
            var dados = new Dictionary<string, string?>
            {
                ["OperationalAudit:Enabled"] = "true",
                ["OperationalAudit:RunAfterAutomation"] = "true",
                ["OperationalAudit:GenerateTxt"] = "false",
                ["OperationalAudit:GenerateCsv"] = "false",
                ["OperationalAudit:PersistToDatabase"] = "true",
                ["OperationalAudit:SourceConnectionStringName"] = "MinhaOrigem",
                ["OperationalAudit:RepositoryConnectionStringName"] = "MeuRepositorio",
                ["OperationalAudit:OutputDirectory"] = "C:\\saida",
                ["OperationalAudit:MaxEvidencePerFinding"] = "5",
                ["OperationalAudit:Stages:0"] = "7",
                ["OperationalAudit:Stages:1"] = "9"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();

            var options = OperationalAuditOptions.LerConfiguracao(config);

            Assert.True(options.Enabled);
            Assert.True(options.RunAfterAutomation);
            Assert.False(options.GenerateTxt);
            Assert.True(options.GenerateJson);
            Assert.False(options.GenerateCsv);
            Assert.True(options.PersistToDatabase);
            Assert.Equal("MinhaOrigem", options.SourceConnectionStringName);
            Assert.Equal("MeuRepositorio", options.RepositoryConnectionStringName);
            Assert.Equal("C:\\saida", options.OutputDirectory);
            Assert.Equal(5, options.MaxEvidencePerFinding);
            Assert.Equal(new[] { "7", "9" }, options.Stages);
        }
    }
}
