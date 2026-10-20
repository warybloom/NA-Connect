using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Checkpoint "PRODUCTION_INTEGRATION" — prova, contra o TEXTO-FONTE REAL de
    /// <c>Services\GrdAutomationWorker.cs</c> (o único arquivo protegido cuja alteração foi autorizada
    /// neste checkpoint, e só para o hook da auditoria), as garantias que um teste unitário isolado nunca
    /// alcançaria: posição exata do hook dentro do fluxo real, e que ele roda em TODO caminho de saída de
    /// um projeto — inclusive os `continue` antecipados (LDP não encontrada, DNA não identificado) — não só
    /// no caminho feliz. Mesmo padrão de <c>ProgramCsContractTests</c> do CLI (Checkpoint 4.1).
    /// </summary>
    public class GrdAutomationWorkerAuditHookContractTests
    {
        private static string LerGrdAutomationWorker() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        private static string ExtrairCorpoDoMetodo(string codigoFonte, string assinaturaDoMetodo)
        {
            int inicio = codigoFonte.IndexOf(assinaturaDoMetodo, StringComparison.Ordinal);
            Assert.True(inicio >= 0, $"Assinatura '{assinaturaDoMetodo}' não encontrada em GrdAutomationWorker.cs.");

            int chaveAbre = codigoFonte.IndexOf('{', inicio);
            Assert.True(chaveAbre >= 0);

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
            Assert.True(profundidade == 0, "Chave de fechamento do método não encontrada (contagem desbalanceada).");
            return codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);
        }

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

        [Fact]
        public void GrdAutomationWorker_ConheceOCoordinator_ResolvidoDoMesmoScopeDeDIDosDemaisServicos()
        {
            string codigoFonte = LerGrdAutomationWorker();

            Assert.Contains("scope.ServiceProvider.GetRequiredService<IOperationalAuditExecutionCoordinator>()", codigoFonte);
        }

        [Fact]
        public void HookDaAuditoria_EChamadoExatamenteUmaVezDentroDoForeachDeProjetos()
        {
            string codigoFonte = LerGrdAutomationWorker();

            int ocorrencias = 0;
            int indice = 0;
            const string chamada = "await ExecutarAuditoriaDoProjetoComProtecaoAsync(auditCoordinator, projectId, projName, stoppingToken);";
            while ((indice = codigoFonte.IndexOf(chamada, indice, StringComparison.Ordinal)) >= 0)
            {
                ocorrencias++;
                indice += chamada.Length;
            }

            Assert.Equal(1, ocorrencias);
        }

        [Fact]
        public void HookDaAuditoria_EstaDentroDoBlocoFinally_NuncaDepoisDele()
        {
            // Crítico: um `continue` dentro do try (LDP não encontrada / DNA não identificado) pula
            // qualquer código posicionado DEPOIS do try/catch/finally, mas SEMPRE executa o finally. Se o
            // hook estivesse posicionado depois do finally (e não dentro dele), projetos que saem
            // antecipadamente via `continue` nunca seriam auditados — o hook TEM que estar dentro do
            // finally para valer para todo projeto do lote, não só o caminho feliz.
            string codigoFonte = LerGrdAutomationWorker();

            // Único "finally" do arquivo que precede a chamada do hook — mira precisamente NELE (não no
            // primeiro "finally" qualquer do arquivo, caso outro exista em outro método no futuro).
            var candidatos = Regex.Matches(codigoFonte, @"finally\s*\{");
            int? chaveAbreDoFinallyCorreto = null;
            foreach (Match candidato in candidatos)
            {
                int chaveAbre = codigoFonte.IndexOf('{', candidato.Index);
                string blocoCandidato = ExtrairBlocoAPartirDaChaveAbre(codigoFonte, chaveAbre);
                if (blocoCandidato.Contains("ExecutarAuditoriaDoProjetoComProtecaoAsync", StringComparison.Ordinal))
                {
                    chaveAbreDoFinallyCorreto = chaveAbre;
                    break;
                }
            }
            Assert.True(chaveAbreDoFinallyCorreto.HasValue, "Nenhum bloco finally contendo a chamada do hook foi encontrado.");

            string corpoFinally = ExtrairBlocoAPartirDaChaveAbre(codigoFonte, chaveAbreDoFinallyCorreto!.Value);
            Assert.Contains("ExecutarAuditoriaDoProjetoComProtecaoAsync", corpoFinally);
            Assert.Contains("File.Delete(ldpPath)", corpoFinally);

            // Ordem dentro do finally: limpeza do arquivo temporário primeiro, hook da auditoria depois.
            int indiceLimpeza = corpoFinally.IndexOf("File.Delete(ldpPath)", StringComparison.Ordinal);
            int indiceHook = corpoFinally.IndexOf("ExecutarAuditoriaDoProjetoComProtecaoAsync", StringComparison.Ordinal);
            Assert.True(indiceLimpeza < indiceHook, "Limpeza do arquivo temporário deveria vir antes do hook da auditoria dentro do finally.");
        }

        [Fact]
        public void MetodoWrapperDoHook_NuncaDeixaExcecaoEscapar_TryEnvolveAChamadaAoCoordinator()
        {
            string codigoFonte = LerGrdAutomationWorker();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "private async Task ExecutarAuditoriaDoProjetoComProtecaoAsync(");

            Assert.Contains("try", corpo);
            Assert.Contains("catch (Exception exAuditoria)", corpo);
            Assert.Contains("auditCoordinator.ExecutarAuditoriaDoProjetoAsync(projectId, projName, stoppingToken)", corpo);

            int indiceTry = corpo.IndexOf("try", StringComparison.Ordinal);
            int indiceChamada = corpo.IndexOf("auditCoordinator.ExecutarAuditoriaDoProjetoAsync(", StringComparison.Ordinal);
            int indiceCatch = corpo.IndexOf("catch (Exception exAuditoria)", StringComparison.Ordinal);
            Assert.True(indiceTry < indiceChamada && indiceChamada < indiceCatch, "A chamada ao coordinator precisa estar dentro do try, antes do catch.");
        }

        [Fact]
        public void MetodoWrapperDoHook_NuncaChamaUmMetodoDeEscritaOperacionalConhecido()
        {
            // Mesma lista de Section 22 (canário) — mas escopada SÓ ao método novo, nunca ao arquivo
            // inteiro (GrdAutomationWorker legitimamente chama esses métodos operacionais em outros pontos
            // — essa é a função dele; o que nunca pode acontecer é o WRAPPER DO HOOK também chamá-los).
            string[] metodosProibidos =
            {
                "ResetAndDumpTimesheetAsync(", "UpdateLdpFromDatabaseAsync(", "SyncGrdsToLdpAsync(",
                "UpdateWorkflowStatusAsync(", "SincronizarSnapshotAsync(", "ExecuteFullSyncProcessAsync(",
                "SalvarDadosLdpUnificada(", "PersistirTabelaAsync(", "SincronizarTabelaDinamicaAsync(",
                "CreateNewVersionAsync(", "UploadFileToFolderAsync(", "UpdateCustomAttributesForVersionAsync(",
                "RunApprovalVerdictSyncAsync(", "SyncScheduleToLdpAsync(", "MirrorLdpControlToProjectAsync(",
                "SincronizarPlanilhaEspecificaAsync(", "Package.Save(", "Package.SaveAs(",
                "HttpMethod.Post", "HttpMethod.Put", "HttpMethod.Patch", "HttpMethod.Delete"
            };

            string codigoFonte = LerGrdAutomationWorker();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "private async Task ExecutarAuditoriaDoProjetoComProtecaoAsync(");

            foreach (var metodo in metodosProibidos)
            {
                Assert.DoesNotContain(metodo, corpo, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void HookDaAuditoria_RecebeOStoppingTokenReal_NuncaCancellationTokenNone()
        {
            string codigoFonte = LerGrdAutomationWorker();
            Assert.Contains("ExecutarAuditoriaDoProjetoComProtecaoAsync(auditCoordinator, projectId, projName, stoppingToken)", codigoFonte);
            Assert.DoesNotContain("ExecutarAuditoriaDoProjetoComProtecaoAsync(auditCoordinator, projectId, projName, CancellationToken.None)", codigoFonte);
        }

        [Fact]
        public void NenhumDosOutros12ArquivosProtegidosEReferenciadoPeloWrapperDoHook()
        {
            // O único arquivo protegido autorizado a mudar neste checkpoint é o próprio
            // GrdAutomationWorker.cs (para o hook) — os outros 12 continuam fora do alcance textual do
            // wrapper novo.
            string[] outrosDozeArquivosProtegidos =
            {
                "LdpUpdateService", "AccReviewWorkflowService", "AccReviewStageSnapshotService",
                "AccScheduleSyncService", "PlanningControlSyncService", "QualityControlSyncService",
                "DatabaseService", "AutodeskApiService", "DocumentMatchingService",
                "GrdToLdpSyncService", "LdpToCustoSyncService", "TimesheetDumpService"
            };

            string codigoFonte = LerGrdAutomationWorker();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "private async Task ExecutarAuditoriaDoProjetoComProtecaoAsync(");

            foreach (var nomeDeClasse in outrosDozeArquivosProtegidos)
            {
                Assert.DoesNotContain(nomeDeClasse, corpo, StringComparison.Ordinal);
            }
        }
    }
}
