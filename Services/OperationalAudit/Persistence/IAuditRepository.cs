using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;

namespace APS.WebApp.Services.OperationalAudit.Persistence
{
    /// <summary>
    /// Único ponto de escrita no schema <c>audit</c> — sempre via
    /// <c>ConnectionStrings:AuditRepositoryConnection</c> (nunca <c>LocalConnection</c>, nunca uma tabela
    /// fora do schema <c>audit</c>). Nenhuma tabela do banco operacional (projetos, LDP, cronograma,
    /// planejamento, qualidade, reviews, cache de atributos — nenhuma delas) é tocada por esta interface;
    /// ver <c>AuditRepositorySqlContractTests</c> para a lista nomeada, mantida só no teste para nunca
    /// divergir da verificação real.
    /// </summary>
    public interface IAuditRepository
    {
        Task<long> IniciarAuditRunAsync(Guid executionId, string triggeredBy, IReadOnlyList<string> stagesRequested, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persiste TUDO que foi produzido para um projeto numa ÚNICA transação curta, escopada a este
        /// projeto (seção 10) — nunca uma transação para o lote inteiro. Falha aqui nunca lança: sempre
        /// volta um <see cref="AuditProjectPersistResult"/> com <c>Sucesso=false</c>, já com rollback feito,
        /// para o chamador continuar com o próximo projeto sem interrupção.
        /// </summary>
        Task<AuditProjectPersistResult> PersistirProjetoAsync(long auditRunId, OperationalAuditProjectData dados, CancellationToken cancellationToken = default);

        Task FinalizarAuditRunAsync(
            long auditRunId,
            OperationalAuditRunStatus status,
            int totalProjetos,
            int totalFindings,
            int totalRequisitos,
            string? erro,
            CancellationToken cancellationToken = default);
    }
}
