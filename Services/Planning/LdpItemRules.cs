namespace APS.WebApp.Services.Planning;

/// <summary>
/// Classificação semântica das linhas da LDP.
/// AT = atividade de esforço/custo, não entregável documental.
/// A linha continua válida para HH/custo, mas não entra em métricas de documentos
/// (emitido, a emitir, atrasado, cancelado, revisão, sem baseline etc.).
/// </summary>
public static class LdpItemRules
{
    public const string ActivityCode = "AT";

    public static bool IsActivity(string? docTypeCode, string? doc4) =>
        Normalize(docTypeCode) == ActivityCode || Normalize(doc4) == ActivityCode;

    public static bool IsDocument(string? docTypeCode, string? doc4) =>
        !IsActivity(docTypeCode, doc4);

    /// <summary>Predicado SQL equivalente à regra C#; alias é controlado internamente pelo código.</summary>
    public static string SqlIsActivity(string alias) =>
        $"(UPPER(LTRIM(RTRIM(ISNULL({alias}.Doc_Type_Code, '')))) = '{ActivityCode}' OR " +
        $"UPPER(LTRIM(RTRIM(ISNULL({alias}.doc4, '')))) = '{ActivityCode}')";

    public static string SqlIsDocument(string alias) => $"NOT {SqlIsActivity(alias)}";

    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();
}
