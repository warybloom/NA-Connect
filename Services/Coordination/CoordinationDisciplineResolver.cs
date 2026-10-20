using System.Text.RegularExpressions;

namespace NA_Connect.Services.Coordination;

/// <summary>
/// Autoridade ÚNICA de resolução de disciplina do HH/Custo REALIZADO (Timesheet), backend, nunca
/// duplicada em React. Entrada: doc3 (3ª parte do N&amp;A Document Number, coluna Doc3 de
/// gestao.TimesheetSnapshotAtual) + o dicionário Sigla-&gt;Disciplina da Norma de Coordenação do PRÓPRIO
/// projeto do lançamento. Saída: disciplina canônica via Norma de Coordenação, nunca via LDP/Cronograma/
/// heurística de texto (Task_Name, Title) — essa é a regra oficial definida em 2026-09-22 (ver
/// AUDITORIA_FORGESYNC_NA). Desde 15/10/2026 a chave é SEMPRE os 2 primeiros caracteres do DOC3, e o Previsto da LDP
/// usa a mesma resolução (PmoExecutiveData), para previsto e realizado caírem na mesma disciplina.
/// </summary>
public static class CoordinationDisciplineSources
{
    public const string NormaExata = "NORMA_EXATA";
    public const string NormaPrefixo = "NORMA_PREFIXO";
    public const string NormaSufixoNumerico = "NORMA_SUFIXO_NUMERICO";
    /// <summary>DOC3 com mais de 2 caracteres resolvido pela sigla de 2 caracteres da Norma (CIM -&gt; CI).</summary>
    public const string NormaDoisCaracteres = "NORMA_DOC3_2_CARACTERES";
    /// <summary>Norma sem a sigla de 2 caracteres, mas todas as siglas com o mesmo prefixo concordam.</summary>
    public const string NormaConsensoDoisCaracteres = "NORMA_CONSENSO_2_CARACTERES";
    public const string AtManual = "AT_MANUAL";
    public const string SemDisciplina = "SEM_DISCIPLINA";
}

public sealed record CoordinationResolution(
    string? RawCode, string NormalizedCode, string? MatchedCode, string? Discipline,
    int? CoordinationStandardId, bool Matched, string Source, string Diagnostic);

public static class CoordinationDisciplineResolver
{
    public const string SemDisciplinaLabel = "SEM DISCIPLINA";
    private static readonly Regex SufixoNumericoFinal = new(@"^(.+?)(\d+)$", RegexOptions.Compiled);

    public static string Normalize(string? raw) => (raw ?? "").Trim().ToUpperInvariant();

    /// <summary>Remove SÓ o sufixo numérico final (ex.: "3D1" -&gt; "3D", nunca "D"). Sem dígito final,
    /// este helper legado devolve null — nunca remove dígito no meio/começo do código.</summary>
    public static string? StripTrailingNumericSuffix(string normalized)
    {
        var m = SufixoNumericoFinal.Match(normalized);
        return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
    }

    /// <summary>Quantidade de caracteres do DOC3 que define a disciplina (regra expressa do usuário, 15/10/2026).</summary>
    public const int TamanhoChaveDisciplina = 2;

    /// <summary>Chave de disciplina do DOC3: SEMPRE os dois primeiros caracteres (trim+upper). "CIM" -&gt; "CI",
    /// "3D0" -&gt; "3D", "PX5" -&gt; "PX". O restante do DOC3 nunca define disciplina. Null quando o DOC3 tem menos de 2 caracteres.</summary>
    public static string? DisciplineKey(string? doc3)
    {
        var normalized = Normalize(doc3);
        return normalized.Length < TamanhoChaveDisciplina ? null : normalized[..TamanhoChaveDisciplina];
    }

