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
    public class Stage02HoursRevisionAuditRuleTests
    {
        private const string FolderIdGestao = "F-GESTAO";
        private const string FolderIdEngenharia = "F-ENG";

        private static (Stage02HoursRevisionAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook)
            NovaRegra(string codigoNa = "DOC-1", bool comColunaHoras = true)
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14 - MANAGEMENT", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });

            var cabecalhos = comColunaHoras
                ? new[] { "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER", "HH REALIZADO" }
                : new[] { "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER" };
            var linhas = new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["N&A DOCUMENT NUMBER"] = codigoNa } };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", cabecalhos, linhas)));

            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderIdEngenharia, "04 - ENGENHARIA"));

            return (new Stage02HoursRevisionAuditRule(acc, workbook), acc, workbook);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _) = NovaRegra();
            Assert.Equal("2", regra.Etapa);
        }

        [Fact]
        public async Task HR004_CabecalhoAusente_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra(comColunaHoras: false);
            acc.ConfigurarListagem(FolderIdEngenharia, new AccFolderListingResult());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HR-004");
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
        }

        [Fact]
        public async Task HR004_CabecalhoPresente_NaoGeraFinding()
        {
            var (regra, acc, _) = NovaRegra(comColunaHoras: true);
            acc.ConfigurarListagem(FolderIdEngenharia, new AccFolderListingResult());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "HR-004");
        }

        [Fact]
        public async Task HR001_DocumentoSemArquivoVivo_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            acc.ConfigurarListagem(FolderIdEngenharia, new AccFolderListingResult());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HR-001");
            Assert.Equal(OperationalAuditClassification.DocumentoLdpSemAcc, finding.Classificacao);
        }

        [Fact]
        public async Task HR001_DocumentoComArquivoVivo_NaoGeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderIdEngenharia, new List<AccCustomAttributeDefinitionRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "HR-001");
        }

        [Fact]
        public async Task HR002_DefinicaoRevAusente_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderIdEngenharia, new List<AccCustomAttributeDefinitionRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HR-002");
            Assert.Equal(OperationalAuditClassification.QualidadeConfiguracaoAcc, finding.Classificacao);
        }

        [Fact]
        public async Task HR002_ApiVisibilidadeInconclusiva_NuncaAfirmaOculto()
        {
            var (regra, acc, _) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderIdEngenharia, new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } });
            acc.ConfigurarValores("urn:1", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = false, EvidenciaVisibilidadeExplicita = null } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HR-002");
            Assert.Equal(OperationalAuditErrorDomain.Inconclusivo, finding.ErrorDomain);
        }

        [Fact]
        public async Task HR003_RevPareceTimestamp_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderIdEngenharia, new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } });
            acc.ConfigurarValores("urn:1", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "2024-01-01T10:00:00" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HR-003");
            Assert.Equal(OperationalAuditClassification.DadoInvalido, finding.Classificacao);
        }

        [Fact]
        public async Task HR003_RevValidaSimples_NaoGeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderIdEngenharia, new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } });
            acc.ConfigurarValores("urn:1", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "B" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "HR-003");
        }

        [Fact]
        public async Task HR003_DataPuraDezCaracteres_NaoDescartadaPelaRegraOperacional()
        {
            // "2024-01-01" tem 10 caracteres e nenhum ":" -> a condição operacional real (Contains(":") &&
            // Length>10) NÃO rejeita — mesmo comportamento tem que valer aqui (nunca uma heurística nova).
            var (regra, acc, _) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderIdEngenharia, new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } });
            acc.ConfigurarValores("urn:1", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "2024-01-01" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "HR-003");
        }
    }
}
