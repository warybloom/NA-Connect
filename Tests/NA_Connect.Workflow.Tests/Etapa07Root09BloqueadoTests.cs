using System.Linq;
using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Correção 17/09/2026 (Fase B): a Etapa 07 nunca mais escreve atributos ACC em arquivos dentro de
    /// "09 - ENGINEERING DOCUMENTS" (raiz + qualquer subpasta) — decisão do usuário, não uma
    /// incompatibilidade técnica. A pasta 09 continua em LdpUpdateService.PastasAtributos (outras etapas
    /// podem lê-la); só a Etapa 07 para de tratá-la como elegível para escrita. Cobre tanto o bloqueio de
    /// CONFIGURAÇÃO (ChavesBloqueadasParaAtualizacaoDeAtributos) quanto a proteção DEFENSIVA por caminho
    /// (PastaPertenceARaizBloqueadaParaAtributos), que é a que realmente decide se um arquivo específico
    /// pode receber POST, independente de qual configuração o descobriu.
    /// </summary>
    public class Etapa07Root09BloqueadoTests
    {
        [Fact]
        public void ApenasProducaoDocumentos09EstaBloqueada_06_08_12ContinuamPermitidos()
        {
            var bloqueadas = LdpUpdateService.ChavesBloqueadasParaAtualizacaoDeAtributos;

            Assert.Single(bloqueadas);
            Assert.Contains(LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09, bloqueadas);
            Assert.DoesNotContain(LdpUpdateService.CHAVE_ENGENHARIA_04, bloqueadas);           // 06 - ENGINEERING
            Assert.DoesNotContain(LdpUpdateService.CHAVE_PRODUCAO_10, bloqueadas);              // 12 - PRODUCT
            Assert.DoesNotContain(LdpUpdateService.CHAVE_PRODUCAO_MODELOS_3D, bloqueadas);      // 08 - FEDERATED MODEL
        }

        // ---- 1/2: arquivo direto/profundo em 06 -> permitido ----

        [Fact]
        public void ArquivoDiretoEm06Engineering_EhPermitido()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos("06 - ENGINEERING/06.01 - PROCESS"));
        }

        [Fact]
        public void ArquivoProfundoEm06Engineering_EhPermitido()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(
                "06 - ENGINEERING/06.06 - PIPING/Related Files/Subpasta/Nivel3"));
        }

        // ---- 3/4: arquivo direto/profundo em 08 -> permitido ----

        [Fact]
        public void ArquivoDiretoEm08FederatedModel_EhPermitido()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos("08 - FEDERATED MODEL"));
        }

        [Fact]
        public void ArquivoProfundoEm08FederatedModel_EhPermitido()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(
                "08 - FEDERATED MODEL/08.01 - 3D AUTHORING SOFTWARE/08.01.01 - AUTODESK REVIT/Nivel3"));
        }

        // ---- 5/6: arquivo direto/profundo em 12 -> permitido ----

        [Fact]
        public void ArquivoDiretoEm12Product_EhPermitido()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos("12 - PRODUCT/12.01 - PROCESS"));
        }

        [Fact]
        public void ArquivoProfundoEm12Product_EhPermitido()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(
                "12 - PRODUCT/12.06 - PIPING/Nivel2/Nivel3"));
        }

        // ---- 7/8/9: arquivo direto/subpasta/subpasta profunda em 09 -> BLOQUEADO ----

        [Fact]
        public void ArquivoDiretoNaRaiz09_EhBloqueado()
        {
            Assert.True(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos("09 - ENGINEERING DOCUMENTS"));
        }

        [Fact]
        public void ArquivoEmSubpastaDe09_EhBloqueado()
        {
            Assert.True(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos("09 - ENGINEERING DOCUMENTS/09.06 - PIPING"));
        }

        [Fact]
        public void ArquivoEmSubpastaProfundaDe09_EhBloqueado()
        {
            // Exatamente o caso citado no pedido: 09/09.06 - PIPING/Related Files/Subpasta/arquivo.pdf
            Assert.True(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(
                "09 - ENGINEERING DOCUMENTS/09.06 - PIPING/Related Files/Subpasta"));
        }

        // ---- 10: variação de espaçamento -> ainda BLOQUEADO (normalização tolerante) ----

        [Theory]
        [InlineData("09 -   ENGINEERING DOCUMENTS/09.01 - PROCESS")]
        [InlineData("09 -  ENGINEERING DOCUMENTS")]
        [InlineData("09 - engineering documents/09.06 - piping")]
        public void VariacoesDeEspacoOuCaixaEm09_AindaSaoBloqueadas(string pasta)
        {
            Assert.True(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(pasta));
        }

        [Fact]
        public void PastaNulaOuVazia_NuncaEhConsideradaBloqueada()
        {
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(null));
            Assert.False(LdpUpdateService.PastaPertenceARaizBloqueadaParaAtributos(""));
        }

        [Fact]
        public void PastasAtributos_ContinuaContendoAConfiguracao09_NuncaRemovidaDoSistema()
        {
            // A pasta 09 continua existindo/configurada para quem mais precisar dela — só bloqueada para
            // escrita de atributos pela Etapa 07.
            Assert.Contains(LdpUpdateService.PastasAtributos, g => g.Chave == LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09);
        }
    }
}
