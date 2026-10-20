using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Aggregation;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage08ApprovalVerdictAuditRuleTests
    {
        private const string FolderId = "F-ENG04";

        private static (Stage08ApprovalVerdictAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkflowReadOnlyAdapter Workflow, FakeAuditWorkbookReadOnlyAdapter Workbook) NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workflow = new FakeAuditWorkflowReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderId, "04 - ENGENHARIA"));
            acc.ConfigurarPasta("09.14", null);
            return (new Stage08ApprovalVerdictAuditRule(acc, workflow, workbook), acc, workflow, workbook);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _, _) = NovaRegra();
            Assert.Equal("8", regra.Etapa);
        }

        [Fact]
        public async Task NenhumaReview_NaoGeraFinding()
        {
            var (regra, acc, _, _) = NovaRegra();
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA")));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId.StartsWith("VRD"));
        }

        [Fact]
        public async Task DecisoesIguais_NaoGeraFinding()
        {
            var (regra, acc, workflow, _) = NovaRegra();
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA")));
            workflow.ConfigurarReviews("I1", new[]
            {
                TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), null, TestFixtures.Veredito("urn:1", "APPROVED"))
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId is "VRD-001" or "VRD-002");
        }

        [Fact]
        public async Task VRD001_MotorHistoricoDivergeDaEtapa5_GeraFinding()
        {
            var (regra, acc, workflow, _) = NovaRegra();
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA")));

            var agora = DateTime.UtcNow;
            // R1 começou antes mas fechou DEPOIS de R2; R2 começou depois mas fechou ANTES de R1.
            // Etapa5 decide pela review mais recente por INÍCIO (R2 -> APPROVED).
            // Motor histórico decide pela review fechada mais recente por FECHAMENTO (R1 -> REJECTED).
            // Isso força uma divergência real entre os dois critérios de ordenação.
            workflow.ConfigurarReviews("I1", new[]
            {
                TestFixtures.Review("R1", "CLOSED", agora.AddDays(-10), agora.AddDays(-1), null, TestFixtures.Veredito("urn:1", "REJECTED")),
                TestFixtures.Review("R2", "CLOSED", agora.AddDays(-5), agora.AddDays(-8), null, TestFixtures.Veredito("urn:1", "APPROVED"))
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "VRD-001");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.RiscoTecnico, finding.Classificacao);
            Assert.Equal("APPROVED", finding.ValorEsperado);
            Assert.Equal("REJECTED", finding.ValorEncontrado);
        }

        [Fact]
        public async Task VRD002_ReviewMaisRecenteVoidSemAprovacao_MotorHistoricoUsariaAprovacaoAntiga_GeraFinding()
        {
            var (regra, acc, workflow, _) = NovaRegra();
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA")));
            workflow.ConfigurarReviews("I1", new[]
            {
                TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2), null, TestFixtures.Veredito("urn:1", "APPROVED")),
                TestFixtures.Review("R2", "VOID", DateTime.UtcNow, null, null) // mais recente por início, VOID, sem veredito.
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "VRD-002");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.RiscoTecnico, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
            Assert.Equal("APPROVED", finding.ValorEncontrado);
        }

        [Fact]
        public async Task VRD003_CodigoCompostoSemNaSemCliente_GeraFinding()
        {
            var (regra, acc, _, workbook) = NovaRegra();
            var pastaGestao = TestFixtures.Pasta("F-GESTAO", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pastaGestao);
            acc.ConfigurarListagem("F-GESTAO", TestFixtures.Listagem(TestFixtures.Item("ILDP", "LDP.xlsx", "F-GESTAO", "09.14 - MANAGEMENT")));
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult());

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [2] = new() { ["CÓDIGO COMPOSTO"] = "Y-Z-AA-AB-AC", ["N&A DOCUMENT NUMBER"] = null, ["CLIENT DOCUMENT NUMBER"] = null }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "CÓDIGO COMPOSTO", "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER" }, linhas)));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "VRD-003");
            Assert.Equal(2, finding.Linha);
            Assert.Equal("Y-Z-AA-AB-AC", finding.ValorEncontrado);
        }

        [Fact]
        public async Task VRD003_CodigoCompostoComNa_NaoGeraFinding()
        {
            var (regra, acc, _, workbook) = NovaRegra();
            var pastaGestao = TestFixtures.Pasta("F-GESTAO", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pastaGestao);
            acc.ConfigurarListagem("F-GESTAO", TestFixtures.Listagem(TestFixtures.Item("ILDP", "LDP.xlsx", "F-GESTAO", "09.14 - MANAGEMENT")));
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult());

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [2] = new() { ["CÓDIGO COMPOSTO"] = "Y-Z-AA-AB-AC", ["N&A DOCUMENT NUMBER"] = "DOC-1", ["CLIENT DOCUMENT NUMBER"] = null }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "CÓDIGO COMPOSTO", "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER" }, linhas)));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "VRD-003");
        }
    }

    /// <summary>Testes diretos da engine pura de comparação (mais fáceis de raciocinar do que via a regra completa).</summary>
    public class ApprovalVerdictComparisonEngineTests
    {
        [Fact]
        public void Etapa5_SemReviews_RetornaSemReviews()
        {
            var decisao = ApprovalVerdictComparisonEngine.DecidirComoEtapa5(Array.Empty<AuditReviewRef>(), "urn:1");
            Assert.Equal(VeredictoTipoDecisao.SemReviews, decisao.Tipo);
        }

        [Fact]
        public void Etapa5_VersaoComApproveStatusExplicito_UsaOStatus()
        {
            var reviews = new[] { TestFixtures.Review("R1", "OPEN", DateTime.UtcNow, null, "Aprovação Cliente", TestFixtures.Veredito("urn:1", "APPROVED")) };
            var decisao = ApprovalVerdictComparisonEngine.DecidirComoEtapa5(reviews, "urn:1");
            Assert.Equal(VeredictoTipoDecisao.StatusExplicito, decisao.Tipo);
            Assert.Equal("APPROVED", decisao.Valor);
        }

        [Fact]
        public void Etapa5_ReviewAbertaSemApproveStatusComEtapaResolvida_UsaNomeDaEtapa()
        {
            var reviews = new[] { TestFixtures.Review("R1", "OPEN", DateTime.UtcNow, null, "Aprovação Cliente", TestFixtures.Veredito("urn:1", null)) };
            var decisao = ApprovalVerdictComparisonEngine.DecidirComoEtapa5(reviews, "urn:1");
            Assert.Equal(VeredictoTipoDecisao.NomeDaEtapa, decisao.Tipo);
            Assert.Equal("Aprovação Cliente", decisao.Valor);
        }

        [Fact]
        public void Etapa5_ReviewVoidSemApproveStatus_PreservaCelula_NuncaCaiParaMaisAntiga()
        {
            var reviews = new[]
            {
                TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(-5), null, TestFixtures.Veredito("urn:1", "APPROVED")),
                TestFixtures.Review("R2", "VOID", DateTime.UtcNow, null, null)
            };
            var decisao = ApprovalVerdictComparisonEngine.DecidirComoEtapa5(reviews, "urn:1");
            Assert.Equal(VeredictoTipoDecisao.PreservarCelula, decisao.Tipo);
        }

        [Fact]
        public void MotorHistorico_PulaReviewVoid_UsaFechadaMaisAntigaComVeredito()
        {
            var reviews = new[]
            {
                TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(-5), null, TestFixtures.Veredito("urn:1", "APPROVED")),
                TestFixtures.Review("R2", "VOID", DateTime.UtcNow, null, null)
            };
            var decisao = ApprovalVerdictComparisonEngine.DecidirComoMotorHistorico(reviews, "urn:1");
            Assert.Equal(VeredictoTipoDecisao.StatusExplicito, decisao.Tipo);
            Assert.Equal("APPROVED", decisao.Valor);
            Assert.Equal("R1", decisao.ReviewIdUsada);
        }

        [Fact]
        public void MotorHistorico_IgnoraReviewsAbertas()
        {
            var reviews = new[] { TestFixtures.Review("R1", "OPEN", DateTime.UtcNow, null, null, TestFixtures.Veredito("urn:1", "APPROVED")) };
            var decisao = ApprovalVerdictComparisonEngine.DecidirComoMotorHistorico(reviews, "urn:1");
            Assert.Equal(VeredictoTipoDecisao.SemReviews, decisao.Tipo);
        }
    }
}
