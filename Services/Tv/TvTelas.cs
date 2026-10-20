namespace APS.WebApp.Services.Tv;

/// <summary>
/// Tela que pode ir para a TV: id estável, se consolida vários projetos e as keys da rota (QUALQUER uma).
/// ProjetoExclusivo (opcional) = único Project_ID_ACC aceito nos itens desta tela (TvConfigurationRules recusa outro).
/// </summary>
public sealed record TvTela(string Id, bool MultiProjeto, IReadOnlyList<string> PermissionKeys)
{
    public string? ProjetoExclusivo { get; init; }
}

/// <summary>
/// Catálogo das telas aceitas na TV = telas IMPLEMENTADAS do Controle de Produção (ClientApp/src/features/production/
/// productionRoutes.ts → PRODUCTION_NAV, catálogo derivado em ClientApp/src/features/tv/tvScreens.tsx) + o painel PMO antigo
/// "gestao-a-vista" (compatibilidade). Telas "em desenvolvimento" NÃO entram (são recusadas ao salvar).
///
/// CONTRATO: ClientApp/src/features/tv/__tests__/tvContrato.test.ts lê ESTE arquivo e exige que os ids, MultiProjeto e
/// keys sejam exatamente os do catálogo do frontend — uma tela nova no Controle de Produção quebra o teste até ser
/// registrada aqui. Mantenha uma linha T(...) por tela, com literais.
///
/// IDs estáveis: dashboards = screenId do registry; "analise-financeira" = Análise Financeira → Visão Geral; demais =
/// slug da rota com "/" trocado por "-". Nunca renomear (apresentações salvas referenciam esses ids).
/// MultiProjeto=false: a tela exibe um projeto por vez — o item da TV só pode ter um projeto.
/// As keys servem para listar as telas autorizadas ao usuário; os dados continuam protegidos pelos endpoints de cada tela.
/// </summary>
public static class TvTelas
{
    private const bool Multi = true, Unico = false;
    private static TvTela T(string id, bool multiProjeto, params string[] keys) => new(id, multiProjeto, keys);

    /// <summary>CHS001 - ETHANOL PLANT (dbo.Projetos.Project_ID_ACC): único projeto das telas Gestão à Vista Tipo 2.</summary>
    public const string ProjetoChs001 = "b.fcdb7ebd-1b23-440d-a04b-90391098522e";

    public static readonly IReadOnlyList<TvTela> Todas = new[]
    {
        // Principais
        T("resumo", Multi, "reports.gestao.view", "production.managerial.hh-custo.view"),
        T("gestao-hh-custo", Unico, "production.managerial.hh-custo.view"),
        T("lista-documentos", Unico, "cde.document-list.view"),
        T("curva-s", Multi, "production.managerial.curva-s.view", "production.operational.curva-s.view"),
        T("controle-hh-custo", Unico, "production.managerial.hh-custo.view"),
        T("resumo-tv-teste", Multi, "permissions.manage"),
        // Produção (dashboards do registry ProductionDashboards — mesmas keys de PermissionCatalog.DashboardsProducao)
        T("ldp", Multi, "production.managerial.ldp.view", "production.operational.ldp.view"),
        T("programacao", Multi, "production.managerial.programacao.view", "production.operational.programacao.view"),
        T("cap-externo", Multi, "production.managerial.cap-externo.view", "production.operational.cap-externo.view"),
        T("cap-interno", Multi, "production.managerial.cap-interno.view", "production.operational.cap-interno.view"),
        T("desvio-escopo", Multi, "production.managerial.desvio-escopo.view", "production.operational.desvio-escopo.view"),
        T("change-order", Multi, "production.managerial.change-order.view", "production.operational.change-order.view"),
        T("hold", Multi, "production.managerial.hold.view", "production.operational.hold.view"),
        T("desvio-qualidade", Multi, "production.managerial.desvio-qualidade.view", "production.operational.desvio-qualidade.view"),
        T("rnc", Multi, "production.managerial.rnc.view", "production.operational.rnc.view"),
        T("nivelamento-programacao", Multi, "production.managerial.nivelamento.view", "production.operational.nivelamento.view"),
        T("controle-programacao", Multi, "production.managerial.controle-programacao.view", "production.operational.controle-programacao.view"),
        T("controle-documental", Multi, "production.managerial.controle-documental.view", "production.operational.controle-documental.view"),
        // Lançamentos
        T("lancamentos-pessoas", Unico, "production.managerial.hh-custo.view", "reports.gestao.view"),
        T("lancamentos-disciplinas", Unico, "production.managerial.hh-custo.view", "reports.gestao.view"),
        T("lancamentos-documentos", Unico, "production.managerial.hh-custo.view", "reports.gestao.view"),
        // Análise Financeira
        T("analise-financeira", Multi, "production.financial.project.view"),
        T("analise-financeira-projeto", Multi, "production.financial.project.view"),
        T("analise-financeira-documento", Multi, "production.financial.document.view"),
        // Gestão à Vista (Jamile): quatro telas do PDF + detalhamentos por disciplina. Multiprojeto desde 15/10/2026: o slide
        // pode consolidar vários projetos (cada fonte consultada por projeto; percentuais pelos totais; falha em qualquer
        // projeto bloqueia os números no frontend). O editor também permite um slide por projeto.
        T("gestao-a-vista-visao-geral", Multi, "reports.gestao.view"),
        T("gestao-a-vista-programacao", Multi, "reports.gestao.view"),
        T("gestao-a-vista-documentacao", Multi, "reports.gestao.view"),
        T("gestao-a-vista-indicadores", Multi, "production.managerial.hh-custo.view"),
        T("gestao-a-vista-hh-disciplina", Multi, "reports.gestao.view"),
        T("gestao-a-vista-custo-disciplina", Multi, "production.managerial.hh-custo.view"),
        // Gestão à Vista Tipo 2 (19/10/2026): reprodução do BI CHS001 (ClientApp/src/features/production/biChs001, catálogo
        // próprio em tvScreens.tsx — fora de PRODUCTION_NAV). SOMENTE as 10 páginas prioritárias (PRIORIDADE_10); as outras 13
        // páginas do BI não são aceitas. Um projeto por item e EXCLUSIVAS da CHS001 (outro projeto é recusado ao salvar).
        T("gestao-vista-tipo2-resumo-30", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-resumo-60", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-resumo-90", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-ldp-3d-30", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-ldp-3d-60", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-ldp-3d-90", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-ldp-engineering-30", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-ldp-engineering-60", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-ldp-engineering-90", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        T("gestao-vista-tipo2-programacao", Unico, "reports.gestao.view") with { ProjetoExclusivo = ProjetoChs001 },
        // Painel PMO antigo (fora do Controle de Produção) — mantido para as apresentações já salvas.
        T("gestao-a-vista", Multi, "reports.gestao.view"),
    };

    private static readonly Dictionary<string, TvTela> PorId = Todas.ToDictionary(t => t.Id, StringComparer.Ordinal);

    public static TvTela? Obter(string? id) => id is not null && PorId.TryGetValue(id, out var t) ? t : null;

    /// <summary>Telas que o usuário pode abrir em ALGUM projeto (keys globais ou de projeto). Master/sem enforcement = todas.</summary>
    public static IReadOnlyList<string> Autorizadas(bool liberaTudo, IEnumerable<string> keysDoUsuario)
    {
        if (liberaTudo) return Todas.Select(t => t.Id).ToArray();
        var tem = new HashSet<string>(keysDoUsuario, StringComparer.Ordinal);
        return Todas.Where(t => t.PermissionKeys.Any(tem.Contains)).Select(t => t.Id).ToArray();
    }
}
