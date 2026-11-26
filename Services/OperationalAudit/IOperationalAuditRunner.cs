using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Executor independente da auditoria operacional (Checkpoint 2, seção 3) — seleciona regras, executa
    /// isoladamente por projeto, agrega, gera relatórios e persiste. NUNCA integrado a
    /// <c>GrdAutomationWorker</c>, nenhum <c>HostedService</c>, nenhum timer, nenhuma execução automática no
    /// startup — só por teste ou harness de desenvolvimento nesta fase.
    /// </summary>
    public interface IOperationalAuditRunner
    {
        Task<OperationalAuditResult> ExecutarAsync(
            IReadOnlyList<string> projectIdsAcc,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default);
    }
}
