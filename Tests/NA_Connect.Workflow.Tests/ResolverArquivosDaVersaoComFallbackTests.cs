using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Regressão da causa raiz REAL do WORKFLOW_MAPA_VAZIO_INESPERADO em BML011.013/BML011.022 — o run real
    /// provou que a hipótese anterior (sufixo de revisão / PREFIXO_TOKENS) estava incompleta:
    /// PorPrefixoDeTokens=0 mesmo com a correção aplicada.
    ///
    /// Causa raiz comprovada por git history (<c>git show 4797da5:Services/AccReviewWorkflowService.cs</c>):
    /// a implementação que funcionava nunca teve um gate físico contra a árvore de "10 - PRODUCTION" — o
    /// nome de CADA versão do Review era usado diretamente. A árvore só reflete o snapshot ATUAL (última
    /// revisão viva de cada documento); a maioria das versões de Reviews antigos nunca terá um arquivo
    /// fisicamente idêntico nela. A segurança real de "mesmo documento lógico" sempre foi aplicada DEPOIS,
    /// em <see cref="LdpUpdateService.EncontrarCorrespondenciaControlada"/>, contra o N&amp;A Document
    /// Number/Client Document Number de uma linha real da LDP — já coberto por
    /// <c>CorrespondenciaLdpTests</c>.
    ///
    /// Estes testes combinam as DUAS metades do fluxo corrigido — <c>ResolverArquivosDaVersaoComFallback</c>
    /// (nunca descarta por falta de match físico) seguido de <c>EncontrarCorrespondenciaControlada</c>
    /// (única fonte real de rejeição/aceitação) — usando os NOMES REAIS de arquivo relatados no bug real de
    /// BML011.013 (P-0602.1-000-IA-DR-0001-02.pdf etc.). O valor de N&amp;A/Client Document Number da LDP
    /// não foi lido ao vivo (sem credenciais reais disponíveis neste ambiente de teste) — é inferido como o
    /// prefixo natural do nome real (mesma convenção documental usada em todo o resto do código/testes
    /// deste projeto, ex. CorrespondenciaLdpTests), nunca inventado como um padrão arbitrário.
    /// </summary>
    public class ResolverArquivosDaVersaoComFallbackTests
    {
        private static (List<AccReviewWorkflowService.ArquivoProducaoAcc> Todos,
                        Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>> PorItemUrn,
                        Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>> PorNomeCompleto,
                        Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>> PorNomeBase)
            ArvoreVazia() => (
                new List<AccReviewWorkflowService.ArquivoProducaoAcc>(),
                new Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>>(),
                new Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>>(),
                new Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>>());

        // ---- Item: "localização sem evidência => inconclusiva, não fora da árvore" ---------------------

        [Fact]
        public void SemMatchNaArvoreAtual_NuncaDescarta_CriaEntradaComMetodoInconclusivo_NaoRejeicao()
        {
            var arvore = ArvoreVazia();

            var (arquivos, metodo, confirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                versionItemUrn: "urn:item:reenviado-com-lineage-novo",
                versionName: "P-0602.1-000-IA-DR-0001-02.pdf",
                versionUrnAtual: "urn:version:atual",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.False(confirmada);
            Assert.Equal("SEM_CORRESPONDENCIA_ARVORE_ATUAL", metodo);
            Assert.Single(arquivos);
            Assert.Equal("P-0602.1-000-IA-DR-0001-02.pdf", arquivos[0].FileName);
            Assert.Equal("P-0602.1-000-IA-DR-0001-02", arquivos[0].FileNameWithoutExtension);
        }

        [Fact]
        public void ComMatchNaArvoreAtual_RetornaConfirmadaTrue_MetodoDoNivelQueAcertou()
        {
            var arvore = ArvoreVazia();
            var arquivoReal = new AccReviewWorkflowService.ArquivoProducaoAcc
            {
                ItemId = "urn:item:real",
                FileName = "P-0602.1-000-IA-DR-0001-02.pdf",
                FileNameWithoutExtension = "P-0602.1-000-IA-DR-0001-02",
                FileNameNormalizado = AccReviewWorkflowService.NormalizarNomeDocumento("P-0602.1-000-IA-DR-0001-02.pdf"),
                NomeBaseNormalizado = AccReviewWorkflowService.NormalizarNomeDocumento("P-0602.1-000-IA-DR-0001-02")
            };
            arvore.Todos.Add(arquivoReal);
            arvore.PorItemUrn["urn:item:real"] = new List<AccReviewWorkflowService.ArquivoProducaoAcc> { arquivoReal };

            var (arquivos, metodo, confirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                "urn:item:real", "P-0602.1-000-IA-DR-0001-02.pdf", "urn:version:x",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.True(confirmada);
            Assert.Equal("ITEM_URN", metodo);
            Assert.Single(arquivos);
        }

        // ---- Itens: Review filename = Client Document Number (BML011.013 real) -> mapeia via fallback+LDP --

        [Fact]
        public void CasoRealBML011013_ReviewSemMatchFisico_MasCodigoBateComLdp_EMapeado()
        {
            // Nomes reais relatados no bug (Seção 3 do checkpoint de correção): a versão do Review não tem
            // correspondência física nos 41 arquivos atuais de Produção (ItemUrn/nome não batem — a mesma
            // rejeição observada no run real). O fallback cria a entrada mesmo assim; o código da LDP
            // (inferido como o prefixo natural do nome real, sem o sufixo de revisão "-02") é quem decide.
            var arvore = ArvoreVazia();

            var (arquivosFallback, metodo, confirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                versionItemUrn: "urn:item:historico-superado",
                versionName: "P-0602.1-000-IA-DR-0001-02.pdf",
                versionUrnAtual: "urn:version:historico",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.False(confirmada);

            var entradaWorkflow = new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = arquivosFallback[0].FileName,
                FileNameWithoutExtension = arquivosFallback[0].FileNameWithoutExtension,
                ItemId = arquivosFallback[0].ItemId,
                DataAtualizacaoReview = new DateTime(2026, 7, 1),
                Status = "Aprovado",
                StatusCalculado = "Aprovado",
                ReviewId = "review-bml011013"
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada(
                "P-0602.1-000-IA-DR-0001", new List<AccReviewWorkflowService.WorkflowStatusEntry> { entradaWorkflow });

            Assert.False(resultado.Ambiguo);
            Assert.Equal("P-0602.1-000-IA-DR-0001-02.pdf", resultado.Arquivo);
            Assert.Equal("CORRESPONDENCIA_LOGICA_CONFIRMADA", resultado.CodigoResultado);
        }

        [Fact]
        public void CasoRealBML011013_PdfEDocxDoMesmoDocumentoLogico_ItemUrnsDiferentes_AmbosConfirmados()
        {
            // "P-0602.1-000-IA-TS-0001-02.pdf" e "...-02.docx" — duas extensões reais relatadas no bug,
            // representando o MESMO documento lógico, cada uma com seu próprio ItemUrn (nenhum dos dois no
            // snapshot atual). O nível lógico de EncontrarCorrespondenciaControlada exige que as duas
            // concordem na mesma decisão (RepresentamAMesmaDecisaoLogica) para confirmar por múltiplas
            // extensões — aqui concordam (mesmo Review, mesmo status).
            var arvore = ArvoreVazia();
            var reviewId = "review-ts-0001";
            var data = new DateTime(2026, 7, 2);

            var (pdfArquivos, _, pdfConfirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                "urn:item:ts-pdf", "P-0602.1-000-IA-TS-0001-02.pdf", "urn:version:ts-pdf",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);
            var (docxArquivos, _, docxConfirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                "urn:item:ts-docx", "P-0602.1-000-IA-TS-0001-02.docx", "urn:version:ts-docx",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.False(pdfConfirmada);
            Assert.False(docxConfirmada);

            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry>
            {
                new() { FileName = pdfArquivos[0].FileName, FileNameWithoutExtension = pdfArquivos[0].FileNameWithoutExtension, ItemId = pdfArquivos[0].ItemId, DataAtualizacaoReview = data, Status = "Aprovado", StatusCalculado = "Aprovado", ReviewId = reviewId },
                new() { FileName = docxArquivos[0].FileName, FileNameWithoutExtension = docxArquivos[0].FileNameWithoutExtension, ItemId = docxArquivos[0].ItemId, DataAtualizacaoReview = data, Status = "Aprovado", StatusCalculado = "Aprovado", ReviewId = reviewId }
            };

            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-IA-TS-0001", entries);

            Assert.False(resultado.Ambiguo);
            Assert.NotNull(resultado.Arquivo);
            Assert.True(resultado.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes);
        }

        // ---- Item: ItemUrn diferente E documento diferente => rejeitar (nunca aceitar por acidente) ----

        [Fact]
        public void ItemUrnDiferenteEDocumentoRealmenteDiferente_NuncaMapeado()
        {
            var arvore = ArvoreVazia();

            var (arquivosFallback, _, confirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                "urn:item:outro-projeto", "DOCUMENTO-DE-OUTRO-PROJETO-QUALQUER.pdf", "urn:version:outro",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.False(confirmada); // inconclusivo na árvore — a entrada ainda é criada...

            var entrada = new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = arquivosFallback[0].FileName,
                FileNameWithoutExtension = arquivosFallback[0].FileNameWithoutExtension,
                ItemId = arquivosFallback[0].ItemId,
                DataAtualizacaoReview = DateTime.UtcNow,
                Status = "Aprovado",
                ReviewId = "review-outro"
            };

            // ...mas nunca reivindicada por um código da LDP que não corresponde — rejeição real acontece aqui.
            var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada("P-0602.1-000-IA-DR-0001", new List<AccReviewWorkflowService.WorkflowStatusEntry> { entrada });

            Assert.False(resultado.Ambiguo);
            Assert.Null(resultado.Arquivo);
            Assert.Equal("SEM_CORRESPONDENCIA", resultado.CodigoResultado);
        }

        // ---- Item: versão realmente fora (sem qualquer relação lógica) => rejeitar --------------------

        [Fact]
        public void VersaoSemQualquerRelacaoLogicaComALdp_PermaneceSemCorrespondencia()
        {
            var arvore = ArvoreVazia();

            var (arquivosFallback, _, _) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                null, "ARQUIVO-PESSOAL-FORA-DO-CONTROLE-DOCUMENTAL.xlsx", "urn:version:pessoal",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            var entrada = new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = arquivosFallback[0].FileName,
                FileNameWithoutExtension = arquivosFallback[0].FileNameWithoutExtension,
                DataAtualizacaoReview = DateTime.UtcNow,
                Status = "Aprovado",
                ReviewId = "review-x"
            };

            foreach (var codigoLdpReal in new[] { "P-0602.1-000-IA-DR-0001", "P-0602.1-310-PX-DR-0055", "P-0602.1-000-IA-TS-0001" })
            {
                var resultado = LdpUpdateService.EncontrarCorrespondenciaControlada(codigoLdpReal, new List<AccReviewWorkflowService.WorkflowStatusEntry> { entrada });
                Assert.Null(resultado.Arquivo);
            }
        }

        // ---- Item: mapa não pode terminar artificialmente vazio quando há dados reais -------------------

        [Fact]
        public void LoteComPadraoRealDoBug_VersoesAntesTodasRejeitadas_AgoraTodasViramEntradas()
        {
            // Reproduz o padrão exato relatado (Seção 3): nenhuma das 5 versões reais tinha correspondência
            // física nos 41 arquivos atuais — antes desta correção, isso resultava em 0 entradas para
            // TODAS. Agora, todas viram entradas (ConfirmadaNaArvoreAtual=false, mas presentes), preservando
            // a chance de serem reivindicadas pela LDP.
            var arvore = ArvoreVazia();
            var nomesReais = new[]
            {
                "P-0602.1-310-PX-DR-0055-02.pdf",
                "P-0602.1-000-IA-DR-0001-02.pdf",
                "P-0602.1-000-IA-TS-0001-02.pdf",
                "P-0602.1-000-IA-DR-0001-02.dwg",
                "P-0602.1-000-IA-TS-0001-02.docx"
            };

            var entradasCriadas = new List<(string Nome, bool Confirmada)>();
            foreach (var nome in nomesReais)
            {
                var (arquivos, _, confirmada) = AccReviewWorkflowService.ResolverArquivosDaVersaoComFallback(
                    null, nome, "urn:version:qualquer",
                    arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);
                Assert.Single(arquivos); // nunca vazio — nunca mais um "continue" silencioso.
                entradasCriadas.Add((nome, confirmada));
            }

            Assert.Equal(5, entradasCriadas.Count);
            Assert.All(entradasCriadas, e => Assert.False(e.Confirmada)); // nenhuma no snapshot atual — esperado e não é rejeição.
        }
    }
}
