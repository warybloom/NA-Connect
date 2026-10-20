using System;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage13InternalActionAuditRuleTests
    {
        private const string FolderId = "F-INT";
        private static readonly string[] Cabecalhos = { "ID", "DATE", "NAME", "DESCRIPTION", "STATUS", "CYCLE", "AFE", "Name2", "Company3", "COMPANY" };

        private static (Stage13InternalActionAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc) NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            return (new Stage13InternalActionAuditRule(acc, new PlanningPreflightService(NullLogger<PlanningPreflightService>.Instance)), acc);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _) = NovaRegra();
            Assert.Equal("13", regra.Etapa);
        }

        [Fact]
        public async Task INT001_PastaAusente_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "INT-001");
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task INT002_PastaValidaSemExcelAlgum_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", TestFixtures.Pasta(FolderId, "01.09 - INTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "INT-002");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task ExcelCorreto_NenhumFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", TestFixtures.Pasta(FolderId, "01.09 - INTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.09")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][] { new object?[] { "1", "01/01/2024", "Fulano", "Descrição do item", "Aberto" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.BloqueiaEtapa);
        }

        [Fact]
        public async Task INT003_CabecalhoAusente_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", TestFixtures.Pasta(FolderId, "01.09 - INTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.09")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", new[] { "ID", "DATE" },
                new object?[][] { new object?[] { "1", "01/01/2024" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "INT-003");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task INT004_LinhaInvalidaNaoBloqueiaLinhaValida()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", TestFixtures.Pasta(FolderId, "01.09 - INTERNAL ACTION & PENDING ITEMS LOG"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "ACTION.xlsx", FolderId, "01.09")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][]
                {
                    new object?[] { "1", "01/01/2024", "Fulano", "Descrição válida", "Aberto" },
                    new object?[] { "", "01/01/2024", "Só o nome preenchido", "", "" }
                }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "INT-004");
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task INT005_SomenteXlsLegado_GeraFinding()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", TestFixtures.Pasta(FolderId, "01.09 - INTERNAL ACTION & PENDING ITEMS LOG"));
            var itemXls = new AccItemRef { ItemId = "I1", Name = "ACTION.xls", Extension = "XLS", ParentFolderId = FolderId, ParentFolderName = "01.09", LatestVersionUrn = "urn:1" };
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemXls));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "INT-005");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task DoisArquivosSomenteUmComHeadersValidos_SelecionaPorConteudo()
        {
            // Mesma prova de equivalência da Stage12 (ambas as etapas compartilham ActionLogAuditHelper) —
            // Services\PlanningControlSyncService.cs:144-146.
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", TestFixtures.Pasta(FolderId, "01.09 - INTERNAL ACTION & PENDING ITEMS LOG"));

            var itemAntigoIncompleto = new AccItemRef
            {
                ItemId = "I1", Name = "ACTION_OLD.xlsx", Extension = "XLSX", ParentFolderId = FolderId, ParentFolderName = "01.09",
                LatestVersionUrn = "urn:1", LastModifiedUtc = new DateTime(2023, 1, 1)
            };
            var itemRecenteCompleto = new AccItemRef
            {
                ItemId = "I2", Name = "ACTION_NEW.xlsx", Extension = "XLSX", ParentFolderId = FolderId, ParentFolderName = "01.09",
                LatestVersionUrn = "urn:2", LastModifiedUtc = new DateTime(2024, 6, 1)
            };
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemAntigoIncompleto, itemRecenteCompleto));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", new[] { "ID", "DATE" },
                new object?[][] { new object?[] { "1", "01/01/2023" } }, linhaCabecalho: 3));
            acc.ConfigurarConteudo("urn:2", ExcelTestBuilder.CriarXlsx("ACTION ITEM LIST", Cabecalhos,
                new object?[][] { new object?[] { "1", "01/06/2024", "Fulano", "Descrição do item", "Aberto" } }, linhaCabecalho: 3));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.BloqueiaEtapa);
        }

        [Fact]
        public async Task ResultadosDeInternaNuncaSeMisturamComExterna()
        {
            var (regra, acc) = NovaRegra();
            acc.ConfigurarPasta("01.09", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId.StartsWith("EXT-"));
            Assert.All(resultado.Findings, f => Assert.Equal("13", f.Etapa));
        }
    }
}
