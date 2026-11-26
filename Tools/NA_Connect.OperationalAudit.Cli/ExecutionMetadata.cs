using System;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Identificação de UMA execução do CLI (Checkpoint 4, seção 13). Nunca contém senha, token, client
    /// secret ou connection string completa — só metadados de proveniência. Serializado como sidecar JSON
    /// junto dos relatórios TXT/CSV/JSON já existentes (nunca substitui nem altera o formato deles).
    /// </summary>
    public sealed class ExecutionMetadata
    {
        public Guid AuditRunId { get; init; } = Guid.NewGuid();
        public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
        public DateTime? FinishedAtUtc { get; set; }
        public string MachineName { get; init; } = Environment.MachineName;
        public string ExecutorVersion { get; init; } = typeof(ExecutionMetadata).Assembly.GetName().Version?.ToString() ?? "desconhecida";
        public string? GitCommit { get; init; }
        public bool? GitDirty { get; init; }
        public CliMode Mode { get; init; }
        public string? ProjectSelectionSummary { get; init; }
        public bool Cancelled { get; set; }

        public static ExecutionMetadata Criar(CliMode mode, string? projectSelectionSummary, string diretorioRepositorio)
        {
            var (commit, dirty) = GitInfoReader.Ler(diretorioRepositorio);
            return new ExecutionMetadata
            {
                Mode = mode,
                ProjectSelectionSummary = projectSelectionSummary,
                GitCommit = commit,
                GitDirty = dirty
            };
        }
    }
}
