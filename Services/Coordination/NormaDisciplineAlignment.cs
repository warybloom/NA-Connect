using APS.WebApp.Services.Planning;

namespace NA_Connect.Services.Coordination;

/// <summary>
/// Alinha um nome de disciplina vindo de OUTRA fonte (pacote terceirizado em nome canônico EN, AT manual do De/Para)
/// ao nome EFETIVO da Norma de Coordenação do projeto — o mesmo nome que previsto (LDP) e realizado (Timesheet)
/// recebem via <see cref="CoordinationDisciplineResolver.Resolve"/>. A Norma continua sendo a fonte oficial do nome;
/// o catálogo global (Nome_Padrao/aliases PT/EN) só decide EQUIVALÊNCIA. Sem equivalente único: null (mantém o nome
/// original, nunca descarta HH/custo). 28/10/2026: corrige PROCESS×PROCESSO, PIPING×TUBULAÇÃO,
/// STEEL STRUCTURE×ESTRUTURA METALICA duplicados nos indicadores por disciplina.
/// </summary>
public static class NormaDisciplineAlignment
{
    /// <summary>Nomes que a Norma realmente atribui: o resolvido para cada chave de 2 caracteres (TU0=PIPING não conta
    /// quando TU=TUBULAÇÃO, porque a chave é sempre TU).</summary>
    public static IReadOnlyList<string> EffectiveNames(int? normaId, IReadOnlyDictionary<string, string> mapeamentos) =>
        normaId is null ? Array.Empty<string>() :
        mapeamentos.Keys.Select(CoordinationDisciplineResolver.DisciplineKey).OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(k => CoordinationDisciplineResolver.Resolve(k, normaId, mapeamentos))
            .Where(r => r.Matched && !string.IsNullOrWhiteSpace(r.Discipline)).Select(r => r.Discipline!)
            .Distinct(StringComparer.Ordinal).ToArray();

    public static string? Align(string? name, IReadOnlyList<string> effectiveNames, IReadOnlyDictionary<string, string> canonicalNames)
    {
        var key = DisciplineCanonicalRules.Normalize(name);
        if (key.Length == 0 || key == DisciplineCanonicalRules.Normalize(CoordinationDisciplineResolver.SemDisciplinaLabel)) return null;
        var exact = effectiveNames.Where(n => DisciplineCanonicalRules.Normalize(n) == key).Distinct(StringComparer.Ordinal).ToArray();
        if (exact.Length == 1) return exact[0];
        if (!canonicalNames.TryGetValue(key, out var canonical)) return null;
        var equivalent = effectiveNames.Where(n => canonicalNames.TryGetValue(DisciplineCanonicalRules.Normalize(n), out var c) && c == canonical)
            .Distinct(StringComparer.Ordinal).ToArray();
        return equivalent.Length == 1 ? equivalent[0] : null;
    }
}
