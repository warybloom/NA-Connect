using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage16HoldControlAuditRuleTests
    {
        private const string FolderId = "F-HLD";
        private static readonly string[] Cabecalhos = { "ITEM", "Nº HOLD", "Nº N&A DO DOCUMENTO", "DISCIPLINA", "TÍTULO DO DOCUMENTO", "REV", "MOTIVO DO HOLD", "STATUS", "RESPONSÁVEL" };

        private static (Stage16HoldControlAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook) NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            acc.ConfigurarPasta("04 - ENGENHARIA", null);
            return (new Stage16HoldControlAuditRule(acc, workbook, new PlanningPreflightService(NullLogger<PlanningPreflightService>.Instance)), acc, workbook);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _) = NovaRegra();
            Assert.Equal("16", regra.Etapa);
        }

        [Fact]
        public async Task PastaAusente_GeraFindingHLD001()
        {
            var (regra, acc, _) = NovaRegra();
            acc.ConfigurarPasta("01.15", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HLD-001");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task HLD001_ArquivoCandidatoSemAbaControle_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            acc.ConfigurarPasta("01.15", TestFixtures.Pasta(FolderId, "01.15 - HOLD CONTROL"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "BML011.013-000-PL0-FM-003.xlsx", FolderId, "01.15 - HOLD CONTROL")));
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsxSemAba("OUTRA"));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HLD-001");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.EstruturaPlanilha, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task HLD002_ColunaObrigatoriaAusente_GeraFinding()
        {
            var (regra, acc, _) = NovaRegra();
            acc.ConfigurarPasta("01.15", TestFixtures.Pasta(FolderId, "01.15 - HOLD CONTROL"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", FolderId, "01.15 - HOLD CONTROL")));
            var semTitulo = new[] { "ITEM", "Nº HOLD", "Nº N&A DO DOCUMENTO", "DISCIPLINA", "REV", "MOTIVO DO HOLD", "STATUS", "RESPONSÁVEL" };
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("CONTROLE", semTitulo, new object?[][] { new object?[] { "1", "H-1", "DOC-1", "CIVIL", "A", "Motivo", "Aberto", "Fulano" } }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "HLD-002");
        }

        [Fact]
        public async Task HLD003_NumeroDocumentoNaSemCorrespondencia_GeraFinding()
        {
            var (regra, acc, workbook) = NovaRegra();
            acc.ConfigurarPasta("01.15", TestFixtures.Pasta(FolderId, "01.15 - HOLD CONTROL"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", FolderId, "01.15 - HOLD CONTROL")));
            var linhas = new object?[][] { new object?[] { "1", "H-1", "DOC-DESCONHECIDO", "CIVIL", "Título", "A", "Motivo", "Aberto", "Fulano" } };
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("CONTROLE", Cabecalhos, linhas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "HLD-003");
            Assert.Equal("DOC-DESCONHECIDO", finding.DocumentoNa);
        }

        [Fact]
        public async Task HLD003_NumeroDocumentoNaComCorrespondenciaNaLdp_NaoGeraFinding()
        {
            var (regra, acc, workbook) = NovaRegra();
            var pastaGestao = TestFixtures.Pasta("F-GESTAO", "09.14");
            acc.ConfigurarPasta("09.14", pastaGestao);
            acc.ConfigurarListagem("F-GESTAO", TestFixtures.Listagem(TestFixtures.Item("ILDP", "LDP.xlsx", "F-GESTAO", "09.14", versionUrn: "urn:ldp")));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A" },
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, string?>> { [2] = new() { ["N&A"] = "DOC-1" } })));

            acc.ConfigurarPasta("01.15", TestFixtures.Pasta(FolderId, "01.15 - HOLD CONTROL"));
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", FolderId, "01.15 - HOLD CONTROL")));
            var linhas = new object?[][] { new object?[] { "1", "H-1", "DOC-1", "CIVIL", "Título", "A", "Motivo", "Aberto", "Fulano" } };
            acc.ConfigurarConteudo("urn:1", ExcelTestBuilder.CriarXlsx("CONTROLE", Cabecalhos, linhas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "HLD-003");
        }
    }
}
