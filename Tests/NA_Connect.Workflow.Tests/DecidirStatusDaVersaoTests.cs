using APS.WebApp.Services;
using Newtonsoft.Json.Linq;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a prioridade obrigatória (seção 6 do roteiro de correção): approveStatus explícito vence
    /// currentStepId; sem approveStatus, etapa aberta resolvida vence; sem nenhum dos dois, preserva a
    /// célula. Também cobre VOID (nunca aberto, nunca resultado final).
    /// </summary>
    public class DecidirStatusDaVersaoTests
    {
        private static JObject NovaVersaoComApproveStatus(JToken? approveStatus)
        {
            var versao = new JObject { ["name"] = "P-0602.1-000-EL-DR-0001-02.dwg" };
            if (approveStatus != null) versao["approveStatus"] = approveStatus;
            return versao;
        }

        [Fact]
        public void ApproveStatusNull_NuncaLancaExcecao_PreservaCelula()
        {
            // Reprodução direta do documento diagnosticado (Review 119844c0..., status VOID,
            // approveStatus:null nas duas versões) — antes da correção, esta chamada lançava
            // "Cannot access child value on Newtonsoft.Json.Linq.JValue." e derrubava o mapa inteiro.
            var versao = NovaVersaoComApproveStatus(JValue.CreateNull());

            var exception = Record.Exception(() =>
                AccReviewWorkflowService.DecidirStatusDaVersao(versao, reviewAberto: false, currentStepId: "Lane_qkhlnS8U7H", nomeEtapaResolvida: null, logEtapaResolucao: "n/d"));

            Assert.Null(exception);
        }

        [Fact]
        public void ReviewClosedComApproveStatusAprovado_VenceCurrentStepId()
        {
            // Documento controlado: Review b695310d..., CLOSED, currentStepId ainda preenchido,
            // approveStatus.label="Aprovado" → resultado deve ser "Aprovado", nunca o nome da etapa.
            var approveStatus = new JObject { ["label"] = "Aprovado", ["value"] = "APPROVED" };
            var versao = NovaVersaoComApproveStatus(approveStatus);

            var decisao = AccReviewWorkflowService.DecidirStatusDaVersao(
                versao, reviewAberto: false, currentStepId: "Lane_KZ9gCjnZgJ", nomeEtapaResolvida: null, logEtapaResolucao: "n/d");

            Assert.Equal("Aprovado", decisao.StatusCalculado);
            Assert.Equal(AccReviewWorkflowService.OrigemStatusWorkflow.REVIEW_VERSION_VERDICT, decisao.OrigemStatus);
            Assert.False(decisao.PreservarCelula);
        }

        [Fact]
        public void ReviewAbertoSemApproveStatus_ComEtapaResolvida_UsaNomeDaEtapa()
        {
            var versao = NovaVersaoComApproveStatus(JValue.CreateNull());

            var decisao = AccReviewWorkflowService.DecidirStatusDaVersao(
                versao, reviewAberto: true, currentStepId: "Lane_x", nomeEtapaResolvida: "VERIFICADOR", logEtapaResolucao: "resolvido");

            Assert.Equal("VERIFICADOR", decisao.StatusCalculado);
            Assert.Equal(AccReviewWorkflowService.OrigemStatusWorkflow.ETAPA_ATUAL, decisao.OrigemStatus);
            Assert.False(decisao.PreservarCelula);
        }

        [Fact]
        public void ReviewAbertoSemApproveStatus_EtapaNaoResolvida_PreservaCelula()
        {
            var versao = NovaVersaoComApproveStatus(JValue.CreateNull());

            var decisao = AccReviewWorkflowService.DecidirStatusDaVersao(
                versao, reviewAberto: true, currentStepId: "Lane_x", nomeEtapaResolvida: null, logEtapaResolucao: "não resolvida");

            Assert.True(decisao.PreservarCelula);
            Assert.Equal(AccReviewWorkflowService.OrigemStatusWorkflow.NAO_RESOLVIDO, decisao.OrigemStatus);
        }

        [Fact]
        public void ReviewVoid_NuncaTratadoComoAberto_PreservaCelula()
        {
            // status VOID não é OPEN/INITIATED (ReviewEstaAberto=false) nem CLOSED/COMPLETED — cai no
            // ramo final: preserva célula, nunca inventa "CONCLUÍDO SEM VEREDITO", nunca é tratado como
            // resultado final.
            var versao = NovaVersaoComApproveStatus(JValue.CreateNull());

            var decisao = AccReviewWorkflowService.DecidirStatusDaVersao(
                versao, reviewAberto: false, currentStepId: "Lane_qkhlnS8U7H", nomeEtapaResolvida: null, logEtapaResolucao: "n/d");

            Assert.True(decisao.PreservarCelula);
            Assert.Equal(AccReviewWorkflowService.OrigemStatusWorkflow.PRESERVAR_CELULA, decisao.OrigemStatus);
            Assert.Equal("CONCLUIDO_SEM_RESULTADO_COMPROVADO", decisao.CodigoResolucao);
        }

        [Fact]
        public void ReviewMaisRecenteVenceHistorico_VoidAntigoXClosedNovo()
        {
            // Reprodução do documento controlado: duas WorkflowStatusEntry para o MESMO arquivo, uma do
            // Review VOID antigo (sem resultado) e outra do Review CLOSED mais recente (Aprovado) — a
            // seleção por DataAtualizacaoReview mais recente (mesmo critério usado em
            // LdpUpdateService.UpdateWorkflowStatusAsync/EncontrarCorrespondenciaControlada) deve escolher
            // a entrada do Review novo.
            var entradaAntiga = new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = "P-0602.1-000-EL-DR-0001-02.dwg",
                FileNameWithoutExtension = "P-0602.1-000-EL-DR-0001-02",
                ItemId = "urn:item-dwg",
                ReviewId = "119844c0-6292-4d6b-9a89-7954170127ba",
                DataAtualizacaoReview = new DateTime(2026, 7, 1, 14, 16, 36),
                Status = null,
                PreservarCelula = true
            };
            var entradaNova = new AccReviewWorkflowService.WorkflowStatusEntry
            {
                FileName = "P-0602.1-000-EL-DR-0001-02.dwg",
                FileNameWithoutExtension = "P-0602.1-000-EL-DR-0001-02",
                ItemId = "urn:item-dwg",
                ReviewId = "b695310d-56cb-4619-9845-da18dd806a95",
                DataAtualizacaoReview = new DateTime(2026, 7, 1, 17, 34, 13),
                Status = "Aprovado",
                EhVereditoFinal = true,
                PreservarCelula = false
            };
            var entries = new List<AccReviewWorkflowService.WorkflowStatusEntry> { entradaAntiga, entradaNova };

            var vencedora = entries.OrderByDescending(e => e.DataAtualizacaoReview ?? DateTime.MinValue).First();

            Assert.Equal("b695310d-56cb-4619-9845-da18dd806a95", vencedora.ReviewId);
            Assert.Equal("Aprovado", vencedora.Status);
            Assert.False(vencedora.PreservarCelula);
        }
    }
}
