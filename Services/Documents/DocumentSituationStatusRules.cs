using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;

namespace APS.WebApp.Services.Documents;

public sealed record ValorOriginalContagem(string Valor, int Documentos);

/// <summary>Contagem por SITUATION (campo Situation da LDP). A emitir = Total − Emitidos − Cancelados (por construção, fechado).</summary>
public sealed record ConsolidadoSituacao(int Total, int Emitidos, int Cancelados, int AEmitir,
    IReadOnlyList<ValorOriginalContagem> AEmitirPorValorOriginal);

/// <summary>
/// Prazo dos documentos A EMITIR (pela Situation), com a regra de prazo EXISTENTE (PmoExecutiveRules.Completar →
/// StatusDocumento), sem alteração. ClassificadosComoEmitidoOuCanceladoPeloStatus = A emitir pela Situation, mas que a
/// regra existente já tirou do prazo por Document_Status/Emission_Date; ficam visíveis para não sumirem da conta.
/// </summary>
public sealed record ConsolidadoPrazo(int Atrasados, int NoPrazo, int SemPrazo, int ClassificadosComoEmitidoOuCanceladoPeloStatus);

public sealed record CategoriaStatusDocumento(string Chave, string Rotulo, int Documentos, IReadOnlyList<ValorOriginalContagem> ValoresOriginais);

/// <summary>Contagem EXCLUSIVA por Document_Status. Fora das 6 categorias: OUTROS (valor original preservado) e NAO_INFORMADO.</summary>
public sealed record ConsolidadoStatusDocumento(int Total, IReadOnlyList<CategoriaStatusDocumento> Categorias);

public sealed record DocumentosConsolidado(
    bool Disponivel, string Fonte, int LinhasDocumentais, int DocumentosUnicos, int LinhasRepetidasIgnoradas,
    ConsolidadoSituacao Situacao, ConsolidadoPrazo Prazo, ConsolidadoStatusDocumento Status, IReadOnlyList<string> Diagnosticos)
{
    public static DocumentosConsolidado Indisponivel(string motivo) => new(false, motivo, 0, 0, 0,
        new(0, 0, 0, 0, Array.Empty<ValorOriginalContagem>()), new(0, 0, 0, 0), new(0, Array.Empty<CategoriaStatusDocumento>()), new[] { motivo });
}

/// <summary>
/// 28/10/2026 — dois consolidados INDEPENDENTES por documento único (revisão vigente):
/// 1) SITUATION (Emitidos/Cancelados/A emitir) usa somente o campo Situation; Document_Status nunca o substitui.
/// 2) STATUS usa somente Document_Status; Situation nunca entra.
/// A regra de emissão gerencial existente (DocumentEmissionRules, robô 4B, StatusDocumento) NÃO é alterada.
/// Somente leitura sobre os documentos já carregados (PmoExecutiveData); AT e linhas sem conteúdo documental ficam fora.
/// </summary>
public static class DocumentSituationStatusRules
{
    public const string Fonte = "dbo.Ldp_Unificada (carga atual): Situation para situação; Document_Status para status; 1 documento = 1 número N&A (revisão vigente).";

    public const string Emitido = "EMITIDO", Cancelado = "CANCELADO", AEmitir = "A_EMITIR";

    /// <summary>Categorias de Document_Status pedidas para o consolidado. Variantes = aliases oficiais PT/EN
    /// (ReportStatusNormalizer/DocumentStatusCanonical) + grafias reais auditadas em 28/10/2026.</summary>
    public static readonly IReadOnlyList<ReportStatusNormalizer.Grupo> CategoriasStatus = new ReportStatusNormalizer.Grupo[]
    {
        new("APROVADO", "Aprovado", new[] { "APPROVED", "APROVADO", "APROVADA" }),
        new("CANCELADO", "Cancelado", new[] { "CANCELED", "CANCELLED", "CANCELADO", "CANCELADA" }),
        // REVISÃO DO CLIENTE = tradução literal de CLIENT REVIEW (3 linhas reais em 28/10/2026).
        new("EM_ANALISE_CLIENTE", "Em análise do cliente", new[] { "CLIENT REVIEW", "EM ANÁLISE DO CLIENTE", "REVISÃO DO CLIENTE" }),
        new("INFORMATIVO", "Informativo", new[] { "INFORMATIVO", "INFORMATIVE", "FOR INFORMATION" }),
        new("ATENDIMENTO_COMENTARIOS", "Atendimento a comentários", new[] { "ADRESSING COMMENTS", "ADDRESSING COMMENTS",
            "ATENDIMENTO A COMENTÁRIOS", "ATENDIMENTO DE COMENTÁRIOS", "EM ATENDIMENTO DE COMENTARIO", "EM ATENDIMENTO DE COMENTÁRIOS" }),
        // ELABORAÇÃO = tradução literal de ELABORATION (9 linhas reais em 28/10/2026).
        new("EM_ELABORACAO", "Em elaboração", new[] { "ELABORATION", "EM ELABORAÇÃO", "ELABORANDO", "ELABORAÇÃO", "IN ELABORATION", "UNDER ELABORATION" }),
    };
    public const string Outros = "OUTROS", NaoInformado = "NAO_INFORMADO";

