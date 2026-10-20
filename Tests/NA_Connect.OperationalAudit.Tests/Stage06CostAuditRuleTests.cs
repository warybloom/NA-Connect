using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage06CostAuditRuleTests
    {
        private const string FolderId = "F-GESTAO";

        private static readonly string[] ColunasCompletas =
        {
            "N&A DOCUMENT NUMBER", "ITEM", "DISCIPLINE", "CLIENT DOCUMENT NUMBER", "DOCUMENT TITLE", "FORMAT",
            "DOCUMENT STATUS", "CR", "CR-OS", "AREA CODE", "DISCIPLINE ACTIVITY", "DOC TYPE CODE", "SEQ"
        };

        private static (Stage06CostAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook) NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderId, "09.14 - MANAGEMENT"));
            var item = TestFixtures.Item("ILDP", "LDP.xlsx", FolderId, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });
            return (new Stage06CostAuditRule(acc, workbook), acc, workbook);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _) = NovaRegra();
            Assert.Equal("6", regra.Etapa);
        }

        [Fact]
        public async Task CST001_AbaCustoAusente_GeraFinding()
        {
            var (regra, _, workbook) = NovaRegra();
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", ColunasCompletas, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CST-001");
            Assert.Equal(OperationalAuditSeverity.Critico, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task CST002_CabecalhoChaveAusenteNaLdp_GeraFinding()
        {
            var (regra, _, workbook) = NovaRegra();
            var abaLdp = TestFixtures.Aba("LDP", new[] { "ITEM" }, new Dictionary<int, Dictionary<string, string?>>());
            var abaCusto = TestFixtures.Aba("CUSTO", ColunasCompletas, new Dictionary<int, Dictionary<string, string?>>());
            workbook.Enfileirar(TestFixtures.Workbook(abaLdp, abaCusto));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CST-002");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task CST003_ColunaNaoChaveAusente_GeraFinding()
        {
            var (regra, _, workbook) = NovaRegra();
            var abaLdp = TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER", "ITEM" }, new Dictionary<int, Dictionary<string, string?>>());
            var abaCusto = TestFixtures.Aba("CUSTO", new[] { "N&A DOCUMENT NUMBER" }, new Dictionary<int, Dictionary<string, string?>>()); // sem ITEM.
            workbook.Enfileirar(TestFixtures.Workbook(abaLdp, abaCusto));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CST-003");
            Assert.Equal(OperationalAuditSeverity.Baixo, finding.Severidade);
        }

        [Fact]
        public async Task CST004_LinhaLdpSemNA_GeraFinding()
        {
            var (regra, _, workbook) = NovaRegra();
            var linhas = new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["N&A DOCUMENT NUMBER"] = "" } };
            var abaLdp = TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, linhas);
            var abaCusto = TestFixtures.Aba("CUSTO", new[] { "N&A DOCUMENT NUMBER" }, new Dictionary<int, Dictionary<string, string?>>());
            workbook.Enfileirar(TestFixtures.Workbook(abaLdp, abaCusto));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Contains(resultado.Findings, f => f.RegraId == "CST-004");
        }

        [Fact]
        public async Task CST005_DivergenciaDeChaves_GeraFinding()
        {
            var (regra, _, workbook) = NovaRegra();
            var linhasLdp = new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-A" } };
            var linhasCusto = new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-B" } };
            var abaLdp = TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, linhasLdp);
            var abaCusto = TestFixtures.Aba("CUSTO", new[] { "N&A DOCUMENT NUMBER" }, linhasCusto);
            workbook.Enfileirar(TestFixtures.Workbook(abaLdp, abaCusto));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CST-005");
            Assert.Contains("DOC-A", finding.Evidencia);
            Assert.Contains("DOC-B", finding.Evidencia);
        }

        [Fact]
        public async Task TudoConsistente_NenhumFinding()
        {
            var (regra, _, workbook) = NovaRegra();
            var linhas = new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-A" } };
            var abaLdp = TestFixtures.Aba("LDP", ColunasCompletas, linhas);
            var abaCusto = TestFixtures.Aba("CUSTO", ColunasCompletas, linhas);
            workbook.Enfileirar(TestFixtures.Workbook(abaLdp, abaCusto));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Empty(resultado.Findings);
        }
    }
}
