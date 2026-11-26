/*
============================================================
OBJETIVO
============================================================
Criar ldp.Template (estrutura lógica de um Template LDP — cabeçalho), ldp.TemplateAba (abas lógicas,
1:N por Template) e ldp.TemplateCampo (campos dinâmicos, 1:N por Aba). Fonte:
AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md — página "Template" do novo
Configurador de LDP.

REGRA ESTRUTURAL — "+ ADICIONAR CAMPO" NUNCA VIRA ALTER TABLE: ldp.TemplateCampo é a única forma de um
campo novo existir — sempre um INSERT nesta tabela, nunca uma coluna física nova em nenhuma tabela.
Isso não é só convenção de código: é a própria forma como o modelo foi desenhado (um campo é uma LINHA,
não uma COLUNA).

Tipo_Dado/Origem usam CHECK com lista fechada pequena (Texto/Número/Decimal/Data/Percentual/Booleano;
Manual/Cronograma/Timesheet/Custo/Weekly/Sistema) — arquitetura preparada para expansão (basta trocar o
CHECK numa migration futura), não uma tabela de domínio separada, por serem listas pequenas e estáveis
por agora (mesmo padrão de decisão aplicado a Naming_Standard/Tipo_LDP nas fases anteriores: enum
simples, não CRUD).

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_ldp.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE. Toda a criação (3 tabelas + índices) roda dentro de UMA
transação com TRY/CATCH — se qualquer CREATE falhar, a transação inteira é desfeita (mesmo padrão
corrigido e validado em Migrations/Configuracao/01 e 03).
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'ldp')
BEGIN
    RAISERROR('Schema [ldp] não existe — execute 00_criar_schema_ldp.sql primeiro.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'Template')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateAba')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateCampo')
BEGIN
    PRINT 'Tabelas [ldp].Template/TemplateAba/TemplateCampo já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'Template')
    BEGIN
        CREATE TABLE ldp.Template
        (
            TemplateId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Nome_Template        NVARCHAR(200)  NOT NULL,
            Descricao            NVARCHAR(1000) NULL,
            Origem_Criacao       NVARCHAR(20)   NOT NULL CONSTRAINT DF_Template_OrigemCriacao DEFAULT 'MANUAL',
            Ativo                BIT            NOT NULL CONSTRAINT DF_Template_Ativo DEFAULT 1,
            Data_Criacao         DATETIME2      NOT NULL CONSTRAINT DF_Template_DataCriacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT UQ_Template_Nome UNIQUE (Nome_Template),
            CONSTRAINT CK_Template_OrigemCriacao CHECK (Origem_Criacao IN ('MANUAL', 'CLONE', 'IMPORT_EXCEL'))
        );
        PRINT 'Tabela [ldp].Template criada.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].Template já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateAba')
    BEGIN
        CREATE TABLE ldp.TemplateAba
        (
            TemplateAbaId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            TemplateId           INT            NOT NULL,
            Nome_Aba             NVARCHAR(200)  NOT NULL,
            Ordem                INT            NOT NULL CONSTRAINT DF_TemplateAba_Ordem DEFAULT 0,
            Ativo                BIT            NOT NULL CONSTRAINT DF_TemplateAba_Ativo DEFAULT 1,

            CONSTRAINT FK_TemplateAba_Template FOREIGN KEY (TemplateId)
                REFERENCES ldp.Template (TemplateId) ON DELETE CASCADE,
            CONSTRAINT UQ_TemplateAba_Nome UNIQUE (TemplateId, Nome_Aba)
        );

        CREATE INDEX IX_TemplateAba_TemplateId ON ldp.TemplateAba (TemplateId);

        PRINT 'Tabela [ldp].TemplateAba e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].TemplateAba já existia — nada a fazer.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateCampo')
    BEGIN
        CREATE TABLE ldp.TemplateCampo
        (
            TemplateCampoId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            TemplateAbaId        INT            NOT NULL,
            Nome_Exibido         NVARCHAR(200)  NOT NULL,
            Chave_Campo          NVARCHAR(100)  NOT NULL,
            Tipo_Dado            NVARCHAR(20)   NOT NULL CONSTRAINT DF_TemplateCampo_TipoDado DEFAULT 'TEXTO',
            Obrigatorio          BIT            NOT NULL CONSTRAINT DF_TemplateCampo_Obrigatorio DEFAULT 0,
            Ordem                INT            NOT NULL CONSTRAINT DF_TemplateCampo_Ordem DEFAULT 0,
            Origem               NVARCHAR(20)   NOT NULL CONSTRAINT DF_TemplateCampo_Origem DEFAULT 'MANUAL',
            Ativo                BIT            NOT NULL CONSTRAINT DF_TemplateCampo_Ativo DEFAULT 1,
            Data_Criacao         DATETIME2      NOT NULL CONSTRAINT DF_TemplateCampo_DataCriacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_TemplateCampo_TemplateAba FOREIGN KEY (TemplateAbaId)
                REFERENCES ldp.TemplateAba (TemplateAbaId) ON DELETE CASCADE,
            CONSTRAINT UQ_TemplateCampo_Chave UNIQUE (TemplateAbaId, Chave_Campo),
            CONSTRAINT CK_TemplateCampo_TipoDado CHECK (Tipo_Dado IN ('TEXTO', 'NUMERO', 'DECIMAL', 'DATA', 'PERCENTUAL', 'BOOLEANO')),
            CONSTRAINT CK_TemplateCampo_Origem CHECK (Origem IN ('MANUAL', 'CRONOGRAMA', 'TIMESHEET', 'CUSTO', 'WEEKLY', 'SISTEMA'))
        );

        CREATE INDEX IX_TemplateCampo_TemplateAbaId ON ldp.TemplateCampo (TemplateAbaId);

        PRINT 'Tabela [ldp].TemplateCampo e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].TemplateCampo já existia — nada a fazer.';
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'ERRO — nenhuma alteração foi mantida (rollback completo). Detalhe: ' + ERROR_MESSAGE();
    THROW;
END CATCH
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM ldp.Template;       -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM ldp.TemplateAba;     -- idem.
SELECT COUNT(*) FROM ldp.TemplateCampo;   -- idem.
SELECT * FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('ldp.Template'), OBJECT_ID('ldp.TemplateCampo'));

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE ldp.TemplateCampo;
-- DROP TABLE ldp.TemplateAba;
-- DROP TABLE ldp.Template;
*/
