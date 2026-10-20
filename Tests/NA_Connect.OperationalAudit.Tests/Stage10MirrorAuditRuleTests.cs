using System.Collections.Generic;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage10MirrorAuditRuleTests
    {
        private const string FolderGestao = "F-GESTAO";
        private const string FolderEspelho = "F-ESPELHO";

        private static (Stage10MirrorAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook) NovaRegraComFonte(int linhasFonte = 1)
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderGestao, "09.14"));
            acc.ConfigurarListagem(FolderGestao, TestFixtures.Listagem(TestFixtures.Item("ILDP", "LDP.xlsx", FolderGestao, "09.14")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });

            var linhasFonteDict = new Dictionary<int, Dictionary<string, string?>>();
            for (int i = 0; i < linhasFonte; i++) linhasFonteDict[2 + i] = new Dictionary<string, string?> { ["N&A"] = $"DOC-{i}" };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A" }, linhasFonteDict)));

            return (new Stage10MirrorAuditRule(acc, workbook), acc, workbook);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _) = NovaRegraComFonte();
            Assert.Equal("10", regra.Etapa);
        }

        [Fact]
        public async Task MIR003_LdpPrincipalIndisponivel_GeraFinding()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            var regra = new Stage10MirrorAuditRule(acc, workbook);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "MIR-003");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task MIR001_PastaEspelhoAusente_GeraFinding()
        {
            var (regra, acc, _) = NovaRegraComFonte();
            acc.ConfigurarPasta("01.06", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "MIR-001");
        }

        [Fact]
        public async Task MIR002_PrimeiraPublicacao_GeraFindingInformativoComportamentoEsperado()
        {
            var (regra, acc, _) = NovaRegraComFonte();
            acc.ConfigurarPasta("01.06", TestFixtures.Pasta(FolderEspelho, "01.06"));
            acc.ConfigurarListagem(FolderEspelho, new AccFolderListingResult()); // espelho ainda não existe.

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "MIR-002");
            Assert.Equal(OperationalAuditErrorDomain.ComportamentoEsperado, finding.ErrorDomain);
            Assert.Equal(OperationalAuditSeverity.Informativo, finding.Severidade);
        }

        private void ConfigurarEspelho(FakeAuditAccReadOnlyAdapter acc, FakeAuditWorkbookReadOnlyAdapter workbook, Dictionary<int, Dictionary<string, string?>>? linhasEspelho = null, string[]? abas = null, HashSet<string>? formulas = null)
        {
            acc.ConfigurarPasta("01.06", TestFixtures.Pasta(FolderEspelho, "01.06"));
            acc.ConfigurarListagem(FolderEspelho, TestFixtures.Listagem(TestFixtures.Item("IESP", "LDP.xlsx", FolderEspelho, "01.06", versionUrn: "urn:2")));
            acc.ConfigurarConteudo("urn:2", new byte[] { 1 });

            var abasWorkbook = new List<APS.WebApp.Services.OperationalAudit.ReadOnly.AuditWorksheetSnapshot>
            {
                TestFixtures.Aba("LDP", new[] { "N&A" }, linhasEspelho ?? new Dictionary<int, Dictionary<string, string?>> { [2] = new() { ["N&A"] = "DOC-0" } }, formulas: formulas)
            };
            if (abas != null)
            {
                foreach (var nomeAba in abas) abasWorkbook.Add(TestFixtures.Aba(nomeAba, new[] { "X" }, new Dictionary<int, Dictionary<string, string?>>()));
            }
            else
            {
                abasWorkbook.Add(TestFixtures.Aba("CAPA", new[] { "X" }, new Dictionary<int, Dictionary<string, string?>>()));
            }
            workbook.Enfileirar(TestFixtures.Workbook(abasWorkbook.ToArray()));
        }

        [Fact]
        public async Task EspelhoNormal_NaoGeraFindingsEstruturais()
        {
            var (regra, acc, workbook) = NovaRegraComFonte(linhasFonte: 1);
            ConfigurarEspelho(acc, workbook);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId is "MIR-004" or "MIR-005" or "MIR-006" or "MIR-007" or "MIR-008" or "MIR-009" or "MIR-011" or "MIR-012");
        }

        [Fact]
        public async Task MIR004_AbaLdpAusenteNoEspelho_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte();
            acc.ConfigurarPasta("01.06", TestFixtures.Pasta(FolderEspelho, "01.06"));
            acc.ConfigurarListagem(FolderEspelho, TestFixtures.Listagem(TestFixtures.Item("IESP", "LDP.xlsx", FolderEspelho, "01.06", versionUrn: "urn:2")));
            acc.ConfigurarConteudo("urn:2", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("CAPA", new[] { "X" }, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "MIR-004");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task MIR006_FormulaResidual_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte();
            ConfigurarEspelho(acc, workbook, formulas: new HashSet<string> { "D8" });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "MIR-006");
            Assert.Equal("D8", finding.Campo);
        }

        [Fact]
        public async Task MIR007_AbaPrivadaMantida_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte();
            ConfigurarEspelho(acc, workbook, abas: new[] { "CAPA", "Planilha1 (oculta)" });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "MIR-007");
        }

        [Fact]
        public async Task MIR008_AbaCapaRemovida_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte();
            ConfigurarEspelho(acc, workbook, abas: new string[0]);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "MIR-008");
        }

        [Fact]
        public async Task MIR012_QuantidadeDeLinhasDivergente_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte(linhasFonte: 3);
            ConfigurarEspelho(acc, workbook, linhasEspelho: new Dictionary<int, Dictionary<string, string?>> { [2] = new() { ["N&A"] = "DOC-0" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "MIR-012");
            Assert.Equal("3", finding.ValorEsperado);
            Assert.Equal("1", finding.ValorEncontrado);
        }

        [Fact]
        public async Task MIR009_EspelhoDesatualizado_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte(linhasFonte: 5);
            ConfigurarEspelho(acc, workbook, linhasEspelho: new Dictionary<int, Dictionary<string, string?>> { [2] = new() { ["N&A"] = "DOC-0" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "MIR-009");
        }

        [Fact]
        public async Task MIR010_EspelhoVazioComFonteComDados_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegraComFonte(linhasFonte: 2);
            ConfigurarEspelho(acc, workbook, linhasEspelho: new Dictionary<int, Dictionary<string, string?>>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "MIR-010");
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
        }
    }
}
