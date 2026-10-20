using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 16/09/2026 (fase 3): duas correções de diretriz sobre a fase 2 anterior.
    ///   - Etapa 02 não tem "equivalente Tipo2" — foi simplesmente DESLIGADA (decisão nossa, temporária,
    ///     independente de Tipo_LDP). A implementação (ldpUpdateService.UpdateLdpFromDatabaseAsync)
    ///     continua intacta em LdpUpdateService.cs — só a CHAMADA em GrdAutomationWorker foi removida,
    ///     substituída por um log [ETAPA 02][PULADA] Motivo=DESABILITADA_TEMPORARIAMENTE.
    ///   - Etapa 05 não tem variante Tipo1/Tipo2 — é UMA SÓ regra (UpdateWorkflowStatusAsync), chamada
    ///     uma única vez, incondicionalmente, ANTES do roteamento Tipo1/Tipo2 (para que o router Tipo2
    ///     leia a LDP já com Status_In_Flow atualizado).
    /// Prova estas garantias contra o TEXTO-FONTE REAL de GrdAutomationWorker.cs/LdpUpdateService.cs
    /// (mesmo padrão de Tipo2RoutingContractTests) — nenhuma delas é alcançável por teste unitário
    /// isolado sem mocks pesados de ACC/Excel.
    /// </summary>
    public class Etapa05UnicaEEtapa02DesabilitadaContractTests
    {
        private static string LerGrdAutomationWorker() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        private static string LerLdpUpdateService() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "LdpUpdateService.cs");

        /// <summary>
        /// Remove comentários de linha ("// ...") antes de checagens negativas (DoesNotContain) — as
        /// próprias explicações em comentário deste código legitimamente citam "Tipo_LDP"/"WorkflowTipo2"
        /// para descrever o que NÃO existe mais; sem isso o teste dispara falso positivo contra seu
        /// próprio texto explicativo, não contra código funcional real.
        /// </summary>
        private static string SemComentariosDeLinha(string codigoFonte) =>
            Regex.Replace(codigoFonte, @"//[^\r\n]*", string.Empty);

        // -----------------------------------------------------------------------------------------
        // ETAPA 02 — desabilitada, implementação preservada.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Etapa02_ChamadaFoiRemovidaDoWorker_LogPuladaDesabilitadaTemporariamente()
        {
            var codigoFonte = LerGrdAutomationWorker();
            Assert.DoesNotContain("ldpUpdateService.UpdateLdpFromDatabaseAsync(", codigoFonte, StringComparison.Ordinal);
            Assert.Contains("[ETAPA 02][PULADA] Motivo=DESABILITADA_TEMPORARIAMENTE", codigoFonte, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa02_MotivoNuncaEhTipoIncompativel()
        {
            // Etapa 02 está desligada por decisão nossa — nunca "TIPO_INCOMPATIVEL" (isso sugeriria
            // incompatibilidade Tipo1/Tipo2, que não é o caso: ela simplesmente não roda para ninguém).
            var codigoFonte = LerGrdAutomationWorker();
            int posicaoLog = codigoFonte.IndexOf("[ETAPA 02][PULADA]", StringComparison.Ordinal);
            Assert.True(posicaoLog >= 0);
            string trechoProximo = codigoFonte.Substring(posicaoLog, Math.Min(200, codigoFonte.Length - posicaoLog));
            Assert.DoesNotContain("TIPO_INCOMPATIVEL", trechoProximo, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa02_ImplementacaoOriginalContinuaPreservadaEmLdpUpdateService()
        {
            // A chamada foi removida do Worker, mas o método em si nunca foi apagado/desmontado.
            var codigoFonte = LerLdpUpdateService();
            Assert.Contains("UpdateLdpFromDatabaseAsync", codigoFonte, StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------------------------
        // ETAPA 05 — uma só regra, incondicional, sem variante Tipo1/Tipo2.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Etapa05_ChamadaUnicaOcorreAntesDoRoteamentoTipo1Tipo2()
        {
            var codigoFonte = LerGrdAutomationWorker();

            int posicaoChamada = codigoFonte.IndexOf("ldpUpdateService.UpdateWorkflowStatusAsync(", StringComparison.Ordinal);
            Assert.True(posicaoChamada >= 0, "Chamada a UpdateWorkflowStatusAsync não encontrada.");

            int posicaoIfTipo2 = codigoFonte.IndexOf("if (ehTipo2)", StringComparison.Ordinal);
            Assert.True(posicaoIfTipo2 >= 0, "'if (ehTipo2)' não encontrado — roteamento pode ter mudado de forma.");

            Assert.True(posicaoChamada < posicaoIfTipo2,
                "UpdateWorkflowStatusAsync deve ser chamado ANTES do 'if (ehTipo2)' — precisa terminar antes do router Tipo2 " +
                "baixar/parsear a LDP, e precisa ser incondicional (nem só Tipo1, nem só Tipo2).");

            // Só uma chamada em todo o arquivo — nunca uma segunda execução por Tipo_LDP.
            int ocorrencias = 0, pos = 0;
            while ((pos = codigoFonte.IndexOf("ldpUpdateService.UpdateWorkflowStatusAsync(", pos, StringComparison.Ordinal)) >= 0)
            {
                ocorrencias++;
                pos += 1;
            }
            Assert.Equal(1, ocorrencias);
        }

        [Fact]
        public void Etapa05_NuncaExisteBranchFuncionalPorTipoLdpEmVoltaDaChamada()
        {
            var codigoFonte = LerGrdAutomationWorker();
            int posicaoChamada = codigoFonte.IndexOf("ldpUpdateService.UpdateWorkflowStatusAsync(", StringComparison.Ordinal);
            Assert.True(posicaoChamada >= 0);

            // Nas ~400 linhas antes da chamada (mesmo escopo/bloco), nunca deve existir uma checagem
            // condicional de Tipo_LDP guardando especificamente esta chamada.
            int inicioTrecho = Math.Max(0, posicaoChamada - 800);
            string trecho = codigoFonte.Substring(inicioTrecho, posicaoChamada - inicioTrecho);
            Assert.DoesNotContain("if (ehTipo2)", trecho, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!ehTipo2)", trecho, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa05_NuncaExisteWorkflowTipo2NemUpdateWorkflowStatusTipo2Async()
        {
            // "WorkflowTipo2" só pode aparecer em COMENTÁRIO explicando que ele não existe — nunca como
            // identificador real (classe, método, variável) no código funcional.
            var worker = SemComentariosDeLinha(LerGrdAutomationWorker());
            var ldpUpdate = SemComentariosDeLinha(LerLdpUpdateService());
            Assert.DoesNotContain("WorkflowTipo2", worker, StringComparison.Ordinal);
            Assert.DoesNotContain("WorkflowTipo2", ldpUpdate, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateWorkflowStatusTipo2Async", worker, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateWorkflowStatusTipo2Async", ldpUpdate, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa05_MetodoUpdateWorkflowStatusAsync_NuncaChecaTipoLdpNoProprioCorpo()
        {
            // A regra em si (dentro do método) nunca deve depender de Tipo_LDP/TipoLdp — só o acesso
            // físico à coluna pode variar (LocalizarColunaStatusInFlow), nunca a decisão de negócio.
            var codigoFonte = LerLdpUpdateService();
            string assinatura = "public async Task<string> UpdateWorkflowStatusAsync(string hubId, string projectId, string accessToken, string userId, string ldpFileId, AccReviewWorkflowService.WorkflowBuildResult workflowResult)";
            int inicio = codigoFonte.IndexOf(assinatura, StringComparison.Ordinal);
            Assert.True(inicio >= 0, "Assinatura do overload principal de UpdateWorkflowStatusAsync não encontrada.");

            int chaveAbre = codigoFonte.IndexOf('{', inicio);
            int profundidade = 0, i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}') { profundidade--; if (profundidade == 0) break; }
            }
            string corpo = SemComentariosDeLinha(codigoFonte.Substring(chaveAbre, i - chaveAbre + 1));

            Assert.DoesNotContain("TipoLdp", corpo, StringComparison.Ordinal);
            Assert.DoesNotContain("Tipo_LDP", corpo, StringComparison.Ordinal);
            Assert.DoesNotContain("ehTipo2", corpo, StringComparison.Ordinal);
        }

        [Fact]
        public void Etapa05BNuncaSubstituiEtapa05_SaoChamadasDistintas()
        {
            var codigoFonte = LerGrdAutomationWorker();
            Assert.Contains("ldpUpdateService.UpdateWorkflowStatusAsync(", codigoFonte, StringComparison.Ordinal);
            Assert.Contains("stageSnapshotService.SincronizarSnapshotAsync(", codigoFonte, StringComparison.Ordinal);
        }
    }
}
