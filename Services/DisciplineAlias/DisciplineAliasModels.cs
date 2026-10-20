namespace APS.WebApp.Services.DisciplineAlias;

// Normalizador de Siglas de Disciplina (Configurações). Regra = sigla original → nome CANÔNICO PT-BR, com contexto
// opcional (projeto ou fonte) para siglas ambíguas. Usado SÓ por painéis que agregam disciplinas entre vários projetos;
// nunca renomeia LDP, Timesheet, Norma de Coordenação nem cadastros de disciplina.

public static class ContextoTipos
{
    public const string Projeto = "PROJETO";
    public const string Fonte = "FONTE";
    public static readonly IReadOnlyList<string> Todos = [Projeto, Fonte];
}

/// <summary>Fontes reconhecidas para contexto FONTE (as origens que os painéis multiprojeto agregam).</summary>
public static class FontesConhecidas
{
    public const string Ldp = "LDP";
    public const string Timesheet = "TIMESHEET";
    public const string Programacao = "PROGRAMACAO";
    public static readonly IReadOnlyList<string> Todas = [Ldp, Timesheet, Programacao];
}

public sealed record RegraSigla(
    long Id,
    string SiglaOriginal,
    string DisciplinaCanonica,
    string? ContextoTipo,
    string? ContextoValor,
    bool Ativo,
    string Origem,
    long? ImportacaoId,
    int Versao,
    DateTime CriadoEm,
    string CriadoPor,
    DateTime AlteradoEm,
    string AlteradoPor)
{
    public string SiglaChave => SiglaNormalizer.Chave(SiglaOriginal);
    public string ContextoChave => SiglaNormalizer.ChaveContexto(ContextoTipo, ContextoValor);
}

/// <summary>Dados editáveis de uma regra (inclusão manual, correção ou linha de importação já validada).</summary>
public sealed record DadosRegra(string SiglaOriginal, string DisciplinaCanonica, string? ContextoTipo, string? ContextoValor, bool Ativo);

public sealed record MapeamentoColunas(string Sigla, string Disciplina, string? ContextoTipo = null, string? ContextoValor = null, string? Ativo = null);

public sealed record LinhaPlanilha(int Numero, IReadOnlyDictionary<string, string?> Celulas);

public sealed record PlanilhaLida(string Aba, IReadOnlyList<string> Abas, IReadOnlyList<string> Cabecalhos, IReadOnlyList<LinhaPlanilha> Linhas,
    IReadOnlyList<string> Erros);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum AcaoImportacao { Incluir, Alterar, Inalterada, Rejeitada, Duplicada }

public sealed record LinhaPlano(
    int Linha,
    string? SiglaOriginal,
    string? DisciplinaCanonica,
    string? ContextoTipo,
    string? ContextoValor,
    bool? Ativo,
    AcaoImportacao Acao,
    string? Motivo,
    // true = a linha muda a disciplina de uma regra já existente (exige confirmação explícita).
    bool ConflitoComExistente = false,
    long? RegraExistenteId = null,
    string? DisciplinaAtual = null,
    bool? AtivoAtual = null);

public sealed record ResumoImportacao(int Incluidas, int Alteradas, int Inalteradas, int Rejeitadas, int Duplicadas, int ConflitosComExistente);

public sealed record PlanoImportacao(string Aba, IReadOnlyList<string> Cabecalhos, MapeamentoColunas Mapeamento, IReadOnlyList<LinhaPlano> Linhas)
{
    public ResumoImportacao Resumo => new(
        Linhas.Count(l => l.Acao == AcaoImportacao.Incluir),
        Linhas.Count(l => l.Acao == AcaoImportacao.Alterar),
        Linhas.Count(l => l.Acao == AcaoImportacao.Inalterada),
        Linhas.Count(l => l.Acao == AcaoImportacao.Rejeitada),
        Linhas.Count(l => l.Acao == AcaoImportacao.Duplicada),
        Linhas.Count(l => l.ConflitoComExistente));
}

public sealed record ResultadoImportacao(long ImportacaoId, ResumoImportacao Resumo, IReadOnlyList<LinhaPlano> Linhas);

public sealed record ImportacaoRegistrada(long Id, string NomeArquivo, string HashSha256, string Aba, int Incluidas, int Alteradas,
    int Inalteradas, int Rejeitadas, int Duplicadas, DateTime ImportadoEm, string ImportadoPor);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum OrigemResolucao { Contexto, Global, Original }

/// <summary>Contexto do item a normalizar: identificadores do projeto (ACC id, código) e fonte (LDP/TIMESHEET/PROGRAMACAO).</summary>
public sealed record ContextoResolucao(IReadOnlyCollection<string> Projetos, string? Fonte);

public sealed record Resolucao(string Nome, OrigemResolucao Origem, long? RegraId, string? Conflito);

public sealed class NormalizadorNaoInstaladoException()
    : Exception("Normalizador de Siglas não instalado: a migração Migrations/Normalizador/20261019_001_NormalizadorSiglas_UP.sql ainda não foi aplicada.");

public sealed class RegraInvalidaException(IReadOnlyList<string> erros) : Exception(string.Join(" ", erros))
{
    public IReadOnlyList<string> Erros { get; } = erros;
}

public sealed class RegraDuplicadaException(string sigla, string? contexto)
    : Exception($"Já existe regra para a sigla '{sigla}'{(string.IsNullOrEmpty(contexto) ? " (global)" : $" no contexto {contexto}")}. Corrija a regra existente.");

public sealed class RegraNaoEncontradaException(long id) : Exception($"Regra {id} não encontrada.");

public sealed class ConcorrenciaException()
    : Exception("A regra foi alterada por outra pessoa desde que a tela foi carregada. Recarregue e tente novamente.");