    private static readonly Dictionary<string, ReportStatusNormalizer.Grupo> IndiceStatus = CategoriasStatus
        .SelectMany(g => g.Variantes.Select(v => (Chave: ReportStatusNormalizer.Chave(v)!, g)))
        .ToDictionary(x => x.Chave, x => x.g, StringComparer.Ordinal);

    /// <summary>EMITIDO / CANCELADO / A_EMITIR somente pela Situation. Qualquer valor fora de EMITIDO/CANCELADO
    /// (TO BE ISSUED, PENDING ISSUED, vazio...) é A emitir — por construção Total = Emitidos + Cancelados + A emitir.</summary>
    public static string ClassificarSituacao(string? situation) => ReportStatusNormalizer.Situation(situation).Key switch
    {
        "EMITIDO" => Emitido,
        "CANCELADO" => Cancelado,
        _ => AEmitir,
    };

    public static (string Chave, string Rotulo) ClassificarStatus(string? documentStatus)
    {
        var chave = ReportStatusNormalizer.Chave(documentStatus);
        if (chave is null) return (NaoInformado, "Não informado");
        return IndiceStatus.TryGetValue(chave, out var g) ? (g.Chave, g.Rotulo) : (Outros, "Outros");
    }

    /// <summary>Chave do documento único: número N&amp;A; sem ele, número do cliente; sem nenhum, a própria linha.</summary>
    public static string ChaveDocumento(PmoDocumento d) =>
        PmoExecutiveRules.Chave(d.Codigo) is { Length: > 0 } codigo ? codigo
        : PmoExecutiveRules.Chave(d.ClientDocumentNumber) is { Length: > 0 } cliente ? "CLIENTE:" + cliente
        : "LINHA:" + d.RowId;

    /// <summary>Ordem de revisão: letras (A, B, …) antes de números (0, 1, …), como na emissão N&amp;A; maior = vigente.</summary>
    public static (int Numerica, long Numero, int Tamanho, string Texto) OrdemRevisao(string? revisao)
    {
        var r = (revisao ?? "").Trim().ToUpperInvariant();
        return long.TryParse(r, out var n) ? (1, n, r.Length, r) : (0, 0, r.Length, r);
    }

    /// <summary>
    /// Situation/Document_Status VIGENTES do documento. Tipo 1: colunas da linha. Tipo 2: o gate mais alto (90 → 60 → 30)
    /// ativo com Situation aplicável (NOT TO BE ISSUED = gate não se aplica); sem gate ativo, o mais alto com Situation;
    /// sem gate, as colunas da linha. Um documento conta UMA vez, nunca uma vez por gate.
    /// </summary>
    public static (string? Situation, string? Status, string Origem) Vigente(PmoDocumento d)
    {
        static bool Aplicavel(PmoGate g) => !string.IsNullOrWhiteSpace(g.Situation) && ReportStatusNormalizer.Chave(g.Situation) != "NOT TO BE ISSUED";
        var gates = d.Gates.Where(Aplicavel).OrderByDescending(g => g.Percentual).ToList();
        var gate = gates.FirstOrDefault(g => g.Ativo) ?? gates.FirstOrDefault();
        return gate is null ? (d.SituacaoEmissao, d.StatusCliente, "LINHA")
            : (gate.Situation, string.IsNullOrWhiteSpace(gate.DocumentStatus) ? d.StatusCliente : gate.DocumentStatus, "GATE_" + gate.Percentual);
    }

