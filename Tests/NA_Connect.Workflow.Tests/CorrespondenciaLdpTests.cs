using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a correspondência arquivo físico → linha da LDP (seção 9.2/9.4/9.5 do roteiro): prefixo de
    /// tokens (tolera sufixo de revisão sem Contains irrestrito) e detecção de ambiguidade quando mais de
    /// um item físico distinto corresponde ao mesmo código sem desempate por Review mais recente.
    /// </summary>
    public class CorrespondenciaLdpTests
    {
        private static AccReviewWorkflowService.WorkflowStatusEntry NovaEntrada(string fileName, string itemId, DateTime data, string status = "Aprovado", string reviewId = "review-unico")
        {
            return new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = fileName,
                FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName),
                ItemId = itemId,
                DataAtualizacaoReview = data,
                Status = status,
                StatusCalculado = status,
                ReviewId = reviewId
            };
        }

        [Fact]
        public void ArquivoNomeadoPeloNumeroCliente_ComRevisaoNoFinal_Corresponde()
        {
            // Documento controlado: Cliente="P-0602.1-000-EL-DR-0001" (sem revisão), arquivo físico
            // "P-0602.1-000-EL-DR-0001-02.dwg" (revisão -02 ao final). Deve corresponder por prefixo de
            // tokens, sem precisar de Contains irrestrito.
            Assert.True(LdpUpdateService.NomeArquivoCorrespondeAoDocumento("P-0602.1-000-EL-DR-0001-02.dwg", "P-0602.1-000-EL-DR-0001"));
        }

        [Fact]
        public void CodigoComPontoIncidental_NaoBateComPrefixoErrado()
        {
            // "DOC-001" não pode corresponder a "DOC-0010" (2º token difere) nem a "X-DOC-001-05" (1º
            // token difere) — nunca por substring/Contains.
            Assert.False(LdpUpdateService.NomeArquivoCorrespondeAoDocumento("DOC-0010-05.dwg", "DOC-001"));
            Assert.False(LdpUpdateService.NomeArquivoCorrespondeAoDocumento("X-DOC-001-05.dwg", "DOC-001"));
            Assert.True(LdpUpdateService.NomeArquivoCorrespondeAoDocumento("DOC-001-05.dwg", "DOC-001"));
        }

        [Fact]
        public void EntradaUnica_SemAmbiguidade_RetornaArquivo()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-dwg", new DateTime(2026, 7, 1))
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("P-0602.1-000-EL-DR-0001-02.dwg", resultado.Arquivo);
        }

        [Fact]
        public void DuasEntradasMesmoItem_ReviewsDiferentes_NuncaEhAmbiguidade_EscolheMaisRecente()
        {
            // Mesmo item físico (ItemId igual) aparecendo em dois Reviews HISTÓRICOS DIFERENTES — isso é
            // o caso normal (documento revisado mais de uma vez), não ambiguidade: o Review mais recente
            // descarta o antigo antes de qualquer comparação de decisão.
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-dwg", new DateTime(2026, 7, 1, 14, 16, 36), status: null, reviewId: "review-antigo"),
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-dwg", new DateTime(2026, 7, 1, 17, 34, 13), status: "Aprovado", reviewId: "review-novo")
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("P-0602.1-000-EL-DR-0001-02.dwg", resultado.Arquivo);
        }

        [Fact]
        public void DoisReviewIdsDiferentes_EmpatadosNaDataMaisRecente_ComDecisoesDiferentes_EhAmbiguoReal()
        {
            // Dois ReviewIds DIFERENTES (documentos/candidatos diferentes) batendo no mesmo código, com a
            // MESMA data e decisões DIFERENTES — não dá para desempatar por Review mais recente nem por
            // concordância de extensões. Deve ser reportado como ambíguo REAL, nunca escolher o "primeiro".
            var mesmaData = new DateTime(2026, 7, 1, 17, 34, 13);
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-A", mesmaData, status: "Aprovado", reviewId: "review-A"),
                NovaEntrada("P-0602.1-000-EL-DR-0001-03.dwg", "urn:item-B", mesmaData, status: "Reprovado", reviewId: "review-B")
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.True(resultado.Ambiguo);
            Assert.Equal("WORKFLOW_CORRESPONDENCIA_AMBIGUA_REAL", resultado.CodigoResultado);
            Assert.Null(resultado.Arquivo);
        }

        [Fact]
        public void DoisReviewIdsDiferentes_ComDataMaisRecenteClara_NaoEhAmbiguo()
        {
            // Dois ReviewIds distintos batem no código, mas um é claramente mais recente — o antigo é
            // descartado antes de qualquer comparação; não é ambiguidade mesmo com status diferentes.
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("P-0602.1-000-EL-DR-0001-02.dwg", "urn:item-A", new DateTime(2026, 1, 1), status: "CHECKER", reviewId: "review-antigo"),
                NovaEntrada("P-0602.1-000-EL-DR-0001-03.dwg", "urn:item-B", new DateTime(2026, 7, 1), status: "Aprovado", reviewId: "review-novo")
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Equal("P-0602.1-000-EL-DR-0001-03.dwg", resultado.Arquivo);
        }

        [Fact]
        public void NenhumaEntradaCorresponde_RetornaNuloSemAmbiguidade()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                NovaEntrada("OUTRO-DOCUMENTO-01.dwg", "urn:item-x", new DateTime(2026, 7, 1))
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-EL-DR-0001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Null(resultado.Arquivo);
        }

        [Fact]
        public void CodigoVazio_RetornaNuloSemAmbiguidade()
        {
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>();

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("", entries);

            Assert.False(resultado.Ambiguo);
            Assert.Null(resultado.Arquivo);
        }
    }
}
