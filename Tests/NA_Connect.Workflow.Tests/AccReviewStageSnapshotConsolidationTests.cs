using APS.WebApp.Models;
using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a consolidação de documento lógico (DWG+PDF) e a preparação do DataTable do snapshot
    /// (AccReviewStageSnapshotService — funcionalidade "ACC_REVIEW_STAGE", seção 8/9/10). Nunca toca
    /// Status_In_Flow, Cronograma, Qualidade/RNC, LDP Tipo 2 ou a LDP em si — este serviço só lê
    /// WorkflowBuildResult.Entries/ReviewStagesByReviewId (já construídos) e prepara registros para SQL.
    /// </summary>
    public class AccReviewStageSnapshotConsolidationTests
    {
        private static AccReviewWorkflowService.WorkflowStatusEntry NovaEntrada(
            string fileName, string itemId, string versionUrn, string reviewId, DateTime dataAtualizacaoReview,
            string pastaEncontrada = "10 - PRODUCTION", string statusCalculado = "CHECKER")
        {
            return new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = fileName,
                FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName),
                Extension = System.IO.Path.GetExtension(fileName),
                ItemId = itemId,
                VersionUrn = versionUrn,
                ReviewId = reviewId,
                DataAtualizacaoReview = dataAtualizacaoReview,
                PastaEncontrada = pastaEncontrada,
                StatusCalculado = statusCalculado
            };
        }

        private static ReviewStageMetadata NovaEtapa(
            string reviewId, string reviewNumber, string etapa, List<string> responsaveis, DateOnly? vencimento, string reviewStatus = "OPEN")
        {
            return new ReviewStageMetadata
            {
                ReviewId = reviewId,
                ReviewNumber = reviewNumber,
                ReviewName = "PACOTE TESTE",
                ReviewStatus = reviewStatus,
                CurrentStepId = "Lane_teste",
                CurrentStepName = etapa,
                ResponsibleNames = responsaveis,
                ResponsibleIds = responsaveis.Select(n => $"id-{n}").ToList(),
                ResponsibleSource = "CANDIDATO_ETAPA",
                DueDateLocal = vencimento,
                DueDateSource = vencimento.HasValue ? "CURRENT_STEP_DUE_DATE" : "NAO_ENCONTRADO"
            };
        }

        private static AccReviewWorkflowService.WorkflowBuildResult NovoWorkflowResult(
            List<AccReviewWorkflowService.WorkflowStatusEntry> entries, Dictionary<string, ReviewStageMetadata> etapas)
        {
            var resultado = new AccReviewWorkflowService.WorkflowBuildResult { Sucesso = true };
            resultado.Entries.AddRange(entries);
            foreach (var kv in etapas) resultado.ReviewStagesByReviewId[kv.Key] = kv.Value;
            return resultado;
        }

        [Fact]
        public void DwgEPdf_MesmoReview_ViramUmDocumentoLogico()
        {
            var data = new DateTime(2026, 7, 31, 17, 41, 51);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-920-PX-DR-0001-02.dwg", "urn:item-dwg", "urn:v-dwg", "review-603", data),
                NovaEntrada("P-0602.1-920-PX-DR-0001-02.pdf", "urn:item-pdf", "urn:v-pdf", "review-603", data)
            };
            var etapas = new Dictionary<string, ReviewStageMetadata>
            {
                ["review-603"] = NovaEtapa("review-603", "603", "CHECKER", new List<string> { "Roberto Andrei" }, new DateOnly(2026, 7, 31))
            };
            var workflowResult = NovoWorkflowResult(entries, etapas);
            var resultado = new ResultadoSnapshotEtapaReview();

            var documentos = AccReviewStageSnapshotService.ConsolidarDocumentosLogicos(workflowResult, resultado);

            Assert.Single(documentos);
            var doc = documentos[0];
            Assert.Equal(0, resultado.Conflitos);
            Assert.Equal("P-0602.1-920-PX-DR-0001-02", doc.NumeroDocumento);
            Assert.Equal(2, doc.ArquivosFisicos.Count);
            Assert.Contains("P-0602.1-920-PX-DR-0001-02.dwg", doc.ArquivosFisicos);
            Assert.Contains("P-0602.1-920-PX-DR-0001-02.pdf", doc.ArquivosFisicos);
            Assert.Equal("CHECKER", doc.EtapaAtual);
            Assert.Equal(new[] { "Roberto Andrei" }, doc.ResponsibleNames);
            Assert.Equal(new DateOnly(2026, 7, 31), doc.DataVencimentoEtapa);
        }

        [Fact]
        public void ItemUrnsDiferentesEntreDwgEPdf_NaoGeraConflito()
        {
            var data = new DateTime(2026, 1, 1);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-001-01.dwg", "urn:AAA", "urn:v1", "review-1", data),
                NovaEntrada("DOC-001-01.pdf", "urn:BBB-completamente-diferente", "urn:v2", "review-1", data)
            };
            var etapas = new Dictionary<string, ReviewStageMetadata>
            {
                ["review-1"] = NovaEtapa("review-1", "1", "CHECKER", new List<string> { "Fulano" }, new DateOnly(2026, 1, 5))
            };
            var workflowResult = NovoWorkflowResult(entries, etapas);
            var resultado = new ResultadoSnapshotEtapaReview();

            var documentos = AccReviewStageSnapshotService.ConsolidarDocumentosLogicos(workflowResult, resultado);

            Assert.Single(documentos);
            Assert.Equal(0, resultado.Conflitos);
            Assert.Equal(2, documentos[0].ItemUrns.Count);
        }

        [Fact]
        public void ReviewMaisRecente_VenceHistorico()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-002-01.dwg", "urn:item", "urn:v-antiga", "review-antigo", new DateTime(2026, 1, 1)),
                NovaEntrada("DOC-002-01.dwg", "urn:item", "urn:v-nova", "review-novo", new DateTime(2026, 6, 1))
            };
            var etapas = new Dictionary<string, ReviewStageMetadata>
            {
                ["review-antigo"] = NovaEtapa("review-antigo", "1", "AUTHORIZER", new List<string> { "Fulano" }, new DateOnly(2026, 1, 5)),
                ["review-novo"] = NovaEtapa("review-novo", "2", "CHECKER", new List<string> { "Ciclano" }, new DateOnly(2026, 6, 5))
            };
            var workflowResult = NovoWorkflowResult(entries, etapas);
            var resultado = new ResultadoSnapshotEtapaReview();

            var documentos = AccReviewStageSnapshotService.ConsolidarDocumentosLogicos(workflowResult, resultado);

            Assert.Single(documentos);
            Assert.Equal("review-novo", documentos[0].ReviewId);
            Assert.Equal("CHECKER", documentos[0].EtapaAtual);
        }

        [Fact]
        public void ReviewVoidAntigo_NaoVenceReviewOpenNovo()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-003-01.dwg", "urn:item", "urn:v-void", "review-void-antigo", new DateTime(2026, 1, 1)),
                NovaEntrada("DOC-003-01.dwg", "urn:item", "urn:v-open", "review-open-novo", new DateTime(2026, 6, 1))
            };
            var etapas = new Dictionary<string, ReviewStageMetadata>
            {
                // Review VOID: reviewAberto=false -> etapa/responsável/vencimento nulos (seção 9).
                ["review-void-antigo"] = new ReviewStageMetadata
                {
                    ReviewId = "review-void-antigo", ReviewNumber = "1", ReviewStatus = "VOID",
                    ResponsibleSource = "REVIEW_NAO_ABERTO", DueDateSource = "REVIEW_NAO_ABERTO"
                },
                ["review-open-novo"] = NovaEtapa("review-open-novo", "2", "CHECKER", new List<string> { "Roberto Andrei" }, new DateOnly(2026, 6, 5))
            };
            var workflowResult = NovoWorkflowResult(entries, etapas);
            var resultado = new ResultadoSnapshotEtapaReview();

            var documentos = AccReviewStageSnapshotService.ConsolidarDocumentosLogicos(workflowResult, resultado);

            Assert.Single(documentos);
            Assert.Equal("review-open-novo", documentos[0].ReviewId);
            Assert.Equal("CHECKER", documentos[0].EtapaAtual);
        }

        [Fact]
        public void ReviewCloseComOpen_MesmaDataDeAtualizacao_GeraConflito()
        {
            // Empate real entre dois ReviewIds diferentes na MESMA DataAtualizacaoReview mais recente —
            // etapa/responsável/vencimento divergem (um vem de CHECKER, outro de AUTHORIZER) e não há como
            // decidir qual vence de forma comprovada (seção 8/9). Documento não entra no snapshot.
            var dataEmpatada = new DateTime(2026, 3, 1, 10, 0, 0);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("DOC-004-01.dwg", "urn:item-a", "urn:va", "review-a", dataEmpatada),
                NovaEntrada("DOC-004-02.dwg", "urn:item-b", "urn:vb", "review-b", dataEmpatada)
            };
            var etapas = new Dictionary<string, ReviewStageMetadata>
            {
                ["review-a"] = NovaEtapa("review-a", "1", "CHECKER", new List<string> { "Fulano" }, new DateOnly(2026, 3, 5)),
                ["review-b"] = NovaEtapa("review-b", "2", "AUTHORIZER", new List<string> { "Ciclano" }, new DateOnly(2026, 3, 10))
            };

            // Ambas as entradas precisam normalizar para o MESMO nome-base para cair no mesmo grupo —
            // usa o mesmo FileNameWithoutExtension propositalmente.
            entries[1].FileNameWithoutExtension = entries[0].FileNameWithoutExtension;
            entries[1].FileName = entries[0].FileNameWithoutExtension + ".pdf";

            var workflowResult = NovoWorkflowResult(entries, etapas);
            var resultado = new ResultadoSnapshotEtapaReview();

            var documentos = AccReviewStageSnapshotService.ConsolidarDocumentosLogicos(workflowResult, resultado);

            Assert.Empty(documentos);
            Assert.Equal(1, resultado.Conflitos);
        }

        [Fact]
        public void DataTable_DataVencimento_ApenasData_SemHora()
        {
            var documentos = new List<RegistroSnapshotEtapaReviewPreparado>
            {
                new RegistroSnapshotEtapaReviewPreparado
                {
                    ReviewId = "review-603",
                    NumeroDocumento = "P-0602.1-920-PX-DR-0001-02",
                    NumeroDocumentoNormalizado = "P060219 20PXDR000102",
                    DataVencimentoEtapa = new DateOnly(2026, 7, 31),
                    ResponsibleNames = new List<string> { "Roberto Andrei" },
                    ArquivosFisicos = new List<string> { "P-0602.1-920-PX-DR-0001-02.dwg" }
                }
            };

            var tabela = AccReviewStageSnapshotService.ConstruirDataTable("b.projeto-teste", documentos, new DateTime(2026, 7, 31, 15, 0, 0));

            var valor = (DateTime)tabela.Rows[0]["DataVencimentoEtapa"];
            Assert.Equal(new DateTime(2026, 7, 31, 0, 0, 0), valor);
            Assert.Equal("Roberto Andrei", tabela.Rows[0]["ResponsavelEtapa"]);
        }

        [Fact]
        public void DataTable_VencimentoAusente_GravaDbNull()
        {
            var documentos = new List<RegistroSnapshotEtapaReviewPreparado>
            {
                new RegistroSnapshotEtapaReviewPreparado
                {
                    ReviewId = "review-1",
                    NumeroDocumento = "DOC-999",
                    NumeroDocumentoNormalizado = "DOC999",
                    DataVencimentoEtapa = null
                }
            };

            var tabela = AccReviewStageSnapshotService.ConstruirDataTable("b.projeto-teste", documentos, new DateTime(2026, 7, 31));

            Assert.Equal(DBNull.Value, tabela.Rows[0]["DataVencimentoEtapa"]);
        }

        [Fact]
        public void TodosOsRegistros_UsamAMesmaDataSincronizacao()
        {
            var documentos = new List<RegistroSnapshotEtapaReviewPreparado>
            {
                new RegistroSnapshotEtapaReviewPreparado { ReviewId = "r1", NumeroDocumento = "A", NumeroDocumentoNormalizado = "A" },
                new RegistroSnapshotEtapaReviewPreparado { ReviewId = "r2", NumeroDocumento = "B", NumeroDocumentoNormalizado = "B" }
            };
            var dataSincronizacao = new DateTime(2026, 7, 31, 15, 30, 0);

            var tabela = AccReviewStageSnapshotService.ConstruirDataTable("b.projeto-teste", documentos, dataSincronizacao);

            Assert.Equal(dataSincronizacao, tabela.Rows[0]["DataSincronizacao"]);
            Assert.Equal(dataSincronizacao, tabela.Rows[1]["DataSincronizacao"]);
        }
    }
}