    public static DocumentosConsolidado Consolidar(IReadOnlyList<PmoDocumento> documentos, bool semLdp)
    {
        if (semLdp) return DocumentosConsolidado.Indisponivel("Projeto sem LDP: não há Situation/Document_Status oficiais; os documentos do Timesheet não comprovam emissão nem status.");
        var linhas = documentos.Where(d => d.IsDocument && !d.SemConteudoDocumental).ToList();
        var diagnosticos = new List<string>();
        var vigentes = new List<PmoDocumento>();
        foreach (var g in linhas.GroupBy(ChaveDocumento, StringComparer.Ordinal))
        {
            var ordenadas = g.OrderByDescending(d => OrdemRevisao(d.Revisao))
                .ThenByDescending(d => long.TryParse(d.SourceRecordId, out var id) ? id : 0).ToList();
            vigentes.Add(ordenadas[0]);
            if (ordenadas.Count > 1)
                diagnosticos.Add($"{g.Key}: {ordenadas.Count} linhas na carga atual; contada uma vez (revisão vigente {ordenadas[0].Revisao ?? "sem revisão"}).");
        }

        var atual = vigentes.ToDictionary(d => d, Vigente);
        if (atual.Values.Any(v => v.Origem != "LINHA"))
            diagnosticos.Add("LDP Tipo 2: Situation/Status lidos do gate vigente — " + string.Join(", ",
                atual.Values.GroupBy(v => v.Origem).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")) + ".");
        var porSituacao = vigentes.Select(d => (Doc: d, Classe: ClassificarSituacao(atual[d].Situation))).ToList();
        var aEmitir = porSituacao.Where(x => x.Classe == AEmitir).Select(x => x.Doc).ToList();
        var situacao = new ConsolidadoSituacao(vigentes.Count,
            porSituacao.Count(x => x.Classe == Emitido), porSituacao.Count(x => x.Classe == Cancelado), aEmitir.Count,
            aEmitir.GroupBy(d => ReportStatusNormalizer.Texto(atual[d].Situation).Label).OrderByDescending(g => g.Count())
                .Select(g => new ValorOriginalContagem(g.Key, g.Count())).ToArray());

        // Regra de prazo existente, só sobre quem a Situation diz que está A emitir.
        var prazo = new ConsolidadoPrazo(aEmitir.Count(d => d.StatusDocumento == "ATRASADO"), aEmitir.Count(d => d.StatusDocumento == "NO_PRAZO"),
            aEmitir.Count(d => d.StatusDocumento is not ("ATRASADO" or "NO_PRAZO" or "EMITIDO" or "CANCELADO")),
            aEmitir.Count(d => d.StatusDocumento is "EMITIDO" or "CANCELADO"));
        if (prazo.ClassificadosComoEmitidoOuCanceladoPeloStatus > 0)
            diagnosticos.Add($"{prazo.ClassificadosComoEmitidoOuCanceladoPeloStatus} documento(s) A emitir pela Situation já constam como emitidos/cancelados pela regra de prazo existente (Document_Status/Emission_Date): revisar a Situation na LDP.");

        var ordemCategorias = CategoriasStatus.Select(c => c.Chave).Concat(new[] { Outros, NaoInformado }).ToList();
        var categorias = vigentes.Select(d => (Doc: d, Cat: ClassificarStatus(atual[d].Status)))
            .GroupBy(x => x.Cat).OrderBy(g => ordemCategorias.IndexOf(g.Key.Chave))
            .Select(g => new CategoriaStatusDocumento(g.Key.Chave, g.Key.Rotulo, g.Count(),
                g.GroupBy(x => ReportStatusNormalizer.Texto(atual[x.Doc].Status).Label).OrderByDescending(v => v.Count())
                    .Select(v => new ValorOriginalContagem(v.Key, v.Count())).ToArray()))
            .ToList();
        // As 6 categorias pedidas aparecem sempre, mesmo com zero.
        foreach (var c in CategoriasStatus.Where(c => categorias.All(x => x.Chave != c.Chave)))
            categorias.Add(new(c.Chave, c.Rotulo, 0, Array.Empty<ValorOriginalContagem>()));
        categorias = categorias.OrderBy(c => ordemCategorias.IndexOf(c.Chave)).ToList();

        if (situacao.Emitidos + situacao.Cancelados + situacao.AEmitir != situacao.Total || categorias.Sum(c => c.Documentos) != vigentes.Count)
            diagnosticos.Add("ERRO: consolidado documental não fecha com o total de documentos únicos.");
        return new(true, Fonte, linhas.Count, vigentes.Count, linhas.Count - vigentes.Count, situacao, prazo,
            new(vigentes.Count, categorias), diagnosticos);
    }
}
