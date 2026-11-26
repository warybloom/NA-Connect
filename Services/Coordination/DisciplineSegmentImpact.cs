using APS.WebApp.Services.DisciplineAlias;

namespace NA_Connect.Services.Coordination;

/// <summary>Documento a avaliar: código original (nunca alterado) e os segmentos Doc1..Doc5 da fonte.</summary>
public sealed record DocumentoSegmentos(string Codigo, IReadOnlyList<string?> Segmentos);

/// <summary>Resultado da resolução de UMA sigla: disciplina reconhecida ou motivo.</summary>
public sealed record SiglaResolvida(bool Reconhecida, string? Disciplina, string? Motivo);

public static class SituacoesImpactoPosicao
{
    /// <summary>Sem disciplina antes, identificada depois.</summary>
    public const string Corrigido = "CORRIGIDO";
    /// <summary>Identificada antes, sem disciplina depois.</summary>
    public const string Piorou = "PIOROU";
    /// <summary>Identificada antes e depois, mas com disciplina diferente.</summary>
    public const string Alterado = "ALTERADO";
    /// <summary>Sem disciplina antes e depois.</summary>
    public const string Pendente = "PENDENTE";
}

public sealed record ImpactoDocumento(string Codigo, string? SiglaAntes, int? PosicaoAntes, string? DisciplinaAntes,
    string? SiglaDepois, int? PosicaoDepois, string? DisciplinaDepois, long? ExcecaoIdDepois, string Situacao, string? Motivo);

public sealed record ImpactoResumo(int DocumentosAnalisados, int IdentificadosAntes, int SemMapeamentoAntes,
    int IdentificadosDepois, int SemMapeamentoDepois, int CorrigidosPorExcecao, int Pioraram, int DisciplinaAlterada,
    int LidosPorExcecaoDepois);

public sealed record ImpactoPosicaoDisciplina(string ProjectId, string Projeto, bool SemLdp, string Fonte,
    IReadOnlyList<ExcecaoPosicao> ExcecoesAntes, IReadOnlyList<ExcecaoPosicao> ExcecoesDepois, ImpactoResumo Resumo,
    // Somente os documentos que mudam ou continuam sem disciplina (os inalterados e identificados ficam fora).
    IReadOnlyList<ImpactoDocumento> Documentos);

/// <summary>Documento sem disciplina hoje cujo segmento na posição pesquisada é sigla reconhecida (candidato a exceção, NÃO aplicado).</summary>
public sealed record CandidatoExcecao(string Codigo, string? Doc3, string Sigla, int Posicao, string Disciplina);
/// <summary>Documento sem disciplina hoje que nenhuma exceção na posição pesquisada resolveria.</summary>
public sealed record PendenteSemCandidato(string Codigo, string? Doc3, string? SegmentoPesquisado, string Motivo);
public sealed record CandidatosExcecao(string ProjectId, string Projeto, bool SemLdp, string Fonte, int Posicao, int DocumentosAnalisados,
    int SemMapeamento, IReadOnlyList<CandidatoExcecao> Candidatos, IReadOnlyList<PendenteSemCandidato> Pendentes);

/// <summary>
/// Prévia (somente leitura): aplica as exceções atuais e as propostas aos MESMOS documentos, com o MESMO resolvedor das telas.
/// Nada é gravado.
/// </summary>
public static class DisciplineSegmentImpact
{
    /// <summary>Resolvedor do backend: Norma do projeto; para projeto sem LDP, também o Normalizador de Siglas
    /// (contexto do projeto → global), igual a PmoActualFactsProvider.</summary>
    public static Func<string?, SiglaResolvida> Resolvedor(int? normaId, IReadOnlyDictionary<string, string> mapeamentos,
        bool usarNormalizador, IReadOnlyList<RegraSigla> regras, ContextoResolucao contexto) => sigla =>
    {
        var r = CoordinationDisciplineResolver.Resolve(sigla, normaId, mapeamentos);
        if (r.Matched) return new(true, r.Discipline, null);
        if (!usarNormalizador) return new(false, null, r.Diagnostic);
        var n = SiglaNormalizer.Resolver(regras, [sigla, CoordinationDisciplineResolver.DisciplineKey(sigla)], "", contexto);
        return n.Origem != OrigemResolucao.Original ? new(true, n.Nome, null)
            : new(false, null, n.Conflito ?? $"{r.Diagnostic} Sem regra no Normalizador de Siglas.");
    };

    /// <summary>Cache por sigla normalizada (o resolvedor é determinístico para um projeto).</summary>
    public static Func<string?, SiglaResolvida> ComCache(Func<string?, SiglaResolvida> resolver)
    {
        var cache = new Dictionary<string, SiglaResolvida>(StringComparer.Ordinal);
        return s =>
        {
            var k = CoordinationDisciplineResolver.Normalize(s);
            if (!cache.TryGetValue(k, out var r)) cache[k] = r = resolver(s);
            return r;
        };
    }

