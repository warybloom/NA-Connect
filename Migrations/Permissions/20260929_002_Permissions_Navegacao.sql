/*
    N&A Connect — Permissões: reorganização da navegação (sidebar/rotas)
    Versão: 20260929_002   (requer 20260929_001 aplicada)

    • NÃO DESTRUTIVO: sem DROP, sem DELETE, sem ALTER TABLE. Idempotente (pode ser reexecutado).
    • Sincroniza perm.PermissionResources com o catálogo atual (Services/Permissions/PermissionCatalog.cs — um teste
      de contrato garante que o bloco CATALOG abaixo é idêntico a ele): insere keys novas, atualiza rótulo/pai/tipo/
      escopo/rota das existentes e marca Ativo = 0 nas keys que saíram do catálogo (as linhas permanecem).
    • TRADUZ acessos já concedidos nas keys substituídas (perfis e exceções individuais), sem apagar as linhas antigas:
        production.<tela>.view   → production.managerial.<tela>.view + production.operational.<tela>.view
        planning4d.view          → cde.planning4d.view
        budget5d.view            → cde.budget5d.view
        federated-model.view     → cde.model3d.view
        planning.weekly.view     → + planning.weekly.current.view (a tela existente passou a ser "Vigente")
    • Perfil "Administrador" (todas as funcionalidades) recebe as keys que ESTE script criar.
      Nenhum outro perfil é alterado além da tradução acima.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'perm.PermissionResources', N'U') IS NULL
    THROW 50001, N'Aplique antes Migrations/Permissions/20260929_001_Permissions.sql.', 1;
GO

DECLARE @catalogo TABLE (PermissionKey NVARCHAR(120) PRIMARY KEY, ParentKey NVARCHAR(120) NULL, Label NVARCHAR(200) NOT NULL,
    ResourceType NVARCHAR(20) NOT NULL, ScopeType NVARCHAR(10) NOT NULL, Route NVARCHAR(200) NULL, SortOrder INT NOT NULL);
INSERT INTO @catalogo (PermissionKey, ParentKey, Label, ResourceType, ScopeType, Route, SortOrder) VALUES
-- BEGIN CATALOG (gerado de PermissionCatalog.Todos)
    (N'home.view', NULL, N'Início', N'PAGE', N'GLOBAL', N'/dashboard', 10),
    (N'cde.view', NULL, N'CDE', N'MODULE', N'PROJECT', N'/cde', 20),
    (N'cde.documents.view', N'cde.view', N'Documentos', N'PAGE', N'PROJECT', N'/cde', 30),
    (N'cde.document-list.view', N'cde.documents.view', N'Lista de Documentos', N'TAB', N'PROJECT', NULL, 40),
    (N'cde.docs.view', N'cde.documents.view', N'Docs (navegação de pastas e arquivos ACC)', N'TAB', N'PROJECT', N'/projects/:hubId/:projectId/:folderId', 50),
    (N'cde.docs.download', N'cde.docs.view', N'Baixar arquivo', N'ACTION', N'PROJECT', NULL, 60),
    (N'cde.docs.upload', N'cde.docs.view', N'Enviar nova versão de arquivo', N'ACTION', N'PROJECT', NULL, 70),
    (N'cde.curva-s.view', N'cde.documents.view', N'Curva S', N'TAB', N'PROJECT', NULL, 80),
    (N'cde.model3d.view', N'cde.view', N'Modelo 3D', N'PAGE', N'GLOBAL', N'/modelo-3d', 90),
    (N'cde.planning4d.view', N'cde.view', N'Planejamento 4D', N'PAGE', N'GLOBAL', N'/planejamento-4d', 100),
    (N'cde.budget5d.view', N'cde.view', N'Orçamento 5D', N'PAGE', N'GLOBAL', N'/orcamento-5d', 110),
    (N'production.view', NULL, N'Controle de Produção', N'MODULE', N'PROJECT', N'/controle-producao', 120),
    (N'production.managerial.view', N'production.view', N'Gerencial', N'MODULE', N'PROJECT', NULL, 130),
    (N'production.managerial.curva-s.view', N'production.managerial.view', N'Curva S', N'REPORT', N'PROJECT', N'/controle-producao/gerencial/curva-s', 140),
    (N'production.managerial.ldp.view', N'production.managerial.view', N'LDP', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/ldp', 150),
    (N'production.managerial.programacao.view', N'production.managerial.view', N'Programação Semanal', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/programacao', 160),
    (N'production.managerial.cap-externo.view', N'production.managerial.view', N'CAP Externo', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/cap-externo', 170),
    (N'production.managerial.cap-interno.view', N'production.managerial.view', N'CAP Interno', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/cap-interno', 180),
    (N'production.managerial.desvio-escopo.view', N'production.managerial.view', N'Desvio de Escopo', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/desvio-escopo', 190),
    (N'production.managerial.change-order.view', N'production.managerial.view', N'Change Order', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/change-order', 200),
    (N'production.managerial.hold.view', N'production.managerial.view', N'HOLD''s', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/hold', 210),
    (N'production.managerial.boletim.view', N'production.managerial.view', N'Boletim de Medição', N'REPORT', N'PROJECT', N'/controle-producao/gerencial/boletim-medicao', 220),
    (N'production.managerial.quality.view', N'production.managerial.view', N'Qualidade', N'PAGE', N'PROJECT', NULL, 230),
    (N'production.managerial.desvio-qualidade.view', N'production.managerial.quality.view', N'Desvio de Qualidade', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/desvio-qualidade', 240),
    (N'production.managerial.rnc.view', N'production.managerial.quality.view', N'RNC', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/rnc', 250),
    (N'production.managerial.hh-custo.view', N'production.managerial.view', N'Controle de HH / Custo', N'REPORT', N'PROJECT', N'/controle-producao/gerencial/controle-hh-custo', 260),
    (N'production.financial.view', N'production.managerial.view', N'Análise Financeira', N'PAGE', N'PROJECT', NULL, 270),
    (N'production.financial.person.view', N'production.financial.view', N'Pessoa', N'REPORT', N'PROJECT', N'/controle-producao/gerencial/analise-financeira/pessoa', 280),
    (N'production.financial.project.view', N'production.financial.view', N'Projeto', N'REPORT', N'PROJECT', N'/controle-producao/gerencial/analise-financeira/projeto', 290),
    (N'production.financial.document.view', N'production.financial.view', N'Documento', N'REPORT', N'PROJECT', N'/controle-producao/gerencial/analise-financeira/documento', 300),
    (N'production.managerial.indicadores.view', N'production.managerial.view', N'Indicadores de Produção', N'PAGE', N'PROJECT', NULL, 310),
    (N'production.managerial.nivelamento.view', N'production.managerial.indicadores.view', N'Nivelamento da Programação', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/nivelamento-programacao', 320),
    (N'production.managerial.controle-programacao.view', N'production.managerial.indicadores.view', N'Controle da Programação', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/controle-programacao', 330),
    (N'production.managerial.controle-documental.view', N'production.managerial.indicadores.view', N'Controle Documental', N'DASHBOARD', N'PROJECT', N'/controle-producao/gerencial/controle-documental', 340),
    (N'production.operational.view', N'production.view', N'Operacional', N'MODULE', N'PROJECT', NULL, 350),
    (N'production.operational.curva-s.view', N'production.operational.view', N'Curva S', N'REPORT', N'PROJECT', N'/controle-producao/operacional/curva-s', 360),
    (N'production.operational.ldp.view', N'production.operational.view', N'LDP', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/ldp', 370),
    (N'production.operational.programacao.view', N'production.operational.view', N'Programação Semanal', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/programacao', 380),
    (N'production.operational.cap-externo.view', N'production.operational.view', N'CAP Externo', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/cap-externo', 390),
    (N'production.operational.cap-interno.view', N'production.operational.view', N'CAP Interno', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/cap-interno', 400),
    (N'production.operational.desvio-escopo.view', N'production.operational.view', N'Desvio de Escopo', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/desvio-escopo', 410),
    (N'production.operational.change-order.view', N'production.operational.view', N'Change Order', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/change-order', 420),
    (N'production.operational.hold.view', N'production.operational.view', N'HOLD''s', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/hold', 430),
    (N'production.operational.boletim.view', N'production.operational.view', N'Boletim de Medição', N'REPORT', N'PROJECT', N'/controle-producao/operacional/boletim-medicao', 440),
    (N'production.operational.quality.view', N'production.operational.view', N'Qualidade', N'PAGE', N'PROJECT', NULL, 450),
    (N'production.operational.desvio-qualidade.view', N'production.operational.quality.view', N'Desvio de Qualidade', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/desvio-qualidade', 460),
    (N'production.operational.rnc.view', N'production.operational.quality.view', N'RNC', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/rnc', 470),
    (N'production.operational.indicadores.view', N'production.operational.view', N'Indicadores de Produção', N'PAGE', N'PROJECT', NULL, 480),
    (N'production.operational.nivelamento.view', N'production.operational.indicadores.view', N'Nivelamento da Programação', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/nivelamento-programacao', 490),
    (N'production.operational.controle-programacao.view', N'production.operational.indicadores.view', N'Controle da Programação', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/controle-programacao', 500),
    (N'production.operational.controle-documental.view', N'production.operational.indicadores.view', N'Controle Documental', N'DASHBOARD', N'PROJECT', N'/controle-producao/operacional/controle-documental', 510),
    (N'tv.view', NULL, N'Gestão à Vista (apresentação TV)', N'MODULE', N'GLOBAL', N'/gestao-a-vista', 520),
    (N'reports.view', NULL, N'Relatórios PMO (legado)', N'MODULE', N'PROJECT', N'/relatorios', 530),
    (N'reports.gestao.view', N'reports.view', N'Gestão à Vista (PMO)', N'REPORT', N'PROJECT', NULL, 540),
    (N'reports.timesheet.view', N'reports.view', N'Timesheet / Financeiro de horas', N'REPORT', N'PROJECT', NULL, 550),
    (N'reports.producao.view', N'reports.view', N'Produção (mapa de calor)', N'REPORT', N'PROJECT', NULL, 560),
    (N'reports.curvas.view', N'reports.view', N'Curva S', N'REPORT', N'PROJECT', NULL, 570),
    (N'reports.avanco4d.view', N'reports.view', N'Avanço 4D', N'REPORT', N'PROJECT', NULL, 580),
    (N'reports.desvios.view', N'reports.view', N'Ofensores', N'REPORT', N'PROJECT', NULL, 590),
    (N'reports.weekly.view', N'reports.view', N'Programação semanal', N'REPORT', N'PROJECT', NULL, 600),
    (N'reports.cronograma.view', N'reports.view', N'Cronograma', N'REPORT', N'PROJECT', NULL, 610),
    (N'reports.ldp.view', N'reports.view', N'LDP detalhada', N'REPORT', N'PROJECT', NULL, 620),
    (N'reports.ncr.view', N'reports.view', N'Qualidade / RNC', N'REPORT', N'PROJECT', NULL, 630),
    (N'reports.op-desvios.view', N'reports.view', N'Desvios de escopo', N'REPORT', N'PROJECT', NULL, 640),
    (N'reports.hold.view', N'reports.view', N'HOLD', N'REPORT', N'PROJECT', NULL, 650),
    (N'reports.cap-interno.view', N'reports.view', N'CAP Interno', N'REPORT', N'PROJECT', NULL, 660),
    (N'reports.cap-externo.view', N'reports.view', N'CAP Externo', N'REPORT', N'PROJECT', NULL, 670),
    (N'reports.lancamentos.view', N'reports.view', N'Lançamentos PMO', N'PAGE', N'GLOBAL', N'/lancamentos', 680),
    (N'planning.view', NULL, N'Painel de Planejamento', N'MODULE', N'GLOBAL', NULL, 690),
    (N'planning.coordination.view', N'planning.view', N'Norma de Coordenação', N'PAGE', N'GLOBAL', N'/planejamento/normas-de-coordenacao', 700),
    (N'planning.coordination.edit', N'planning.coordination.view', N'Editar normas de coordenação', N'ACTION', N'GLOBAL', NULL, 710),
    (N'planning.weekly.view', N'planning.view', N'Programação Semanal', N'PAGE', N'PROJECT', N'/planejamento/programacao-semanal', 720),
    (N'planning.weekly.current.view', N'planning.weekly.view', N'Programação Semanal Vigente', N'PAGE', N'PROJECT', N'/planejamento/programacao-semanal/vigente', 730),
    (N'planning.weekly.previous.view', N'planning.weekly.view', N'Análise da Programação da Semana Anterior', N'PAGE', N'PROJECT', N'/planejamento/programacao-semanal/semana-anterior', 740),
    (N'planning.weekly.histogram.view', N'planning.weekly.view', N'Histograma do Projeto', N'PAGE', N'PROJECT', N'/planejamento/programacao-semanal/histograma', 750),
    (N'planning.planner.view', N'planning.view', N'Painel do Planejador', N'PAGE', N'PROJECT', N'/planejamento/painel-do-planejador', 760),
    (N'robot.view', NULL, N'Robô (visualizar andamento e configuração)', N'MODULE', N'GLOBAL', N'/automacao/configuracao-robo', 770),
    (N'robot.configure', N'robot.view', N'Configurar robô', N'ACTION', N'GLOBAL', NULL, 780),
    (N'robot.run', N'robot.view', N'Executar robô (cada projeto ainda passa por IProjectReportAccess)', N'ACTION', N'GLOBAL', NULL, 790),
    (N'robot.stop', N'robot.view', N'Parar robô', N'ACTION', N'GLOBAL', NULL, 800),
    (N'robot.schedule.view', N'robot.view', N'Agendador do Robô', N'PAGE', N'GLOBAL', N'/agendamento', 810),
    (N'plant3d.view', NULL, N'Plant 3D', N'MODULE', N'GLOBAL', N'/plant3d', 820),
    (N'plant3d.schedule4d.view', N'plant3d.view', N'Avanço e Cronograma 4D', N'REPORT', N'GLOBAL', NULL, 830),
    (N'plant3d.results.view', N'plant3d.view', N'Tela de Resultados', N'REPORT', N'GLOBAL', NULL, 840),
    (N'plant3d.consolidated.view', N'plant3d.view', N'Relatórios Consolidados', N'REPORT', N'GLOBAL', NULL, 850),
    (N'plant3d.measurement.view', N'plant3d.view', N'Critérios de Medição', N'SETTING', N'GLOBAL', NULL, 860),
    (N'plant3d.measurement.edit', N'plant3d.measurement.view', N'Editar critérios de medição', N'ACTION', N'GLOBAL', NULL, 870),
    (N'plant3d.database.view', N'plant3d.view', N'Banco de Dados', N'SETTING', N'GLOBAL', NULL, 880),
    (N'plant3d.database.edit', N'plant3d.database.view', N'Editar banco de dados', N'ACTION', N'GLOBAL', NULL, 890),
    (N'plant3d.mapping.view', N'plant3d.view', N'Mapeamento DE/PARA', N'SETTING', N'GLOBAL', NULL, 900),
    (N'plant3d.mapping.edit', N'plant3d.mapping.view', N'Editar mapeamento DE/PARA', N'ACTION', N'GLOBAL', NULL, 910),
    (N'plant3d.naming.view', N'plant3d.view', N'Regras de Nomenclatura', N'SETTING', N'GLOBAL', NULL, 920),
    (N'plant3d.naming.edit', N'plant3d.naming.view', N'Editar regras de nomenclatura', N'ACTION', N'GLOBAL', NULL, 930),
    (N'plant3d.gen.view', N'plant3d.view', N'Gestão à Vista (GEN) — oculta', N'REPORT', N'GLOBAL', NULL, 940),
    (N'settings.view', NULL, N'Configurações', N'MODULE', N'GLOBAL', NULL, 950),
    (N'settings.projects.view', N'settings.view', N'Configurações dos Projetos', N'PAGE', N'GLOBAL', N'/painel', 960),
    (N'settings.projects.edit', N'settings.projects.view', N'Editar projetos (ativo, Tipo LDP, idioma, norma)', N'ACTION', N'GLOBAL', NULL, 970),
    (N'settings.ldp.view', N'settings.view', N'Configuração LDP', N'PAGE', N'GLOBAL', N'/configuracao-ldp', 980),
    (N'settings.ldp.template.view', N'settings.ldp.view', N'Template', N'PAGE', N'GLOBAL', N'/configuracao-ldp/template', 990),
    (N'settings.ldp.template.edit', N'settings.ldp.template.view', N'Editar template', N'ACTION', N'GLOBAL', NULL, 1000),
    (N'settings.ldp.cronograma.view', N'settings.ldp.view', N'Mapeamento do Cronograma', N'PAGE', N'GLOBAL', N'/configuracao-ldp/cronograma', 1010),
    (N'settings.ldp.cronograma.edit', N'settings.ldp.cronograma.view', N'Editar mapeamento do cronograma', N'ACTION', N'GLOBAL', NULL, 1020),
    (N'settings.ldp.espelho.view', N'settings.ldp.view', N'Geração LDP Espelho', N'PAGE', N'GLOBAL', N'/configuracao-ldp/espelho', 1030),
    (N'settings.ldp.espelho.edit', N'settings.ldp.espelho.view', N'Editar/gerar espelho', N'ACTION', N'GLOBAL', NULL, 1040),
    (N'settings.ldp.controle.view', N'settings.ldp.view', N'Geração LDP Controle', N'PAGE', N'GLOBAL', N'/configuracao-ldp/controle', 1050),
    (N'settings.ldp.controle.edit', N'settings.ldp.controle.view', N'Editar LDP controle', N'ACTION', N'GLOBAL', NULL, 1060),
    (N'settings.ldp.projeto.view', N'settings.ldp.view', N'Configuração LDP do projeto', N'SETTING', N'PROJECT', NULL, 1070),
    (N'settings.ldp.projeto.edit', N'settings.ldp.projeto.view', N'Editar configuração LDP do projeto', N'ACTION', N'PROJECT', NULL, 1080),
    (N'settings.tv.configure', N'settings.view', N'Configuração de Telas para TV', N'PAGE', N'GLOBAL', N'/configuracoes/telas-tv', 1090),
    (N'settings.templates.view', N'settings.view', N'Configuração de Templates', N'PAGE', N'GLOBAL', NULL, 1100),
    (N'settings.templates.rnc.edit', N'settings.templates.view', N'Template RNC', N'SETTING', N'GLOBAL', N'/configuracoes/templates/rnc', 1110),
    (N'settings.templates.desvio-escopo.edit', N'settings.templates.view', N'Template Desvio de Escopo', N'SETTING', N'GLOBAL', N'/configuracoes/templates/desvio-de-escopo', 1120),
    (N'settings.templates.hold.edit', N'settings.templates.view', N'Template Controle de HOLD', N'SETTING', N'GLOBAL', N'/configuracoes/templates/controle-de-hold', 1130),
    (N'settings.templates.cap-interno.edit', N'settings.templates.view', N'Template CAP Interno', N'SETTING', N'GLOBAL', N'/configuracoes/templates/cap-interno', 1140),
    (N'settings.templates.cap-externo.edit', N'settings.templates.view', N'Template CAP Externo', N'SETTING', N'GLOBAL', N'/configuracoes/templates/cap-externo', 1150),
    (N'settings.templates.desvio.edit', N'settings.templates.view', N'Template Controle de Desvio', N'SETTING', N'GLOBAL', N'/configuracoes/templates/controle-de-desvio', 1160),
    (N'settings.templates.ordem-servico.edit', N'settings.templates.view', N'Template Ordem de Serviço', N'SETTING', N'GLOBAL', N'/configuracoes/templates/ordem-de-servico', 1170),
    (N'settings.templates.contratos.edit', N'settings.templates.view', N'Template Controle de Contratos', N'SETTING', N'GLOBAL', N'/configuracoes/templates/controle-de-contratos', 1180),
    (N'settings.templates.custos.edit', N'settings.templates.view', N'Template Controle de Custos', N'SETTING', N'GLOBAL', N'/configuracoes/templates/controle-de-custos', 1190),
    (N'settings.quality-template.view', N'settings.view', N'Configurações de Template Qualidade (legado)', N'PAGE', N'GLOBAL', N'/templates', 1200),
    (N'settings.quality-template.edit', N'settings.quality-template.view', N'Editar templates de qualidade', N'ACTION', N'GLOBAL', NULL, 1210),
    (N'settings.ldp-legacy.view', N'settings.view', N'Configurador de LDP (OLD / demonstração)', N'PAGE', N'GLOBAL', N'/configurar-ldp', 1220),
    (N'settings.ocr.view', N'settings.view', N'Auditoria de Qualidade de Documentos', N'PAGE', N'GLOBAL', N'/ocr', 1230),
    (N'projects.create', NULL, N'Criar projeto a partir de template (ACC)', N'ACTION', N'GLOBAL', NULL, 1240),
    (N'ldp.sync.execute', NULL, N'Ações de LDP no ACC (sincronizar, placeholders, webhook, atualizar do banco)', N'ACTION', N'PROJECT', NULL, 1250),
    (N'permissions.manage', NULL, N'Gerenciar permissões e perfis', N'ACTION', N'GLOBAL', N'/configuracoes/permissoes', 1260),
    (N'system.tenants.view', NULL, N'Administração de tenants (Master Admin, legado)', N'PAGE', N'GLOBAL', N'/master-admin', 1270),
    (N'system.diagnostics.execute', NULL, N'Diagnósticos técnicos (ACC)', N'ACTION', N'GLOBAL', NULL, 1280),
    (N'system.commercial.view', NULL, N'Dados comerciais da conta', N'ACTION', N'GLOBAL', NULL, 1290)
-- END CATALOG
;

-- Mapa de keys substituídas → novas (tradução de acessos já concedidos).
DECLARE @traducao TABLE (Antiga NVARCHAR(120) NOT NULL, Nova NVARCHAR(120) NOT NULL, PRIMARY KEY (Antiga, Nova));
INSERT INTO @traducao (Antiga, Nova)
SELECT N'production.' + s.seg + N'.view', N'production.' + v.variante + N'.' + s.seg + N'.view'
FROM (VALUES (N'ldp'), (N'nivelamento'), (N'controle-programacao'), (N'desvio-qualidade'), (N'cap-interno'), (N'cap-externo'),
             (N'change-order'), (N'controle-documental'), (N'hold'), (N'desvio-escopo'), (N'rnc'), (N'programacao')) s(seg)
CROSS JOIN (VALUES (N'managerial'), (N'operational')) v(variante);
INSERT INTO @traducao (Antiga, Nova) VALUES
    (N'planning4d.view', N'cde.planning4d.view'),
    (N'budget5d.view', N'cde.budget5d.view'),
    (N'federated-model.view', N'cde.model3d.view'),
    (N'planning.weekly.view', N'planning.weekly.current.view');

BEGIN TRANSACTION;
    DECLARE @novas TABLE (PermissionKey NVARCHAR(120) PRIMARY KEY);

    -- 1) insere as keys novas sem pai (a FK de ParentKey exige o pai presente); 2) acerta atributos e pai de todas.
    INSERT INTO perm.PermissionResources (PermissionKey, ParentKey, Label, ResourceType, ScopeType, Route, SortOrder)
    OUTPUT inserted.PermissionKey INTO @novas (PermissionKey)
    SELECT c.PermissionKey, NULL, c.Label, c.ResourceType, c.ScopeType, c.Route, c.SortOrder
    FROM @catalogo c WHERE NOT EXISTS (SELECT 1 FROM perm.PermissionResources r WHERE r.PermissionKey = c.PermissionKey);

    UPDATE r SET ParentKey = c.ParentKey, Label = c.Label, ResourceType = c.ResourceType, ScopeType = c.ScopeType,
                 Route = c.Route, SortOrder = c.SortOrder, Ativo = 1, AtualizadoEm = SYSUTCDATETIME()
    FROM perm.PermissionResources r JOIN @catalogo c ON c.PermissionKey = r.PermissionKey;

    -- Keys fora do catálogo: continuam no banco (histórico/auditoria), apenas inativas. Soltas do pai para não prender a árvore.
    UPDATE r SET Ativo = 0, AtualizadoEm = SYSUTCDATETIME()
    FROM perm.PermissionResources r
    WHERE r.Ativo = 1 AND NOT EXISTS (SELECT 1 FROM @catalogo c WHERE c.PermissionKey = r.PermissionKey);

    -- 3) tradução: perfis
    INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey)
    SELECT DISTINCT pp.ProfileId, t.Nova
    FROM perm.ProfilePermissions pp JOIN @traducao t ON t.Antiga = pp.PermissionKey
    WHERE EXISTS (SELECT 1 FROM @catalogo c WHERE c.PermissionKey = t.Nova)
      AND NOT EXISTS (SELECT 1 FROM perm.ProfilePermissions x WHERE x.ProfileId = pp.ProfileId AND x.PermissionKey = t.Nova);

    -- 4) tradução: exceções individuais (mesmo efeito, mesmo escopo; não sobrescreve exceção já existente na key nova)
    INSERT INTO perm.UserPermissionOverrides (UserId, ProjetoId, PermissionKey, Effect)
    SELECT o.UserId, o.ProjetoId, t.Nova, MIN(o.Effect) -- DENY < ALLOW: havendo conflito, prevalece DENY
    FROM perm.UserPermissionOverrides o JOIN @traducao t ON t.Antiga = o.PermissionKey
    WHERE EXISTS (SELECT 1 FROM @catalogo c WHERE c.PermissionKey = t.Nova)
      AND NOT EXISTS (SELECT 1 FROM perm.UserPermissionOverrides x
                      WHERE x.UserId = o.UserId AND ((x.ProjetoId IS NULL AND o.ProjetoId IS NULL) OR x.ProjetoId = o.ProjetoId) AND x.PermissionKey = t.Nova)
    GROUP BY o.UserId, o.ProjetoId, t.Nova;

    -- 5) Administrador recebe as keys criadas por este script.
    INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey)
    SELECT p.ProfileId, n.PermissionKey
    FROM perm.PermissionProfiles p CROSS JOIN @novas n
    WHERE p.Name = N'Administrador' AND p.IsSystem = 0
      AND NOT EXISTS (SELECT 1 FROM perm.ProfilePermissions x WHERE x.ProfileId = p.ProfileId AND x.PermissionKey = n.PermissionKey);

    INSERT INTO perm.PermissionAuditLog (AlteradoPorUserId, AlteradoPorEmail, UsuarioAfetadoId, ProjetoId, Alvo, Referencia, ValorAnterior, ValorNovo)
    VALUES (NULL, N'migration', NULL, NULL, N'CATALOG_MIGRATION', N'20260929_002', NULL,
            (SELECT (SELECT COUNT(*) FROM @novas) AS keysNovas, (SELECT COUNT(*) FROM @catalogo) AS keysCatalogo FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
COMMIT TRANSACTION;
GO
