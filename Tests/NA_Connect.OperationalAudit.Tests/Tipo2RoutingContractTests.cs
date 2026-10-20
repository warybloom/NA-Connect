using System;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção arquitetural de 16/09/2026: CHS001/Tipo2 pulava integralmente as etapas 0-17 por causa de
    /// um `continue` logo após o roteamento para LdpProcessingRouterService. Prova, contra o TEXTO-FONTE
    /// REAL de Services\GrdAutomationWorker.cs (mesmo padrão de GrdAutomationWorkerAuditHookContractTests),
    /// as garantias estruturais que um teste unitário isolado não alcançaria sem mocks pesados: o router é
    /// chamado dentro do ramo Tipo2, nenhum `continue` interrompe o fluxo depois dele, a etapa 9 (Cronograma
    /// legado Tipo1) nunca aparece nesse ramo, e as etapas comuns ficam fisicamente fora dos dois ramos.
    /// </summary>
    public class Tipo2RoutingContractTests
    {
        private static string LerGrdAutomationWorker() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        private static string ExtrairBlocoAPartirDaChaveAbre(string codigoFonte, int chaveAbre)
        {
            int profundidade = 0;
            int i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}')
                {
                    profundidade--;
                    if (profundidade == 0) break;
                }
            }
            Assert.True(profundidade == 0, "Chave de fechamento do bloco não encontrada (contagem desbalanceada).");
            return codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);
        }

        private static (string Ramo, int FimDoBloco) ExtrairRamoTipo2(string codigoFonte)
        {
            int marcador = codigoFonte.IndexOf("bool ehTipo2 =", StringComparison.Ordinal);
            Assert.True(marcador >= 0, "Declaração 'bool ehTipo2 = ...' não encontrada — o roteamento pode ter sido reescrito.");

            int ifTipo2 = codigoFonte.IndexOf("if (ehTipo2)", marcador, StringComparison.Ordinal);
            Assert.True(ifTipo2 >= 0, "'if (ehTipo2)' não encontrado logo após a declaração — roteamento pode ter mudado de forma.");

            int chaveAbre = codigoFonte.IndexOf('{', ifTipo2);
            Assert.True(chaveAbre >= 0);
            string ramo = ExtrairBlocoAPartirDaChaveAbre(codigoFonte, chaveAbre);
            return (ramo, chaveAbre + ramo.Length);
        }

        [Fact]
        public void RouterEhChamadoDentroDoRamoTipo2()
        {
            var (ramoTipo2, _) = ExtrairRamoTipo2(LerGrdAutomationWorker());
            Assert.Contains("ldpProcessingRouter.ProcessarAsync(", ramoTipo2, StringComparison.Ordinal);
        }

        [Fact]
        public void NenhumContinueApareceNoRamoTipo2_FluxoContinuaParaAsEtapasComuns()
        {
            var (ramoTipo2, _) = ExtrairRamoTipo2(LerGrdAutomationWorker());

            // Regex de "continue;" isolado (nunca "// ... continue ..." em comentário nem parte de outro
            // identificador) — o bug original era exatamente um `continue;` logo após a chamada do router.
            var temContinue = System.Text.RegularExpressions.Regex.IsMatch(ramoTipo2, @"(?<![\w.])continue\s*;");
            Assert.False(temContinue, "Encontrado 'continue;' dentro do ramo Tipo2 — reintroduziria o bug que pulava as etapas comuns.");
        }

        [Fact]
        public void Etapa9CronogramaLegadoTipo1NuncaApareceNoRamoTipo2()
        {
            var (ramoTipo2, _) = ExtrairRamoTipo2(LerGrdAutomationWorker());
            Assert.DoesNotContain("scheduleService.SyncScheduleToLdpAsync(", ramoTipo2, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ldpToCustoService.ExecuteFullSyncProcessAsync(")]
        [InlineData("ldpUpdateService.MirrorLdpControlToProjectAsync(")]
        [InlineData("dbService.SalvarDadosLdpUnificada(")]
        public void EtapasTipo1OnlyNuncaAparecemNoRamoTipo2(string chamadaTipo1Only)
        {
            var (ramoTipo2, _) = ExtrairRamoTipo2(LerGrdAutomationWorker());
            Assert.DoesNotContain(chamadaTipo1Only, ramoTipo2, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("grdSyncService.SyncGrdsToLdpAsync(")]
        [InlineData("planningSyncService.SincronizarWeeklyScheduleAsync(")]
        [InlineData("qualitySyncService.SincronizarArquivoQualidadeAsync(")]
        public void EtapasComunsFicamForaDoRamoTipo2_RodamDepoisDoRoteamentoParaOsDoisTipos(string chamadaComum)
        {
            var codigoFonte = LerGrdAutomationWorker();
            var (ramoTipo2, fimDoBlocoDeRoteamento) = ExtrairRamoTipo2(codigoFonte);

            // Etapas comuns não podem estar DENTRO do ramo Tipo2 (senão duplicariam a execução ao
            // reaproveitar o padrão antigo) — precisam estar DEPOIS do bloco if/else de roteamento, para
            // rodar exatamente uma vez, para qualquer Tipo_LDP.
            Assert.DoesNotContain(chamadaComum, ramoTipo2, StringComparison.Ordinal);

            int posicaoDaChamada = codigoFonte.IndexOf(chamadaComum, StringComparison.Ordinal);
            Assert.True(posicaoDaChamada >= 0, $"Chamada '{chamadaComum}' não encontrada em GrdAutomationWorker.cs.");
            Assert.True(posicaoDaChamada > fimDoBlocoDeRoteamento,
                $"'{chamadaComum}' aparece antes do fim do bloco de roteamento Tipo1/Tipo2 — etapa comum precisa rodar depois, não dentro de um dos ramos.");
        }
    }
}
