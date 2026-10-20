using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a resolução em dois níveis (físico x lógico) de EncontrarCorrespondenciaControlada: extensões
    /// companheiras (.dwg/.pdf/.docx) do MESMO documento e do MESMO Review, com a MESMA decisão, nunca são
    /// ambiguidade — só divergência REAL de decisão (status/etapa diferentes) é. Reproduz os dois
    /// documentos controlados do teste real do Worker (linha 40 e linha 424).
    /// </summary>
    public class GrupoLogicoExtensoesTests
    {
        private static AccReviewWorkflowService.WorkflowStatusEntry NovaEntrada(
            string fileName, string itemId, string versionUrn, string reviewId, DateTime data,
            string status, AccReviewWorkflowService.OrigemStatusWorkflow origem, bool preservarCelula = false,
            string codigoResolucao = "RESULTADO_FINAL_EXPLICITO", string currentStepId = "")
        {
            return new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = fileName,
                FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName),
                Extension = System.IO.Path.GetExtension(fileName),
                ItemId = itemId,
                VersionUrn = versionUrn,
                ReviewId = reviewId,
                DataAtualizacaoReview = data,
                Status = status,
                StatusCalculado = status,
                OrigemStatus = origem,
                PreservarCelula = preservarCelula,
                CodigoResolucao = codigoResolucao,
                CurrentStepId = currentStepId
            };
        }

        [Fact]
        public void SomenteDwg_Aceita()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-001-01.dwg", "urn:dwg", "urn:v1", "review-1", new DateTime(2026, 1, 1), "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("DOC-001-01.dwg", resultado.Arquivo);
        }

        [Fact]
        public void SomentePdf_Aceita()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-001-01.pdf", "urn:pdf", "urn:v1", "review-1", new DateTime(2026, 1, 1), "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("DOC-001-01.pdf", resultado.Arquivo);
        }

        [Fact]
        public void DwgEPdf_MesmoReviewIdEAuthorizer_AceitaAuthorizer()
        {
            // Documento controlado — linha 40: P-0602.1-000-PX-FD-0003-01, ReviewId 08c203c5...,
            // OPEN, currentStepId Lane_xRkdCwuXCu resolvido como AUTHORIZER, dwg e pdf concordantes.
            var mesmaData = new DateTime(2026, 7, 1, 17, 15, 8);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-PX-FD-0003-01.dwg", "urn:item-dwg", "urn:v-dwg", "08c203c5-dd8e-4f49-9a94-0361acfad927", mesmaData,
                    "AUTHORIZER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_xRkdCwuXCu"),
                NovaEntrada("P-0602.1-000-PX-FD-0003-01.pdf", "urn:item-pdf", "urn:v-pdf", "08c203c5-dd8e-4f49-9a94-0361acfad927", mesmaData,
                    "AUTHORIZER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_xRkdCwuXCu")
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-PX-FD-0003", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("CORRESPONDENCIA_LOGICA_CONFIRMADA", resultado.CodigoResultado);
            Assert.True(resultado.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes);
            Assert.NotNull(resultado.Arquivo);

            // O status final (obtido depois, via lookup pelo FileName vencedor) é o mesmo nos dois arquivos.
            var vencedora = entries.First(e => e.FileName == resultado.Arquivo);
            Assert.Equal("AUTHORIZER", vencedora.StatusCalculado);
        }

        [Fact]
        public void DwgEPdf_MesmoReviewIdEAprovado_AceitaAprovado()
        {
            // Documento controlado — linha 424: P-0602.1-000-EL-DR-0001-02, ReviewId b695310d...,
            // CLOSED, approveStatus.label=Aprovado nos dois arquivos.
            var mesmaData = new DateTime(2026, 7, 1, 17, 34, 13);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-dwg", "urn:v-dwg-8", "b695310d-56cb-4619-9845-da18dd806a95", mesmaData,
                    "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.pdf", "urn:item-pdf", "urn:v-pdf-4", "b695310d-56cb-4619-9845-da18dd806a95", mesmaData,
                    "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("CORRESPONDENCIA_LOGICA_CONFIRMADA", resultado.CodigoResultado);
            var vencedora = entries.First(e => e.FileName == resultado.Arquivo);
            Assert.Equal("Aprovado", vencedora.StatusCalculado);
        }

        [Fact]
        public void DocxEPdf_MesmoReviewIdEMesmoStatus_Aceita()
        {
            var mesmaData = new DateTime(2026, 1, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-002-01.docx", "urn:item-docx", "urn:v-docx", "review-2", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("DOC-002-01.pdf", "urn:item-pdf", "urn:v-pdf", "review-2", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-002", entries);

            Assert.False(resultado.Ambiguo);
        }

        [Fact]
        public void ItemUrnsDiferentes_MesmoStatus_NaoEhAmbiguidade()
        {
            var mesmaData = new DateTime(2026, 1, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-003-01.dwg", "urn:AAA", "urn:v1", "review-3", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("DOC-003-01.pdf", "urn:BBB-completamente-diferente", "urn:v2", "review-3", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-003", entries);

            Assert.False(resultado.Ambiguo);
        }

        [Fact]
        public void VersionUrnsDiferentes_MesmoStatus_NaoEhAmbiguidade()
        {
            var mesmaData = new DateTime(2026, 1, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-004-01.dwg", "urn:item", "urn:versao-dwg-diferente", "review-4", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("DOC-004-01.pdf", "urn:item", "urn:versao-pdf-diferente", "review-4", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-004", entries);

            Assert.False(resultado.Ambiguo);
        }

        [Fact]
        public void ReviewAntigoVoid_ReviewNovoOpen_ReviewNovoVence()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-005-01.dwg", "urn:item", "urn:v-antiga", "review-antigo-void", new DateTime(2026, 1, 1),
                    status: null, AccReviewWorkflowService.OrigemStatusWorkflow.PRESERVAR_CELULA, preservarCelula: true, codigoResolucao: "CONCLUIDO_SEM_RESULTADO_COMPROVADO"),
                NovaEntrada("DOC-005-01.dwg", "urn:item", "urn:v-nova", "review-novo-open", new DateTime(2026, 6, 1),
                    "CHECKER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_checker")
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-005", entries);

            Assert.False(resultado.Ambiguo);
            var vencedora = entries.First(e => e.ReviewId == "review-novo-open");
            Assert.Equal(resultado.Arquivo, vencedora.FileName);
        }

        [Fact]
        public void ReviewAntigoOpen_ReviewNovoClosedAprovado_NovoVence()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-006-01.dwg", "urn:item", "urn:v-antiga", "review-antigo-open", new DateTime(2026, 1, 1),
                    "VERIFICADOR", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_verificador"),
                NovaEntrada("DOC-006-01.dwg", "urn:item", "urn:v-nova", "review-novo-closed", new DateTime(2026, 6, 1),
                    "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-006", entries);

            Assert.False(resultado.Ambiguo);
            var vencedora = entries.First(e => resultado.Arquivo == e.FileName && e.ReviewId == "review-novo-closed");
            Assert.Equal("Aprovado", vencedora.StatusCalculado);
        }

        [Fact]
        public void DwgAprovado_PdfReprovado_MesmoReview_ConflitoReal()
        {
            var mesmaData = new DateTime(2026, 1, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-007-01.dwg", "urn:dwg", "urn:v1", "review-7", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("DOC-007-01.pdf", "urn:pdf", "urn:v2", "review-7", mesmaData, "Reprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-007", entries);

            Assert.True(resultado.Ambiguo);
            Assert.Equal("WORKFLOW_STATUS_CONFLITANTE_ENTRE_EXTENSOES", resultado.CodigoResultado);
        }

        [Fact]
        public void DwgAuthorizer_PdfChecker_MesmoReview_ConflitoReal()
        {
            var mesmaData = new DateTime(2026, 1, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-008-01.dwg", "urn:dwg", "urn:v1", "review-8", mesmaData, "AUTHORIZER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_A"),
                NovaEntrada("DOC-008-01.pdf", "urn:pdf", "urn:v2", "review-8", mesmaData, "CHECKER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_B")
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-008", entries);

            Assert.True(resultado.Ambiguo);
            Assert.Equal("WORKFLOW_STATUS_CONFLITANTE_ENTRE_EXTENSOES", resultado.CodigoResultado);
        }

        [Fact]
        public void DoisReviewIds_DatasDiferentes_MaisRecenteVence()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-009-01.dwg", "urn:item", "urn:v-antiga", "review-antigo", new DateTime(2026, 1, 1), "CHECKER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_checker"),
                NovaEntrada("DOC-009-01.dwg", "urn:item", "urn:v-nova", "review-novo", new DateTime(2026, 12, 1), "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-009", entries);

            Assert.False(resultado.Ambiguo);
            var vencedora = entries.First(e => e.ReviewId == "review-novo");
            Assert.Equal(vencedora.FileName, resultado.Arquivo);
        }

        [Fact]
        public void DoisReviewIdsEmpatados_StatusDiferentes_ConflitoReal()
        {
            var mesmaData = new DateTime(2026, 3, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-010-01.dwg", "urn:item-a", "urn:va", "review-a", mesmaData, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("DOC-010-02.dwg", "urn:item-b", "urn:vb", "review-b", mesmaData, "Reprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("DOC-010", entries);

            Assert.True(resultado.Ambiguo);
            Assert.Equal("WORKFLOW_CORRESPONDENCIA_AMBIGUA_REAL", resultado.CodigoResultado);
        }

        [Fact]
        public void MuitosParesDwgPdfConcordantes_ZeroAmbiguidades()
        {
            // Reproduz em escala menor a situação real (83 pares concordantes no projeto de teste): um
            // lote de pares dwg/pdf, cada par com seu próprio código/Review, todos concordantes — nenhum
            // deve ser reportado como ambíguo.
            for (int i = 0; i < 10; i++)
            {
                string codigo = $"LOTE-{i:000}";
                var data = new DateTime(2026, 1, 1).AddDays(i);
                var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
                {
                    NovaEntrada($"{codigo}-01.dwg", $"urn:dwg-{i}", $"urn:vdwg-{i}", $"review-{i}", data, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                    NovaEntrada($"{codigo}-01.pdf", $"urn:pdf-{i}", $"urn:vpdf-{i}", $"review-{i}", data, "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
                };

                var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada(codigo, entries);

                Assert.False(resultado.Ambiguo);
                Assert.True(resultado.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes);
            }
        }

        [Fact]
        public void Linha40_DocumentoControlado_ResolveAuthorizer()
        {
            var data = new DateTime(2026, 7, 1, 17, 15, 8);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-PX-FD-0003-01.dwg", "urn:item-dwg", "urn:v-dwg", "08c203c5-dd8e-4f49-9a94-0361acfad927", data,
                    "AUTHORIZER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_xRkdCwuXCu"),
                NovaEntrada("P-0602.1-000-PX-FD-0003-01.pdf", "urn:item-pdf", "urn:v-pdf", "08c203c5-dd8e-4f49-9a94-0361acfad927", data,
                    "AUTHORIZER", AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, currentStepId: "Lane_xRkdCwuXCu")
            };

            // Réplica da prioridade N&A -> Cliente de UpdateWorkflowStatusAsync: N&A (BML011.013-000-PX2-FD-003)
            // não bate no nome físico; Cliente (P-0602.1-000-PX-FD-0003) bate.
            var candidatoNA = LdpUpdateService.EncontrarCorrespondenciaControlada("BML011.013-000-PX2-FD-003", entries);
            var candidatoCliente = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-PX-FD-0003", entries);

            Assert.Null(candidatoNA.Arquivo);
            Assert.False(candidatoCliente.Ambiguo);
            var vencedora = entries.First(e => e.FileName == candidatoCliente.Arquivo);
            Assert.Equal("AUTHORIZER", vencedora.StatusCalculado);
        }

        [Fact]
        public void Linha424_DocumentoControlado_ResolveAprovado()
        {
            var data = new DateTime(2026, 7, 1, 17, 34, 13);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-dwg", "urn:v-dwg-8", "b695310d-56cb-4619-9845-da18dd806a95", data,
                    "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT),
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.pdf", "urn:item-pdf", "urn:v-pdf-4", "b695310d-56cb-4619-9845-da18dd806a95", data,
                    "Aprovado", AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT)
            };

            var candidatoNA = LdpUpdateService.EncontrarCorrespondenciaControlada("BML011.013-000-EL2-DR-001", entries);
            var candidatoCliente = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.Null(candidatoNA.Arquivo);
            Assert.False(candidatoCliente.Ambiguo);
            var vencedora = entries.First(e => e.FileName == candidatoCliente.Arquivo);
            Assert.Equal("Aprovado", vencedora.StatusCalculado);
        }
    }
}
