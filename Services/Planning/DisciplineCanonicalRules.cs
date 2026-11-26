using System.Globalization;
using System.Text;

namespace APS.WebApp.Services.Planning;

/// <summary>
/// Regra canônica de equivalência PT/EN de disciplinas. Usa as colunas padronizadas já existentes na
/// VW_Ldp_Unificada. Em consolidação multi-projeto, a chave EN é usada como chave neutra comum entre
/// projetos em português e inglês. Valor não mapeado nunca é inventado: cai para o nome bruto.
/// </summary>
public static class DisciplineCanonicalRules
{
    public static string Normalize(string? value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool IsMapped(string? pt, string? en)
    {
        var npt = Normalize(pt);
        var nen = Normalize(en);
        if (npt.Length == 0 || nen.Length == 0) return false;
        return !npt.Contains("NAO MAPEADO", StringComparison.Ordinal)
            && !nen.Contains("NOT MAPPED", StringComparison.Ordinal);
    }

    public static string? Canonical(string? raw, string? pt, string? en)
    {
        if (IsMapped(pt, en)) return en?.Trim();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    public static string AuditStatus(string? raw, string? pt, string? en)
    {
        if (!IsMapped(pt, en)) return "NAO_MAPEADO";
        var nr = Normalize(raw);
        return nr == Normalize(pt) || nr == Normalize(en) ? "OK" : "VARIANTE";
    }
}
