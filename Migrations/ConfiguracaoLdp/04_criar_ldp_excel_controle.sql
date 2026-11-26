/*
============================================================
OBJETIVO
============================================================
Criar ldp.ExcelControle (configuração de geração da LDP operacional/de controle — cabeçalho, N:1 por
Template), ldp.ExcelControleAba (quais abas do Template entram, com nome de exibição e ordem, 1:N por
Controle) e ldp.ExcelControleCampo (quais campos de cada aba entram, com nome de exibição opcional e
ordem, 1:N por ExcelControleAba). Fonte: AUDITORIA_FORGESYNC_NA/17-*.md (rodada 2) — página "Geração
LDP Controle".

DIFERENÇA ESTRUTURAL vs. ldp.ExcelEspelho (03_criar_ldp_excel_espelho.sql): Controle NÃO tem XLSX base
(gera uma planilha limpa, sem logo/carimbo/estilos) — por isso não tem `Arquivo_Referencia`,
`Linha_Cabecalho` nem `Coluna_Inicial`: a geração escreve cabeçalho + dados diretamente a partir da
configuração, sem preservar nenhum arquivo visual. Nome_Exibido_Override em ExcelControleCampo permite
renomear o cabeçalho gerado sem alterar o Nome_Exibido do campo no Template (ex.: "N&A Document Number"
no Template pode sair como "Nº Documento" só nesta LDP de Controle).

Arquitetura preparada para, em fase futura, ExcelControleCampo referenciar diretamente uma fonte de
domínio (Custo/HH Previsto/HH Real/Weekly) em vez de só ldp.TemplateCampo — não implementado agora
(seria uma nova coluna nullable de origem de domínio, adicionada só quando essas conexões reais
existirem; não inventada aqui sem necessidade comprovada).

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_ldp.sql, 01_criar_ldp_template.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE, criação dentro de UMA transação com TRY/CATCH (mesmo padrão de
01/02/03).
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'ldp')
BEGIN
    RAISERROR('Schema [ldp] não existe — execute 00_criar_schema_ldp.sql primeiro.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'Template')
   OR NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateAba')
   OR NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'TemplateCampo')
BEGIN
    RAISERROR('Tabelas [ldp].Template/TemplateAba/TemplateCampo não existem — execute 01_criar_ldp_template.sql primeiro.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControle')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControleAba')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControleCampo')
BEGIN
    PRINT 'Tabelas [ldp].ExcelControle/ExcelControleAba/ExcelControleCampo já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControle')
    BEGIN
        CREATE TABLE ldp.ExcelControle
        (
            ExcelControleId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            TemplateId            INT            NOT NULL,
            Nome_Configuracao     NVARCHAR(200)  NOT NULL,
            Ativo                 BIT            NOT NULL CONSTRAINT DF_ExcelControle_Ativo DEFAULT 1,
            Data_Criacao          DATETIME2      NOT NULL CONSTRAINT DF_ExcelControle_DataCriacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_ExcelControle_Template FOREIGN KEY (TemplateId)
                REFERENCES ldp.Template (TemplateId) ON DELETE CASCADE,
            CONSTRAINT UQ_ExcelControle_Nome UNIQUE (TemplateId, Nome_Configuracao)
        );

        CREATE INDEX IX_ExcelControle_TemplateId ON ldp.ExcelControle (TemplateId);

        PRINT 'Tabela [ldp].ExcelControle e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ExcelControle já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControleAba')
    BEGIN
        -- TemplateAbaId usa NO ACTION pela mesma restrição técnica documentada em
        -- 02_criar_ldp_cronograma_mapeamento.sql.
        CREATE TABLE ldp.ExcelControleAba
        (
            ExcelControleAbaId  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            ExcelControleId       INT            NOT NULL,
            TemplateAbaId         INT            NOT NULL,
            Nome_Aba_Exibido      NVARCHAR(200)  NULL,   -- NULL = usa TemplateAba.Nome_Aba como está
            Ordem                 INT            NOT NULL CONSTRAINT DF_ExcelControleAba_Ordem DEFAULT 0,

            CONSTRAINT FK_ExcelControleAba_ExcelControle FOREIGN KEY (ExcelControleId)
                REFERENCES ldp.ExcelControle (ExcelControleId) ON DELETE CASCADE,
            CONSTRAINT FK_ExcelControleAba_TemplateAba FOREIGN KEY (TemplateAbaId)
                REFERENCES ldp.TemplateAba (TemplateAbaId) ON DELETE NO ACTION,
            CONSTRAINT UQ_ExcelControleAba_TemplateAba UNIQUE (ExcelControleId, TemplateAbaId)
        );

        CREATE INDEX IX_ExcelControleAba_ExcelControleId ON ldp.ExcelControleAba (ExcelControleId);
        CREATE INDEX IX_ExcelControleAba_TemplateAbaId ON ldp.ExcelControleAba (TemplateAbaId);

        PRINT 'Tabela [ldp].ExcelControleAba e índices criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ExcelControleAba já existia — nada a fazer.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControleCampo')
    BEGIN
        CREATE TABLE ldp.ExcelControleCampo
        (
            ExcelControleCampoId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            ExcelControleAbaId     INT            NOT NULL,
            TemplateCampoId        INT            NOT NULL,
            Nome_Exibido_Override   NVARCHAR(200)  NULL,   -- NULL = usa TemplateCampo.Nome_Exibido como está
            Ordem                   INT            NOT NULL CONSTRAINT DF_ExcelControleCampo_Ordem DEFAULT 0,

            CONSTRAINT FK_ExcelControleCampo_ExcelControleAba FOREIGN KEY (ExcelControleAbaId)
                REFERENCES ldp.ExcelControleAba (ExcelControleAbaId) ON DELETE CASCADE,
            CONSTRAINT FK_ExcelControleCampo_TemplateCampo FOREIGN KEY (TemplateCampoId)
                REFERENCES ldp.TemplateCampo (TemplateCampoId) ON DELETE NO ACTION,
            CONSTRAINT UQ_ExcelControleCampo_Campo UNIQUE (ExcelControleAbaId, TemplateCampoId)
        );

        CREATE INDEX IX_ExcelControleCampo_ExcelControleAbaId ON ldp.ExcelControleCampo (ExcelControleAbaId);
        CREATE INDEX IX_ExcelControleCampo_TemplateCampoId ON ldp.ExcelControleCampo (TemplateCampoId);

        PRINT 'Tabela [ldp].ExcelControleCampo e índices criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ExcelControleCampo já existia — nada a fazer.';
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
SELECT COUNT(*) FROM ldp.ExcelControle;       -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM ldp.ExcelControleAba;     -- idem.
SELECT COUNT(*) FROM ldp.ExcelControleCampo;   -- idem.

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE ldp.ExcelControleCampo;
-- DROP TABLE ldp.ExcelControleAba;
-- DROP TABLE ldp.ExcelControle;
*/
