using System;
using System.Collections.Generic;
using APS.WebApp.Services.OperationalAudit.Persistence;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.OperationalAudit.Tests
{
    public class AuditConnectionStringResolverTests
    {
        [Fact]
        public void Resolver_ConnectionStringConfigurada_RetornaOValorExato()
        {
            var dados = new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=leitura;Database=Db;"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();

            string resultado = AuditConnectionStringResolver.Resolver(config, "AuditSourceConnection", "teste");

            Assert.Equal("Server=leitura;Database=Db;", resultado);
        }

        [Fact]
        public void Resolver_ConnectionStringAusente_Lanca()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

            Assert.Throws<InvalidOperationException>(() =>
                AuditConnectionStringResolver.Resolver(config, "AuditSourceConnection", "teste"));
        }

        [Fact]
        public void Resolver_ConnectionStringAusente_NuncaCaiSilenciosamenteParaLocalConnection()
        {
            // LocalConnection existe (e' a credencial operacional real), mas AuditSourceConnection nao —
            // o resolver tem que falhar explicitamente, nunca "emprestar" a credencial operacional.
            var dados = new Dictionary<string, string?>
            {
                ["ConnectionStrings:LocalConnection"] = "Server=.\\SQLEXPRESS;Database=DB_NA_CONNECT;"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                AuditConnectionStringResolver.Resolver(config, "AuditSourceConnection", "teste"));

            Assert.DoesNotContain("SQLEXPRESS", ex.Message);
            Assert.Contains("LocalConnection", ex.Message);
        }

        [Fact]
        public void Resolver_ConnectionStringEmBranco_TratadaComoAusente()
        {
            var dados = new Dictionary<string, string?> { ["ConnectionStrings:AuditRepositoryConnection"] = "   " };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();

            Assert.Throws<InvalidOperationException>(() =>
                AuditConnectionStringResolver.Resolver(config, "AuditRepositoryConnection", "teste"));
        }
    }
}
