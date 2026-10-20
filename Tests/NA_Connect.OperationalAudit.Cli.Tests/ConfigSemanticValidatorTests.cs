using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seção 18/16 (teste 2): --validate-config nunca abre conexão — prova estrutural: o método é síncrono, só lê <see cref="IConfiguration"/> em memória, nunca retorna Task nem aceita CancellationToken.</summary>
    public class ConfigSemanticValidatorTests
    {
        private static IConfiguration Config(Dictionary<string, string?> valores) =>
            new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

        [Fact]
        public void MetodoValidar_ESincrono_NuncaAssincrono()
        {
            var metodo = typeof(ConfigSemanticValidator).GetMethod(nameof(ConfigSemanticValidator.Validar));

            Assert.NotNull(metodo);
            Assert.False(typeof(System.Threading.Tasks.Task).IsAssignableFrom(metodo!.ReturnType));
            Assert.DoesNotContain(metodo.GetParameters(), p => p.ParameterType == typeof(System.Threading.CancellationToken));
        }

        [Fact]
        public void ConfiguracaoCompleta_Sucesso()
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=fake;Database=fake;",
                ["ConnectionStrings:AuditRepositoryConnection"] = "Server=fake;Database=fake_audit;",
                ["APS:ClientId"] = "id-fake",
                ["APS:ClientSecret"] = "segredo-fake",
                ["APS:Scope"] = "data:read"
            });

            var resultado = ConfigSemanticValidator.Validar(config);

            Assert.True(resultado.Sucesso);
        }

        [Fact]
        public void ConfiguracaoIncompleta_FalhaEIdentificaChaveAusente()
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=fake;Database=fake;"
                // as demais faltando de propósito.
            });

            var resultado = ConfigSemanticValidator.Validar(config);

            Assert.False(resultado.Sucesso);
            Assert.Contains(resultado.Itens, i => i.Chave == "ConnectionStrings:AuditRepositoryConnection" && !i.Presente);
            Assert.Contains(resultado.Itens, i => i.Chave == "APS:ClientSecret" && !i.Presente);
        }

        [Fact]
        public void ClientSecret_NuncaApareceEmTextoPuroNoResumo()
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=fake;Database=fake;",
                ["ConnectionStrings:AuditRepositoryConnection"] = "Server=fake;Database=fake_audit;",
                ["APS:ClientId"] = "id-fake",
                ["APS:ClientSecret"] = "SEGREDO-SUPER-SECRETO-12345",
                ["APS:Scope"] = "data:read"
            });

            var resultado = ConfigSemanticValidator.Validar(config);

            var itemSecret = System.Linq.Enumerable.Single(resultado.Itens, i => i.Chave == "APS:ClientSecret");
            Assert.DoesNotContain("SEGREDO-SUPER-SECRETO-12345", itemSecret.ResumoSeguro);
        }

        [Fact]
        public void ConnectionString_NuncaApareceCompletaNoResumo()
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditSourceConnection"] = "Server=meuservidor.com;Database=meubanco;User ID=admin;Password=SenhaSecreta123;",
                ["ConnectionStrings:AuditRepositoryConnection"] = "Server=fake;Database=fake_audit;",
                ["APS:ClientId"] = "id-fake",
                ["APS:ClientSecret"] = "segredo-fake",
                ["APS:Scope"] = "data:read"
            });

            var resultado = ConfigSemanticValidator.Validar(config);

            var item = System.Linq.Enumerable.Single(resultado.Itens, i => i.Chave == "ConnectionStrings:AuditSourceConnection");
            Assert.DoesNotContain("SenhaSecreta123", item.ResumoSeguro);
            Assert.DoesNotContain("admin", item.ResumoSeguro);
            Assert.Contains("meuservidor.com", item.ResumoSeguro); // servidor pode aparecer — nunca credencial.
        }
    }
}
