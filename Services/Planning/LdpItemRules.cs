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

    /// <summary>
    /// Doc4 estruturado (Doc_Type_Code/doc4) tem prioridade. Só quando ambos estão vazios o 4º segmento do código
    /// N&amp;A (Doc1-Doc2-Doc3-Doc4-Doc5) decide; código malformado nunca vira AT.
    /// </summary>
    public static bool IsActivity(string? docTypeCode, string? doc4, string? codigo) =>
        string.IsNullOrWhiteSpace(docTypeCode) && string.IsNullOrWhiteSpace(doc4)
            ? IsActivityCode(codigo)
            : IsActivity(docTypeCode, doc4);

    /// <summary>4º segmento do código N&amp;A; null se o código não tiver ao menos 5 segmentos ou o 4º estiver vazio.</summary>
    public static string? Doc4FromCode(string? codigo)
    {
        var partes = (codigo ?? "").Split('-');
        if (partes.Length < 5) return null;
        var doc4 = Normalize(partes[3]);
        return doc4.Length == 0 ? null : doc4;
    }

    /// <summary>Atividade pelo código: 4º segmento EXATAMENTE "AT" (trim, sem diferenciar maiúsculas). Nunca Contains.</summary>
    public static bool IsActivityCode(string? codigo) => Doc4FromCode(codigo) == ActivityCode;

    /// <summary>Predicado SQL equivalente à regra C#; alias é controlado internamente pelo código.</summary>
    public static string SqlIsActivity(string alias) =>
        $"(UPPER(LTRIM(RTRIM(ISNULL({alias}.Doc_Type_Code, '')))) = '{ActivityCode}' OR " +
        $"UPPER(LTRIM(RTRIM(ISNULL({alias}.doc4, '')))) = '{ActivityCode}')";

    public static string SqlIsDocument(string alias) => $"NOT {SqlIsActivity(alias)}";

    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();
}
