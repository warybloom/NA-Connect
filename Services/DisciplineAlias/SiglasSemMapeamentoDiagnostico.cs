using System.Text.RegularExpressions;

namespace APS.WebApp.Services.DisciplineAlias;

public static class SituacoesSiglaSemMapeamento
{
    public const string NaoCadastrada = "NAO_CADASTRADA";
    public const string NaoIdentificada = "NAO_IDENTIFICADA";
    public const string ConfiguracaoInconsistente = "CONFIGURACAO_INCONSISTENTE";
    public const string InformacaoInsuficiente = "INFORMACAO_INSUFICIENTE";
}

public sealed record SiglaSemMapeamentoItem(string? Sigla, string ProjectId, string Projeto, string DocumentoId,
    string? NumeroDocumento, string OrigemNumero, string? NomeArquivo, string IdentificadorOrigem,
    string? DisciplinaOriginal, string? DisciplinaNormalizada,
    string Situacao, string Motivo);

public sealed record SiglasSemMapeamentoResumo(int DocumentosAnalisados, int ComSiglaIdentificada, int ComDisciplinaNormalizada,
    int SiglasDistintas, int DocumentosAfetados, int ProjetosAfetados, int DocumentosSemSiglaExtraivel,
    int DocumentosComInformacaoInsuficiente, int AtividadesForaDaAnalise, int LinhasDescartadas);

/// <summary>Linha do snapshot LDP sem nenhum conteúdo documental (ex.: "----" de linha vazia do template). Não é documento.</summary>
public sealed record LinhaDescartadaDiagnostico(string ProjectId, string Projeto, string DocumentoId, string IdentificadorOrigem,
    string? ValorOriginal, string Motivo);

public sealed record SiglasSemMapeamentoResultado(int VersaoContrato, IReadOnlyList<SiglaSemMapeamentoItem> Itens,
    SiglasSemMapeamentoResumo Resumo, IReadOnlyList<LinhaDescartadaDiagnostico> Descartadas);

public sealed record DocumentoSiglaDiagnostico(string Id, string? Codigo, string? CodigoCliente, string? Titulo, bool Documento,
    string? Sigla, string? SiglaDoc3, string? DisciplinaOriginal, string? DisciplinaNorma, string? DisciplinaCanonizada,
    string? DiagnosticoNorma, int? NormaCoordenacaoId, string? NomeArquivo = null, string? IdentificadorOrigem = null,
    bool PossuiConteudoDocumental = true);
public sealed record ProjetoSiglasDiagnostico(string ProjectId, string Projeto, IReadOnlyList<DocumentoSiglaDiagnostico> Documentos);

/// <summary>Diagnóstico por documento do mesmo snapshot atual usado pela Gestão à Vista.</summary>
public static partial class SiglasSemMapeamentoDiagnostico
{
    public const int VersaoContratoAtual = 4;

    [GeneratedRegex(@"^\s*([A-Z]{2,5}\d{2,4}(?:\.\d{1,3})*)\s*[-–—]", RegexOptions.IgnoreCase)]
    private static partial Regex CodigoProjetoRegex();

