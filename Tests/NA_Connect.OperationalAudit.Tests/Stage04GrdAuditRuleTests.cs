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
    public class Stage04GrdAuditRuleTests
    {
        private const string FolderIdGrd = "F-GRD";
        private const string FolderIdGestao = "F-GESTAO";

        private static (Stage04GrdAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook)
            NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            return (new Stage04GrdAuditRule(acc, workbook), acc, workbook);
        }

        private static Dictionary<int, Dictionary<string, string?>> LinhasGrdValida(string transmittal, string data, params string[] documentos)
        {
            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [3] = new() { ["Col7"] = transmittal, ["Col8"] = "", ["Col9"] = "" },
                [4] = new() { ["Col7"] = data }
            };
            for (int i = 0; i < documentos.Length; i++)
            {
                linhas[24 + i] = new() { ["Col3"] = documentos[i] };
            }
            return linhas;
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _) = NovaRegra();
            Assert.Equal("4", regra.Etapa);
        }

        [Fact]
        public async Task GRD001_PastaAusente_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            acc.ConfigurarPasta("02.02", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "GRD-001");
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task GRD002_PastaSemCandidato_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            acc.ConfigurarPasta("02.02", TestFixtures.Pasta(FolderIdGrd, "02.02 - OUTGOING TRANSMITTALS TO CLIENT"));
            acc.ConfigurarListagem(FolderIdGrd, new AccFolderListingResult());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "GRD-002");
            Assert.Equal(OperationalAuditClassification.DocumentoAusenteAcc, finding.Classificacao);
        }

        [Fact]
        public async Task GRD003_ForaDoTemplate_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegra();
            acc.ConfigurarPasta("02.02", TestFixtures.Pasta(FolderIdGrd, "02.02 - OUTGOING TRANSMITTALS TO CLIENT"));
            var item = TestFixtures.Item("I1", "arquivo.xlsx", FolderIdGrd, "02.02", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdGrd, TestFixtures.Listagem(item));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("Sheet1", Array.Empty<string>(),
                new Dictionary<int, Dictionary<string, string?>> { [3] = new() { ["Col7"] = "NADA AQUI" }, [4] = new() { ["Col7"] = "data-invalida" } })));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "GRD-003");
            Assert.Equal(OperationalAuditClassification.EstruturaPlanilha, finding.Classificacao);
        }

        [Fact]
        public async Task GRD004_DocumentoLdpSemCoberturaDeGrd_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegra();
            acc.ConfigurarPasta("02.02", TestFixtures.Pasta(FolderIdGrd, "02.02 - OUTGOING TRANSMITTALS TO CLIENT"));
            var itemGrd = TestFixtures.Item("I1", "GRD-001.xlsx", FolderIdGrd, "02.02", versionUrn: "urn:grd");
            acc.ConfigurarListagem(FolderIdGrd, TestFixtures.Listagem(itemGrd));
            acc.ConfigurarConteudo("urn:grd", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("GRD", Array.Empty<string>(), LinhasGrdValida("GRD-001", "2024-01-01", "DOC-A"))));

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER", "TRANSMITTAL" },
                new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-B", ["TRANSMITTAL"] = "" } })));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "GRD-004");
            Assert.Equal("DOC-B", finding.DocumentoNa);
        }

        [Fact]
        public async Task GRD005_DocumentoGrdSemLinhaLdp_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegra();
            acc.ConfigurarPasta("02.02", TestFixtures.Pasta(FolderIdGrd, "02.02 - OUTGOING TRANSMITTALS TO CLIENT"));
            var itemGrd = TestFixtures.Item("I1", "GRD-001.xlsx", FolderIdGrd, "02.02", versionUrn: "urn:grd");
            acc.ConfigurarListagem(FolderIdGrd, TestFixtures.Listagem(itemGrd));
            acc.ConfigurarConteudo("urn:grd", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("GRD", Array.Empty<string>(), LinhasGrdValida("GRD-001", "2024-01-01", "DOC-A"))));

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER", "TRANSMITTAL" }, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "GRD-005");
            Assert.Equal("DOC-A", finding.DocumentoNa);
        }

        [Fact]
        public async Task GRD006_ColunaTransmittalAusente_GeraFindingAutomacao()
        {
            var (regra, acc, workbook) = NovaRegra();
            acc.ConfigurarPasta("02.02", TestFixtures.Pasta(FolderIdGrd, "02.02 - OUTGOING TRANSMITTALS TO CLIENT"));
            var itemGrd = TestFixtures.Item("I1", "GRD-001.xlsx", FolderIdGrd, "02.02", versionUrn: "urn:grd");
            acc.ConfigurarListagem(FolderIdGrd, TestFixtures.Listagem(itemGrd));
            acc.ConfigurarConteudo("urn:grd", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("GRD", Array.Empty<string>(), LinhasGrdValida("GRD-001", "2024-01-01", "DOC-A"))));

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "GRD-006");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
        }

        [Fact]
        public async Task GRD007_DesempateDeterministico_NuncaDeclaraAmbiguidade()
        {
            var (regra, acc, workbook) = NovaRegra();
            acc.ConfigurarPasta("02.02", TestFixtures.Pasta(FolderIdGrd, "02.02 - OUTGOING TRANSMITTALS TO CLIENT"));
            var item1 = TestFixtures.Item("I1", "GRD-001.xlsx", FolderIdGrd, "02.02", versionUrn: "urn:grd1");
            var item2 = TestFixtures.Item("I2", "GRD-002.xlsx", FolderIdGrd, "02.02", versionUrn: "urn:grd2");
            acc.ConfigurarListagem(FolderIdGrd, TestFixtures.Listagem(item1, item2));
            acc.ConfigurarConteudo("urn:grd1", new byte[] { 1 });
            acc.ConfigurarConteudo("urn:grd2", new byte[] { 1 });
            // Mesma data, GRD-002 > GRD-001 ordinalmente -> desempate determinístico, sem ambiguidade.
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("GRD", Array.Empty<string>(), LinhasGrdValida("GRD-001", "2024-01-01", "DOC-A"))));
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("GRD", Array.Empty<string>(), LinhasGrdValida("GRD-002", "2024-01-01", "DOC-A"))));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "GRD-007");
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "GRD-004"); // DOC-A coberto pelo vencedor.
        }
    }
}
