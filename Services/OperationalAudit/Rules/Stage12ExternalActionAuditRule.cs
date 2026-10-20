using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.Planning;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 12 — External Action Log (Checkpoint 3). Tabela operacional: <c>[dbo].[tb_ExternalActionLog]</c>,
    /// pasta "01.08 - EXTERNAL ACTION &amp; PENDING ITEMS LOG", aba "ACTION ITEM LIST". Fonte de referência
    /// (nunca chamada): <c>Services\PlanningControlSyncService.cs</c>, método
    /// <c>SincronizarTabelaDinamicaAsync</c>. Reutiliza o MESMO preflight real
    /// (<see cref="IPlanningPreflightService"/> — mesmo padrão já usado por Stage14/Stage16 no Checkpoint 2)
    /// e a MESMA definição real (<see cref="PlanningDefinicoes.External"/>) — nunca uma cópia inventada de
    /// aliases/colunas. Nunca persiste <c>tb_ExternalActionLog</c>.
    /// </summary>
    public sealed class Stage12ExternalActionAuditRule : IOperationalAuditRule
    {
        public string Etapa => "12";
        public string NomeEtapa => "External Action Log";

        private static readonly string[] AliasesPasta =
        {
            "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG",
            "01.08 -EXTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)",
            "01.08 - EXTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)",
            "01.08 - EXTERNO - CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)"
        };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IPlanningPreflightService _preflight;

        public Stage12ExternalActionAuditRule(IAuditAccReadOnlyAdapter acc, IPlanningPreflightService preflight)
        {
            _acc = acc;
            _preflight = preflight;
        }

        public Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto) =>
            ActionLogAuditHelper.ExecutarAsync(contexto, _acc, _preflight, AliasesPasta, PlanningDefinicoes.External(), "12", "EXT");
    }
}
