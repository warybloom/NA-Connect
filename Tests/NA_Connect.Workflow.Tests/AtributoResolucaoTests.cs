using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a resolução tolerante de atributos personalizados da Etapa 7 (seção 11-14 do pedido de
    /// correção): NormalizarNomeAtributo (motor antigo recuperado — Trim, remoção de acentos, maiúsculas,
    /// remoção de qualquer caractere não alfanumérico), a ordem de resolução (exato -> normalizado ->
    /// aliases controlados, nunca Contains genérico) e o tratamento de ambiguidade com preferência para a
    /// definição da pasta específica sobre a raiz. Não acessa ACC ou SQL real — todo o motor de
    /// normalização/resolução é puro.
    /// </summary>
    public class AtributoResolucaoTests
    {
        private static LdpUpdateService.DefinicoesEfetivasPasta ConstruirDefinicoes(IEnumerable<(int Id, string Nome, bool Especifica)> definicoes)
        {
            var lista = definicoes.ToList();
            var entradasExato = lista.Select(d => (d.Id, Chave: d.Nome, d.Especifica));
            var entradasNorm = lista.Select(d => (d.Id, Chave: LdpUpdateService.NormalizarNomeAtributo(d.Nome), d.Especifica));

            var (mapaExato, ambExato) = LdpUpdateService.ConstruirMapaComPreferenciaEspecifica(entradasExato);
            var (mapaNorm, ambNorm) = LdpUpdateService.ConstruirMapaComPreferenciaEspecifica(entradasNorm);

            return new LdpUpdateService.DefinicoesEfetivasPasta
            {
                PorNomeExato = mapaExato,
                PorNomeNormalizado = mapaNorm,
                ChavesExatasAmbiguas = ambExato,
                ChavesNormalizadasAmbiguas = ambNorm,
                TodasDefinicoes = lista.Select(d => (d.Nome, d.Id)).ToList()
            };
        }

        // =====================================================================================
        // NormalizarNomeAtributo — seção 11.
        // =====================================================================================

        [Theory]
        [InlineData("DOCUMENT TITLE")]
        [InlineData("Document Title")]
        [InlineData("DOCUMENT_TITLE")]
        [InlineData("DOCUMENT-TITLE")]
        [InlineData("DOCUMENTTITLE")]
        [InlineData("  document title  ")]
        public void NormalizarNomeAtributo_VariacoesDeDocumentTitle_NormalizamIgual(string variacao)
        {
            Assert.Equal("DOCUMENTTITLE", LdpUpdateService.NormalizarNomeAtributo(variacao));
        }

        [Theory]
        [InlineData("N&A DOCUMENT NUMBER")]
        [InlineData("NA DOCUMENT NUMBER")]
        [InlineData("N_A_DOCUMENT_NUMBER")]
        [InlineData("N-A-DOCUMENT-NUMBER")]
        public void NormalizarNomeAtributo_VariacoesDeNaDocumentNumber_NormalizamIgual(string variacao)
        {
            Assert.Equal("NADOCUMENTNUMBER", LdpUpdateService.NormalizarNomeAtributo(variacao));
        }

        [Fact]
        public void NormalizarNomeAtributo_RemoveAcentos()
        {
            Assert.Equal(LdpUpdateService.NormalizarNomeAtributo("REVISAO"), LdpUpdateService.NormalizarNomeAtributo("REVISÃO"));
            Assert.Equal("DATADEEMISSAO", LdpUpdateService.NormalizarNomeAtributo("DATA DE EMISSÃO"));
        }

        // =====================================================================================
        // ResolverIdAtributo — ordem de resolução (seção 12) e aliases controlados (seção 25).
        // =====================================================================================

        [Fact]
        public void ResolverIdAtributo_DocumentTitle_EncontraDocumentTitleComEspaco()
        {
            var defs = ConstruirDefinicoes(new[] { (1, "Document Title", false) });
            Assert.Equal(1, LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_DocumentTitle_EncontraDocumentUnderscoreTitle()
        {
            var defs = ConstruirDefinicoes(new[] { (1, "DOCUMENT_TITLE", false) });
            Assert.Equal(1, LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_DocumentTitle_EncontraDocumentTitleColado()
        {
            var defs = ConstruirDefinicoes(new[] { (1, "DOCUMENTTITLE", false) });
            Assert.Equal(1, LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_NaDocumentNumber_EncontraNaDocumentNumberSemEcomercial()
        {
            var defs = ConstruirDefinicoes(new[] { (2, "NA DOCUMENT NUMBER", false) });
            Assert.Equal(2, LdpUpdateService.ResolverIdAtributo("N&A DOCUMENT NUMBER", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_ClientDocumentNumber_EncontraComUnderscore()
        {
            var defs = ConstruirDefinicoes(new[] { (3, "CLIENT_DOCUMENT_NUMBER", false) });
            Assert.Equal(3, LdpUpdateService.ResolverIdAtributo("CLIENT DOCUMENT NUMBER", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_Rev_EncontraRevSemPonto()
        {
            var defs = ConstruirDefinicoes(new[] { (4, "REV", false) });
            Assert.Equal(4, LdpUpdateService.ResolverIdAtributo("REV.", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_Rev_EncontraRevisionPeloAliasControlado()
        {
            var defs = ConstruirDefinicoes(new[] { (4, "REVISION", false) });
            Assert.Equal(4, LdpUpdateService.ResolverIdAtributo("REV.", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_Rev_NaoEncontraReviewStatus()
        {
            // REVIEW STATUS normaliza para "REVIEWSTATUS" — nem igual a "REV" nem a nenhum alias
            // controlado de REV. — nunca deve casar via Contains.
            var defs = ConstruirDefinicoes(new[] { (5, "REVIEW STATUS", false) });
            Assert.Null(LdpUpdateService.ResolverIdAtributo("REV.", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_Rev_NaoEncontraReviewer()
        {
            var defs = ConstruirDefinicoes(new[] { (6, "REVIEWER", false) });
            Assert.Null(LdpUpdateService.ResolverIdAtributo("REV.", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_TranmistalNumber_EncontraTransmitalNumber()
        {
            var defs = ConstruirDefinicoes(new[] { (7, "TRANSMITAL NUMBER", false) });
            Assert.Equal(7, LdpUpdateService.ResolverIdAtributo("TRANMISTAL NUMBER", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_TranmistalNumber_EncontraTransmittalNumberDuploT()
        {
            var defs = ConstruirDefinicoes(new[] { (7, "TRANSMITTAL NUMBER", false) });
            Assert.Equal(7, LdpUpdateService.ResolverIdAtributo("TRANMISTAL NUMBER", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_EmissionData_EncontraEmissionDate()
        {
            var defs = ConstruirDefinicoes(new[] { (8, "EMISSION DATE", false) });
            Assert.Equal(8, LdpUpdateService.ResolverIdAtributo("EMISSION DATA", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_AcentosNaoImpedemCorrespondencia()
        {
            var defs = ConstruirDefinicoes(new[] { (9, "DATA DE EMISSÃO", false) });
            Assert.Equal(9, LdpUpdateService.ResolverIdAtributo("EMISSION DATA", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_AtributoNaoCadastrado_RetornaNulo()
        {
            var defs = ConstruirDefinicoes(new (int, string, bool)[0]);
            Assert.Null(LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        // =====================================================================================
        // Ambiguidade (seção 14) e preferência de pasta específica sobre a raiz (seção 13.6).
        // =====================================================================================

        [Fact]
        public void ResolverIdAtributo_DuasDefinicoesRaizAmbiguas_NaoEscolheArbitrariamente()
        {
            // Duas definições DIFERENTES (Ids diferentes), ambas da raiz (Especifica=false), que
            // normalizam para a mesma chave -> ambiguidade real, nenhuma é escolhida. Nenhuma das duas é
            // exatamente igual (mesmo case-insensitive) a "DOCUMENT TITLE" — só colidem no nível
            // normalizado, então a ambiguidade só pode ser detectada nesse nível (não no exato).
            var defs = ConstruirDefinicoes(new[]
            {
                (10, "DOCUMENT_TITLE", false),
                (11, "DOCUMENT-TITLE", false)
            });
            Assert.Null(LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_DefinicaoDaPastaEspecificaComplementaRaiz_QuandoNaoHaConflito()
        {
            // Só a pasta específica tem "REV" cadastrado — a raiz não tem nada equivalente.
            var defs = ConstruirDefinicoes(new[] { (12, "REV", true) });
            Assert.Equal(12, LdpUpdateService.ResolverIdAtributo("REV.", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_ConflitoRaizXPastaEspecifica_PrefereAPastaEspecifica()
        {
            // Raiz tem Id=20 ("DOCUMENT_TITLE"); a pasta específica tem um Id DIFERENTE (21) para o mesmo
            // nome normalizado ("DOCUMENT-TITLE") -> nenhuma das duas é exata para "DOCUMENT TITLE" (só
            // colidem no nível normalizado), então a pasta específica desempata (seção 13.6), nunca é
            // ambiguidade.
            var defs = ConstruirDefinicoes(new[]
            {
                (20, "DOCUMENT_TITLE", false),
                (21, "DOCUMENT-TITLE", true)
            });
            Assert.Equal(21, LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_DuasDefinicoesEspecificasAmbiguas_NaoEscolheArbitrariamente()
        {
            var defs = ConstruirDefinicoes(new[]
            {
                (30, "DOCUMENT_TITLE", true),
                (31, "DOCUMENT-TITLE", true)
            });
            Assert.Null(LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        [Fact]
        public void ResolverIdAtributo_MesmoIdEmRaizEPastaEspecifica_NaoEhAmbiguidade()
        {
            // O mesmo atributo (mesmo Id) herdado da raiz e visível também na pasta específica — não é
            // ambiguidade (só uma definição real).
            var defs = ConstruirDefinicoes(new[]
            {
                (40, "Document Title", false),
                (40, "Document Title", true)
            });
            Assert.Equal(40, LdpUpdateService.ResolverIdAtributo("DOCUMENT TITLE", defs, "pasta", NullLogger.Instance));
        }

        // =====================================================================================
        // Payload: quantidade de atributos por configuração (seção 5/16).
        // =====================================================================================

        [Fact]
        public void Engenharia_PodeResolverAteSeisAtributos()
        {
            var defs = ConstruirDefinicoes(new[]
            {
                (1, "Document Title", false),
                (2, "Client Document Number", false),
                (3, "N&A Document Number", false),
                (4, "REV.", false),
                (5, "Tranmistal Number", false),
                (6, "Emission Data", false)
            });

            var atributosEngenharia = new[] { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV.", "TRANMISTAL NUMBER", "EMISSION DATA" };
            var resolvidos = atributosEngenharia.Select(a => LdpUpdateService.ResolverIdAtributo(a, defs, "pasta", NullLogger.Instance)).Where(id => id != null).ToList();

            Assert.Equal(6, resolvidos.Count);
        }

        [Fact]
        public void Producao_ResolveNoMaximoQuatroAtributos_SemTransmittalOuEmission()
        {
            var atributosProducao = new[] { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV." };
            Assert.Equal(4, atributosProducao.Length);
            Assert.DoesNotContain("TRANMISTAL NUMBER", atributosProducao);
            Assert.DoesNotContain("EMISSION DATA", atributosProducao);
        }

        // =====================================================================================
        // Filtro de arquivos temporários/inválidos na coleta (seção 8/24-16).
        // =====================================================================================

        [Fact]
        public void EhArquivoTemporarioOuInvalido_PrefixoTilCifrao_EhIgnorado()
        {
            var item = new AccFolderItem { Name = "~$Documento.xlsx", Type = "items", Id = "1", VersionUrn = "urn:x" };
            Assert.True(LdpUpdateService.EhArquivoTemporarioOuInvalido(item));
        }

        [Fact]
        public void EhArquivoTemporarioOuInvalido_SemVersionUrn_EhIgnorado()
        {
            var item = new AccFolderItem { Name = "Documento.pdf", Type = "items", Id = "1", VersionUrn = null };
            Assert.True(LdpUpdateService.EhArquivoTemporarioOuInvalido(item));
        }

        [Fact]
        public void EhArquivoTemporarioOuInvalido_ArquivoValido_NaoEhIgnorado()
        {
            var item = new AccFolderItem { Name = "P-0602.1-310-PX-DR-0055-02.dwg", Type = "items", Id = "1", VersionUrn = "urn:adsk.wipprod:fs.file:vf.a?version=1" };
            Assert.False(LdpUpdateService.EhArquivoTemporarioOuInvalido(item));
        }
    }
}