    public static SiglasSemMapeamentoResultado Analisar(IEnumerable<ProjetoSiglasDiagnostico> relatorios,
        IReadOnlyList<RegraSigla> regras)
    {
        var itens = new List<SiglaSemMapeamentoItem>();
        var descartadas = new List<LinhaDescartadaDiagnostico>();
        var atividades = 0;
        var analisados = 0;
        var identificados = 0;
        var normalizados = 0;

        foreach (var relatorio in relatorios)
        {
            var nomeProjeto = relatorio.Projeto ?? "";
            var codigoProjeto = CodigoProjetoRegex().Match(nomeProjeto);
            var contextos = new[] { relatorio.ProjectId, nomeProjeto, codigoProjeto.Success ? codigoProjeto.Groups[1].Value : null }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
            var documentos = new List<DocumentoSiglaDiagnostico>();
            foreach (var d in relatorio.Documentos)
            {
                if (!d.Documento) { atividades++; continue; }
                // Só descarta quando NÃO há número identificável E nenhum outro dado (título, disciplina, tipo, HH, custo,
                // datas, status). Documento real sem número continua como pendência de qualidade documental.
                if (!d.PossuiConteudoDocumental && ChaveNumeroIdentificavel(d.Codigo) is null && ChaveNumeroIdentificavel(d.CodigoCliente) is null)
                {
                    descartadas.Add(new(relatorio.ProjectId, nomeProjeto, d.Id, LimparOuNulo(d.IdentificadorOrigem) ?? $"LDP {d.Id}",
                        LimparOuNulo(d.Codigo) ?? LimparOuNulo(d.CodigoCliente),
                        "Linha da LDP sem número identificável, título, disciplina, tipo, HH, custo, datas ou status: não representa documento."));
                    continue;
                }
                documentos.Add(d);
            }

            foreach (var doc in documentos.GroupBy(ChaveDocumento, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                analisados++;
                var sigla = doc.Sigla;
                if (sigla is not null) identificados++;
                var (numeroOriginal, origemNumero) = Numero(doc);
                var numero = ChaveNumeroIdentificavel(numeroOriginal) is not null ? numeroOriginal : null;

                var disciplinaBackend = LimparOuNulo(doc.DisciplinaNorma) ?? LimparOuNulo(doc.DisciplinaCanonizada);
                if (disciplinaBackend is not null)
                {
                    normalizados++;
                    continue;
                }

                var original = LimparOuNulo(doc.DisciplinaOriginal) ?? sigla ?? LimparOuNulo(doc.SiglaDoc3) ?? "";
                var resolucao = SiglaNormalizer.Resolver(regras, [sigla, doc.SiglaDoc3, doc.DisciplinaOriginal], original,
                    new ContextoResolucao(contextos, FontesConhecidas.Ldp));
                if (resolucao.Conflito is not null)
                {
                    itens.Add(Item(doc, relatorio, sigla, numero, origemNumero, null,
                        SituacoesSiglaSemMapeamento.ConfiguracaoInconsistente, resolucao.Conflito));
                    continue;
                }
                if (resolucao.Origem != OrigemResolucao.Original)
                {
                    normalizados++;
                    continue;
                }

                if (sigla is null)
                {
                    var semNumero = numeroOriginal is null;
                    var numeroSemIdentificacao = numeroOriginal is not null && numero is null;
                    itens.Add(Item(doc, relatorio, null, numero, origemNumero, null,
                        semNumero || numeroSemIdentificacao
                            ? SituacoesSiglaSemMapeamento.InformacaoInsuficiente
                            : SituacoesSiglaSemMapeamento.NaoIdentificada,
                        semNumero
                            ? $"Registro documental sem número N&A nem número do cliente. Título: {LimparOuNulo(doc.Titulo) ?? "não informado"}."
                            : numeroSemIdentificacao
                                ? $"Número {origemNumero} contém somente separadores e não identifica um documento. Valor original: {numeroOriginal}."
                            : ChaveNumeroIdentificavel(doc.Codigo) is null
                                ? "Número N&A vazio ou sem identificação; o número do cliente identifica o documento, mas não é usado para inferir DOC3."
                                : doc.DiagnosticoNorma ?? "Número N&A sem DOC3 extraível."));
                    continue;
                }

                var inconsistente = doc.DiagnosticoNorma?.Contains("disciplinas diferentes", StringComparison.OrdinalIgnoreCase) == true
                    || doc.NormaCoordenacaoId is null;
                itens.Add(Item(doc, relatorio, sigla, numero, origemNumero, null,
                    inconsistente ? SituacoesSiglaSemMapeamento.ConfiguracaoInconsistente : SituacoesSiglaSemMapeamento.NaoCadastrada,
                    doc.DiagnosticoNorma ?? "Sigla sem correspondência na Norma e no Normalizador."));
            }
        }

        var ordenados = itens.OrderBy(x => x.Projeto).ThenBy(x => x.Sigla is null).ThenBy(x => x.Sigla)
            .ThenBy(x => x.NumeroDocumento).ThenBy(x => x.DocumentoId).ToArray();
        return new(VersaoContratoAtual, ordenados, new(analisados, identificados, normalizados,
            ordenados.Where(x => x.Sigla is not null).Select(x => SiglaNormalizer.Chave(x.Sigla)).Distinct().Count(),
            ordenados.Length, ordenados.Select(x => x.ProjectId).Distinct().Count(),
            ordenados.Count(x => x.Situacao == SituacoesSiglaSemMapeamento.NaoIdentificada),
            ordenados.Count(x => x.Situacao == SituacoesSiglaSemMapeamento.InformacaoInsuficiente), atividades, descartadas.Count),
            descartadas.OrderBy(x => x.Projeto).ThenBy(x => x.DocumentoId.Length).ThenBy(x => x.DocumentoId, StringComparer.Ordinal).ToArray());
    }

    private static string ChaveDocumento(DocumentoSiglaDiagnostico d) =>
        ChaveNumeroIdentificavel(d.Codigo) is { } na ? $"NA:{na}"
        : ChaveNumeroIdentificavel(d.CodigoCliente) is { } cliente ? $"CLIENTE:{cliente}" : $"ROW:{d.Id}";

    private static string? ChaveNumeroIdentificavel(string? numero)
    {
        var chave = SiglaNormalizer.Chave(numero);
        return chave.Length > 0 && chave.Any(char.IsLetterOrDigit) ? chave : null;
    }

    // Número N&A só de separadores ("----") não esconde um número do cliente válido; sem nenhum identificável,
    // devolve o valor bruto para o motivo mostrar exatamente o que veio da LDP.
    private static (string? Numero, string Origem) Numero(DocumentoSiglaDiagnostico d) =>
        ChaveNumeroIdentificavel(d.Codigo) is not null ? (LimparOuNulo(d.Codigo), "NA")
        : ChaveNumeroIdentificavel(d.CodigoCliente) is not null ? (LimparOuNulo(d.CodigoCliente), "CLIENTE")
        : LimparOuNulo(d.Codigo) is { } na ? (na, "NA")
        : LimparOuNulo(d.CodigoCliente) is { } cliente ? (cliente, "CLIENTE") : (null, "AUSENTE");

    private static SiglaSemMapeamentoItem Item(DocumentoSiglaDiagnostico doc, ProjetoSiglasDiagnostico projeto,
        string? sigla, string? numero, string origemNumero, string? normalizada, string situacao, string motivo) =>
        new(sigla, projeto.ProjectId, projeto.Projeto, doc.Id, numero, origemNumero, LimparOuNulo(doc.NomeArquivo),
            LimparOuNulo(doc.IdentificadorOrigem) ?? $"LDP {doc.Id}", LimparOuNulo(doc.DisciplinaOriginal),
            normalizada, situacao, motivo);

    private static string? LimparOuNulo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
