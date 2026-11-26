namespace NA_Connect.Services.Coordination;

/// <summary>Critérios de exceção de posição da sigla de disciplina.</summary>
public static class CriteriosExcecaoPosicao
{
    /// <summary>Número de documento exato (prova o documento afetado). Pode prevalecer sobre um DOC3 reconhecido.</summary>
    public const string Codigo = "CODIGO";
    /// <summary>Padrão limitado: valor exato no DOC3 + sigla esperada exata na posição da exceção. Nunca sobrescreve DOC3 reconhecido.</summary>
    public const string Padrao = "PADRAO";
    public static readonly IReadOnlyList<string> Todos = [Codigo, Padrao];
}

/// <summary>
/// Exceção pontual (26/10/2026, revisão BYD002): neste projeto, os documentos que atendem ao critério leem a sigla na
/// posição <see cref="Posicao"/> (ex.: DOC4) em vez do DOC3, e SOMENTE se o segmento for exatamente
/// <see cref="SiglaEsperada"/>. Tudo o que não atende continua no DOC3, inclusive na mesma disciplina.
/// </summary>
public sealed record ExcecaoPosicao(long Id, string Criterio, string? CodigoDocumento, string? SegmentoDoc3, string SiglaEsperada,
    int Posicao, bool Ativo)
{
    public const int PosicaoPadrao = 3;
    public const int PosicaoMinima = 2;
    public const int PosicaoMaxima = 5;

    public string Descricao => Criterio == CriteriosExcecaoPosicao.Codigo
        ? $"{CodigoDocumento}: sigla {SiglaEsperada} no DOC{Posicao}"
        : $"DOC3 = {SegmentoDoc3} e DOC{Posicao} = {SiglaEsperada}";

    /// <summary>Validação estrutural (a validação contra a Norma/inventário é feita no serviço).</summary>
    public static IReadOnlyList<string> Validar(string? criterio, string? codigo, string? segmentoDoc3, string? siglaEsperada, int posicao)
    {
        var erros = new List<string>();
        var c = DisciplineSegments.Chave(criterio);
        if (!CriteriosExcecaoPosicao.Todos.Contains(c)) erros.Add("Critério inválido (use CODIGO ou PADRAO).");
        if (posicao is < PosicaoMinima or > PosicaoMaxima || posicao == PosicaoPadrao)
            erros.Add($"Posição DOC{posicao} inválida para exceção (use DOC2, DOC4 ou DOC5; DOC3 é o padrão).");
        var sigla = DisciplineSegments.Chave(siglaEsperada);
        if (sigla.Length == 0) erros.Add("Sigla esperada obrigatória.");
        else if (sigla.Contains('-')) erros.Add("Sigla esperada não pode conter '-'.");
        if (c == CriteriosExcecaoPosicao.Codigo)
        {
            var partes = DisciplineSegments.DoCodigo(DisciplineSegments.Chave(codigo));
            if (partes.Count == 0) erros.Add("Número do documento obrigatório.");
            else if (sigla.Length > 0 && DisciplineSegments.Chave(DisciplineSegments.Segmento(partes, posicao)) != sigla)
                erros.Add($"O DOC{posicao} de '{DisciplineSegments.Chave(codigo)}' não é '{sigla}'.");
        }
        if (c == CriteriosExcecaoPosicao.Padrao)
        {
            var s3 = DisciplineSegments.Chave(segmentoDoc3);
            if (s3.Length == 0) erros.Add("Padrão exige o valor exato do DOC3.");
            else if (s3.Contains('-')) erros.Add("Valor do DOC3 não pode conter '-'.");
        }
        return erros;
    }
}

/// <summary>Exceções ATIVAS de um projeto. Sem exceções = padrão DOC3, idêntico ao comportamento anterior.</summary>
public sealed class DisciplineSegmentRules
{
    public static readonly DisciplineSegmentRules Padrao = new([]);
    private readonly ILookup<string, ExcecaoPosicao> _porCodigo;
    private readonly ExcecaoPosicao[] _padroes;

    public DisciplineSegmentRules(IEnumerable<ExcecaoPosicao> excecoes)
    {
        Ativas = excecoes.Where(e => e.Ativo).ToArray();
        _porCodigo = Ativas.Where(e => e.Criterio == CriteriosExcecaoPosicao.Codigo)
            .ToLookup(e => DisciplineSegments.Chave(e.CodigoDocumento), StringComparer.Ordinal);
        _padroes = Ativas.Where(e => e.Criterio == CriteriosExcecaoPosicao.Padrao).ToArray();
    }

