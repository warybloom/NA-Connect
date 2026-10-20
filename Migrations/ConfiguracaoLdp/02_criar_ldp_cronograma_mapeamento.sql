/*
============================================================
OBJETIVO
============================================================
Criar ldp.CronogramaMapeamento (mapeamento De/Para nomeado e reutilizável, N:1 por Template) e
ldp.CronogramaMapeamentoCampo (uma linha por coluna do cronograma mapeada para um campo do Template,
1:N por Mapeamento). Fonte: AUDITORIA_FORGESYNC_NA/17-*.md — página "Mapeamento do Cronograma".

DECISÃO DE VÍNCULO (ver documento 17 §7): um CronogramaMapeamento pertence a um Template (várias colunas
De/Para dentro dele referenciam campos DESSE Template), mas um Template pode ter VÁRIOS mapeamentos
nomeados (ex.: "Mapeamento Primavera", "Mapeamento MS Project") — reutilizáveis por quantos projetos
quiserem. A escolha de qual mapeamento um projeto usa é uma fase futura (junto com
dbo.Projetos.LdpTemplateId, ainda não implementada) — esta migration não cria esse vínculo.

Persiste MAPEAMENTO (nomes de coluna como texto), nunca dados reais do cronograma — esta migration não
lê nem grava em dbo.Cronograma_Projetos.

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_ldp.sql, 01_criar_ldp_template.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE, criação dentro de UMA transação com TRY/CATCH (mesmo padrão de
01_criar_ldp_template.sql).
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'ldp')
BEGIN
    RAISERROR('Schema [ldp] não existe — execute 00_criar_schema_ldp.sql primeiro.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'Template')
   OR NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateCampo')
BEGIN
    RAISERROR('Tabelas [ldp].Template/TemplateCampo não existem — execute 01_criar_ldp_template.sql primeiro.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'CronogramaMapeamento')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'CronogramaMapeamentoCampo')
BEGIN
    PRINT 'Tabelas [ldp].CronogramaMapeamento/CronogramaMapeamentoCampo já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'CronogramaMapeamento')
    BEGIN
        CREATE TABLE ldp.CronogramaMapeamento
        (
            CronogramaMapeamentoId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            TemplateId               INT            NOT NULL,
            Nome_Mapeamento          NVARCHAR(200)  NOT NULL,
            Ativo                    BIT            NOT NULL CONSTRAINT DF_CronogramaMapeamento_Ativo DEFAULT 1,
            Data_Criacao             DATETIME2      NOT NULL CONSTRAINT DF_CronogramaMapeamento_DataCriacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_CronogramaMapeamento_Template FOREIGN KEY (TemplateId)
                REFERENCES ldp.Template (TemplateId) ON DELETE CASCADE,
            CONSTRAINT UQ_CronogramaMapeamento_Nome UNIQUE (TemplateId, Nome_Mapeamento)
        );

        CREATE INDEX IX_CronogramaMapeamento_TemplateId ON ldp.CronogramaMapeamento (TemplateId);

        PRINT 'Tabela [ldp].CronogramaMapeamento e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].CronogramaMapeamento já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'CronogramaMapeamentoCampo')
    BEGIN
        -- TemplateCampoId usa ON DELETE NO ACTION (não CASCADE) por restrição técnica do SQL Server:
        -- esta tabela já cascateia a partir de CronogramaMapeamentoId (que por sua vez cascateia de
        -- Template) — um segundo caminho CASCADE por TemplateCampoId (que também cascateia, por outra
        -- rota, do mesmo Template) criaria múltiplos caminhos de cascade convergindo na mesma tabela
        -- ancestral, o que o SQL Server rejeita na criação da FK. Efeito prático, também desejável:
        -- excluir um campo do Template enquanto uma linha de mapeamento ainda o referencia falha
        -- explicitamente, em vez de apagar silenciosamente a linha de mapeamento.
        CREATE TABLE ldp.CronogramaMapeamentoCampo
        (
            CronogramaMapeamentoCampoId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            CronogramaMapeamentoId        INT            NOT NULL,
            TemplateCampoId                INT            NOT NULL,
            Coluna_Cronograma_Origem        NVARCHAR(200)  NOT NULL,
            Ordem                           INT            NOT NULL CONSTRAINT DF_CronogramaMapeamentoCampo_Ordem DEFAULT 0,

            CONSTRAINT FK_CronogramaMapeamentoCampo_Mapeamento FOREIGN KEY (CronogramaMapeamentoId)
                REFERENCES ldp.CronogramaMapeamento (CronogramaMapeamentoId) ON DELETE CASCADE,
            CONSTRAINT FK_CronogramaMapeamentoCampo_TemplateCampo FOREIGN KEY (TemplateCampoId)
                REFERENCES ldp.TemplateCampo (TemplateCampoId) ON DELETE NO ACTION,
            CONSTRAINT UQ_CronogramaMapeamentoCampo_Campo UNIQUE (CronogramaMapeamentoId, TemplateCampoId)
        );

        CREATE INDEX IX_CronogramaMapeamentoCampo_MapeamentoId ON ldp.CronogramaMapeamentoCampo (CronogramaMapeamentoId);
        CREATE INDEX IX_CronogramaMapeamentoCampo_TemplateCampoId ON ldp.CronogramaMapeamentoCampo (TemplateCampoId);

        PRINT 'Tabela [ldp].CronogramaMapeamentoCampo e índices criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].CronogramaMapeamentoCampo já existia — nada a fazer.';
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
SELECT COUNT(*) FROM ldp.CronogramaMapeamento;       -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM ldp.CronogramaMapeamentoCampo;   -- idem.
SELECT * FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('ldp.CronogramaMapeamentoCampo');

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE ldp.CronogramaMapeamentoCampo;
-- DROP TABLE ldp.CronogramaMapeamento;
*/
