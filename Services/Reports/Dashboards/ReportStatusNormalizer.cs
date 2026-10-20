using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.Reports.Dashboards;

/// <summary>
/// Normalização de RELATÓRIO (nunca altera dado gravado). Agrupa valores equivalentes que o banco guarda
/// em PT ou EN conforme o idioma de cada projeto. Só entram equivalências em que AS DUAS variantes
/// aparecem nos dados reais (levantamento DISTINCT de 29/09/2026 em Ldp_Unificada e
/// tb_WeeklyEngineeringSchedule) e são tradução literal do mesmo vocabulário controlado da LDP;
/// CANCELED/CANCELADO já era equivalência do código (PmoExecutiveRules.Cancelado).
/// Valor sem par comprovado (ex.: PENDING ISSUED, LIBERADO PARA CONSTRUÇÃO, INFORMATIVO) NÃO é mapeado:
/// continua como o próprio texto (agrupado só por maiúsculas/acentos/espaços).
/// </summary>
public static class ReportStatusNormalizer
{
    public const string NullKey = "__NULL__";
    public const string NullLabel = "Não informado";

    public sealed record Grupo(string Chave, string Rotulo, string[] Variantes);

    /// <summary>Vocabulário de status documental (Document_Status da LDP e Weekly_Progress_Status).</summary>
    public static readonly IReadOnlyList<Grupo> StatusDocumental = new Grupo[]
    {
        // CANCELLED (dois L) aparece nos gates da LDP Tipo 2 (VW_Ldp_Documento_Gates_Ultima_Versao).
        new("CANCELADO", "Cancelado", new[] { "CANCELED", "CANCELLED", "CANCELADO" }),
        new("EM_ANALISE_CLIENTE", "Em análise do cliente", new[] { "CLIENT REVIEW", "EM ANÁLISE DO CLIENTE" }),
        new("APROVADO", "Aprovado", new[] { "APPROVED", "APROVADO" }),
        new("NAO_INICIADO", "Não iniciado", new[] { "NOT STARTED", "NÃO INICIADO" }),
        new("EM_ELABORACAO", "Em elaboração", new[] { "ELABORATION", "EM ELABORAÇÃO", "ELABORANDO" }),
        new("ATENDIMENTO_COMENTARIOS", "Atendimento a comentários",
            new[] { "ADRESSING COMMENTS", "ADDRESSING COMMENTS", "ATENDIMENTO A COMENTÁRIOS", "ATENDIMENTO DE COMENTÁRIOS" }),
        new("EM_FLUXO", "Em fluxo de emissão", new[] { "ISSUANCE FLOW", "EM FLUXO" }),
        new("EM_VERIFICACAO", "Em verificação", new[] { "VERIFICATION", "EM VERIFICAÇÃO", "VERIFICAÇÃO" }),
    };

    /// <summary>Vocabulário de Situation da LDP.</summary>
    public static readonly IReadOnlyList<Grupo> Situacao = new Grupo[]
    {
        new("EMITIDO", "Emitido", new[] { "ISSUED", "EMITIDO" }),
        new("CANCELADO", "Cancelado", new[] { "CANCELED", "CANCELLED", "CANCELADO" }),
        new("A_EMITIR", "A emitir", new[] { "TO BE ISSUED", "A EMITIR" }),
    };

    private static readonly Dictionary<string, Grupo> IndiceStatus = Indexar(StatusDocumental);
    private static readonly Dictionary<string, Grupo> IndiceSituacao = Indexar(Situacao);

    private static Dictionary<string, Grupo> Indexar(IEnumerable<Grupo> grupos) =>
        grupos.SelectMany(g => g.Variantes.Select(v => (Chave: Chave(v), g))).ToDictionary(x => x.Chave, x => x.g);

    /// <summary>Chave de comparação: sem acentos, maiúsculas, espaços colapsados. Vazio → null.</summary>
    public static string? Chave(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var sb = new StringBuilder();
        foreach (var ch in valor.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim().ToUpperInvariant();
    }

    /// <summary>Valor genérico: agrupa só por maiúsculas/acentos/espaços; rótulo = texto original aparado.</summary>
    public static (string Key, string Label) Texto(object? valor)
    {
        var s = valor?.ToString();
        var k = Chave(s);
        return k is null ? (NullKey, NullLabel) : (k, s!.Trim());
    }

    public static (string Key, string Label) StatusDoc(object? valor) => Mapear(valor, IndiceStatus);
    public static (string Key, string Label) Situation(object? valor) => Mapear(valor, IndiceSituacao);

    private static (string Key, string Label) Mapear(object? valor, Dictionary<string, Grupo> indice)
    {
        var (k, label) = Texto(valor);
        return k != NullKey && indice.TryGetValue(k, out var g) ? (g.Chave, g.Rotulo) : (k, label);
    }
}
