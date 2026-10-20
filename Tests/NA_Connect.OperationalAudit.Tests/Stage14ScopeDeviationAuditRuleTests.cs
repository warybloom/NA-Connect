using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Rules;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage14ScopeDeviationAuditRuleTests
    {
        private const string FolderId = "F-SCP";
        private static readonly string[] Cabecalhos = { "ITEM", "DESCRIÇÃO", "DISCIPLINA", "Nº DO DESVIO", "DATA", "QUANT. HH", "STATUS", "DESVIO INTERNO", "CHANGE ORDER" };

        private static (Stage14ScopeDeviationAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc) NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            return (new Stage14ScopeDeviationAuditRule(acc, new PlanningPreflightService(NullLogger<PlanningPreflightService>.Instance)), acc);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _) = NovaRegra();
            Assert.Equal("14", regra.Etapa);
        }

        [Fact]
        public async Task PastaAusente_GeraFindingSCP001()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.12", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "SCP-001");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task SCP001_ArquivoCandidatoSemAbaControle_GeraFinding_ComCamposDoPedido()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.12", TestFixtures.Pasta(FolderId, "01.12 - SCOPE DEVIATION"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "BML011.13-000-PL0-FM-002.xlsx", FolderId, "01.12 - SCOPE DEVIATION")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsxSemAba("OUTRA ABA"));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "SCP-001");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.EstruturaPlanilha, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
            Assert.Equal("Autor do template / Planejamento", finding.ResponsavelProvavel);
        }

        [Fact]
        public async Task SCP002_ColunaObrigatoriaAusente_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.12", TestFixtures.Pasta(FolderId, "01.12 - SCOPE DEVIATION"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", FolderId, "01.12 - SCOPE DEVIATION")));
            var cabecalhosSemStatus = new[] { "ITEM", "DESCRIÇÃO", "DISCIPLINA", "Nº DO DESVIO", "DATA", "QUANT. HH", "DESVIO INTERNO", "CHANGE ORDER" };
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("CONTROLE", cabecalhosSemStatus, new object?[][] { new object?[] { "1", "Desc", "CIVIL", "D-1", "01/01/2026", "10", "Interno", "CO-1" } }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "SCP-002");
            Assert.Contains("STATUS", finding.Titulo, System.StringComparison.OrdinalIgnoreCase);
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task SCP003_LinhaComIdentificadorMasDataInvalida_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.12", TestFixtures.Pasta(FolderId, "01.12 - SCOPE DEVIATION"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", FolderId, "01.12 - SCOPE DEVIATION")));
            var linhas = new object?[][]
            {
                new object?[] { "1", "Desc válida", "CIVIL", "D-1", "01/01/2026", "10", "Aberto", "Interno", "CO-1" },
                new object?[] { "2", "Desc sem data", "CIVIL", "D-2", null, "5", null, "Interno", "CO-2" }
            };
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("CONTROLE", Cabecalhos, linhas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "SCP-003" && f.ValorEncontrado!.Contains("DATA: CÉLULA VAZIA NO EXCEL"));
        }

        [Fact]
        public async Task LinhaCompleta_NaoGeraFindingSCP003()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.12", TestFixtures.Pasta(FolderId, "01.12 - SCOPE DEVIATION"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", FolderId, "01.12 - SCOPE DEVIATION")));
            var linhas = new object?[][] { new object?[] { "1", "Desc válida", "CIVIL", "D-1", "01/01/2026", "10", "Aberto", "Interno", "CO-1" } };
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("CONTROLE", Cabecalhos, linhas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Empty(resultado.Findings);
        }
    }
}
