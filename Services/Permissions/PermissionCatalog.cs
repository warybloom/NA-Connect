namespace APS.WebApp.Services.Permissions;

public static class PermissionResourceTypes
{
    public const string Modulo = "MODULE", Pagina = "PAGE", Aba = "TAB", Relatorio = "REPORT", Dashboard = "DASHBOARD",
        Configuracao = "SETTING", Acao = "ACTION";
}

public static class PermissionScopes
{
    /// <summary>Concedida por atribuições/overrides globais (ProjetoId NULL).</summary>
    public const string Global = "GLOBAL";
    /// <summary>Avaliada no contexto de um projeto (atribuições do projeto + globais).</summary>
    public const string Projeto = "PROJECT";
}

public sealed record PermissionResource(string Key, string? ParentKey, string Label, string Type, string Scope, string? Route, int SortOrder);

/// <summary>
/// CATÁLOGO CANÔNICO de permissões do N&amp;A Connect (fonte única). A identidade é a KEY estável — nunca a URL.
/// Montado a partir da auditoria da navegação real (Layout.NAVEGACAO, App.tsx, productionScreens.ts, abas de CDE,
/// Relatórios, Plant 3D e Painel) e de todos os controllers. O seed de perm.PermissionResources
/// (Migrations/Permissions/*.sql) e os registries do frontend são amarrados a este catálogo por contract tests.
/// </summary>
public static class PermissionCatalog
{
    private static int _ordem;
    private static PermissionResource R(string key, string? parent, string label, string type, string scope, string? route = null) =>
        new(key, parent, label, type, scope, route, ++_ordem * 10);

    private const string G = PermissionScopes.Global, P = PermissionScopes.Projeto;
    private const string MOD = PermissionResourceTypes.Modulo, PAG = PermissionResourceTypes.Pagina, ABA = PermissionResourceTypes.Aba,
        REL = PermissionResourceTypes.Relatorio, DASH = PermissionResourceTypes.Dashboard, CFG = PermissionResourceTypes.Configuracao,
        ACT = PermissionResourceTypes.Acao;

