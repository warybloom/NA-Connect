using System;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 17/09/2026: regressão funcional corrigida — a Etapa 01 (reconstrução da aba TIMESHEET a
    /// partir de tbTimeSheetRealizadoDocumento, via TimesheetDumpService.ResetAndDumpTimesheetAsync)
    /// havia sido indevidamente tratada como substituível por Gestão à Vista para Tipo2 (log
    /// "[ETAPA 01][TIPO2] Timesheet via Gestão à Vista: 0 inserido(s)."). Não existe "Etapa 01
    /// Tipo1"/"Etapa 01 Tipo2" — é UMA SÓ regra, chamada uma única vez, incondicionalmente, ANTES do
    /// roteamento Tipo1/Tipo2 e ANTES de Gestão à Vista (que lê o Timesheet já reconstruído, nunca o
    /// substitui). Prova estas garantias contra o TEXTO-FONTE REAL de GrdAutomationWorker.cs/
    /// TimesheetDumpService.cs (mesmo padrão de Tipo2RoutingContractTests/
    /// Etapa05UnicaEEtapa02DesabilitadaContractTests) — comportamental completo (SQL real, upload real)
    /// só é alcançável na execução real (validação CHS001).
    /// </summary>
    public class Etapa01TimesheetUniversalContractTests
    {
        private static string LerGrdAutomationWorker() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        private static string LerTimesheetDumpService() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "TimesheetDumpService.cs");

        /// <summary>
        /// Remove comentários de linha antes de checagens negativas — os próprios comentários deste
        /// arquivo citam "TimesheetLancamento" para explicar o que NÃO deve ser usado; sem isso o teste
        /// dispara falso positivo contra seu próprio texto explicativo, não contra código funcional.
        /// </summary>
        private static string SemComentariosDeLinha(string codigoFonte) =>
            Regex.Replace(codigoFonte, @"//[^\r\n]*", string.Empty);

        [Fact]
        public void Etapa01_ChamadaUnicaOcorreAntesDoRoteamentoTipo1Tipo2()
        {
            // Tipo1 e Tipo2 chamam a MESMA função (nenhuma variante por Tipo_LDP) — a chamada precisa
            // estar ANTES de "if (ehTipo2)", nunca dentro de um dos dois ramos.
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoChamada = codigoFonte.IndexOf("tsDumpService.ResetAndDumpTimesheetAsync(", StringComparison.Ordinal);
            Assert.True(posicaoChamada >= 0, "Chamada a ResetAndDumpTimesheetAsync não encontrada.");

            int posicaoIfTipo2 = codigoFonte.IndexOf("if (ehTipo2)", StringComparison.Ordinal);
            Assert.True(posicaoIfTipo2 >= 0, "'if (ehTipo2)' não encontrado — roteamento pode ter mudado de forma.");

            Assert.True(posicaoChamada < posicaoIfTipo2,
                "ResetAndDumpTimesheetAsync deve ser chamado ANTES do 'if (ehTipo2)' — precisa ser incondicional " +
                "(nem só Tipo1, nem só Tipo2) e terminar antes de Gestão à Vista ler o Timesheet.");

            int ocorrencias = 0, pos = 0;
            while ((pos = codigoFonte.IndexOf("tsDumpService.ResetAndDumpTimesheetAsync(", pos, StringComparison.Ordinal)) >= 0)
            {
                ocorrencias++;
                pos += 1;
            }
            Assert.Equal(1, ocorrencias);
        }

        [Fact]
        public void Etapa01_NuncaExisteBranchFuncionalPorTipoLdpEmVoltaDaChamada()
        {
            var codigoFonte = LerGrdAutomationWorker();
            int posicaoChamada = codigoFonte.IndexOf("tsDumpService.ResetAndDumpTimesheetAsync(", StringComparison.Ordinal);
            Assert.True(posicaoChamada >= 0);

            int inicioTrecho = Math.Max(0, posicaoChamada - 800);
            string trecho = codigoFonte.Substring(inicioTrecho, posicaoChamada - inicioTrecho);
            Assert.DoesNotContain("if (ehTipo2)", trecho, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!ehTipo2)", trecho, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa01_ExecutaAntesDeGestaoAVista_NuncaDepois()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoEtapa01 = codigoFonte.IndexOf("tsDumpService.ResetAndDumpTimesheetAsync(", StringComparison.Ordinal);
            int posicaoGestaoVista = codigoFonte.IndexOf("gestaoVistaSyncService.SincronizarAsync(", StringComparison.Ordinal);

            Assert.True(posicaoEtapa01 >= 0);
            Assert.True(posicaoGestaoVista >= 0);
            Assert.True(posicaoEtapa01 < posicaoGestaoVista,
                "Etapa 01 (reconstrução da Timesheet) precisa rodar ANTES de Gestão à Vista — senão ela ingeriria " +
                "a Timesheet ANTIGA, exigindo um segundo ciclo do Worker para ver os dados novos.");
        }

        [Fact]
        public void Etapa01_LdpLocalEhAtualizadaAntesDeGestaoAVistaLer()
        {
            // Entre a chamada da Etapa 01 e a leitura de Gestão à Vista, precisa existir um re-download
            // da versão mais recente da LDP para o mesmo arquivo local (ldpPath) que Gestão à Vista abre
            // — senão ela leria a cópia baixada ANTES da Etapa 01 rodar (Timesheet velha).
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoEtapa01 = codigoFonte.IndexOf("tsDumpService.ResetAndDumpTimesheetAsync(", StringComparison.Ordinal);
            int posicaoRedownload = codigoFonte.IndexOf("ldpDetailsPosEtapa01", StringComparison.Ordinal);
            int posicaoLeituraGestaoVista = codigoFonte.IndexOf("new FileStream(ldpPath, FileMode.Open", StringComparison.Ordinal);

            Assert.True(posicaoEtapa01 >= 0);
            Assert.True(posicaoRedownload >= 0, "Re-download da LDP após a Etapa 01 não encontrado (variável ldpDetailsPosEtapa01).");
            Assert.True(posicaoLeituraGestaoVista >= 0, "Leitura de Gestão à Vista (FileStream de ldpPath em modo Open) não encontrada.");

            Assert.True(posicaoEtapa01 < posicaoRedownload && posicaoRedownload < posicaoLeituraGestaoVista,
                "Ordem esperada: chamada da Etapa 01 -> re-download de ldpPath -> leitura de Gestão à Vista.");
        }

        [Fact]
        public void Etapa01_ConsultaSqlOcorreAntesDeLimparAAbaTimesheet()
        {
            // Validação de segurança: se a consulta SQL lançar exceção, o Clear() nunca é alcançado —
            // prova estrutural de que a ordem no código é consulta -> validação -> limpeza -> escrita.
            var codigoFonte = LerTimesheetDumpService();

            int posicaoConsulta = codigoFonte.IndexOf("ToListAsync()", StringComparison.Ordinal);
            int posicaoClear = codigoFonte.IndexOf("wsTs.Cells.Clear()", StringComparison.Ordinal);

            Assert.True(posicaoConsulta >= 0, "Consulta SQL (ToListAsync) não encontrada.");
            Assert.True(posicaoClear >= 0, "wsTs.Cells.Clear() não encontrado.");
            Assert.True(posicaoConsulta < posicaoClear,
                "A consulta SQL precisa ocorrer ANTES de limpar a aba TIMESHEET — se a consulta falhar, a " +
                "exceção deve propagar antes que qualquer célula seja apagada.");
        }

        [Fact]
        public void Etapa01_FonteOperacionalOriginalPreservada_NuncaSubstituidaPorGestaoTimesheetLancamento()
        {
            var codigoFonte = LerTimesheetDumpService();
            Assert.Contains("TbTimeSheetRealizadoDocumentos", codigoFonte, StringComparison.Ordinal);
            Assert.Contains("tbTimeSheetRealizadoDocumento", codigoFonte, StringComparison.Ordinal);
            Assert.DoesNotContain("TimesheetLancamento", SemComentariosDeLinha(codigoFonte), StringComparison.Ordinal);
        }
    }
}
