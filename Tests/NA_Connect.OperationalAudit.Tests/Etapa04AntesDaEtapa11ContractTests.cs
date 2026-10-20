using System;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 24/09/2026: a Etapa 11 Tipo1 (persistência SQL da LDP) rodava ANTES da Etapa 04 (GRD ->
    /// LDP), persistindo a LDP sem as colunas de GRD. Ordem exigida para Tipo1:
    ///   04 (comum) -> download da TipVersionUrn mais recente -> 11 Tipo1 -> 07.
    /// Tipo2 mantém router/persistência própria e NUNCA executa a persistência Tipo1.
    /// Prova contra o TEXTO-FONTE REAL de GrdAutomationWorker.cs (mesmo padrão de Tipo2RoutingContractTests).
    /// </summary>
    public class Etapa04AntesDaEtapa11ContractTests
    {
        private static string LerWorkerSemComentarios() =>
            Regex.Replace(RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs"), @"//[^\r\n]*", string.Empty);

        private static int ContarOcorrencias(string texto, string trecho) =>
            Regex.Matches(texto, Regex.Escape(trecho)).Count;

        [Fact]
        public void Etapa04_ExisteUmUnicoFluxoOperacional()
        {
            var fonte = LerWorkerSemComentarios();
            Assert.Equal(1, ContarOcorrencias(fonte, "grdSyncService.SyncGrdsToLdpAsync("));
            Assert.Equal(1, ContarOcorrencias(fonte, "\"[ETAPA 04][INICIO]\""));
        }

        [Fact]
        public void Etapa11Tipo1_ExisteUmaUnicaPersistencia()
        {
            var fonte = LerWorkerSemComentarios();
            Assert.Equal(1, ContarOcorrencias(fonte, "dbService.SalvarDadosLdpUnificada("));
            Assert.Equal(1, ContarOcorrencias(fonte, "ETAPA 11/17 - PERSISTÊNCIA NA LDP UNIFICADA"));
        }

        [Fact]
        public void Tipo1_Ordem_Etapa04_Download_Etapa11_Etapa07()
        {
            var fonte = LerWorkerSemComentarios();
            int etapa04 = fonte.IndexOf("grdSyncService.SyncGrdsToLdpAsync(", StringComparison.Ordinal);
            int persistencia = fonte.IndexOf("dbService.SalvarDadosLdpUnificada(", StringComparison.Ordinal);
            // "[ETAPA 11][INICIO]" também aparece em RegistrarEquivalenteTipo2Async (reconhecimento Tipo2,
            // sem persistência) — âncora no INICIO que antecede a persistência Tipo1.
            int inicio11 = persistencia > 0 ? fonte.LastIndexOf("\"[ETAPA 11][INICIO]\"", persistencia, StringComparison.Ordinal) : -1;
            int download = fonte.IndexOf("latestLdpDetails.TipVersionUrn", StringComparison.Ordinal);
            int etapa07 = fonte.IndexOf("ldpUpdateService.SyncAccAttributesFromSqlAsync(", StringComparison.Ordinal);

            Assert.True(etapa04 > 0 && inicio11 > 0 && download > 0 && persistencia > 0 && etapa07 > 0);
            Assert.True(etapa04 < inicio11, "Etapa 04 precisa ocorrer antes da Etapa 11.");
            Assert.True(inicio11 < download, "O download da LDP mais recente precisa pertencer à Etapa 11.");
            Assert.True(download < persistencia, "A persistência precisa usar a LDP baixada depois da Etapa 04.");
            Assert.True(persistencia < etapa07, "Etapa 07 consome o SQL e precisa ocorrer depois da Etapa 11.");
        }

        [Fact]
        public void Tipo2_NaoExecutaPersistenciaTipo1()
        {
            var fonte = LerWorkerSemComentarios();
            int guarda = fonte.IndexOf("if (!ehTipo2)", StringComparison.Ordinal);
            Assert.True(guarda >= 0, "Guarda 'if (!ehTipo2)' da Etapa 11 Tipo1 não encontrada.");

            int chaveAbre = fonte.IndexOf('{', guarda);
            int profundidade = 0, i = chaveAbre;
            for (; i < fonte.Length; i++)
            {
                if (fonte[i] == '{') profundidade++;
                else if (fonte[i] == '}' && --profundidade == 0) break;
            }
            string bloco = fonte.Substring(chaveAbre, i - chaveAbre + 1);

            Assert.Contains("dbService.SalvarDadosLdpUnificada(", bloco, StringComparison.Ordinal);
            Assert.Contains("latestLdpDetails.TipVersionUrn", bloco, StringComparison.Ordinal);
            Assert.DoesNotContain("grdSyncService.SyncGrdsToLdpAsync(", bloco, StringComparison.Ordinal);
        }
    }
}