    public static (ImpactoResumo Resumo, IReadOnlyList<ImpactoDocumento> Documentos) Comparar(IEnumerable<DocumentoSegmentos> documentos,
        DisciplineSegmentRules antes, DisciplineSegmentRules depois, Func<string?, SiglaResolvida> resolver)
    {
        var resolve = ComCache(resolver);
        bool Reconhecida(string? s) => resolve(s).Reconhecida;

        var lista = new List<ImpactoDocumento>();
        int analisados = 0, antesOk = 0, depoisOk = 0, corrigidosExc = 0, pioraram = 0, alterados = 0, lidosExc = 0;
        foreach (var doc in documentos)
        {
            analisados++;
            var a = DisciplineSegments.Escolher(doc.Codigo, doc.Segmentos, antes, Reconhecida);
            var b = DisciplineSegments.Escolher(doc.Codigo, doc.Segmentos, depois, Reconhecida);
            var ra = resolve(a.Valor);
            var rb = resolve(b.Valor);
            if (ra.Reconhecida) antesOk++;
            if (rb.Reconhecida) depoisOk++;
            if (rb.Reconhecida && b.ExcecaoId is not null) lidosExc++;
            string? situacao = (ra.Reconhecida, rb.Reconhecida) switch
            {
                (false, true) => SituacoesImpactoPosicao.Corrigido,
                (true, false) => SituacoesImpactoPosicao.Piorou,
                (false, false) => SituacoesImpactoPosicao.Pendente,
                _ => string.Equals(ra.Disciplina?.Trim(), rb.Disciplina?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : SituacoesImpactoPosicao.Alterado,
            };
            if (situacao == SituacoesImpactoPosicao.Corrigido && b.ExcecaoId is not null) corrigidosExc++;
            if (situacao == SituacoesImpactoPosicao.Piorou) pioraram++;
            if (situacao == SituacoesImpactoPosicao.Alterado) alterados++;
            if (situacao is null) continue;
            var motivo = situacao switch
            {
                SituacoesImpactoPosicao.Corrigido => b.ExcecaoId is not null
                    ? $"DOC3 '{a.Valor}' não reconhecido; exceção {b.ExcecaoId}: DOC{b.Posicao} '{b.Valor}'."
                    : $"Sigla '{b.Valor}' passa a ser reconhecida.",
                SituacoesImpactoPosicao.Piorou or SituacoesImpactoPosicao.Pendente => b.Diagnostico ?? rb.Motivo ?? "Sigla não reconhecida.",
                _ => $"Disciplina muda de '{ra.Disciplina}' para '{rb.Disciplina}'"
                     + (b.ExcecaoId is not null ? $" pela exceção {b.ExcecaoId} (DOC{b.Posicao} '{b.Valor}')." : "."),
            };
            lista.Add(new(doc.Codigo, Vazio(a.Valor), Vazio(a.Valor) is null ? null : a.Posicao, ra.Disciplina,
                Vazio(b.Valor), Vazio(b.Valor) is null ? null : b.Posicao, rb.Disciplina, b.ExcecaoId, situacao, motivo));
        }
        var ordem = new[] { SituacoesImpactoPosicao.Piorou, SituacoesImpactoPosicao.Alterado, SituacoesImpactoPosicao.Corrigido, SituacoesImpactoPosicao.Pendente };
        var ordenados = lista.OrderBy(d => Array.IndexOf(ordem, d.Situacao)).ThenBy(d => d.Codigo, StringComparer.Ordinal).ToArray();
        return (new(analisados, antesOk, analisados - antesOk, depoisOk, analisados - depoisOk, corrigidosExc, pioraram, alterados, lidosExc), ordenados);
    }

    /// <summary>
    /// Documentos SEM disciplina com as exceções atuais, separados em: candidatos (o segmento na <paramref name="posicao"/> é sigla
    /// reconhecida) e pendentes (com motivo). Só identifica; quem decide criar a exceção é o usuário.
    /// </summary>
    public static (IReadOnlyList<CandidatoExcecao> Candidatos, IReadOnlyList<PendenteSemCandidato> Pendentes, int SemMapeamento) Candidatos(
        IEnumerable<DocumentoSegmentos> documentos, DisciplineSegmentRules atuais, int posicao, Func<string?, SiglaResolvida> resolver)
    {
        var resolve = ComCache(resolver);
        var candidatos = new List<CandidatoExcecao>();
        var pendentes = new List<PendenteSemCandidato>();
        foreach (var doc in documentos)
        {
            var escolha = DisciplineSegments.Escolher(doc.Codigo, doc.Segmentos, atuais, s => resolve(s).Reconhecida);
            var atual = resolve(escolha.Valor);
            if (atual.Reconhecida) continue;
            var doc3 = Vazio(DisciplineSegments.Segmento(doc.Segmentos, ExcecaoPosicao.PosicaoPadrao));
            var seg = Vazio(DisciplineSegments.Segmento(doc.Segmentos, posicao));
            var alvo = seg is null ? null : resolve(seg);
            if (alvo is { Reconhecida: true }) candidatos.Add(new(doc.Codigo, doc3, seg!, posicao, alvo.Disciplina!.Trim()));
            else pendentes.Add(new(doc.Codigo, doc3, seg, seg is null
                ? $"Sem DOC{posicao}; DOC3: {escolha.Diagnostico ?? atual.Motivo}"
                : $"DOC{posicao} '{seg}' também não é sigla reconhecida; DOC3: {escolha.Diagnostico ?? atual.Motivo}"));
        }
        return (candidatos.OrderBy(c => c.Disciplina).ThenBy(c => c.Sigla).ThenBy(c => c.Codigo, StringComparer.Ordinal).ToArray(),
            pendentes.OrderBy(p => p.Codigo, StringComparer.Ordinal).ToArray(), candidatos.Count + pendentes.Count);
    }

    private static string? Vazio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
