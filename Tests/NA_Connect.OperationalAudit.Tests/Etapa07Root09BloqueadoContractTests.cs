using System;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 17/09/2026 (Fase B): prova, contra o TEXTO-FONTE REAL de LdpUpdateService.cs (mesmo
    /// padrão de Tipo2RoutingContractTests), que NENHUMA chamada POST de atributos da Etapa 07 pode
    /// ocorrer para um arquivo cuja pasta pertence à árvore bloqueada — a checagem defensiva
    /// (PastaPertenceARaizBloqueadaParaAtributos) precisa estar POSICIONADA ANTES da chamada real
    /// (UpdateCustomAttributesForVersionAsync) no mesmo bloco, com um `continue` que nunca deixa o fluxo
    /// alcançar o POST.
    /// </summary>
    public class Etapa07Root09BloqueadoContractTests
    {
        private static string LerLdpUpdateService() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "LdpUpdateService.cs");

        [Fact]
        public void ChecagemDefensiva_EstaPosicionadaAntesDoPostDeAtributos()
        {
            var codigoFonte = LerLdpUpdateService();

            int posicaoChecagem = codigoFonte.IndexOf("if (PastaPertenceARaizBloqueadaParaAtributos(arquivo.PastaEncontrada))", StringComparison.Ordinal);
            int posicaoPost = codigoFonte.IndexOf("await _autodeskApi.UpdateCustomAttributesForVersionAsync(", StringComparison.Ordinal);

            Assert.True(posicaoChecagem >= 0, "Checagem defensiva PastaPertenceARaizBloqueadaParaAtributos não encontrada no loop de match.");
            Assert.True(posicaoPost >= 0, "Chamada UpdateCustomAttributesForVersionAsync não encontrada.");
            Assert.True(posicaoChecagem < posicaoPost,
                "A checagem defensiva precisa vir ANTES da chamada de POST — nunca depois, senão um arquivo bloqueado poderia já ter sido enviado.");
        }

        [Fact]
        public void ChecagemDefensiva_TemContinueImediato_NuncaDeixaCairNoRestoDoLoop()
        {
            var codigoFonte = LerLdpUpdateService();

            int posicaoChecagem = codigoFonte.IndexOf("if (PastaPertenceARaizBloqueadaParaAtributos(arquivo.PastaEncontrada))", StringComparison.Ordinal);
            Assert.True(posicaoChecagem >= 0);

            // Extrai só o corpo do 'if' (balanceamento de chaves) e confirma que ele termina em 'continue;'.
            int chaveAbre = codigoFonte.IndexOf('{', posicaoChecagem);
            int profundidade = 0, i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}') { profundidade--; if (profundidade == 0) break; }
            }
            string corpoDoIf = codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);

            Assert.Contains("continue;", corpoDoIf, StringComparison.Ordinal);
        }

        [Fact]
        public void Root09_NuncaAparaceNaListaDeRootsPermitidosDoResumo()
        {
            // O bloco [ETAPA7][RESUMO] calcula "permitidos" excluindo explicitamente
            // ChavesBloqueadasParaAtualizacaoDeAtributos — nunca lista 09 como permitido.
            var codigoFonte = LerLdpUpdateService();
            Assert.Contains("ChavesBloqueadasParaAtualizacaoDeAtributos.Contains(g.Chave)", codigoFonte, StringComparison.Ordinal);
            Assert.Contains("[ETAPA7][RESUMO] RootsPermitidos=", codigoFonte, StringComparison.Ordinal);
        }

        [Fact]
        public void ConfiguracaoDaPasta09_NuncaFoiRemovidaDoSistema_SoBloqueadaParaEscrita()
        {
            // A configuração PRODUCAO_DOCUMENTOS_09 precisa continuar definida em PastasAtributos —
            // só a lista de bloqueio impede a Etapa 07 de usá-la para escrita.
            var codigoFonte = LerLdpUpdateService();
            Assert.Contains("Chave = CHAVE_PRODUCAO_DOCUMENTOS_09,", codigoFonte, StringComparison.Ordinal);
        }
    }
}
