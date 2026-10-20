using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage12ExternalActionAuditRuleTests
    {
        private const string FolderId = "F-EXT";
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DescobertaPorConteudo_IsolaWorkbookInvalidoEBloqueiaDoisValidos(bool bothValid)
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(
                TestFixtures.Item("a", "a.xlsx", FolderId, "01.08", "urn:a"),
                TestFixtures.Item("b", "b.xlsm", FolderId, "01.08", "urn:b")));
            byte[] valid = ExcelTestBuilder.CriarXlsx("Nome totalmente alterado", Cabecalhos,
                new object?[][] { new object?[] { "1", "10/09/2026", "Pessoa", "Ação", "Aberto" } }, linhaCabecalho: 1);
            acc.ConfigurarConteudo("urn:a", bothValid ? valid : System.Text.Encoding.UTF8.GetBytes("corrupt workbook"));
            acc.ConfigurarConteudo("urn:b", valid);
            var result = await regra.ExecutarAsync(TestFixtures.NovoContexto());
            Assert.Contains(result.Requirements, r => r.RequirementCode == "EXT-002" && r.Status == (bothValid ? OperationalRequirementStatus.Failed : OperationalRequirementStatus.Approved));
            if (bothValid) Assert.Contains(result.Findings, f => f.MotivoNormalizado == "ARQUIVO_AMBIGUO" && f.BloqueiaEtapa);
            else Assert.DoesNotContain(result.Findings, f => f.BloqueiaEtapa);
        }
        private static readonly string[] Cabecalhos = { "ID", "DATE", "NAME", "DESCRIPTION", "STATUS", "CYCLE", "AFE", "Name2", "Company3", "COMPANY" };

        private static (Stage12ExternalActionAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc) NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            return (new Stage12ExternalActionAuditRule(acc, new PlanningPreflightService(NullLogger<PlanningPreflightService>.Instance)), acc);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _) = NovaRegra();
            Assert.Equal("12", regra.Etapa);
        }

        [Fact]
        public async Task EXT001_PastaAusente_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "EXT-001");
            Assert.Equal(OperationalAuditClassification.EstruturaAcc, finding.Classificacao);
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task EXT002_PastaValidaSemExcelAlgum_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "EXT-002");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task EXT002_ExcelErradoSemActionItemList_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "CAP.xlsx", FolderId, "01.08")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsxSemAba("CAP"));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "EXT-002");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
        }

        [Fact]
        public async Task ExcelCorreto_CabecalhoNaLinha3_NenhumFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.08")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][] { new object?[] { "1", "01/01/2024", "Fulano", "Descrição do item", "Aberto" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.BloqueiaEtapa);
        }

        [Fact]
        public async Task ExcelCorreto_CabecalhoNaLinha10_NenhumFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.08")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][] { new object?[] { "1", "01/01/2024", "Fulano", "Descrição do item", "Aberto" } }, linhaCabecalho: 10));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.BloqueiaEtapa);
        }

        [Fact]
        public async Task EXT003_CabecalhoAusente_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.08")));
            // Só ID/DATE — faltam DESCRIPTION/STATUS obrigatórios.
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", new[] { "ID", "DATE" },
                new object?[][] { new object?[] { "1", "01/01/2024" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "EXT-003");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task EXT004_LinhaInvalidaNaoBloqueiaLinhaValida()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.08")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][]
                {
                    new object?[] { "1", "01/01/2024", "Fulano", "Descrição válida", "Aberto" },
                    new object?[] { "", "01/01/2024", "Só o nome preenchido", "", "" } // sem ID/Description/Status -> SEM_IDENTIFICADOR.
                }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "EXT-004");
            Assert.Equal(OperationalAuditSeverity.Baixo, finding.Severidade);
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task EXT005_SomenteXlsLegado_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            var itemXls = new AccItemRef { ItemId = "I1", Name = "ACTION.xls", Extension = "XLS", ParentFolderId = FolderId, ParentFolderName = "01.08", LatestVersionUrn = "urn:1" };
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemXls));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "EXT-005");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task DoisCandidatosSomenteUmCorreto_EscolheODeterministico()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));
            var itemErrado = TestFixtures.Item("I1", "CAP.xlsx", FolderId, "01.08");
            var itemCerto = TestFixtures.Item("I2", "ACTION.xlsx", FolderId, "01.08", versionUrn: "urn:2");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemErrado, itemCerto));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsxSemAba("CAP"));
            acc.ConfigurarConteudo("urn:2", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][] { new object?[] { "1", "01/01/2024", "Fulano", "Descrição do item", "Aberto" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.BloqueiaEtapa);
        }

        [Fact]
        public async Task DoisArquivosSomenteUmComHeadersValidos_SelecionaPorConteudo()
        {
            // Um arquivo tem assinatura incompleta; somente o completo pode ser selecionado.
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.08", TestFixtures.Pasta(FolderId, "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG"));

            var itemAntigoIncompleto = new AccItemRef
            {
                ItemId = "I1", Name = "ACTION_OLD.xlsx", Extension = "XLSX", ParentFolderId = FolderId, ParentFolderName = "01.08",
                LatestVersionUrn = "urn:1", LastModifiedUtc = new DateTime(2023, 1, 1)
            };
            var itemRecenteCompleto = new AccItemRef
            {
                ItemId = "I2", Name = "ACTION_NEW.xlsx", Extension = "XLSX", ParentFolderId = FolderId, ParentFolderName = "01.08",
                LatestVersionUrn = "urn:2", LastModifiedUtc = new DateTime(2024, 6, 1)
            };
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemAntigoIncompleto, itemRecenteCompleto));
            // Mais antigo: tem a aba, mas SEM os cabeçalhos obrigatórios (DESCRIPTION/STATUS ausentes).
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", new[] { "ID", "DATE" },
                new object?[][] { new object?[] { "1", "01/01/2023" } }, linhaCabecalho: 3));
            // Mais recente: completo e válido.
            acc.ConfigurarConteudo("urn:2", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][] { new object?[] { "1", "01/06/2024", "Fulano", "Descrição do item", "Aberto" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.BloqueiaEtapa);
        }
    }
}