    /// <summary>Segmento da key de cada dashboard do registry (productionScreens.ts): production.&lt;variante&gt;.&lt;segmento&gt;.view.</summary>
    private static readonly IReadOnlyDictionary<string, string> SegmentoDashboard = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ldp"] = "ldp", ["nivelamento-programacao"] = "nivelamento", ["controle-programacao"] = "controle-programacao",
        ["desvio-qualidade"] = "desvio-qualidade", ["cap-interno"] = "cap-interno", ["cap-externo"] = "cap-externo",
        ["change-order"] = "change-order", ["controle-documental"] = "controle-documental", ["hold"] = "hold",
        ["desvio-escopo"] = "desvio-escopo", ["rnc"] = "rnc", ["programacao"] = "programacao",
    };

    /// <summary>Variantes do Controle de Produção: segmento da key → rótulo. Operacional nunca recebe telas de custo absoluto.</summary>
    public static readonly IReadOnlyDictionary<string, string> VariantesProducao = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["managerial"] = "Gerencial", ["operational"] = "Operacional",
    };

    /// <summary>
    /// Os 12 dashboards do Controle de Produção: id do registry → keys aceitas (Gerencial e Operacional). O endpoint
    /// GET api/reports/production/dashboard/{id} exige QUALQUER uma delas no projeto do contexto.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DashboardsProducao =
        SegmentoDashboard.ToDictionary(kv => kv.Key,
            kv => (IReadOnlyList<string>)VariantesProducao.Keys.Select(v => $"production.{v}.{kv.Value}.view").ToArray(), StringComparer.Ordinal);

    /// <summary>Keys de uma tela de produção nas duas variantes (para os endpoints compartilhados).</summary>
    public static string[] ProducaoAmbas(string segmento) => VariantesProducao.Keys.Select(v => $"production.{v}.{segmento}.view").ToArray();

    public static readonly IReadOnlyList<PermissionResource> Todos = Montar();

    private static IEnumerable<PermissionResource> Producao()
    {
        yield return R("production.view", null, "Controle de Produção", MOD, P, "/controle-producao");
        // Permissões separadas para o BI Tipo 2: NÃO reutilizar as keys compartilhadas
        // dos demais painéis, para permitir perfis de produção sem acesso às páginas BI.
        yield return R("production.bi-chs001.view", "production.view", "Gestão à Vista Tipo 2", PAG, P);
        yield return R("production.bi-chs001.cost.view", "production.view", "Gestão à Vista Tipo 2 (HH/Custo)", PAG, P);
        static string Rotulo(string id) => id switch
        {
            "ldp" => "LDP", "programacao" => "Programação Semanal", "cap-externo" => "CAP Externo", "cap-interno" => "CAP Interno",
            "desvio-escopo" => "Desvio de Escopo", "change-order" => "Change Order", "hold" => "HOLD's",
            "desvio-qualidade" => "Desvio de Qualidade", "rnc" => "RNC", "nivelamento-programacao" => "Nivelamento da Programação",
            "controle-programacao" => "Controle da Programação", "controle-documental" => "Controle Documental", _ => id,
        };
        foreach (var (variante, rotulo) in VariantesProducao)
        {
            var v = $"production.{variante}";
            var rota = variante == "managerial" ? "gerencial" : "operacional";
            PermissionResource Dash(string id, string pai) => R($"{v}.{SegmentoDashboard[id]}.view", pai, Rotulo(id), DASH, P, $"/controle-producao/{rota}/{id}");
            yield return R($"{v}.view", "production.view", rotulo, MOD, P);
            yield return R($"{v}.curva-s.view", $"{v}.view", "Curva S", REL, P, $"/controle-producao/{rota}/curva-s");
            foreach (var id in new[] { "ldp", "programacao", "cap-externo", "cap-interno", "desvio-escopo", "change-order", "hold" })
                yield return Dash(id, $"{v}.view");
            yield return R($"{v}.boletim.view", $"{v}.view", "Boletim de Medição", REL, P, $"/controle-producao/{rota}/boletim-medicao");
            yield return R($"{v}.quality.view", $"{v}.view", "Qualidade", PAG, P);
            yield return Dash("desvio-qualidade", $"{v}.quality.view");
            yield return Dash("rnc", $"{v}.quality.view");
            if (variante == "managerial")
            {
                // Telas com HH/custo absolutos: existem SOMENTE no Gerencial.
                yield return R("production.managerial.hh-custo.view", $"{v}.view", "Controle de HH / Custo", REL, P, "/controle-producao/gerencial/controle-hh-custo");
                yield return R("production.financial.view", $"{v}.view", "Análise Financeira", PAG, P);
                yield return R("production.financial.person.view", "production.financial.view", "Pessoa", REL, P, "/controle-producao/gerencial/analise-financeira/pessoa");
                yield return R("production.financial.project.view", "production.financial.view", "Projeto", REL, P, "/controle-producao/gerencial/analise-financeira/projeto");
                yield return R("production.financial.document.view", "production.financial.view", "Documento", REL, P, "/controle-producao/gerencial/analise-financeira/documento");
            }
            yield return R($"{v}.indicadores.view", $"{v}.view", "Indicadores de Produção", PAG, P);
            foreach (var id in new[] { "nivelamento-programacao", "controle-programacao", "controle-documental" })
                yield return Dash(id, $"{v}.indicadores.view");
        }
    }

    private static IReadOnlyList<PermissionResource> Montar()
    {
        _ordem = 0;
        var l = new List<PermissionResource>
        {
            R("home.view", null, "Início", PAG, G, "/dashboard"),

            // ------------------------------------------------------------------------------ CDE
            R("cde.view", null, "CDE", MOD, P, "/cde"),
            R("cde.documents.view", "cde.view", "Documentos", PAG, P, "/cde"),
            R("cde.document-list.view", "cde.documents.view", "Lista de Documentos", ABA, P),
            R("cde.docs.view", "cde.documents.view", "Docs (navegação de pastas e arquivos ACC)", ABA, P, "/projects/:hubId/:projectId/:folderId"),
            R("cde.docs.download", "cde.docs.view", "Baixar arquivo", ACT, P),
            R("cde.docs.upload", "cde.docs.view", "Enviar nova versão de arquivo", ACT, P),
            R("cde.curva-s.view", "cde.documents.view", "Curva S", ABA, P),
            R("cde.model3d.view", "cde.view", "Modelo 3D", PAG, G, "/modelo-3d"),
            R("cde.planning4d.view", "cde.view", "Planejamento 4D", PAG, G, "/planejamento-4d"),
            R("cde.budget5d.view", "cde.view", "Orçamento 5D", PAG, G, "/orcamento-5d"),
        };
        l.AddRange(Producao());

        l.AddRange(new[]
        {
            // Player da Gestão à Vista (TV). A configuração fica em Configurações → Configuração de Telas para TV.
            R("tv.view", null, "Gestão à Vista (apresentação TV)", MOD, G, "/gestao-a-vista"),

            // --------------------------------------------------- Relatórios PMO (/relatorios, legado)
            R("reports.view", null, "Relatórios PMO (legado)", MOD, P, "/relatorios"),
            R("reports.gestao.view", "reports.view", "Gestão à Vista (PMO)", REL, P),
            R("reports.timesheet.view", "reports.view", "Timesheet / Financeiro de horas", REL, P),
            R("reports.producao.view", "reports.view", "Produção (mapa de calor)", REL, P),
            R("reports.curvas.view", "reports.view", "Curva S", REL, P),
            R("reports.avanco4d.view", "reports.view", "Avanço 4D", REL, P),
            R("reports.desvios.view", "reports.view", "Ofensores", REL, P),
            R("reports.weekly.view", "reports.view", "Programação semanal", REL, P),
            R("reports.cronograma.view", "reports.view", "Cronograma", REL, P),
            R("reports.ldp.view", "reports.view", "LDP detalhada", REL, P),
            R("reports.ncr.view", "reports.view", "Qualidade / RNC", REL, P),
            R("reports.op-desvios.view", "reports.view", "Desvios de escopo", REL, P),
            R("reports.hold.view", "reports.view", "HOLD", REL, P),
            R("reports.cap-interno.view", "reports.view", "CAP Interno", REL, P),
            R("reports.cap-externo.view", "reports.view", "CAP Externo", REL, P),
            R("reports.lancamentos.view", "reports.view", "Lançamentos PMO", PAG, G, "/lancamentos"),

            // ------------------------------------------------------- Planejamento (legado, /planejamento/*)
            R("planning.view", null, "Painel de Planejamento", MOD, G),
            R("planning.coordination.view", "planning.view", "Norma de Coordenação", PAG, G, "/planejamento/normas-de-coordenacao"),
            R("planning.coordination.edit", "planning.coordination.view", "Editar normas de coordenação", ACT, G),
            R("planning.weekly.view", "planning.view", "Programação Semanal", PAG, P, "/planejamento/programacao-semanal"),
            R("planning.weekly.current.view", "planning.weekly.view", "Programação Semanal Vigente", PAG, P, "/planejamento/programacao-semanal/vigente"),
            R("planning.weekly.previous.view", "planning.weekly.view", "Análise da Programação da Semana Anterior", PAG, P, "/planejamento/programacao-semanal/semana-anterior"),
            R("planning.weekly.histogram.view", "planning.weekly.view", "Histograma do Projeto", PAG, P, "/planejamento/programacao-semanal/histograma"),
            R("planning.planner.view", "planning.view", "Painel do Planejador", PAG, P, "/planejamento/painel-do-planejador"),
            R("planning.costs.analysis", "planning.view", "Custos do Projeto (homologação)", PAG, P, "/planejamento/custos/profissionais"),

            // -------------------------------------------------------------------------------- Robô
            R("robot.view", null, "Robô (visualizar andamento e configuração)", MOD, G, "/automacao/configuracao-robo"),
            R("robot.configure", "robot.view", "Configurar robô", ACT, G),
            R("robot.run", "robot.view", "Executar robô (cada projeto ainda passa por IProjectReportAccess)", ACT, G),
            R("robot.stop", "robot.view", "Parar robô", ACT, G),
            R("robot.schedule.view", "robot.view", "Agendador do Robô", PAG, G, "/agendamento"),

            // -------------------------------------------------------------------------- Plant 3D
            R("plant3d.view", null, "Plant 3D", MOD, G, "/plant3d"),
            R("plant3d.schedule4d.view", "plant3d.view", "Avanço e Cronograma 4D", REL, G),
            R("plant3d.results.view", "plant3d.view", "Tela de Resultados", REL, G),
            R("plant3d.consolidated.view", "plant3d.view", "Relatórios Consolidados", REL, G),
            R("plant3d.measurement.view", "plant3d.view", "Critérios de Medição", CFG, G),
            R("plant3d.measurement.edit", "plant3d.measurement.view", "Editar critérios de medição", ACT, G),
            R("plant3d.database.view", "plant3d.view", "Banco de Dados", CFG, G),
            R("plant3d.database.edit", "plant3d.database.view", "Editar banco de dados", ACT, G),
            R("plant3d.mapping.view", "plant3d.view", "Mapeamento DE/PARA", CFG, G),
            R("plant3d.mapping.edit", "plant3d.mapping.view", "Editar mapeamento DE/PARA", ACT, G),
            R("plant3d.naming.view", "plant3d.view", "Regras de Nomenclatura", CFG, G),
            R("plant3d.naming.edit", "plant3d.naming.view", "Editar regras de nomenclatura", ACT, G),
            R("plant3d.gen.view", "plant3d.view", "Gestão à Vista (GEN) — oculta", REL, G),

            // --------------------------------------------------------------------------- Configurações
            R("settings.view", null, "Configurações", MOD, G),
            R("settings.projects.view", "settings.view", "Configurações dos Projetos", PAG, G, "/painel"),
            R("settings.projects.edit", "settings.projects.view", "Editar projetos (ativo, Tipo LDP, idioma, norma)", ACT, G),
            R("settings.ldp.view", "settings.view", "Configuração LDP", PAG, G, "/configuracao-ldp"),
            R("settings.ldp.template.view", "settings.ldp.view", "Template", PAG, G, "/configuracao-ldp/template"),
            R("settings.ldp.template.edit", "settings.ldp.template.view", "Editar template", ACT, G),
            R("settings.ldp.cronograma.view", "settings.ldp.view", "Mapeamento do Cronograma", PAG, G, "/configuracao-ldp/cronograma"),
            R("settings.ldp.cronograma.edit", "settings.ldp.cronograma.view", "Editar mapeamento do cronograma", ACT, G),
            R("settings.ldp.espelho.view", "settings.ldp.view", "Geração LDP Espelho", PAG, G, "/configuracao-ldp/espelho"),
            R("settings.ldp.espelho.edit", "settings.ldp.espelho.view", "Editar/gerar espelho", ACT, G),
            R("settings.ldp.controle.view", "settings.ldp.view", "Geração LDP Controle", PAG, G, "/configuracao-ldp/controle"),
            R("settings.ldp.controle.edit", "settings.ldp.controle.view", "Editar LDP controle", ACT, G),
            R("settings.ldp.projeto.view", "settings.ldp.view", "Configuração LDP do projeto", CFG, P),
            R("settings.ldp.projeto.edit", "settings.ldp.projeto.view", "Editar configuração LDP do projeto", ACT, P),
            R("settings.tv.configure", "settings.view", "Configuração de Telas para TV", PAG, G, "/configuracoes/telas-tv"),
            R("settings.templates.view", "settings.view", "Configuração de Templates", PAG, G),
            R("settings.templates.rnc.edit", "settings.templates.view", "Template RNC", CFG, G, "/configuracoes/templates/rnc"),
            R("settings.templates.desvio-escopo.edit", "settings.templates.view", "Template Desvio de Escopo", CFG, G, "/configuracoes/templates/desvio-de-escopo"),
            R("settings.templates.hold.edit", "settings.templates.view", "Template Controle de HOLD", CFG, G, "/configuracoes/templates/controle-de-hold"),
            R("settings.templates.cap-interno.edit", "settings.templates.view", "Template CAP Interno", CFG, G, "/configuracoes/templates/cap-interno"),
            R("settings.templates.cap-externo.edit", "settings.templates.view", "Template CAP Externo", CFG, G, "/configuracoes/templates/cap-externo"),
            R("settings.templates.desvio.edit", "settings.templates.view", "Template Controle de Desvio", CFG, G, "/configuracoes/templates/controle-de-desvio"),
            R("settings.templates.ordem-servico.edit", "settings.templates.view", "Template Ordem de Serviço", CFG, G, "/configuracoes/templates/ordem-de-servico"),
            R("settings.templates.contratos.edit", "settings.templates.view", "Template Controle de Contratos", CFG, G, "/configuracoes/templates/controle-de-contratos"),
            R("settings.templates.custos.edit", "settings.templates.view", "Template Controle de Custos", CFG, G, "/configuracoes/templates/controle-de-custos"),
            R("settings.quality-template.view", "settings.view", "Configurações de Template Qualidade (legado)", PAG, G, "/templates"),
            R("settings.quality-template.edit", "settings.quality-template.view", "Editar templates de qualidade", ACT, G),
            R("settings.ldp-legacy.view", "settings.view", "Configurador de LDP (OLD / demonstração)", PAG, G, "/configurar-ldp"),
            R("settings.ocr.view", "settings.view", "Auditoria de Qualidade de Documentos", PAG, G, "/ocr"),

            // ------------------------------------------------------------ Ações sensíveis ACC / LDP
            R("projects.create", null, "Criar projeto a partir de template (ACC)", ACT, G),
            R("ldp.sync.execute", null, "Ações de LDP no ACC (sincronizar, placeholders, webhook, atualizar do banco)", ACT, P),

            // ------------------------------------------------------------------------------ Sistema
            R("permissions.manage", null, "Gerenciar permissões e perfis", ACT, G, "/configuracoes/permissoes"),
            // Controle fino: acesso à tela não concede edição nem acesso à outra tela.
            R("quality.view", null, "Qualidade", MOD, P),
            R("quality.audit.view", "quality.view", "Auditoria da Informação", PAG, P, "/qualidade/auditoria"),
            R("quality.audit.edit", "quality.audit.view", "Redistribuir documentos / classificar atividades", ACT, P),
            R("quality.unassigned.view", "quality.view", "HH sem Número do Documento", PAG, P, "/qualidade/hh-sem-documento"),
            R("quality.unassigned.edit", "quality.unassigned.view", "Editar roteamento e tratar HH", ACT, P),
            R("quality.outside-ldp.view", "quality.view", "Timesheet fora da LDP", PAG, P),
            R("quality.outside-ldp.edit", "quality.outside-ldp.view", "Direcionar documentos fora da LDP", ACT, P),
            R("quality.professionals.view", "quality.view", "Classificação de Profissionais", PAG, P, "/qualidade/classificacao-profissionais"),
            R("quality.professionals.edit", "quality.professionals.view", "Editar classificação de profissionais", ACT, P),
            R("quality.comparison.view", "quality.view", "Comparativo de HH e Custos", REL, P, "/qualidade/comparativo-hh-custos"),
            R("quality.divergences.view", "quality.view", "Auditoria de Divergências", REL, P, "/qualidade/auditoria-divergencias"),
            R("quality.progress.view", "quality.view", "Configuração de Avanço Físico", PAG, P, "/qualidade/configuracao-avanco-fisico"),
            R("quality.progress.edit", "quality.progress.view", "Alterar e confirmar avanço físico", ACT, P),
            R("quality.documents.view", "quality.view", "Auditoria de Qualidade de Documentos", PAG, P, "/qualidade/auditoria-documentos"),
            R("quality.documents.execute", "quality.documents.view", "Executar auditoria de documentos", ACT, P),
            R("quality.folders.view", "quality.view", "Configuração de Pastas", PAG, P, "/qualidade/configuracao-pastas-documentos"),
            R("quality.folders.edit", "quality.folders.view", "Alterar configuração de pastas", ACT, P),
            R("quality.ldp-acc.view", "quality.view", "Auditoria LDP x ACC", PAG, P, "/qualidade/auditoria-ldp-acc"),
            R("quality.ldp-acc.run", "quality.ldp-acc.view", "Executar conciliação LDP x ACC", ACT, P),
            R("quality.disciplines.view", "quality.view", "Nomes das Disciplinas", PAG, P, "/qualidade/auditoria-disciplinas"),
            R("quality.disciplines.edit", "quality.disciplines.view", "Padronizar nomes e aliases", ACT, P),

            // Custos: permissões independentes por tela e ação, sem acesso por herança.
            R("planning.costs.professionals.view", "planning.costs.analysis", "Custos de Profissionais", PAG, P, "/planejamento/custos/profissionais"),
            R("planning.costs.professionals.edit", "planning.costs.professionals.view", "Editar salários e taxas", ACT, P),
            R("planning.costs.taxes.view", "planning.costs.analysis", "Impostos do Projeto", PAG, P, "/planejamento/custos/impostos"),
            R("planning.costs.taxes.edit", "planning.costs.taxes.view", "Editar impostos", ACT, P),
            R("planning.costs.softwares.view", "planning.costs.analysis", "Softwares e Computadores", PAG, P, "/planejamento/custos/softwares"),
            R("planning.costs.softwares.edit", "planning.costs.softwares.view", "Editar softwares e computadores", ACT, P),
            R("planning.costs.packages.view", "planning.costs.analysis", "Pacotes de Custos", PAG, P, "/planejamento/custos/pacotes"),
            R("planning.costs.packages.edit", "planning.costs.packages.view", "Editar pacotes", ACT, P),
            R("system.tenants.view", null, "Administração de tenants (Master Admin, legado)", PAG, G, "/master-admin"),
            R("system.diagnostics.execute", null, "Diagnósticos técnicos (ACC)", ACT, G),
            R("system.commercial.view", null, "Dados comerciais da conta", ACT, G),
        });
        return l;
    }

    private static readonly Dictionary<string, PermissionResource> PorKey = Todos.ToDictionary(r => r.Key, StringComparer.Ordinal);

    public static bool Existe(string key) => PorKey.ContainsKey(key);
    public static PermissionResource? Obter(string key) => PorKey.TryGetValue(key, out var r) ? r : null;
    public static IReadOnlyCollection<string> Keys => PorKey.Keys;

    /// <summary>A key e todos os seus descendentes (marcar o pai marca os filhos na tela de administração).</summary>
    public static IReadOnlyList<string> ComDescendentes(string key)
    {
        var resultado = new List<string> { key };
        foreach (var filho in Todos.Where(r => r.ParentKey == key)) resultado.AddRange(ComDescendentes(filho.Key));
        return resultado;
    }
}