    /// <summary>
    /// REGRA DEFINITIVA (15/10/2026): disciplina = 2 primeiros caracteres do DOC3, resolvidos na Norma de Coordenação
    /// do PRÓPRIO projeto. 1) sigla da Norma igual à chave de 2 caracteres; 2) se a Norma não cadastrou a sigla de 2
    /// caracteres, todas as siglas que começam com a chave precisam apontar para a MESMA disciplina (consenso);
    /// 3) senão SEM DISCIPLINA com diagnóstico — nunca exclui HH/custo, nunca decide pelo 3º caractere
    /// (ex.: CIM=3D MODELING na Norma é ignorado, CIM é CI).
    /// </summary>
    public static CoordinationResolution Resolve(string? doc3, int? coordinationStandardId,
        IReadOnlyDictionary<string, string> mapeamentos, IReadOnlyDictionary<string,string>? canonicalNames = null)
    {
        var normalized = Normalize(doc3);
        if(coordinationStandardId is null)
            return new(doc3, normalized, null, null, null, false, CoordinationDisciplineSources.SemDisciplina, "Projeto sem Norma de Coordenação ativa.");
        if (normalized == "")
            return new(doc3, normalized, null, null, coordinationStandardId, false,
                CoordinationDisciplineSources.SemDisciplina, "doc3 vazio/nulo.");
        var key = DisciplineKey(normalized);
        if (key is null)
            return new(doc3, normalized, null, null, coordinationStandardId, false,
                CoordinationDisciplineSources.SemDisciplina, $"DOC3 '{normalized}' com menos de {TamanhoChaveDisciplina} caracteres.");

        CoordinationResolution Matched(string code,string name,string source,string diagnostic)
        {
            // A Norma de Coordenação VINCULADA AO PROJETO é a única autoridade.
            // O catálogo global não pode rejeitar nem renomear a disciplina da Norma.
            // canonicalNames continua como parâmetro legado para compatibilidade.
            return new(doc3,normalized,code,name.Trim(),coordinationStandardId,true,source,diagnostic);
        }

        if (mapeamentos.TryGetValue(key, out var disc))
            return Matched(key, disc, key == normalized ? CoordinationDisciplineSources.NormaExata : CoordinationDisciplineSources.NormaDoisCaracteres,
                $"DOC3 '{normalized}' -> chave '{key}' (2 primeiros caracteres) cadastrada na Norma.");

        var irmas = mapeamentos.Where(kv => kv.Key.StartsWith(key, StringComparison.Ordinal)).ToArray();
        var nomes = irmas.Select(kv => kv.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (nomes.Length == 1)
            return Matched(key, nomes[0], CoordinationDisciplineSources.NormaConsensoDoisCaracteres,
                $"Norma sem a sigla '{key}'; todas as {irmas.Length} siglas '{key}*' apontam para '{nomes[0]}'.");
        if (nomes.Length > 1)
            return new(doc3, normalized, null, null, coordinationStandardId, false, CoordinationDisciplineSources.SemDisciplina,
                $"Norma sem a sigla '{key}' e as siglas '{key}*' apontam para disciplinas diferentes ({string.Join(", ", nomes)}); cadastrar '{key}'.");

        return new(doc3, normalized, null, null, coordinationStandardId, false,
            CoordinationDisciplineSources.SemDisciplina,
            $"Chave '{key}' (DOC3 '{normalized}') sem sigla cadastrada na Norma de Coordenação do projeto.");
    }

    /// <summary>Identificação documental do Timesheet: reusa DocumentoCodigo quando já é a representação
    /// canônica; senão reconstrói doc1-doc2-doc3-doc4-doc5 usando somente segmentos realmente existentes
    /// (nunca título/nome da tarefa).</summary>
    public static string? ResolveDocumentCode(string? documentoCodigo, string? doc1, string? doc2, string? doc3,
        string? doc4, string? doc5)
    {
        if (!string.IsNullOrWhiteSpace(documentoCodigo)) return documentoCodigo.Trim();
        var partes = new[] { doc1, doc2, doc3, doc4, doc5 }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return partes.Length == 0 ? null : string.Join("-", partes);
    }
}
