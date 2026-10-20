using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.Planning;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 13 — Internal Action Log (Checkpoint 3). Tabela operacional: <c>[dbo].[tb_InternalActionLog]</c>,
    /// pasta "01.09 - INTERNAL ACTION & PENDING ITEMS LOG", aba "ACTION ITEM LIST". Mesma filosofia da
    /// Stage12 (ver <see cref="ActionLogAuditHelper"/>), mas regra INDEPENDENTE — RegraIds próprios
    /// (INT-001..005, nunca reutiliza EXT-*), resultados nunca misturados com a Etapa 12. Nunca persiste
    /// <c>tb_InternalActionLog</c>.
    /// </summary>
    public sealed class Stage13InternalActionAuditRule : IOperationalAuditRule
    {
        public string Etapa => "13";
        public string NomeEtapa => "Internal Action Log";

        private static readonly string[] AliasesPasta =
        {
            "01.09 - INTERNAL ACTION & PENDING ITEMS LOG",
            "01.09 -INTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)",
            "01.09 - INTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)",
            "01.09 - INTERNO - CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)"
        };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IPlanningPreflightService _preflight;

        public Stage13InternalActionAuditRule(IAuditAccReadOnlyAdapter acc, IPlanningPreflightService preflight)
        {
            _acc = acc;
            _preflight = preflight;
        }

        public Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto) =>
            ActionLogAuditHelper.ExecutarAsync(contexto, _acc, _preflight, AliasesPasta, PlanningDefinicoes.Internal(), "13", "INT");
    }
}
