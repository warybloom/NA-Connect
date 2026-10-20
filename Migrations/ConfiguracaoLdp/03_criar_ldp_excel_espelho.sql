/*
============================================================
OBJETIVO
============================================================
Criar ldp.ExcelEspelho (modelo visual de saída "LDP Espelho" — cabeçalho, N:1 por Template),
ldp.ExcelEspelhoAba (vínculo aba lógica do Template → aba física do XLSX base, 1:N por Espelho) e
ldp.ExcelEspelhoCampo (vínculo campo do Template → ordem de escrita na aba física, 1:N por
ExcelEspelhoAba). Fonte: AUDITORIA_FORGESYNC_NA/17-*.md (rodada 2) — página "Geração LDP Espelho".

RENOMEIO (rodada 2, 2026-09-18): ExcelModelo/ExcelModeloAba/ExcelModeloCampo → ExcelEspelho/
ExcelEspelhoAba/ExcelEspelhoCampo, para distinguir explicitamente de ldp.ExcelControle
(04_criar_ldp_excel_controle.sql) — Espelho preserva um XLSX base (logo/carimbo/estilos), Controle
gera uma planilha limpa. Nunca executada na rodada 1 — renomear agora não migra dado nenhum.

Arquivo_Referencia é um PONTEIRO (nome de arquivo sob o diretório configurado em
appsettings.json:LdpConfig:ArmazenamentoBasePath), nunca o arquivo em si — decisão de infraestrutura
definitiva (ACC/blob/storage do app) continua PENDENTE (ver documento 17 §20); o armazenamento local
temporário desta fase é só para viabilizar o teste real de geração, não a solução definitiva. Nenhuma
coluna VARBINARY/BLOB foi criada.

Coluna_Inicial (NOVO na rodada 2) — item 12 do pedido: o XLSX base pode ter colunas decorativas antes
dos dados reais (ex.: coluna A reservada para logo/margem). Em vez de expor notação A1, um inteiro
simples (1 = coluna A, 2 = coluna B, ...) marca onde a escrita de campos começa; a posição de cada
campo dentro do bloco é calculada a partir da ordem (ExcelEspelhoCampo.Ordem), nunca uma letra fixa.

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_ldp.sql, 01_criar_ldp_template.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE, criação dentro de UMA transação com TRY/CATCH (mesmo padrão de
01/02).
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

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelho')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelhoAba')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelhoCampo')
BEGIN
    PRINT 'Tabelas [ldp].ExcelEspelho/ExcelEspelhoAba/ExcelEspelhoCampo já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelho')
    BEGIN
        CREATE TABLE ldp.ExcelEspelho
        (
            ExcelEspelhoId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            TemplateId            INT            NOT NULL,
            Nome_Modelo           NVARCHAR(200)  NOT NULL,
            Arquivo_Referencia    NVARCHAR(500)  NULL,   -- ponteiro; local físico definitivo ainda não decidido (ver OBJETIVO)
            Ativo                 BIT            NOT NULL CONSTRAINT DF_ExcelEspelho_Ativo DEFAULT 1,
            Data_Criacao          DATETIME2      NOT NULL CONSTRAINT DF_ExcelEspelho_DataCriacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_ExcelEspelho_Template FOREIGN KEY (TemplateId)
                REFERENCES ldp.Template (TemplateId) ON DELETE CASCADE,
            CONSTRAINT UQ_ExcelEspelho_Nome UNIQUE (TemplateId, Nome_Modelo)
        );

        CREATE INDEX IX_ExcelEspelho_TemplateId ON ldp.ExcelEspelho (TemplateId);

        PRINT 'Tabela [ldp].ExcelEspelho e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ExcelEspelho já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelhoAba')
    BEGIN
        -- TemplateAbaId usa NO ACTION pela mesma restrição técnica documentada em
        -- 02_criar_ldp_cronograma_mapeamento.sql (evitar múltiplos caminhos CASCADE convergindo em
        -- ldp.Template através de rotas diferentes).
        CREATE TABLE ldp.ExcelEspelhoAba
        (
            ExcelEspelhoAbaId   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            ExcelEspelhoId        INT            NOT NULL,
            TemplateAbaId         INT            NOT NULL,
            Nome_Aba_Fisica       NVARCHAR(200)  NOT NULL,
            Linha_Cabecalho       INT            NOT NULL CONSTRAINT DF_ExcelEspelhoAba_LinhaCabecalho DEFAULT 1,
            Coluna_Inicial        INT            NOT NULL CONSTRAINT DF_ExcelEspelhoAba_ColunaInicial DEFAULT 1,
            Ordem                 INT            NOT NULL CONSTRAINT DF_ExcelEspelhoAba_Ordem DEFAULT 0,

            CONSTRAINT FK_ExcelEspelhoAba_ExcelEspelho FOREIGN KEY (ExcelEspelhoId)
                REFERENCES ldp.ExcelEspelho (ExcelEspelhoId) ON DELETE CASCADE,
            CONSTRAINT FK_ExcelEspelhoAba_TemplateAba FOREIGN KEY (TemplateAbaId)
                REFERENCES ldp.TemplateAba (TemplateAbaId) ON DELETE NO ACTION,
            CONSTRAINT UQ_ExcelEspelhoAba_TemplateAba UNIQUE (ExcelEspelhoId, TemplateAbaId),
            CONSTRAINT CK_ExcelEspelhoAba_LinhaCabecalho CHECK (Linha_Cabecalho > 0),
            CONSTRAINT CK_ExcelEspelhoAba_ColunaInicial CHECK (Coluna_Inicial > 0)
        );

        CREATE INDEX IX_ExcelEspelhoAba_ExcelEspelhoId ON ldp.ExcelEspelhoAba (ExcelEspelhoId);
        CREATE INDEX IX_ExcelEspelhoAba_TemplateAbaId ON ldp.ExcelEspelhoAba (TemplateAbaId);

        PRINT 'Tabela [ldp].ExcelEspelhoAba e índices criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ExcelEspelhoAba já existia — nada a fazer.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelhoCampo')
    BEGIN
        CREATE TABLE ldp.ExcelEspelhoCampo
        (
            ExcelEspelhoCampoId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            ExcelEspelhoAbaId     INT            NOT NULL,
            TemplateCampoId       INT            NOT NULL,
            Ordem                 INT            NOT NULL CONSTRAINT DF_ExcelEspelhoCampo_Ordem DEFAULT 0,

            CONSTRAINT FK_ExcelEspelhoCampo_ExcelEspelhoAba FOREIGN KEY (ExcelEspelhoAbaId)
                REFERENCES ldp.ExcelEspelhoAba (ExcelEspelhoAbaId) ON DELETE CASCADE,
            CONSTRAINT FK_ExcelEspelhoCampo_TemplateCampo FOREIGN KEY (TemplateCampoId)
                REFERENCES ldp.TemplateCampo (TemplateCampoId) ON DELETE NO ACTION,
            CONSTRAINT UQ_ExcelEspelhoCampo_Campo UNIQUE (ExcelEspelhoAbaId, TemplateCampoId)
        );

        CREATE INDEX IX_ExcelEspelhoCampo_ExcelEspelhoAbaId ON ldp.ExcelEspelhoCampo (ExcelEspelhoAbaId);
        CREATE INDEX IX_ExcelEspelhoCampo_TemplateCampoId ON ldp.ExcelEspelhoCampo (TemplateCampoId);

        PRINT 'Tabela [ldp].ExcelEspelhoCampo e índices criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ExcelEspelhoCampo já existia — nada a fazer.';
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
SELECT COUNT(*) FROM ldp.ExcelEspelho;       -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM ldp.ExcelEspelhoAba;     -- idem.
SELECT COUNT(*) FROM ldp.ExcelEspelhoCampo;   -- idem.
SELECT * FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('ldp.ExcelEspelhoAba');

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE ldp.ExcelEspelhoCampo;
-- DROP TABLE ldp.ExcelEspelhoAba;
-- DROP TABLE ldp.ExcelEspelho;
*/
