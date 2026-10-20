using System;
using System.Threading;
using APS.WebApp.Services.OperationalAudit.Configuration;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Contexto de execução passado para cada <see cref="IOperationalAuditRule"/> — um projeto, uma
    /// execução. Nunca carrega uma conexão de escrita: quando uma regra precisar ler dados, ela recebe (via
    /// injeção própria do Checkpoint 2+, não deste contexto) só adaptadores somente-leitura.
    /// </summary>
    public sealed class OperationalAuditContext
    {
        public Guid ExecutionId { get; init; }
        public DateTime IniciadoEmUtc { get; init; }

        public string ProjectIdAcc { get; init; } = string.Empty;
        public string? AccountIdAcc { get; init; }
        public string? ProjectName { get; init; }

        public OperationalAuditOptions Options { get; init; } = new();

        public CancellationToken CancellationToken { get; init; }
    }
}
