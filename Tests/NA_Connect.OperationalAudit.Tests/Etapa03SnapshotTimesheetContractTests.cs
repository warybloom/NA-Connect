using System;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 17/09/2026 — nova ETAPA 03 (snapshot do Timesheet para Gestão à Vista/PMO). Prova, contra
    /// o TEXTO-FONTE REAL (mesmo padrão de Tipo2RoutingContractTests/Etapa01TimesheetUniversalContractTests),
    /// a REGRA ARQUITETURAL DEFINITIVA (revisão 17/09/2026): a Etapa 03 é ADITIVA, nunca substitui o
    /// fluxo existente. Etapa 03 só é chamada quando a Etapa 01 teve sucesso; ela roda ANTES de Gestão à
    /// Vista; Gestão à Vista CONTINUA chamando PersistirTimesheetAsync normalmente (ledger histórico,
    /// gestao.TimesheetLancamento, nunca desligado); Etapa 03/seu repositório nunca tocam
    /// TimesheetLancamento; e os dois consumidores de PMO (HH Real / itemização) leem exclusivamente o
    /// snapshot atual (gestao.TimesheetSnapshotAtual) para o KPI — sem que isso signifique parar de
    /// alimentar o ledger.
    /// </summary>
    public class Etapa03SnapshotTimesheetContractTests
    {
        private static string LerGrdAutomationWorker() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        private static string LerLdpGestaoVistaSyncService() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GestaoVista", "LdpGestaoVistaSyncService.cs");

        private static string LerGestaoVistaRepository() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GestaoVista", "GestaoVistaRepository.cs");

        private static string LerEtapa03Service() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GestaoVista", "Etapa03TimesheetSnapshotService.cs");

        private static string LerGestaoVistaTimesheetSnapshotRepository() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GestaoVista", "GestaoVistaTimesheetSnapshotRepository.cs");

        private static string LerPmoActualFactsProvider() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "Planning", "PmoActualFactsProvider.cs");

        private static string LerPmoExecutiveData() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "Reports", "PmoExecutiveData.cs");

        /// <summary>Remove comentários de linha C# ("// ...") E comentários SQL ("-- ...", já que as
        /// duas queries aqui são strings SQL brutas embutidas em C#) — os próprios comentários explicam
        /// a troca de tabela citando o nome antigo; sem isso o teste dispara falso positivo contra seu
        /// próprio texto explicativo, não contra a query SQL real.</summary>
        private static string SemComentariosDeLinha(string codigoFonte) =>
            Regex.Replace(Regex.Replace(codigoFonte, @"//[^\r\n]*", string.Empty), @"--[^\r\n]*", string.Empty);

        [Fact]
        public void Etapa03_SoExecutaDentroDoRamoDeSucessoDaEtapa01()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoGuarda = codigoFonte.IndexOf("if (!etapa01Sucesso)", StringComparison.Ordinal);
            Assert.True(posicaoGuarda >= 0, "Guarda 'if (!etapa01Sucesso)' não encontrada.");

            int posicaoNaoExecutada = codigoFonte.IndexOf("[ETAPA 03][FIM] Resultado=NAO_EXECUTADA Motivo=DEPENDENCIA_ETAPA_01", StringComparison.Ordinal);
            Assert.True(posicaoNaoExecutada >= 0, "Log NAO_EXECUTADA por dependência da Etapa 01 não encontrado.");

            int posicaoChamada = codigoFonte.IndexOf("etapa03SnapshotService.ExecutarAsync(", StringComparison.Ordinal);
            Assert.True(posicaoChamada >= 0, "Chamada a Etapa03TimesheetSnapshotService.ExecutarAsync não encontrada.");

            // A chamada real do serviço precisa estar DEPOIS do log de dependência (ou seja, no ramo
            // 'else' da guarda, nunca antes/fora dela).
            Assert.True(posicaoNaoExecutada < posicaoChamada,
                "A chamada a ExecutarAsync deve estar no ramo 'else' da guarda de dependência da Etapa 01, depois do log NAO_EXECUTADA.");

            // Só uma chamada em todo o arquivo — nunca uma segunda execução.
            int ocorrencias = 0, pos = 0;
            while ((pos = codigoFonte.IndexOf("etapa03SnapshotService.ExecutarAsync(", pos, StringComparison.Ordinal)) >= 0)
            {
                ocorrencias++;
                pos += 1;
            }
            Assert.Equal(1, ocorrencias);
        }

        [Fact]
        public void Etapa03_ExecutaAntesDeGestaoAVista_NuncaDepois()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoEtapa03 = codigoFonte.IndexOf("etapa03SnapshotService.ExecutarAsync(", StringComparison.Ordinal);
            int posicaoGestaoVista = codigoFonte.IndexOf("gestaoVistaSyncService.SincronizarAsync(", StringComparison.Ordinal);

            Assert.True(posicaoEtapa03 >= 0);
            Assert.True(posicaoGestaoVista >= 0);
            Assert.True(posicaoEtapa03 < posicaoGestaoVista,
                "Etapa 03 (snapshot do Timesheet) precisa rodar ANTES de Gestão à Vista consumir o snapshot.");
        }

        [Fact]
        public void GestaoAVista_ContinuaChamandoPersistirTimesheetAsync_FluxoExistentePreservado()
        {
            // Regra arquitetural definitiva: a Etapa 03 é ADITIVA — PersistirTimesheetAsync (ledger
            // histórico) precisa continuar sendo chamado normalmente dentro de SincronizarAsync, exatamente
            // como antes da Etapa 03 existir.
            var codigoFonte = LerLdpGestaoVistaSyncService();

            int inicioMetodoPublico = codigoFonte.IndexOf("public async Task<GestaoVistaSyncResultado> SincronizarAsync(", StringComparison.Ordinal);
            Assert.True(inicioMetodoPublico >= 0);

            int chaveAbre = codigoFonte.IndexOf('{', inicioMetodoPublico);
            int profundidade = 0, i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}') { profundidade--; if (profundidade == 0) break; }
            }
            string corpoSincronizarAsync = codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);

            Assert.Contains("await PersistirTimesheetAsync(", corpoSincronizarAsync, StringComparison.Ordinal);
            Assert.Contains("private async Task PersistirTimesheetAsync(", codigoFonte, StringComparison.Ordinal);
        }

        [Fact]
        public void GestaoVistaRepository_ContinuaGravandoTimesheetLancamento_LedgerHistoricoIntacto()
        {
            // gestao.TimesheetLancamento continua recebendo histórico — o repositório original (INSERT
            // idempotente por RowHash) nunca foi alterado por esta correção.
            var codigoFonte = LerGestaoVistaRepository();
            Assert.Contains("INSERT INTO gestao.TimesheetLancamento", codigoFonte, StringComparison.Ordinal);
            Assert.Contains("public async Task<bool> PersistirTimesheetAsync(", codigoFonte, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa03_NuncaAlteraNemApagaTimesheetLancamento()
        {
            // A Etapa 03 e seu repositório (TimesheetSnapshotAtual) nunca podem tocar
            // gestao.TimesheetLancamento — são caminhos de escrita completamente independentes.
            Assert.DoesNotContain("TimesheetLancamento", LerEtapa03Service(), StringComparison.Ordinal);
            Assert.DoesNotContain("TimesheetLancamento", LerGestaoVistaTimesheetSnapshotRepository(), StringComparison.Ordinal);
        }

        [Fact]
        public void PmoActualFactsProvider_LeSomenteSnapshotAtual_NuncaOLedgerHistorico()
        {
            var codigoFonte = SemComentariosDeLinha(LerPmoActualFactsProvider());
            // 30/09/2026: lê a view CORRIGIDA gestao.VW_TimeSheet_Corrigido_Espelho, que é gestao.TimesheetSnapshotAtual + De/Para
            // (continua sendo o snapshot atual — nunca o ledger histórico).
            Assert.Contains("gestao.VW_TimeSheet_Corrigido_Espelho", codigoFonte, StringComparison.Ordinal);
            Assert.DoesNotContain("gestao.TimesheetLancamento", codigoFonte, StringComparison.Ordinal);
        }

        [Fact]
        public void PmoExecutiveData_LeSomenteSnapshotAtual_NuncaOLedgerHistorico()
        {
            var codigoFonte = SemComentariosDeLinha(LerPmoExecutiveData());
            Assert.Contains("FROM gestao.VW_TimeSheet_Corrigido_Espelho", codigoFonte, StringComparison.Ordinal);
            Assert.DoesNotContain("gestao.TimesheetLancamento", codigoFonte, StringComparison.Ordinal);
        }
    }
}
