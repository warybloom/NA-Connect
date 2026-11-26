/*
    N&A Connect — Permissões granulares (schema perm)
    Versão: 20260929_001

    • NÃO DESTRUTIVO: sem DROP, sem ALTER em tabelas existentes, sem DELETE. Idempotente (pode ser reexecutado).
    • Cria o schema [perm] e 7 tabelas novas; semeia o catálogo (PermissionResources) e 3 perfis iniciais.
    • O catálogo é a cópia de Services/Permissions/PermissionCatalog.cs (um teste de contrato garante que sejam iguais).
    • Reexecução: insere keys novas e atualiza rótulo/pai/tipo/escopo/rota das existentes; NÃO remove keys e
      NÃO reconcede permissões a perfis já existentes (edições feitas pela tela são preservadas).
    • Após aplicar: configurar Permissions:MasterAdmins (e-mail Autodesk) e, quando os perfis estiverem atribuídos,
      Permissions:Enforce = true. Enquanto Enforce = false o comportamento atual do sistema é mantido.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'perm') IS NULL EXEC(N'CREATE SCHEMA perm AUTHORIZATION dbo;');
GO

/* ---------- Catálogo ---------- */
IF OBJECT_ID(N'perm.PermissionResources', N'U') IS NULL
CREATE TABLE perm.PermissionResources (
    PermissionKey  NVARCHAR(120) NOT NULL CONSTRAINT PK_PermissionResources PRIMARY KEY,
    ParentKey      NVARCHAR(120) NULL,
    Label          NVARCHAR(200) NOT NULL,
    ResourceType   NVARCHAR(20)  NOT NULL CONSTRAINT CK_PermissionResources_Type
                       CHECK (ResourceType IN (N'MODULE', N'PAGE', N'TAB', N'REPORT', N'DASHBOARD', N'SETTING', N'ACTION')),
    ScopeType      NVARCHAR(10)  NOT NULL CONSTRAINT CK_PermissionResources_Scope CHECK (ScopeType IN (N'GLOBAL', N'PROJECT')),
    Route          NVARCHAR(200) NULL,
    SortOrder      INT NOT NULL CONSTRAINT DF_PermissionResources_Sort DEFAULT (0),
    Ativo          BIT NOT NULL CONSTRAINT DF_PermissionResources_Ativo DEFAULT (1),
    CriadoEm       DATETIME2(0) NOT NULL CONSTRAINT DF_PermissionResources_Criado DEFAULT (SYSUTCDATETIME()),
    AtualizadoEm   DATETIME2(0) NOT NULL CONSTRAINT DF_PermissionResources_Atualizado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_PermissionResources_Parent FOREIGN KEY (ParentKey) REFERENCES perm.PermissionResources (PermissionKey)
);
GO

