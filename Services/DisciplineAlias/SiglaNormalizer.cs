using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.DisciplineAlias;

/// <summary>
/// Função ÚNICA de normalização (espelhada em ClientApp/src/features/disciplinas/normalizadorSiglas.ts; os dois lados
/// rodam os mesmos casos de Tests/NA_Connect.DisciplineAlias.Tests/Fixtures/casos_resolucao.json).
/// Comparação: Unicode NFC, espaços colapsados, sem espaços nas pontas, caixa ignorada. Acentos NÃO são removidos
/// (o nome canônico preserva Unicode; "ELÉTRICA" ≠ "ELETRICA" na comparação, para nunca unir por semelhança).
/// </summary>
public static partial class SiglaNormalizer
{
    public const int MaxSigla = 50;
    public const int MaxDisciplina = 200;
    public const int MaxContexto = 200;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    /// <summary>Texto para exibição/persistência: NFC, espaços colapsados e aparados; caixa e acentos preservados.</summary>
    public static string Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? "" : Espacos().Replace(valor.Normalize(NormalizationForm.FormC), " ").Trim();

    /// <summary>Chave de comparação (sigla, nome, contexto).</summary>
    public static string Chave(string? valor) => Limpar(valor).ToUpperInvariant();

    public static string ChaveContexto(string? tipo, string? valor)
    {
        var t = Chave(tipo);
        return t.Length == 0 ? "" : $"{t}:{Chave(valor)}";
    }

    /// <summary>Valida e limpa uma regra (inclusão manual, correção ou linha de importação). Nunca completa dado ausente.</summary>
    public static (DadosRegra? Regra, IReadOnlyList<string> Erros) Validar(string? sigla, string? disciplina, string? contextoTipo,
        string? contextoValor, bool ativo)
    {
        var erros = new List<string>();
        var s = Limpar(sigla);
        var d = Limpar(disciplina);
        var t = Chave(contextoTipo);
        var v = Limpar(contextoValor);
        if (s.Length == 0) erros.Add("Sigla vazia.");
        else if (s.Length > MaxSigla) erros.Add($"Sigla com mais de {MaxSigla} caracteres.");
        if (d.Length == 0) erros.Add("Disciplina canônica vazia.");
        else if (d.Length > MaxDisciplina) erros.Add($"Disciplina canônica com mais de {MaxDisciplina} caracteres.");
        if (TemControle(s) || TemControle(d) || TemControle(v)) erros.Add("Texto com caractere de controle.");
        if (t.Length == 0 && v.Length > 0) erros.Add("Contexto informado sem tipo (use PROJETO ou FONTE).");
        if (t.Length > 0)
        {
            if (!ContextoTipos.Todos.Contains(t)) erros.Add($"Tipo de contexto '{Limpar(contextoTipo)}' inválido (use PROJETO ou FONTE).");
            if (v.Length == 0) erros.Add("Tipo de contexto sem valor.");
            else if (v.Length > MaxContexto) erros.Add($"Contexto com mais de {MaxContexto} caracteres.");
            if (t == ContextoTipos.Fonte && v.Length > 0 && !FontesConhecidas.Todas.Contains(Chave(v)))
                erros.Add($"Fonte '{v}' inválida (use {string.Join(", ", FontesConhecidas.Todas)}).");
        }
        if (erros.Count > 0) return (null, erros);
        return (new DadosRegra(s, d, t.Length == 0 ? null : t, t.Length == 0 ? null : t == ContextoTipos.Fonte ? Chave(v) : v, ativo), erros);
    }

    private static bool TemControle(string s) => s.Any(char.IsControl);

    /// <summary>
    /// Resolve o nome de agregação de um item. Candidatos em ordem de preferência (ex.: sigla DOC3, nome da disciplina).
    /// Prioridade: regra contexto-específica (projeto/fonte) → regra global → rótulo original. Duas regras ativas no
    /// MESMO nível que apontam para nomes diferentes = conflito: NÃO é resolvido; devolve o rótulo original + diagnóstico.
    /// </summary>
    public static Resolucao Resolver(IEnumerable<RegraSigla> regras, IReadOnlyList<string?> candidatos, string rotuloOriginal,
        ContextoResolucao contexto)
    {
        var ativas = regras.Where(r => r.Ativo).ToList();
        var chaves = candidatos.Select(Chave).Where(c => c.Length > 0).Distinct().ToList();
        var projetos = contexto.Projetos.Select(Chave).Where(c => c.Length > 0).ToHashSet();
        var fonte = Chave(contexto.Fonte);
        bool NoContexto(RegraSigla r) =>
            r.ContextoTipo == ContextoTipos.Projeto && projetos.Contains(Chave(r.ContextoValor))
            || r.ContextoTipo == ContextoTipos.Fonte && fonte.Length > 0 && Chave(r.ContextoValor) == fonte;

        foreach (var (filtro, origem) in new (Func<RegraSigla, bool>, OrigemResolucao)[]
                 { (NoContexto, OrigemResolucao.Contexto), (r => r.ContextoTipo is null, OrigemResolucao.Global) })
        {
            foreach (var chave in chaves)
            {
                var achadas = ativas.Where(r => r.SiglaChave == chave && filtro(r)).ToList();
                if (achadas.Count == 0) continue;
                var nomes = achadas.GroupBy(r => Chave(r.DisciplinaCanonica)).ToList();
                if (nomes.Count > 1)
                    return new Resolucao(Limpar(rotuloOriginal), OrigemResolucao.Original, null,
                        $"Sigla '{achadas[0].SiglaOriginal}' com regras conflitantes ({string.Join(" / ", nomes.Select(g => g.First().DisciplinaCanonica))}); mantido o rótulo original.");
                var regra = achadas.OrderBy(r => r.Id).First();
                return new Resolucao(Limpar(regra.DisciplinaCanonica), origem, regra.Id, null);
            }
        }
        return new Resolucao(Limpar(rotuloOriginal), OrigemResolucao.Original, null, null);
    }
}
