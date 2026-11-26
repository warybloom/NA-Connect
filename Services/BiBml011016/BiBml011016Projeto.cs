using System.Text.RegularExpressions;

namespace APS.WebApp.Services.BiBml011016;

public sealed record BiBml011016CandidatoProjeto(int ProjetoId, string? ProjectIdAcc, string? Nome, bool Ativo);

public sealed record BiBml011016Projeto(string Estado, string Codigo, int? ProjetoId, string? ProjectIdAcc, string? Nome, string? Motivo,
    IReadOnlyList<BiBml011016CandidatoProjeto> Candidatos)
{
    public const string Resolvido = "RESOLVIDO";
    public const string NaoEncontrado = "NAO_ENCONTRADO";
    public const string Ambiguo = "AMBIGUO";
    public const string Indisponivel = "INDISPONIVEL";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstaResolvido =>Estado == Resolvido && ProjectIdAcc is not null;

    public static BiBml011016Projeto Falha(string estado, string motivo, IReadOnlyList<BiBml011016CandidatoProjeto>? candidatos = null) =>
        new(estado, BiBml011016Catalogo.CodigoProjeto, null, null, null, motivo, candidatos ?? []);
}

/// <summary>
/// Resolve o projeto do BI no SERVIDOR. Correspondência EXATA do código como token em dbo.Projetos.Nome_Projeto:
/// LIKE '%BML011.016%' é só pré-filtro; "BML011.0160", "BML011.016.1", "XBML011.016", "BML011.015" e "BML016" não casam.
/// </summary>
public static class BiBml011016ProjetoResolver
{
    private static readonly Regex Token = new(@"(?<![A-Za-z0-9.])BML011\.016(?![A-Za-z0-9]|\.[A-Za-z0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    public static bool CorrespondeExato(string? nome) => !string.IsNullOrWhiteSpace(nome) && Token.IsMatch(nome);

    /// <param name="fixadoAcc">BiBml011016:ProjectIdAcc (configuração do servidor). Restringe; nunca dispensa o casamento do nome.</param>
    public static BiBml011016Projeto Escolher(IEnumerable<BiBml011016CandidatoProjeto> linhas, string? fixadoAcc)
    {
        var casados = linhas.Where(l => CorrespondeExato(l.Nome) && !string.IsNullOrWhiteSpace(l.ProjectIdAcc)).ToList();
        var ativos = casados.Where(l => l.Ativo).ToList();
        if (!string.IsNullOrWhiteSpace(fixadoAcc))
        {
            var alvo = Canonico(fixadoAcc);
            ativos = ativos.Where(l => Canonico(l.ProjectIdAcc!) == alvo).ToList();
            if (ativos.Count == 0)
                return BiBml011016Projeto.Falha(BiBml011016Projeto.NaoEncontrado,
                    $"BiBml011016:ProjectIdAcc configurado não corresponde a um projeto ativo cujo nome contenha exatamente {BiBml011016Catalogo.CodigoProjeto}.", casados);
        }
        return ativos.Count switch
        {
            1 => new(BiBml011016Projeto.Resolvido, BiBml011016Catalogo.CodigoProjeto, ativos[0].ProjetoId, ativos[0].ProjectIdAcc, ativos[0].Nome, null, []),
            0 => BiBml011016Projeto.Falha(BiBml011016Projeto.NaoEncontrado, casados.Count == 0
                ? $"Nenhum projeto em dbo.Projetos com o código exato {BiBml011016Catalogo.CodigoProjeto} no nome e Project_ID_ACC preenchido."
                : $"Projeto {BiBml011016Catalogo.CodigoProjeto} existe no cadastro mas está inativo.", casados),
            _ => BiBml011016Projeto.Falha(BiBml011016Projeto.Ambiguo,
                $"{ativos.Count} projetos ativos casam {BiBml011016Catalogo.CodigoProjeto}; fixe BiBml011016:ProjectIdAcc no servidor.", ativos),
        };
    }

    private static string Canonico(string acc)
    {
        var t = acc.Trim();
        return (t.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? t[2..] : t).ToLowerInvariant();
    }
}
