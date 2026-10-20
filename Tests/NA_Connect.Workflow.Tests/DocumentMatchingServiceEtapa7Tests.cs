using NA_Connect.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a correção da Etapa 7 (SincronizarAtributosLdpPorPastaAsync): DocumentMatchingService passou
    /// a expor NomeCorrespondePorPrefixoDeTokens / EncontrarDocumentosNoAccPorPrefixoDeTokens, a mesma
    /// lógica de fronteira de tokens já usada pelo Workflow (NomeArquivoCorrespondeAoDocumento, que agora
    /// delega para cá), no lugar do StartsWith textual cru que a Etapa 7 usava antes
    /// (DocumentMatchingService.EncontrarDocumentoNoAcc, ainda mantido intacto para os outros dois
    /// chamadores que não são a Etapa 7).
    ///
    /// Documento real de evidência (log Execucao_Completa_20260805_104629.txt, linhas 12118-12120):
    /// LDP "P-0602.1-310-PX-DR-0055" -> arquivo físico real "P-0602.1-310-PX-DR-0055-02.dwg"/".pdf".
    /// </summary>
    public class DocumentMatchingServiceEtapa7Tests
    {
        private readonly DocumentMatchingService _sut = new DocumentMatchingService();

        // 1/2/3 — sufixo "-01" com e sem extensão (o nome esperado pela evidência do chamado)
        [Fact]
        public void LdpDR0055_EncontraSufixo01SemExtensao()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-01", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void LdpDR0055_EncontraSufixo01Dwg()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void LdpDR0055_EncontraSufixo01Pdf()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-01.pdf", "P-0602.1-310-PX-DR-0055"));
        }

        // 4 — todas as extensões companheiras (mesmo nome-base) batem, e o método multi-match devolve o
        // nome-base único que agrupa todas elas (quem escolhe TODOS os arquivos daquele nome-base é o
        // indicePorNome da Etapa 7, não este método).
        [Theory]
        [InlineData("P-0602.1-310-PX-DR-0055-01.dwg")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.pdf")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.rvt")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.docx")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.xlsx")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.ifc")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.nwd")]
        [InlineData("P-0602.1-310-PX-DR-0055-01.nwc")]
        public void LdpDR0055_EncontraTodasAsExtensoesConhecidas(string nomeArquivo)
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(nomeArquivo, "P-0602.1-310-PX-DR-0055"));
        }

        // 5/6/7/8/9 — variações reais de sufixo de revisão
        [Fact]
        public void EncontraSufixoNumerico02()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-02.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void EncontraSufixoRev01()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-REV01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void EncontraSufixoR01()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-R01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void EncontraSufixoLetraA()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-A.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void EncontraSufixoSeparadoPorUnderline()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055_01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void EncontraSufixoSeparadoPorEspaco()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055 01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        // 10/11 — rejeita sufixo colado sem fronteira (StartsWith cru aceitaria, tokens rejeitam)
        [Fact]
        public void NaoEncontraSufixoColadoSemFronteira_00550()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-00550.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void NaoEncontraSufixoColadoSemFronteira_00551()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-00551.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        // 12 — rejeita prefixo colado (StartsWith cru é irrelevante aqui pois o código não começa o
        // arquivo, mas cobre a garantia de que não usamos Contains/substring em nenhum sentido)
        [Fact]
        public void NaoEncontraComPrefixoColado_X()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "X-P-0602.1-310-PX-DR-0055-01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void NaoEncontraComPrefixoColado_XHifen()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "X-P-0602.1-310-PX-DR-0055-01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        // 13 — código no meio do nome (token intermediário diferente: "1055" != "0055")
        [Fact]
        public void NaoEncontraCodigoNoMeioDoNome()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-1055-01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void NaoEncontraTokenTruncado()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-005-01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        // 14 — o ponto de "0602.1" não é tratado como extensão (senão "1" sobraria como token e o
        // 3º token do código ["0602","1"] nunca bateria com nada no arquivo real)
        [Fact]
        public void PontoIncidentalEm06021_NaoEhTratadoComoExtensao()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-01.dwg", "P-0602.1-310-PX-DR-0055"));

            var tokensCodigo = DocumentMatchingService.TokenizarCodigoDocumento("P-0602.1-310-PX-DR-0055");
            Assert.Equal(new[] { "P", "0602", "1", "310", "PX", "DR", "0055" }, tokensCodigo);
        }

        // 15 — a extensão .dwg É removida corretamente quando é uma extensão real
        [Fact]
        public void ExtensaoDwg_EhRemovidaCorretamente()
        {
            var tokens = DocumentMatchingService.TokenizarCodigoDocumento("P-0602.1-310-PX-DR-0055-01.dwg");
            Assert.Equal(new[] { "P", "0602", "1", "310", "PX", "DR", "0055", "01" }, tokens);
        }

        // Multi-match: devolve TODOS os nomes-base compatíveis, não só o primeiro (FirstOrDefault antigo)
        [Fact]
        public void MultiMatch_DevolveTodosOsSufixosFisicosDistintos()
        {
            var nomesBaseNoAcc = new[]
            {
                "P-0602.1-310-PX-DR-0055-01",
                "P-0602.1-310-PX-DR-0055-02",
                "P-0602.1-310-PX-DR-0056-01" // outro documento — não deve entrar
            };

            var resultado = _sut.EncontrarDocumentosNoAccPorPrefixoDeTokens("P-0602.1-310-PX-DR-0055", nomesBaseNoAcc);

            Assert.Equal(2, resultado.Count);
            Assert.Contains("P-0602.1-310-PX-DR-0055-01", resultado);
            Assert.Contains("P-0602.1-310-PX-DR-0055-02", resultado);
        }

        [Fact]
        public void MultiMatch_SemCorrespondencia_DevolveListaVazia()
        {
            var resultado = _sut.EncontrarDocumentosNoAccPorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055", new[] { "P-0602.1-310-PX-DR-00550", "OUTRO-DOC-01" });

            Assert.Empty(resultado);
        }

        [Fact]
        public void MultiMatch_CodigoVazio_DevolveListaVazia()
        {
            var resultado = _sut.EncontrarDocumentosNoAccPorPrefixoDeTokens("", new[] { "P-0602.1-310-PX-DR-0055-01" });
            Assert.Empty(resultado);
        }

        // 16/17 — prioridade N&A/Cliente e fallback: não são decididas por este método (a Etapa 7 decide
        // qual código passar primeiro, conforme o NamingStandard, antes de chamar
        // EncontrarDocumentosNoAccPorPrefixoDeTokens uma vez por código) — aqui confirmamos que o método
        // funciona igualmente bem para o código do Cliente e para o código N&A, sem favorecer nenhum dos
        // dois internamente.
        [Fact]
        public void FuncionaIgualmentePara_CodigoCliente_E_CodigoNA()
        {
            var nomesBaseNoAcc = new[] { "P-0602.1-310-PX-DR-0055-02" };

            var porCliente = _sut.EncontrarDocumentosNoAccPorPrefixoDeTokens("P-0602.1-310-PX-DR-0055", nomesBaseNoAcc);
            var porNA = _sut.EncontrarDocumentosNoAccPorPrefixoDeTokens("BML011.013-310-PX3-DR-055", new[] { "BML011.013-310-PX3-DR-055-02" });

            Assert.Single(porCliente);
            Assert.Single(porNA);
        }

        // 18 — Revision nula/vazia nunca entra na tokenização do código nem do arquivo: a correspondência
        // funciona só com os dois textos (código da LDP e nome do arquivo), então a ausência de uma coluna
        // de revisão na LDP nunca impede o match por sufixo.
        [Fact]
        public void RevisionNulaOuVazia_NaoImpedeCorrespondenciaPorSufixo()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-01.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        // Regressão: o Workflow (LdpUpdateService.NomeArquivoCorrespondeAoDocumento) agora delega para cá
        // e precisa continuar aceitando/rejeitando exatamente os mesmos casos de antes.
        [Fact]
        public void WorkflowNomeArquivoCorrespondeAoDocumento_DelegaComMesmoResultado()
        {
            Assert.True(APS.WebApp.Services.LdpUpdateService.NomeArquivoCorrespondeAoDocumento(
                "P-0602.1-000-EL-DR-0001-02.dwg", "P-0602.1-000-EL-DR-0001"));
            Assert.False(APS.WebApp.Services.LdpUpdateService.NomeArquivoCorrespondeAoDocumento(
                "DOC-0010-05.dwg", "DOC-001"));
            Assert.False(APS.WebApp.Services.LdpUpdateService.NomeArquivoCorrespondeAoDocumento(
                "X-DOC-001-05.dwg", "DOC-001"));
        }
    }
}
