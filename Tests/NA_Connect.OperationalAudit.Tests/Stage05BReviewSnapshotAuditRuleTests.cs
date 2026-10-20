using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using Newtonsoft.Json.Linq;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage05BReviewSnapshotAuditRuleTests
    {
        private const string FolderIdEngenharia = "F-ENG";

        private static (Stage05BReviewSnapshotAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkflowReadOnlyAdapter Workflow, FakeAuditDatabaseReadOnlyAdapter Db)
            NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var workflow = new FakeAuditWorkflowReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();

            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderIdEngenharia, "04 - ENGENHARIA"));

            return (new Stage05BReviewSnapshotAuditRule(acc, workbook, workflow, db), acc, workflow, db);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _, _) = NovaRegra();
            Assert.Equal("5B", regra.Etapa);
        }

        [Fact]
        public async Task SNP004_DocumentoAusenteNaTabelaPersistida_GeraFinding()
        {
            var (regra, acc, workflow, db) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, TestFixtures.Veredito("urn:I1", "Aprovado")) });
            db.ConfigurarResposta("ACC_Review_Current_Stage", TestFixtures.Tabela(new[] { "NumeroDocumentoNormalizado", "ReviewId", "ReviewStatus", "CurrentStepId", "EtapaAtual", "OrigemResponsavel" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Contains(resultado.Findings, f => f.RegraId == "SNP-004" && f.DocumentoNa == "DOC-1");
        }

        [Fact]
        public async Task SNP004_SnapshotConsistenteComPersistido_NaoGeraFinding()
        {
            var (regra, acc, workflow, db) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, TestFixtures.Veredito("urn:I1", "Aprovado")) });
            db.ConfigurarResposta("ACC_Review_Current_Stage", TestFixtures.Tabela(
                new[] { "NumeroDocumentoNormalizado", "ReviewId", "ReviewStatus", "CurrentStepId", "EtapaAtual", "OrigemResponsavel" },
                new object?[] { "DOC-1", "R1", "CLOSED", null, null, "RESPONSAVEL_ATRIBUIDO" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "SNP-004");
        }

        [Fact]
        public async Task SNP002_ResponsavelNaoEncontrado_GeraFindingInformativo()
        {
            var (regra, acc, workflow, db) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, TestFixtures.Veredito("urn:I1", "Aprovado")) });
            db.ConfigurarResposta("ACC_Review_Current_Stage", TestFixtures.Tabela(
                new[] { "NumeroDocumentoNormalizado", "ReviewId", "ReviewStatus", "CurrentStepId", "EtapaAtual", "OrigemResponsavel" },
                new object?[] { "DOC-1", "R1", "CLOSED", null, null, "NAO_ENCONTRADO" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "SNP-002");
            Assert.Equal(OperationalAuditErrorDomain.ComportamentoEsperado, finding.ErrorDomain);
            Assert.Equal(OperationalAuditSeverity.Informativo, finding.Severidade);
            Assert.False(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task SNP003_ExtensoesComReviewsDivergentes_GeraFinding()
        {
            var (regra, acc, workflow, db) = NovaRegra();
            var itemDwg = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            var itemPdf = TestFixtures.Item("I2", "DOC-1.pdf", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I2");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(itemDwg, itemPdf));

            var agora = DateTime.UtcNow;
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.Review("R-DWG", "CLOSED", agora.AddDays(-2), agora.AddDays(-1), null, TestFixtures.Veredito("urn:I1", "Aprovado")) });
            workflow.ConfigurarReviews("I2", new[] { TestFixtures.Review("R-PDF", "CLOSED", agora.AddDays(-2), agora, null, TestFixtures.Veredito("urn:I2", "Reprovado")) });
            db.ConfigurarResposta("ACC_Review_Current_Stage", TestFixtures.Tabela(new[] { "NumeroDocumentoNormalizado", "ReviewId", "ReviewStatus", "CurrentStepId", "EtapaAtual", "OrigemResponsavel" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "SNP-003");
            Assert.Equal(OperationalAuditClassification.ReviewInconsistente, finding.Classificacao);
        }

        [Fact]
        public async Task SNP001_EtapaResolvivelMasPersistidaVazia_GeraFindingAutomacao()
        {
            var (regra, acc, workflow, db) = NovaRegra();
            var item = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            var review = TestFixtures.ReviewComEtapa("R1", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP1", "WF1", null);
            workflow.ConfigurarReviews("I1", new[] { review });
            workflow.ConfigurarDefinicaoDoWorkflow("WF1", new JObject { ["steps"] = new JArray(new JObject { ["id"] = "STEP1", ["name"] = "Revisão Técnica" }) });
            db.ConfigurarResposta("ACC_Review_Current_Stage", TestFixtures.Tabela(
                new[] { "NumeroDocumentoNormalizado", "ReviewId", "ReviewStatus", "CurrentStepId", "EtapaAtual", "OrigemResponsavel" },
                new object?[] { "DOC-1", "R1", "OPEN", "STEP1", "", "NAO_ENCONTRADO" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "SNP-001");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
        }
    }
}
