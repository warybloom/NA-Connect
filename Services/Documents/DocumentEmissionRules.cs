namespace APS.WebApp.Services.Documents;

/// <summary>
/// REGRA CANÔNICA ÚNICA de emissão gerencial de documento (relatórios, dashboards e Etapa 4B do robô).
/// Precedência: 1) CANCELADO (CANCELED/CANCELADO em Document_Status, Situation ou Status_In_Flow) sempre prevalece;
/// 2) EMITIDO = Document_Status APPROVED/APROVADO OU Emission_Date preenchida; 3) o restante é NAO_EMITIDO
/// (só este pode ser classificado depois em No prazo / Atrasado / Sem prazo pela lógica de prazo existente).
/// Somente leitura/classificação: nunca preenche Emission_Date, GRD, Transmittal, Revision nem altera a LDP.
/// </summary>
public static class DocumentEmissionRules
{
    public const string Cancelado = "CANCELADO", Emitido = "EMITIDO", NaoEmitido = "NAO_EMITIDO";

    /// <summary>Trim + maiúsculas (null → "").</summary>
    public static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();

    public static bool IsCancelled(string? documentStatus, string? situation, string? statusInFlow) =>
        new[] { documentStatus, situation, statusInFlow }.Any(v => Normalize(v) is "CANCELED" or "CANCELADO");

    /// <summary>Document_Status APPROVED/APROVADO = evidência gerencial suficiente de emissão (sem inventar data/GRD).</summary>
    public static bool IsApproved(string? documentStatus) => Normalize(documentStatus) is "APPROVED" or "APROVADO";

    /// <summary>Evidência de emissão (sem avaliar cancelamento — use <see cref="Classify"/> para a classificação completa).</summary>
    public static bool IsIssued(string? documentStatus, DateTime? emissionDate) => IsApproved(documentStatus) || emissionDate.HasValue;

    public static string Classify(string? documentStatus, string? situation, string? statusInFlow, DateTime? emissionDate) =>
        IsCancelled(documentStatus, situation, statusInFlow) ? Cancelado
        : IsIssued(documentStatus, emissionDate) ? Emitido
        : NaoEmitido;
}
