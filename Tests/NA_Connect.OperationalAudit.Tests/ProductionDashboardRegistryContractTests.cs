using System.Linq;
using System.Text.RegularExpressions;
using APS.WebApp.Services.Reports.Dashboards;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// O registry do frontend (productionScreens.ts) é a fonte única das 12 telas; cada tela aponta para o
    /// dashboard de mesmo id no backend. Este contrato impede os dois lados de divergirem, e garante que
    /// nenhuma tela declare custo/portfólio sem implementação correspondente.
    /// </summary>
    public class ProductionDashboardRegistryContractTests
    {
        private static string Registry() =>
            RepoFileLocator.LerArquivoDaRaiz("ClientApp", "src", "features", "production", "productionScreens.ts");

        [Fact]
        public void TodaTelaDoRegistry_TemDashboardNoBackend_EViceVersa()
        {
            var fonte = Registry();
            var ids = Regex.Matches(fonte, @"\{\s*id:\s*""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            var dashboards = Regex.Matches(fonte, @"createDashboardScreen\(""([^""]+)""\)").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(12, ids.Count);
            Assert.Equal(ids, dashboards);                                      // cada tela usa o dashboard do próprio id
            Assert.Equal(ids.OrderBy(x => x), ProductionDashboards.Todos.Keys.OrderBy(x => x));
        }

        [Fact]
        public void NenhumaTelaDeclaraCustoOuPortfolio()
        {
            var fonte = Registry();
            Assert.DoesNotContain("containsCostData: true", fonte);
            // 30/09/2026: supportsPortfolio=true é suportado de fato — o endpoint avalia a mesma definição sobre a união das
            // linhas dos projetos (parâmetro "projetos"); sem esse suporte no backend, nenhuma tela pode declarar portfólio.
            if (fonte.Contains("supportsPortfolio: true"))
                Assert.Contains("[FromQuery(Name = \"projetos\")]", RepoFileLocator.LerArquivoDaRaiz("Controllers", "ProductionDashboardsController.cs"));
        }
    }
}
