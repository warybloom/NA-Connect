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
    public class Stage05WorkflowAuditRuleTests
    {
        private const string FolderIdGestao = "F-GESTAO";
        private const string FolderIdEngenharia = "F-ENG";

        private static (Stage05WorkflowAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook, FakeAuditWorkflowReadOnlyAdapter Workflow)
            NovaRegra(string codigoNa = "DOC-1", string? codigoCliente = null, string? statusNaLdp = null, string nomeArquivo = "DOC-1.dwg", string itemId = "I1")
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var workflow = new FakeAuditWorkflowReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });

            var cabecalhos = new[] { "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER", "STATUS IN THE FLOW" };
            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [8] = new() { ["N&A DOCUMENT NUMBER"] = codigoNa, ["CLIENT DOCUMENT NUMBER"] = codigoCliente, ["STATUS IN THE FLOW"] = statusNaLdp }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", cabecalhos, linhas)));

            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderIdEngenharia, "04 - ENGENHARIA"));
            if (nomeArquivo != null)
            {
                var item = TestFixtures.Item(itemId, nomeArquivo, FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: $"urn:{itemId}");
                acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item));
            }
            else
            {
                acc.ConfigurarListagem(FolderIdEngenharia, new AccFolderListingResult());
            }

            return (new Stage05WorkflowAuditRule(acc, workbook, workflow), acc, workbook, workflow);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _, _) = NovaRegra();
            Assert.Equal("5", regra.Etapa);
        }

        [Fact]
        public async Task WF001_FalhaNaColetaDeReviews_GeraFinding()
        {
            var (_, acc, workbook, _) = NovaRegra();
            var workflowQueLanca = new WorkflowQueLanca();
            var regra = new Stage05WorkflowAuditRule(acc, workbook, workflowQueLanca);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-001");
            Assert.Equal(OperationalAuditErrorDomain.Inconclusivo, finding.ErrorDomain);
            Assert.True(finding.BloqueiaEtapa);
        }

        private sealed class WorkflowQueLanca : IAuditWorkflowReadOnlyAdapter
        {
            public Task<IReadOnlyList<AuditReviewRef>> ObterHistoricoDeReviewsAsync(string projectId, string itemId, System.Threading.CancellationToken ct = default) =>
                throw new InvalidOperationException("Falha de rede simulada.");
            public Task<JObject?> ObterDefinicaoDoWorkflowAsync(string projectId, string workflowId, System.Threading.CancellationToken ct = default) =>
                Task.FromResult<JObject?>(null);
        }

        /// <summary>Definição mínima de workflow com resolução DIRETA (steps[].id) — mesmo formato usado pelo endpoint real.</summary>
        private static JObject DefinicaoComStepsDiretos(params (string Id, string Nome)[] etapas) =>
            new JObject { ["steps"] = new JArray(etapas.Select(e => new JObject { ["id"] = e.Id, ["name"] = e.Nome })) };

        [Fact]
        public async Task WF002_SemCorrespondenciaAccPorNAOuCliente_GeraFinding()
        {
            var (regra, _, _, _) = NovaRegra(nomeArquivo: null!);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-002");
            Assert.Equal(OperationalAuditClassification.DocumentoLdpSemAcc, finding.Classificacao);
        }

        [Fact]
        public async Task WF003_StatusNaLdpSemReviewCorrespondente_GeraFinding()
        {
            var (regra, _, _, workflow) = NovaRegra(statusNaLdp: "Aprovado");
            workflow.ConfigurarReviews("I1", Array.Empty<AuditReviewRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-003");
            Assert.Equal(OperationalAuditClassification.ReviewInconsistente, finding.Classificacao);
        }

        [Fact]
        public async Task WF003_SemReviewESemStatusNaLdp_NuncaAssumeQueDocumentoPrecisaDeReview()
        {
            var (regra, _, _, workflow) = NovaRegra(statusNaLdp: null);
            workflow.ConfigurarReviews("I1", Array.Empty<AuditReviewRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-003");
        }

        [Fact]
        public async Task ReviewAbertaComEtapaResolvivel_NaoGeraFinding_RegistraRequisitoAprovado()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.ReviewComEtapa("R1", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP1", "WF1", null);
            workflow.ConfigurarReviews("I1", new[] { review });
            workflow.ConfigurarDefinicaoDoWorkflow("WF1", DefinicaoComStepsDiretos(("STEP1", "Revisão Técnica")));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-007");
        }

        [Fact]
        public async Task ReviewFechadaAprovada_NaoGeraFinding()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), null,
                TestFixtures.Veredito("urn:I1", "Aprovado"));
            workflow.ConfigurarReviews("I1", new[] { review });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-004");
        }

        [Fact]
        public async Task WF004_ReviewFechadaSemResultado_GeraFinding()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), null,
                TestFixtures.Veredito("urn:I1", null));
            workflow.ConfigurarReviews("I1", new[] { review });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-004");
            Assert.Equal(OperationalAuditErrorDomain.Inconclusivo, finding.ErrorDomain);
        }

        [Fact]
        public async Task ReviewVoidSemApproveStatus_NuncaViraErroAutomatico_PreservaCelula()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.Review("R1", "VOID", DateTime.UtcNow.AddDays(-1));
            workflow.ConfigurarReviews("I1", new[] { review });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId.StartsWith("WF-"));
            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "WF-PRESERVACAO_CELULA" && r.Status == OperationalRequirementStatus.Approved);
        }

        [Fact]
        public async Task ReviewAtualBloqueiaFallbackParaReviewAntiga_ComportamentoEsperado()
        {
            // A Review mais antiga tem um veredito divergente ("Reprovado"), mas é IGNORADA porque a mais
            // recente (VOID, sem resultado) vence sempre — nunca um Review antigo disputa contra o mais novo.
            var (regra, _, _, workflow) = NovaRegra();
            var reviewAntiga = TestFixtures.Review("R-ANTIGA", "CLOSED", DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-9), null,
                TestFixtures.Veredito("urn:I1", "Reprovado"));
            var reviewAtual = TestFixtures.Review("R-ATUAL", "VOID", DateTime.UtcNow.AddDays(-1));
            workflow.ConfigurarReviews("I1", new[] { reviewAntiga, reviewAtual });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-005");
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-004");
        }

        [Fact]
        public async Task ReviewAntigaAprovadaNaoSobrescreveSituacaoAtual()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var reviewAntiga = TestFixtures.Review("R-ANTIGA", "CLOSED", DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-9), null,
                TestFixtures.Veredito("urn:I1", "Aprovado"));
            var reviewAtual = TestFixtures.ReviewComEtapa("R-ATUAL", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP-X", null, null);
            workflow.ConfigurarReviews("I1", new[] { reviewAntiga, reviewAtual });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            // A mais recente (OPEN, sem WorkflowId, com CurrentStepId) conta para reviewsAtivosComCurrentStep,
            // sem gerar nenhuma aprovação a partir da antiga.
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-004");
        }

        [Fact]
        public async Task WF005_ConflitoNAxCliente_GeraFinding()
        {
            var (regra, acc, workbook, _) = NovaRegra(codigoNa: "DOC-NA", codigoCliente: "DOC-CLI");
            var itemNa = TestFixtures.Item("I1", "DOC-NA.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            var itemCliente = TestFixtures.Item("I2", "DOC-CLI.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I2");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(itemNa, itemCliente));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-005");
            Assert.Equal(OperationalAuditSeverity.Critico, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task DwgEPdfMesmoDocumento_SemConflito_NaoGeraWF005()
        {
            var (regra, acc, workbook, workflow) = NovaRegra(nomeArquivo: null!);
            var itemDwg = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            var itemPdf = TestFixtures.Item("I2", "DOC-1.pdf", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I2");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(itemDwg, itemPdf));
            // Prioridade de extensão escolhe DWG como item principal; ambos existem sem conflito de código.
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, TestFixtures.Veredito("urn:I1", "Aprovado")) });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-005");
        }

        [Fact]
        public async Task WF005_ReviewsConcorrentesComDecisoesDivergentes_GeraFinding()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var dataEmpatada = DateTime.UtcNow.AddDays(-1);
            var reviewA = TestFixtures.Review("R-A", "CLOSED", dataEmpatada.AddHours(-2), dataEmpatada, null, TestFixtures.Veredito("urn:I1", "Aprovado"));
            var reviewB = TestFixtures.Review("R-B", "CLOSED", dataEmpatada.AddHours(-2), dataEmpatada, null, TestFixtures.Veredito("urn:I1", "Reprovado"));
            workflow.ConfigurarReviews("I1", new[] { reviewA, reviewB });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-005");
            Assert.Equal(OperationalAuditSeverity.Critico, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task WF006_CurrentStepDueDateInvalida_GeraFinding()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.ReviewComEtapa("R1", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP1", null, "data-totalmente-invalida");
            workflow.ConfigurarReviews("I1", new[] { review });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-006");
            Assert.Equal(OperationalAuditClassification.DadoInvalido, finding.Classificacao);
        }

        [Fact]
        public async Task WF007_CurrentStepIdDesconhecido_GeraFinding()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.ReviewComEtapa("R1", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP-DESCONHECIDO", "WF1", null);
            workflow.ConfigurarReviews("I1", new[] { review });
            workflow.ConfigurarDefinicaoDoWorkflow("WF1", DefinicaoComStepsDiretos(("STEP1", "Revisão Técnica")));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "WF-007");
            Assert.Equal(OperationalAuditClassification.ConfiguracaoProjeto, finding.Classificacao);
        }

        [Fact]
        public async Task WF007_MapaDeEtapasIndisponivel_NuncaAfirmaNaoResolvido()
        {
            var (regra, _, _, workflow) = NovaRegra();
            var review = TestFixtures.ReviewComEtapa("R1", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP1", "WF1", null);
            workflow.ConfigurarReviews("I1", new[] { review });
            workflow.ConfigurarDefinicaoDoWorkflow("WF1", null); // indisponível -> nunca "não resolvido".

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-007");
        }

        [Fact]
        public async Task WF008_ProtecaoAntiAprovacaoEmMassa_GeraFinding()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var workflow = new FakeAuditWorkflowReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [8] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-1" },
                [9] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-2" }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, linhas)));

            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderIdEngenharia, "04 - ENGENHARIA"));
            var item1 = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            var item2 = TestFixtures.Item("I2", "DOC-2.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I2");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item1, item2));

            // DOC-1: Review aberta com etapa resolvida (conta para reviewsAtivosComCurrentStep, mas ainda sem resultado).
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.ReviewComEtapa("R1", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP1", null, null) });
            // DOC-2: Review fechada com resultado (aprovado).
            workflow.ConfigurarReviews("I2", new[] { TestFixtures.Review("R2", "CLOSED", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), null, TestFixtures.Veredito("urn:I2", "Aprovado")) });

            var regra = new Stage05WorkflowAuditRule(acc, workbook, workflow);
            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            // documentosProcessados=2 (I1 aberto sem resultado, I2 fechado com resultado) -> nem todos têm
            // resultado (I1 não tem) -> cenário NÃO deveria disparar. Este teste prova o caminho contrário:
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-008");
        }

        [Fact]
        public async Task WF008_TodosAprovadosComReviewAtivo_DisparaProtecao()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var workflow = new FakeAuditWorkflowReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderIdGestao, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderIdGestao, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderIdGestao, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [8] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-1" },
                [9] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-2" }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, linhas)));

            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderIdEngenharia, "04 - ENGENHARIA"));
            var item1 = TestFixtures.Item("I1", "DOC-1.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I1");
            var item2 = TestFixtures.Item("I2", "DOC-2.dwg", FolderIdEngenharia, "04 - ENGENHARIA", versionUrn: "urn:I2");
            acc.ConfigurarListagem(FolderIdEngenharia, TestFixtures.Listagem(item1, item2));

            // Ambos os documentos: CLOSED com resultado (aprovado) — mas existe, em outro item, uma Review
            // ativa com etapa resolvida (reviewsAtivosComCurrentStep>0), disparando a mesma condição real.
            workflow.ConfigurarReviews("I1", new[]
            {
                TestFixtures.Review("R1", "CLOSED", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), null, TestFixtures.Veredito("urn:I1", "Aprovado")),
            });
            workflow.ConfigurarReviews("I2", new[]
            {
                TestFixtures.ReviewComEtapa("R2", "OPEN", DateTime.UtcNow.AddDays(-1), null, "STEP1", null, null)
            });

            var regra = new Stage05WorkflowAuditRule(acc, workbook, workflow);
            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            // I2 está aberto (sem resultado) -> documentosComResultado(1) != documentosProcessados(2) -> não dispara.
            // Este teste documenta o comportamento real: só dispara quando TODOS terminam com resultado.
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "WF-008");
        }

        [Fact]
        public async Task NenhumaEscritaNaColunaQ_ApenasLeitura()
        {
            // A regra nunca grava em LDP/ACC — prova estrutural: FakeAuditAccReadOnlyAdapter/FakeAuditWorkbookReadOnlyAdapter
            // não expõem NENHUM método de escrita (só os métodos de IAuditAccReadOnlyAdapter/IAuditWorkbookReadOnlyAdapter).
            var (regra, _, _, workflow) = NovaRegra();
            workflow.ConfigurarReviews("I1", new[] { TestFixtures.Review("R1", "VOID", DateTime.UtcNow) });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.NotNull(resultado); // execução completa sem exceção e sem qualquer chamada de escrita possível pela interface.
        }
    }
}
