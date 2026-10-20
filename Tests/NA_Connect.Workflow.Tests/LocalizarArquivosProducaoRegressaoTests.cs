using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Testes de regressão do bug real WORKFLOW_MAPA_VAZIO_INESPERADO (projetos BML011.013/BML011.022):
    /// os três níveis originais de <c>AccReviewWorkflowService.LocalizarArquivosProducao</c> (ItemUrn / nome
    /// completo / nome-base) exigiam igualdade EXATA após normalização, descartando como "fora da árvore
    /// permitida" documentos legítimos cujo nome na versão do Review e nome atual na Produção diferem só
    /// por sufixo de revisão. A correção adiciona um 4º nível — prefixo de tokens, reaproveitando
    /// <see cref="NA_Connect.Services.DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens"/> — sem
    /// nunca aceitar nada que não esteja no conjunto já restrito à árvore real de Produção.
    ///
    /// Testa <c>LocalizarArquivosProducao</c> diretamente (internal static, mesmo padrão de
    /// <c>IdentificadoresVersaoTests</c>) — nível certo para esta correção: a função é pura, sem HTTP, e é
    /// exatamente onde o bug e a correção vivem.
    /// </summary>
    public class LocalizarArquivosProducaoRegressaoTests
    {
        private static AccReviewWorkflowService.ArquivoProducaoAcc NovoArquivo(string itemId, string fileName, string pasta = "10 - PRODUCTION")
        {
            return new AccReviewWorkflowService.ArquivoProducaoAcc
            {
                ItemId = itemId,
                VersionUrn = itemId + "?version=1",
                FileName = fileName,
                FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName),
                Extension = System.IO.Path.GetExtension(fileName),
                FileNameNormalizado = AccReviewWorkflowService.NormalizarNomeDocumento(fileName),
                NomeBaseNormalizado = AccReviewWorkflowService.NormalizarNomeDocumento(System.IO.Path.GetFileNameWithoutExtension(fileName)),
                PastaEncontrada = pasta
            };
        }

        private static (List<AccReviewWorkflowService.ArquivoProducaoAcc> Todos,
                        Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>> PorItemUrn,
                        Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>> PorNomeCompleto,
                        Dictionary<string, List<AccReviewWorkflowService.ArquivoProducaoAcc>> PorNomeBase)
            MontarArvore(params AccReviewWorkflowService.ArquivoProducaoAcc[] arquivos)
        {
            var todos = arquivos.ToList();

            var porItemUrn = todos
                .Where(a => !string.IsNullOrEmpty(a.ItemId))
                .GroupBy(a => a.ItemId, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), System.StringComparer.OrdinalIgnoreCase);

            var porNomeCompleto = todos
                .Where(a => !string.IsNullOrEmpty(a.FileNameNormalizado))
                .GroupBy(a => a.FileNameNormalizado, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), System.StringComparer.OrdinalIgnoreCase);

            var porNomeBase = todos
                .GroupBy(a => a.NomeBaseNormalizado, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), System.StringComparer.OrdinalIgnoreCase);

            return (todos, porItemUrn, porNomeCompleto, porNomeBase);
        }

        // ---- Caso 1: documento válido na Produção + Review da versão -> deve entrar no mapa -----------

        [Fact]
        public void Caso1_DocumentoValidoComNomeIdentico_EntraPorNomeCompleto()
        {
            var arvore = MontarArvore(NovoArquivo("urn:item:1", "P-0602.1-310-PX-DR-0055.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: null, versionName: "P-0602.1-310-PX-DR-0055.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Single(arquivos);
            Assert.Equal("NOME_COMPLETO", metodo);
        }

        // ---- Caso 2: Review referencia versão válida por ItemUrn -> deve entrar -----------------------

        [Fact]
        public void Caso2_VersaoComItemUrnConhecido_EntraPorItemUrn_MesmoComNomeDiferente()
        {
            var arvore = MontarArvore(NovoArquivo("urn:item:2", "P-0602.1-310-PX-DR-0099-REV_ATUAL.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: "urn:item:2", versionName: "nome-completamente-diferente-na-versao-do-review.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Single(arquivos);
            Assert.Equal("ITEM_URN", metodo);
        }

        // ---- Caso 3: PDF + DWG do mesmo documento lógico -> nenhum descartado só pela extensão --------

        [Fact]
        public void Caso3_PdfEDwgDoMesmoDocumento_AmbosEncontradosIndependentemente()
        {
            var arvore = MontarArvore(
                NovoArquivo("urn:item:pdf", "P-0602.1-000-PM-LI-0001.pdf"),
                NovoArquivo("urn:item:dwg", "P-0602.1-000-PM-LI-0001.dwg"));

            var (arquivosPdf, metodoPdf) = AccReviewWorkflowService.LocalizarArquivosProducao(
                null, "P-0602.1-000-PM-LI-0001.pdf", arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);
            var (arquivosDwg, metodoDwg) = AccReviewWorkflowService.LocalizarArquivosProducao(
                null, "P-0602.1-000-PM-LI-0001.dwg", arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Single(arquivosPdf);
            Assert.Equal(".pdf", arquivosPdf[0].Extension);
            Assert.Single(arquivosDwg);
            Assert.Equal(".dwg", arquivosDwg[0].Extension);
            Assert.Equal("NOME_COMPLETO", metodoPdf);
            Assert.Equal("NOME_COMPLETO", metodoDwg);
        }

        // ---- Caso 4: arquivo realmente fora da árvore -> continua rejeitado ---------------------------

        [Fact]
        public void Caso4_ArquivoRealmenteForaDaArvore_ContinuaRejeitado()
        {
            var arvore = MontarArvore(NovoArquivo("urn:item:4", "P-0602.1-310-PX-DR-0055.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: "urn:item:NAO_EXISTE_NA_ARVORE", versionName: "DOCUMENTO-COMPLETAMENTE-DIFERENTE-9999.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Empty(arquivos);
            Assert.Equal("NENHUM", metodo);
        }

        // ---- Caso 5: arquivo fora da árvore com MESMO NOME -> não pode entrar só pelo nome ------------

        [Fact]
        public void Caso5_NomeIgualMasArquivoNaoFaltNaArvoreCrawleada_NaoEntra()
        {
            // Simula um arquivo com o MESMO nome de um documento real, mas que nunca foi crawleado como
            // parte da árvore de Produção (nunca aparece em nenhum dos três dicionários/lista) — o "nome
            // igual" sozinho nunca pode ser fonte de verdade; só o que veio de dentro da árvore conta.
            var arvore = MontarArvore(NovoArquivo("urn:item:5-real", "OUTRO-DOCUMENTO-QUALQUER.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: null, versionName: "DOCUMENTO-QUE-NAO-ESTA-NA-ARVORE.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Empty(arquivos);
            Assert.Equal("NENHUM", metodo);
        }

        // ---- Correção em si: sufixo de revisão diferente entre Review e Produção ----------------------

        [Fact]
        public void Correcao_ReviewReferenciaVersaoAntiga_ProducaoJaAvancouParaSufixoNovo_EntraPorPrefixoDeTokens()
        {
            // Bug real: Produção já tem "...-0055-02.dwg" (revisão mais nova), mas a versão do Review
            // ainda referencia o nome "...-0055.dwg" (sem sufixo) — os 3 níveis exatos falhavam todos.
            var arvore = MontarArvore(NovoArquivo("urn:item:novo", "P-0602.1-310-PX-DR-0055-02.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: "urn:item:DIFERENTE_PORQUE_FOI_REENVIADO", versionName: "P-0602.1-310-PX-DR-0055.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Single(arquivos);
            Assert.Equal("PREFIXO_TOKENS", metodo);
        }

        [Fact]
        public void Correcao_ReviewReferenciaSufixoNovo_ProducaoAindaTemNomeSemSufixo_EntraPorPrefixoDeTokens()
        {
            // Direção oposta: a versão citada pelo Review já tem o sufixo mais novo, mas o item na
            // Produção (por algum motivo — cache, latência) ainda está com o nome-base sem sufixo.
            var arvore = MontarArvore(NovoArquivo("urn:item:base", "P-0602.1-310-PX-DR-0088.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: null, versionName: "P-0602.1-310-PX-DR-0088-01.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Single(arquivos);
            Assert.Equal("PREFIXO_TOKENS", metodo);
        }

        [Fact]
        public void Correcao_NuncaAceitaPorPrefixoQuandoDocumentoBaseEDiferente_ApesarDeSufixoBater()
        {
            // Nunca "enfraquece": "P-0602.1-310-PX-DR-0099" não é prefixo de tokens de
            // "X-P-0602.1-310-PX-DR-0055-01" nem o contrário — bases de documento diferentes continuam
            // rejeitadas mesmo com sufixos parecidos.
            var arvore = MontarArvore(NovoArquivo("urn:item:x", "X-P-0602.1-310-PX-DR-0055-01.dwg"));

            var (arquivos, metodo) = AccReviewWorkflowService.LocalizarArquivosProducao(
                versionItemUrn: null, versionName: "P-0602.1-310-PX-DR-0099.dwg",
                arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Empty(arquivos);
            Assert.Equal("NENHUM", metodo);
        }

        // ---- Caso 8 (BML011.013-like): múltiplas versões, múltiplas extensões -> não descartar todas --

        [Fact]
        public void Caso8_BML011013Like_MultiplasVersoesEExtensoes_NenhumaDescartadaPorErroDeAncestry()
        {
            var arvore = MontarArvore(
                NovoArquivo("urn:item:A-pdf", "P-0602.1-310-PX-DR-0001-03.pdf"),
                NovoArquivo("urn:item:A-dwg", "P-0602.1-310-PX-DR-0001-03.dwg"),
                NovoArquivo("urn:item:B-pdf", "P-0602.1-310-PX-DR-0002-01.pdf"));

            // Review 1: referencia a versão ANTIGA (sem sufixo) do documento 0001 — o nome-base bate para
            // PDF e DWG ao mesmo tempo (mesmo comportamento já existente no nível 3/NOME_BASE, que também
            // ignora extensão — nível 4 é consistente com ele, nunca mais restritivo nem mais permissivo
            // por conta própria): a versão do Review não é descartada, o documento 0001 inteiro (as duas
            // extensões) é encontrado dentro da árvore.
            var (arquivosA, metodoA) = AccReviewWorkflowService.LocalizarArquivosProducao(
                null, "P-0602.1-310-PX-DR-0001.pdf", arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            // Review 2: referencia por ItemUrn direto o documento 0002 — só o PDF, nunca cruza com o 0001.
            var (arquivosB, metodoB) = AccReviewWorkflowService.LocalizarArquivosProducao(
                "urn:item:B-pdf", "nome-qualquer.pdf", arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);

            Assert.Equal(2, arquivosA.Count);
            Assert.Contains(arquivosA, a => a.Extension == ".pdf");
            Assert.Contains(arquivosA, a => a.Extension == ".dwg");
            Assert.DoesNotContain(arquivosA, a => a.ItemId == "urn:item:B-pdf");
            Assert.Equal("PREFIXO_TOKENS", metodoA);

            Assert.Single(arquivosB);
            Assert.Equal("urn:item:B-pdf", arquivosB[0].ItemId);
            Assert.Equal("ITEM_URN", metodoB);
        }

        // ---- Caso 6/7 (agregado): pelo menos um válido nunca produz vazio; todos inválidos permanece vazio ----

        [Fact]
        public void Caso6_LoteComAoMenosUmaVersaoValida_NuncaResultaEmMapaVazioArtificial()
        {
            var arvore = MontarArvore(
                NovoArquivo("urn:item:valido", "P-0602.1-000-EL-DR-0010.dwg"),
                NovoArquivo("urn:item:outro", "P-0602.1-000-EL-DR-0011.dwg"));

            var versoesDoLote = new[]
            {
                ("urn:item:invalido-1", "DOCUMENTO-QUE-NAO-EXISTE-1.dwg"),
                ("urn:item:invalido-2", "DOCUMENTO-QUE-NAO-EXISTE-2.dwg"),
                ((string)null, "P-0602.1-000-EL-DR-0010.dwg") // a única realmente válida do lote.
            };

            int comCorrespondencia = 0;
            foreach (var (itemUrn, nome) in versoesDoLote)
            {
                var (arquivos, _) = AccReviewWorkflowService.LocalizarArquivosProducao(
                    itemUrn, nome, arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);
                if (arquivos.Count > 0) comCorrespondencia++;
            }

            Assert.Equal(1, comCorrespondencia);
        }

        [Fact]
        public void Caso7_LoteComTodasAsVersoesRealmenteInvalidas_TodasContinuamRejeitadas()
        {
            var arvore = MontarArvore(NovoArquivo("urn:item:unico", "P-0602.1-000-EL-DR-0010.dwg"));

            var versoesDoLote = new[]
            {
                ("urn:item:invalido-1", "DOCUMENTO-QUE-NAO-EXISTE-1.dwg"),
                ("urn:item:invalido-2", "DOCUMENTO-QUE-NAO-EXISTE-2.dwg"),
                ((string)null, "OUTRO-DOCUMENTO-QUE-NAO-EXISTE.dwg")
            };

            int comCorrespondencia = 0;
            foreach (var (itemUrn, nome) in versoesDoLote)
            {
                var (arquivos, _) = AccReviewWorkflowService.LocalizarArquivosProducao(
                    itemUrn, nome, arvore.PorItemUrn, arvore.PorNomeCompleto, arvore.PorNomeBase, arvore.Todos);
                if (arquivos.Count > 0) comCorrespondencia++;
            }

            Assert.Equal(0, comCorrespondencia);
        }
    }
}
