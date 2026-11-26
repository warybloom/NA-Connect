namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>Argumentos já validados e semanticamente coerentes (nunca uma combinação proibida chega até aqui — ver <see cref="ArgumentParser"/>).</summary>
    public sealed class CliArguments
    {
        public CliMode Mode { get; init; }
        public string? ProjectId { get; init; }
        public bool AllProjects { get; init; }
        public bool ConfirmAllProjects { get; init; }
        public bool AllowLiveRead { get; init; }
        public bool Persist { get; init; }

        public System.Collections.Generic.IReadOnlyList<string> ProjectIds() =>
            !string.IsNullOrWhiteSpace(ProjectId) ? new[] { ProjectId } : System.Array.Empty<string>();
    }

    public sealed class ArgumentParseResult
    {
        public bool Sucesso { get; init; }
        public CliArguments? Argumentos { get; init; }
        public string? MotivoRecusa { get; init; }

        public static ArgumentParseResult Ok(CliArguments argumentos) => new() { Sucesso = true, Argumentos = argumentos };
        public static ArgumentParseResult Recusar(string motivo) => new() { Sucesso = false, MotivoRecusa = motivo };
    }
}
