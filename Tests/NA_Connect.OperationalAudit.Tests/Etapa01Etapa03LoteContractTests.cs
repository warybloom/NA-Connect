using System;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 17/09/2026 ("URGENTE — EXECUTAR ETAPA 01 + ETAPA 03 EM TODOS OS PROJETOS ATIVOS"). Prova,
    /// contra o TEXTO-FONTE REAL (mesmo padrão dos demais contract tests desta pasta), que:
    /// (1) WorkerTest:ProjectId vazio + Enabled=true usa o universo normal de projetos (Is_Ativo=1), nunca
    ///     GetProjetoPorId; (2) o novo gate "apenasEtapa01Etapa03" está posicionado DEPOIS do bloco da
    ///     Etapa 03 (nunca antes, senão a Etapa 03 nunca executaria); (3) nenhuma etapa numerada
    ///     posterior (02/04/05/06/07/09/10/Gestão à Vista/0/5B/11-17) aparece entre o gate e o 'continue'
    ///     que ele dispara.
    /// </summary>
    public class Etapa01Etapa03LoteContractTests
    {
        private static string LerGrdAutomationWorker() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        [Fact]
        public void ProjectIdVazio_UsaGetProjetosAtivos_NuncaGetProjetoPorId()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoRamoVazio = codigoFonte.IndexOf(
                "if (workerTestConfig.Enabled && string.IsNullOrWhiteSpace(workerTestConfig.ProjectId))",
                StringComparison.Ordinal);
            Assert.True(posicaoRamoVazio >= 0, "Ramo 'ProjectId vazio' não encontrado.");

            int chaveAbre = codigoFonte.IndexOf('{', posicaoRamoVazio);
            int profundidade = 0, i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}') { profundidade--; if (profundidade == 0) break; }
            }
            string corpoRamoVazio = codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);

            Assert.Contains("dbService.GetProjetosAtivos()", corpoRamoVazio, StringComparison.Ordinal);
            Assert.DoesNotContain("GetProjetoPorId", corpoRamoVazio, StringComparison.Ordinal);
        }

        [Fact]
        public void ApenasEtapa01Etapa03_EstaPosicionadoDepoisDoBlocoDaEtapa03()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoBlocoEtapa03 = codigoFonte.IndexOf("etapa03SnapshotService.ExecutarAsync(", StringComparison.Ordinal);
            int posicaoGate = codigoFonte.IndexOf("bool apenasEtapa01Etapa03 =", StringComparison.Ordinal);
            int posicaoGateEtapa01 = codigoFonte.IndexOf("bool apenasEtapa01 =", StringComparison.Ordinal);

            Assert.True(posicaoBlocoEtapa03 >= 0, "Chamada a Etapa03TimesheetSnapshotService.ExecutarAsync não encontrada.");
            Assert.True(posicaoGate >= 0, "Gate 'apenasEtapa01Etapa03' não encontrado.");
            Assert.True(posicaoGateEtapa01 >= 0, "Gate 'apenasEtapa01' (isolado) não encontrado.");

            Assert.True(posicaoBlocoEtapa03 < posicaoGate,
                "apenasEtapa01Etapa03 precisa vir DEPOIS da chamada à Etapa 03, senão ela nunca executaria neste modo.");
            Assert.True(posicaoGateEtapa01 < posicaoBlocoEtapa03,
                "apenasEtapa01 (isolado) precisa continuar ANTES do bloco da Etapa 03 — comportamento original preservado.");
        }

        [Fact]
        public void ApenasEtapa01Etapa03_ExigeExatamenteAsDuasEtapas()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoGate = codigoFonte.IndexOf("bool apenasEtapa01Etapa03 =", StringComparison.Ordinal);
            Assert.True(posicaoGate >= 0);

            int fimExpressao = codigoFonte.IndexOf(';', posicaoGate);
            string expressao = codigoFonte.Substring(posicaoGate, fimExpressao - posicaoGate);

            Assert.Contains("workerTestConfig.Etapas.Count == 2", expressao, StringComparison.Ordinal);
            Assert.Contains("Contains(\"01\", StringComparer.OrdinalIgnoreCase)", expressao, StringComparison.Ordinal);
            Assert.Contains("Contains(\"03\", StringComparer.OrdinalIgnoreCase)", expressao, StringComparison.Ordinal);
        }

        [Fact]
        public void ApenasEtapa01Etapa03_TemContinueImediato_AntesDeGestaoAVista()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoGate = codigoFonte.IndexOf("if (apenasEtapa01Etapa03)", StringComparison.Ordinal);
            int posicaoGestaoVista = codigoFonte.IndexOf("gestaoVistaSyncService.SincronizarAsync(", StringComparison.Ordinal);
            Assert.True(posicaoGate >= 0);
            Assert.True(posicaoGestaoVista >= 0);
            Assert.True(posicaoGate < posicaoGestaoVista,
                "O gate apenasEtapa01Etapa03 precisa vir antes da chamada a Gestão à Vista, para poder pulá-la.");

            int chaveAbre = codigoFonte.IndexOf('{', posicaoGate);
            int profundidade = 0, i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}') { profundidade--; if (profundidade == 0) break; }
            }
            string corpoDoIf = codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);
            Assert.Contains("continue;", corpoDoIf, StringComparison.Ordinal);

            // A posição do 'continue' precisa ser ANTES de gestaoVistaSyncService.SincronizarAsync no arquivo.
            int posicaoContinue = codigoFonte.IndexOf("continue;", chaveAbre, StringComparison.Ordinal);
            Assert.True(posicaoContinue >= 0 && posicaoContinue < posicaoGestaoVista);
        }
    }
}