/* ---------- Usuários (identidade = usuário Autodesk; não há tabela de usuários local anterior) ---------- */
IF OBJECT_ID(N'perm.Users', N'U') IS NULL
CREATE TABLE perm.Users (
    UserId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PermUsers PRIMARY KEY,
    AccUserId       NVARCHAR(100) NULL,
    Email           NVARCHAR(256) NOT NULL,
    DisplayName     NVARCHAR(200) NULL,
    Ativo           BIT NOT NULL CONSTRAINT DF_PermUsers_Ativo DEFAULT (1),
    CriadoEm        DATETIME2(0) NOT NULL CONSTRAINT DF_PermUsers_Criado DEFAULT (SYSUTCDATETIME()),
    UltimoAcessoEm  DATETIME2(0) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PermUsers_Email' AND object_id = OBJECT_ID(N'perm.Users'))
    CREATE UNIQUE INDEX UX_PermUsers_Email ON perm.Users (Email);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PermUsers_AccUserId' AND object_id = OBJECT_ID(N'perm.Users'))
    CREATE UNIQUE INDEX UX_PermUsers_AccUserId ON perm.Users (AccUserId) WHERE AccUserId IS NOT NULL;
GO

/* ---------- Perfis ---------- */
IF OBJECT_ID(N'perm.PermissionProfiles', N'U') IS NULL
CREATE TABLE perm.PermissionProfiles (
    ProfileId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PermissionProfiles PRIMARY KEY,
    Name           NVARCHAR(100) NOT NULL,
    Description    NVARCHAR(500) NULL,
    Ativo          BIT NOT NULL CONSTRAINT DF_PermissionProfiles_Ativo DEFAULT (1),
    IsSystem       BIT NOT NULL CONSTRAINT DF_PermissionProfiles_System DEFAULT (0),
    IsMasterAdmin  BIT NOT NULL CONSTRAINT DF_PermissionProfiles_Master DEFAULT (0),
    CriadoEm       DATETIME2(0) NOT NULL CONSTRAINT DF_PermissionProfiles_Criado DEFAULT (SYSUTCDATETIME()),
    AtualizadoEm   DATETIME2(0) NOT NULL CONSTRAINT DF_PermissionProfiles_Atualizado DEFAULT (SYSUTCDATETIME())
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PermissionProfiles_Name' AND object_id = OBJECT_ID(N'perm.PermissionProfiles'))
    CREATE UNIQUE INDEX UX_PermissionProfiles_Name ON perm.PermissionProfiles (Name);
GO

IF OBJECT_ID(N'perm.ProfilePermissions', N'U') IS NULL
CREATE TABLE perm.ProfilePermissions (
    ProfileId      INT NOT NULL CONSTRAINT FK_ProfilePermissions_Profile REFERENCES perm.PermissionProfiles (ProfileId),
    PermissionKey  NVARCHAR(120) NOT NULL CONSTRAINT FK_ProfilePermissions_Resource REFERENCES perm.PermissionResources (PermissionKey),
    CONSTRAINT PK_ProfilePermissions PRIMARY KEY (ProfileId, PermissionKey)
);
GO

/* ---------- Vínculos usuário × projeto ---------- */
-- ProjetoId NULL = perfil GLOBAL (vale em todos os projetos).
IF OBJECT_ID(N'perm.UserProjectProfiles', N'U') IS NULL
CREATE TABLE perm.UserProjectProfiles (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserProjectProfiles PRIMARY KEY,
    UserId     INT NOT NULL CONSTRAINT FK_UserProjectProfiles_User REFERENCES perm.Users (UserId),
    ProjetoId  INT NULL CONSTRAINT FK_UserProjectProfiles_Projeto REFERENCES dbo.Projetos (ProjetoID),
    ProfileId  INT NOT NULL CONSTRAINT FK_UserProjectProfiles_Profile REFERENCES perm.PermissionProfiles (ProfileId),
    Ativo      BIT NOT NULL CONSTRAINT DF_UserProjectProfiles_Ativo DEFAULT (1),
    CriadoEm   DATETIME2(0) NOT NULL CONSTRAINT DF_UserProjectProfiles_Criado DEFAULT (SYSUTCDATETIME())
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_UserProjectProfiles' AND object_id = OBJECT_ID(N'perm.UserProjectProfiles'))
    CREATE UNIQUE INDEX UX_UserProjectProfiles ON perm.UserProjectProfiles (UserId, ProjetoId, ProfileId);
GO

-- Exceções individuais. ProjetoId NULL = global. DENY vence ALLOW; override do projeto vence o global.
IF OBJECT_ID(N'perm.UserPermissionOverrides', N'U') IS NULL
CREATE TABLE perm.UserPermissionOverrides (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserPermissionOverrides PRIMARY KEY,
    UserId         INT NOT NULL CONSTRAINT FK_UserPermissionOverrides_User REFERENCES perm.Users (UserId),
    ProjetoId      INT NULL CONSTRAINT FK_UserPermissionOverrides_Projeto REFERENCES dbo.Projetos (ProjetoID),
    PermissionKey  NVARCHAR(120) NOT NULL CONSTRAINT FK_UserPermissionOverrides_Resource REFERENCES perm.PermissionResources (PermissionKey),
    Effect         NVARCHAR(5) NOT NULL CONSTRAINT CK_UserPermissionOverrides_Effect CHECK (Effect IN (N'ALLOW', N'DENY')),
    CriadoEm       DATETIME2(0) NOT NULL CONSTRAINT DF_UserPermissionOverrides_Criado DEFAULT (SYSUTCDATETIME())
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_UserPermissionOverrides' AND object_id = OBJECT_ID(N'perm.UserPermissionOverrides'))
    CREATE UNIQUE INDEX UX_UserPermissionOverrides ON perm.UserPermissionOverrides (UserId, ProjetoId, PermissionKey);
GO

-- Usuário ativo/inativo por projeto (inativo = nada naquele projeto, mesmo com perfil global).
IF OBJECT_ID(N'perm.UserProjects', N'U') IS NULL
CREATE TABLE perm.UserProjects (
    UserId        INT NOT NULL CONSTRAINT FK_UserProjects_User REFERENCES perm.Users (UserId),
    ProjetoId     INT NOT NULL CONSTRAINT FK_UserProjects_Projeto REFERENCES dbo.Projetos (ProjetoID),
    Ativo         BIT NOT NULL CONSTRAINT DF_UserProjects_Ativo DEFAULT (1),
    AtualizadoEm  DATETIME2(0) NOT NULL CONSTRAINT DF_UserProjects_Atualizado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_UserProjects PRIMARY KEY (UserId, ProjetoId)
);
GO

/* ---------- Auditoria ---------- */
IF OBJECT_ID(N'perm.PermissionAuditLog', N'U') IS NULL
CREATE TABLE perm.PermissionAuditLog (
    Id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PermissionAuditLog PRIMARY KEY,
    DataHora           DATETIME2(0) NOT NULL CONSTRAINT DF_PermissionAuditLog_Data DEFAULT (SYSUTCDATETIME()),
    AlteradoPorUserId  INT NULL,
    AlteradoPorEmail   NVARCHAR(256) NULL,
    UsuarioAfetadoId   INT NULL,
    ProjetoId          INT NULL,
    Alvo               NVARCHAR(50) NOT NULL,
    Referencia         NVARCHAR(200) NULL,
    ValorAnterior      NVARCHAR(MAX) NULL,
    ValorNovo          NVARCHAR(MAX) NULL
);
GO

/* ---------- Seed do catálogo (upsert sem remoção) ---------- */
DECLARE @catalogo TABLE (PermissionKey NVARCHAR(120) PRIMARY KEY, ParentKey NVARCHAR(120) NULL, Label NVARCHAR(200) NOT NULL,
    ResourceType NVARCHAR(20) NOT NULL, ScopeType NVARCHAR(10) NOT NULL, Route NVARCHAR(200) NULL, SortOrder INT NOT NULL);
INSERT INTO @catalogo (PermissionKey, ParentKey, Label, ResourceType, ScopeType, Route, SortOrder) VALUES
-- BEGIN CATALOG (gerado de PermissionCatalog.Todos)
    (N'home.view', NULL, N'Início', N'PAGE', N'GLOBAL', N'/dashboard', 10),
    (N'planning4d.view', NULL, N'Planejamento 4D', N'MODULE', N'GLOBAL', N'/planejamento-4d', 20),
    (N'budget5d.view', NULL, N'Orçamento 5D', N'MODULE', N'GLOBAL', N'/orcamento-5d', 30),
    (N'cde.view', NULL, N'CDE', N'MODULE', N'PROJECT', N'/cde', 40),
    (N'cde.document-list.view', N'cde.view', N'Lista de Documentos', N'TAB', N'PROJECT', NULL, 50),
    (N'cde.docs.view', N'cde.view', N'Docs (navegação de pastas e arquivos ACC)', N'TAB', N'PROJECT', N'/projects/:hubId/:projectId/:folderId', 60),
    (N'cde.docs.download', N'cde.docs.view', N'Baixar arquivo', N'ACTION', N'PROJECT', NULL, 70),
    (N'cde.docs.upload', N'cde.docs.view', N'Enviar nova versão de arquivo', N'ACTION', N'PROJECT', NULL, 80),
    (N'cde.curva-s.view', N'cde.view', N'Curva S', N'TAB', N'PROJECT', NULL, 90),
    (N'production.view', NULL, N'Controle de Produção', N'MODULE', N'PROJECT', N'/controle-producao', 100),
    (N'production.ldp.view', N'production.view', N'LDP', N'DASHBOARD', N'PROJECT', NULL, 110),
    (N'production.nivelamento.view', N'production.view', N'Nivelamento Programação', N'DASHBOARD', N'PROJECT', NULL, 120),
    (N'production.controle-programacao.view', N'production.view', N'Controle Programação', N'DASHBOARD', N'PROJECT', NULL, 130),
    (N'production.desvio-qualidade.view', N'production.view', N'Desvio de Qualidade', N'DASHBOARD', N'PROJECT', NULL, 140),
    (N'production.cap-interno.view', N'production.view', N'CAP Interno', N'DASHBOARD', N'PROJECT', NULL, 150),
    (N'production.cap-externo.view', N'production.view', N'CAP Externo', N'DASHBOARD', N'PROJECT', NULL, 160),
    (N'production.change-order.view', N'production.view', N'Change Order', N'DASHBOARD', N'PROJECT', NULL, 170),
    (N'production.controle-documental.view', N'production.view', N'Controle Documental', N'DASHBOARD', N'PROJECT', NULL, 180),
    (N'production.hold.view', N'production.view', N'HOLD', N'DASHBOARD', N'PROJECT', NULL, 190),
    (N'production.desvio-escopo.view', N'production.view', N'Desvio de Escopo', N'DASHBOARD', N'PROJECT', NULL, 200),
    (N'production.rnc.view', N'production.view', N'RNC', N'DASHBOARD', N'PROJECT', NULL, 210),
    (N'production.programacao.view', N'production.view', N'Programação', N'DASHBOARD', N'PROJECT', NULL, 220),
    (N'tv.view', NULL, N'Gestão à Vista', N'MODULE', N'GLOBAL', N'/gestao-a-vista', 230),
    (N'federated-model.view', NULL, N'Modelo Federado', N'MODULE', N'GLOBAL', N'/modelo-federado', 240),
    (N'reports.view', NULL, N'Relatórios PMO (legado)', N'MODULE', N'PROJECT', N'/relatorios', 250),
    (N'reports.gestao.view', N'reports.view', N'Gestão à Vista (PMO)', N'REPORT', N'PROJECT', NULL, 260),
    (N'reports.timesheet.view', N'reports.view', N'Timesheet / Financeiro de horas', N'REPORT', N'PROJECT', NULL, 270),
    (N'reports.producao.view', N'reports.view', N'Produção (mapa de calor)', N'REPORT', N'PROJECT', NULL, 280),
    (N'reports.curvas.view', N'reports.view', N'Curva S', N'REPORT', N'PROJECT', NULL, 290),
    (N'reports.avanco4d.view', N'reports.view', N'Avanço 4D', N'REPORT', N'PROJECT', NULL, 300),
    (N'reports.desvios.view', N'reports.view', N'Ofensores', N'REPORT', N'PROJECT', NULL, 310),
    (N'reports.weekly.view', N'reports.view', N'Programação semanal', N'REPORT', N'PROJECT', NULL, 320),
    (N'reports.cronograma.view', N'reports.view', N'Cronograma', N'REPORT', N'PROJECT', NULL, 330),
    (N'reports.ldp.view', N'reports.view', N'LDP detalhada', N'REPORT', N'PROJECT', NULL, 340),
    (N'reports.ncr.view', N'reports.view', N'Qualidade / RNC', N'REPORT', N'PROJECT', NULL, 350),
    (N'reports.op-desvios.view', N'reports.view', N'Desvios de escopo', N'REPORT', N'PROJECT', NULL, 360),
    (N'reports.hold.view', N'reports.view', N'HOLD', N'REPORT', N'PROJECT', NULL, 370),
    (N'reports.cap-interno.view', N'reports.view', N'CAP Interno', N'REPORT', N'PROJECT', NULL, 380),
    (N'reports.cap-externo.view', N'reports.view', N'CAP Externo', N'REPORT', N'PROJECT', NULL, 390),
    (N'reports.lancamentos.view', N'reports.view', N'Lançamentos PMO', N'PAGE', N'GLOBAL', N'/lancamentos', 400),
    (N'planning.view', NULL, N'Planejamento (legado)', N'MODULE', N'GLOBAL', NULL, 410),
    (N'planning.weekly.view', N'planning.view', N'Programação semanal', N'PAGE', N'PROJECT', N'/planejamento/programacao-semanal', 420),
    (N'planning.planner.view', N'planning.view', N'Painel do planejador', N'PAGE', N'PROJECT', N'/planejamento/painel-do-planejador', 430),
    (N'planning.coordination.view', N'planning.view', N'Normas de coordenação', N'PAGE', N'GLOBAL', N'/planejamento/normas-de-coordenacao', 440),
    (N'planning.coordination.edit', N'planning.coordination.view', N'Editar normas de coordenação', N'ACTION', N'GLOBAL', NULL, 450),
    (N'robot.view', NULL, N'Robô (visualizar andamento e configuração)', N'MODULE', N'GLOBAL', N'/automacao/configuracao-robo', 460),
    (N'robot.configure', N'robot.view', N'Configurar robô', N'ACTION', N'GLOBAL', NULL, 470),
    (N'robot.run', N'robot.view', N'Executar robô (cada projeto ainda passa por IProjectReportAccess)', N'ACTION', N'GLOBAL', NULL, 480),
    (N'robot.stop', N'robot.view', N'Parar robô', N'ACTION', N'GLOBAL', NULL, 490),
    (N'robot.schedule.view', N'robot.view', N'Agendador do Robô', N'PAGE', N'GLOBAL', N'/agendamento', 500),
    (N'plant3d.view', NULL, N'Plant 3D', N'MODULE', N'GLOBAL', N'/plant3d', 510),
    (N'plant3d.schedule4d.view', N'plant3d.view', N'Avanço e Cronograma 4D', N'REPORT', N'GLOBAL', NULL, 520),
    (N'plant3d.results.view', N'plant3d.view', N'Tela de Resultados', N'REPORT', N'GLOBAL', NULL, 530),
    (N'plant3d.consolidated.view', N'plant3d.view', N'Relatórios Consolidados', N'REPORT', N'GLOBAL', NULL, 540),
    (N'plant3d.measurement.view', N'plant3d.view', N'Critérios de Medição', N'SETTING', N'GLOBAL', NULL, 550),
    (N'plant3d.measurement.edit', N'plant3d.measurement.view', N'Editar critérios de medição', N'ACTION', N'GLOBAL', NULL, 560),
    (N'plant3d.database.view', N'plant3d.view', N'Banco de Dados', N'SETTING', N'GLOBAL', NULL, 570),
    (N'plant3d.database.edit', N'plant3d.database.view', N'Editar banco de dados', N'ACTION', N'GLOBAL', NULL, 580),
    (N'plant3d.mapping.view', N'plant3d.view', N'Mapeamento DE/PARA', N'SETTING', N'GLOBAL', NULL, 590),
    (N'plant3d.mapping.edit', N'plant3d.mapping.view', N'Editar mapeamento DE/PARA', N'ACTION', N'GLOBAL', NULL, 600),
    (N'plant3d.naming.view', N'plant3d.view', N'Regras de Nomenclatura', N'SETTING', N'GLOBAL', NULL, 610),
    (N'plant3d.naming.edit', N'plant3d.naming.view', N'Editar regras de nomenclatura', N'ACTION', N'GLOBAL', NULL, 620),
    (N'plant3d.gen.view', N'plant3d.view', N'Gestão à Vista (GEN) — oculta', N'REPORT', N'GLOBAL', NULL, 630),
    (N'settings.view', NULL, N'Configurações', N'MODULE', N'GLOBAL', NULL, 640),
    (N'settings.projects.view', N'settings.view', N'Configurações dos Projetos', N'PAGE', N'GLOBAL', N'/painel', 650),
    (N'settings.projects.edit', N'settings.projects.view', N'Editar projetos (ativo, Tipo LDP, idioma, norma)', N'ACTION', N'GLOBAL', NULL, 660),
    (N'settings.ldp.view', N'settings.view', N'Configuração LDP', N'PAGE', N'GLOBAL', N'/configuracao-ldp', 670),
    (N'settings.ldp.template.view', N'settings.ldp.view', N'Template', N'PAGE', N'GLOBAL', N'/configuracao-ldp/template', 680),
    (N'settings.ldp.template.edit', N'settings.ldp.template.view', N'Editar template', N'ACTION', N'GLOBAL', NULL, 690),
    (N'settings.ldp.cronograma.view', N'settings.ldp.view', N'Mapeamento do Cronograma', N'PAGE', N'GLOBAL', N'/configuracao-ldp/cronograma', 700),
    (N'settings.ldp.cronograma.edit', N'settings.ldp.cronograma.view', N'Editar mapeamento do cronograma', N'ACTION', N'GLOBAL', NULL, 710),
    (N'settings.ldp.espelho.view', N'settings.ldp.view', N'Geração LDP Espelho', N'PAGE', N'GLOBAL', N'/configuracao-ldp/espelho', 720),
    (N'settings.ldp.espelho.edit', N'settings.ldp.espelho.view', N'Editar/gerar espelho', N'ACTION', N'GLOBAL', NULL, 730),
    (N'settings.ldp.controle.view', N'settings.ldp.view', N'Geração LDP Controle', N'PAGE', N'GLOBAL', N'/configuracao-ldp/controle', 740),
    (N'settings.ldp.controle.edit', N'settings.ldp.controle.view', N'Editar LDP controle', N'ACTION', N'GLOBAL', NULL, 750),
    (N'settings.ldp.projeto.view', N'settings.ldp.view', N'Configuração LDP do projeto', N'SETTING', N'PROJECT', NULL, 760),
    (N'settings.ldp.projeto.edit', N'settings.ldp.projeto.view', N'Editar configuração LDP do projeto', N'ACTION', N'PROJECT', NULL, 770),
    (N'settings.quality-template.view', N'settings.view', N'Configurações de Template Qualidade', N'PAGE', N'GLOBAL', N'/templates', 780),
    (N'settings.quality-template.edit', N'settings.quality-template.view', N'Editar templates de qualidade', N'ACTION', N'GLOBAL', NULL, 790),
    (N'settings.ldp-legacy.view', N'settings.view', N'Configurador de LDP (OLD / demonstração)', N'PAGE', N'GLOBAL', N'/configurar-ldp', 800),
    (N'settings.ocr.view', N'settings.view', N'Auditoria de Qualidade de Documentos', N'PAGE', N'GLOBAL', N'/ocr', 810),
    (N'projects.create', NULL, N'Criar projeto a partir de template (ACC)', N'ACTION', N'GLOBAL', NULL, 820),
    (N'ldp.sync.execute', NULL, N'Ações de LDP no ACC (sincronizar, placeholders, webhook, atualizar do banco)', N'ACTION', N'PROJECT', NULL, 830),
    (N'permissions.manage', NULL, N'Gerenciar permissões e perfis', N'ACTION', N'GLOBAL', N'/configuracoes/permissoes', 840),
    (N'system.tenants.view', NULL, N'Administração de tenants (Master Admin, legado)', N'PAGE', N'GLOBAL', N'/master-admin', 850),
    (N'system.diagnostics.execute', NULL, N'Diagnósticos técnicos (ACC)', N'ACTION', N'GLOBAL', NULL, 860),
    (N'system.commercial.view', NULL, N'Dados comerciais da conta', N'ACTION', N'GLOBAL', NULL, 870)
-- END CATALOG
;

BEGIN TRANSACTION;
    -- 1) insere sem pai (a FK de ParentKey exige o pai presente), 2) acerta atributos e pai de todos.
    INSERT INTO perm.PermissionResources (PermissionKey, ParentKey, Label, ResourceType, ScopeType, Route, SortOrder)
    SELECT c.PermissionKey, NULL, c.Label, c.ResourceType, c.ScopeType, c.Route, c.SortOrder
    FROM @catalogo c WHERE NOT EXISTS (SELECT 1 FROM perm.PermissionResources r WHERE r.PermissionKey = c.PermissionKey);

    UPDATE r SET ParentKey = c.ParentKey, Label = c.Label, ResourceType = c.ResourceType, ScopeType = c.ScopeType,
                 Route = c.Route, SortOrder = c.SortOrder, Ativo = 1, AtualizadoEm = SYSUTCDATETIME()
    FROM perm.PermissionResources r JOIN @catalogo c ON c.PermissionKey = r.PermissionKey;

    /* ---------- Perfis iniciais (criados uma única vez) ---------- */
    IF NOT EXISTS (SELECT 1 FROM perm.PermissionProfiles WHERE Name = N'MasterAdmin')
        INSERT INTO perm.PermissionProfiles (Name, Description, Ativo, IsSystem, IsMasterAdmin)
        VALUES (N'MasterAdmin', N'Perfil de sistema protegido: acesso total. Só pode ser atribuído globalmente, por outro MasterAdmin.', 1, 1, 1);

    IF NOT EXISTS (SELECT 1 FROM perm.PermissionProfiles WHERE Name = N'Administrador')
    BEGIN
        INSERT INTO perm.PermissionProfiles (Name, Description, Ativo, IsSystem, IsMasterAdmin)
        VALUES (N'Administrador', N'Todas as funcionalidades do catálogo (editável).', 1, 0, 0);
        INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey)
        SELECT SCOPE_IDENTITY(), c.PermissionKey FROM @catalogo c;
    END

    IF NOT EXISTS (SELECT 1 FROM perm.PermissionProfiles WHERE Name = N'Somente Leitura')
    BEGIN
        INSERT INTO perm.PermissionProfiles (Name, Description, Ativo, IsSystem, IsMasterAdmin)
        VALUES (N'Somente Leitura', N'Visualização de módulos, telas, relatórios e dashboards; sem edição, execução ou administração.', 1, 0, 0);
        INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey)
        SELECT SCOPE_IDENTITY(), c.PermissionKey FROM @catalogo c
        WHERE c.PermissionKey LIKE N'%.view'
          AND c.PermissionKey NOT LIKE N'system.%'
          AND c.PermissionKey NOT LIKE N'permissions.%';
    END
COMMIT TRANSACTION;
GO
