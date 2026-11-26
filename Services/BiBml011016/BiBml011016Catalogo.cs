namespace APS.WebApp.Services.BiBml011016;

/// <summary>Estado de mapeamento de uma fonte do BI no banco do N&amp;A Connect (contrato docs/CONTRATO_BI_BML011016.md).</summary>
public static class BiBml011016Mapeamento
{
    public const string Mapeada = "MAPEADA";
    public const string Pendente = "PENDENTE";
}

/// <summary>Estados do envelope de dados devolvido ao frontend.</summary>
public static class BiBml011016Estado
{
    public const string Ok = "OK";
    public const string Vazio = "VAZIO";
    public const string Pendente = "PENDENTE";
    public const string Indisponivel = "INDISPONIVEL";
}

/// <param name="Correspondencia">
/// INVENTARIO = tabela do PBIX casada pelo inventário de páginas/campos (docs/ATIVIDADE_CLAUDE_BI_BML011016_20261026.md),
/// ainda sem conferência das expressões DAX do DataModel; COMPROVADA = conferida contra o modelo.
/// </param>
public sealed record BiBml011016Fonte(string Id, string? Origem, string[] TabelasPbix, string Mapeamento, string? MotivoPendente,
    string Correspondencia = "INVENTARIO");

public sealed record BiBml011016Pagina(int Ordem, string Nome, int Visuais, string[] Fontes, string? Observacao = null, bool Estatica = false);

/// <summary>
/// Catálogo FIXO do BI BML011.016 (PBIX SHA256 448947F7…E634). Nada aqui vem do request: o frontend só escolhe um Id da lista.
/// </summary>
public static class BiBml011016Catalogo
{
    public const string CodigoProjeto = "BML011.016";
    public const string Sha256Pbix = "448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634";

    public static readonly IReadOnlyList<BiBml011016Fonte> Fontes =
    [
        new("ldp", "dbo.Ldp_Unificada (snapshot atual PmoLdpSnapshot) + dbo.VW_Ldp_Documento_Gates_Ultima_Versao",
            ["LDP", "DISCIPLINE", "DOCUMENT STATUS", "SITUATION"], BiBml011016Mapeamento.Mapeada, null),
        new("cronograma", "dbo.Cronograma_Projetos (LDP Tipo 1); dbo.VW_Cronograma_Tipo2_Ultima_Versao (LDP Tipo 2)", ["WBX"], BiBml011016Mapeamento.Mapeada,
            "Correspondência de tarefas/WBS; não comprova distribuição temporal AssignmentByDay nem medidas DAX do PBIX."),
        new("cronograma-diario", "dbo.VW_Cronograma_Tarefa_Dia_Ultima_Versao", ["AssignmentByDay"], BiBml011016Mapeamento.Mapeada, null),
        new("programacao-semanal", "dbo.tb_WeeklyEngineeringSchedule", ["KPI"], BiBml011016Mapeamento.Mapeada, null),
        new("cap-externo", "dbo.vw_Latest_ExternalAction", ["ACTION ITEM LIST"], BiBml011016Mapeamento.Mapeada, null),
        new("desvios", "dbo.vw_Latest_ScopeDeviation", ["DESVIO"], BiBml011016Mapeamento.Mapeada, null),
        new("holds", "dbo.vw_Latest_HoldControl", ["HOLDS"], BiBml011016Mapeamento.Mapeada, null),
        new("curva-financeira", null, ["Curva financeira"], BiBml011016Mapeamento.Pendente,
            "Nenhuma fonte do N&A Connect com progresso planejado, receita e valor agregado mensais do BML011.016 foi comprovada " +
            "(costs.* guarda pacotes e custo de horas, sem receita/valor agregado por mês). Necessário: arquivo/sistema de origem " +
            "da tabela 'Curva financeira' do PBIX e aprovação de importação."),
        new("pagamentos", null, ["Pagamentos"], BiBml011016Mapeamento.Pendente,
            "Banco do N&A Connect não possui BM, competência, Nota Fiscal nem prazos/valores de pagamento comprovados para o " +
            "BML011.016. Necessário: arquivo/sistema de origem da tabela 'Pagamentos' do PBIX e aprovação de importação."),
    ];

    public static readonly IReadOnlyList<BiBml011016Pagina> Paginas =
    [
        new(1, "Resumo", 34, ["ldp", "cronograma", "cronograma-diario", "programacao-semanal"],
            "Curva S (AssignmentByDay), weekly planned/actual/deviation (KPI), datas, WBS e status. dCalendario derivado das datas."),
        new(2, "LDP", 18, ["ldp"]),
        new(3, "CAP externo", 13, ["cap-externo"]),
        new(4, "Critical Analysis", 10, [], "Layouts estáticos no PBIX; interações ainda não verificadas.", Estatica: true),
        new(5, "Programação", 8, ["cronograma", "cronograma-diario"], "Hierarquia WBX Level 3..9, início/término e HH de trabalho diário."),
        new(6, "Controle de Desvio", 13, ["desvios"]),
        new(7, "Controle de Holds", 9, ["holds"]),
        new(8, "Curva financeira", 6, ["curva-financeira"]),
        new(9, "Pagamento", 7, ["pagamentos"]),
    ];

    public static BiBml011016Fonte? Fonte(string? id) =>
        Fontes.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    /// <summary>MAPEADA (todas as fontes mapeadas), PARCIAL, PENDENTE (nenhuma) ou ESTATICA (só layout).</summary>
    public static string EstadoPagina(BiBml011016Pagina pagina)
    {
        if (pagina.Estatica) return "ESTATICA";
        var mapeadas = pagina.Fontes.Count(id => Fonte(id)?.Mapeamento == BiBml011016Mapeamento.Mapeada);
        return mapeadas == pagina.Fontes.Length ? "MAPEADA" : mapeadas == 0 ? "PENDENTE" : "PARCIAL";
    }
}