    public IReadOnlyList<ExcecaoPosicao> Ativas { get; }
    public bool Vazia => Ativas.Count == 0;
    internal IEnumerable<ExcecaoPosicao> PorCodigo(string chave) => _porCodigo[chave];
    internal IReadOnlyList<ExcecaoPosicao> Padroes => _padroes;
}

/// <summary>Segmento escolhido para a sigla. <see cref="ExcecaoId"/> = exceção aplicada; <see cref="Diagnostico"/> explica exceção não aplicada.</summary>
public sealed record DisciplineSegmentChoice(string? Valor, int Posicao, long? ExcecaoId, string? Diagnostico = null);

public static class DisciplineSegments
{
    public static string Chave(string? valor) => (valor ?? "").Trim().ToUpperInvariant();

    /// <summary>Segmentos do código separados por '-' (sem trim aqui; vazio continua vazio). Código nulo/vazio: nenhum segmento.</summary>
    public static IReadOnlyList<string?> DoCodigo(string? codigo) =>
        string.IsNullOrEmpty(codigo) ? Array.Empty<string?>() : codigo.Split('-');

    /// <summary>Segmento na posição DOCn (1-based), com trim. Posição inexistente: null (nunca inferir).
    /// Segmento presente porém vazio devolve "" — mesmo comportamento da extração anterior do DOC3.</summary>
    public static string? Segmento(IReadOnlyList<string?> segmentos, int posicao) =>
        posicao >= 1 && posicao <= segmentos.Count ? (segmentos[posicao - 1] ?? "").Trim() : null;

    /// <summary>
    /// Ordem (docs/BYD002_EXCECAO_PONTUAL_DOC4_20261026.md): 1) exceção por número exato de documento; 2) DOC3 reconhecido;
    /// 3) exceção por padrão limitado (só quando o DOC3 NÃO é reconhecido); 4) DOC3 não identificado, com diagnóstico.
    /// Uma exceção só vale se o segmento na posição dela for exatamente a sigla esperada E for reconhecido.
    /// Sem exceções ativas, devolve o DOC3 sem consultar nada (comportamento anterior).
    /// </summary>
    public static DisciplineSegmentChoice Escolher(string? codigo, IReadOnlyList<string?> segmentos, DisciplineSegmentRules regras,
        Func<string?, bool> reconhecida)
    {
        var doc3 = Segmento(segmentos, ExcecaoPosicao.PosicaoPadrao);
        if (regras.Vazia) return new(doc3, ExcecaoPosicao.PosicaoPadrao, null);

        string? Confere(ExcecaoPosicao e)
        {
            var s = Segmento(segmentos, e.Posicao);
            return Chave(s) == Chave(e.SiglaEsperada) && reconhecida(s) ? s : null;
        }

        string? diagnostico = null;
        var porCodigo = regras.PorCodigo(Chave(codigo)).ToArray();
        if (porCodigo.Length > 0)
        {
            var validas = porCodigo.Select(e => (e, s: Confere(e))).Where(x => x.s is not null).ToArray();
            if (validas.Length == 1) return new(validas[0].s, validas[0].e.Posicao, validas[0].e.Id);
            diagnostico = validas.Length > 1
                ? $"Mais de uma exceção por código aplicável ({string.Join(", ", validas.Select(x => x.e.Id))}); mantido o DOC3."
                : $"Exceção {porCodigo[0].Id} não confere com o código (sigla {porCodigo[0].SiglaEsperada} no DOC{porCodigo[0].Posicao}); mantido o DOC3.";
        }

        if (reconhecida(doc3)) return new(doc3, ExcecaoPosicao.PosicaoPadrao, null, diagnostico);

        var padroes = regras.Padroes.Where(e => Chave(e.SegmentoDoc3) == Chave(doc3))
            .Select(e => (e, s: Confere(e))).Where(x => x.s is not null).ToArray();
        if (padroes.Length == 1) return new(padroes[0].s, padroes[0].e.Posicao, padroes[0].e.Id);
        if (padroes.Length > 1)
            diagnostico ??= $"Mais de uma exceção por padrão aplicável ({string.Join(", ", padroes.Select(x => x.e.Id))}); mantido o DOC3.";
        return new(doc3, ExcecaoPosicao.PosicaoPadrao, null, diagnostico);
    }

    /// <summary>Escolha reconhecendo somente pela Norma de Coordenação do projeto (projetos com LDP).</summary>
    public static DisciplineSegmentChoice EscolherNaNorma(string? codigo, DisciplineSegmentRules regras, int? normaId,
        IReadOnlyDictionary<string, string> mapeamentos) =>
        Escolher(codigo, DoCodigo(codigo), regras, v => CoordinationDisciplineResolver.Resolve(v, normaId, mapeamentos).Matched);
}
