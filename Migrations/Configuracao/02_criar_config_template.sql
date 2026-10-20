/*
============================================================
OBJETIVO
============================================================
Criar config.Template (template de documento — sigla + nome + tipo), opcionalmente vinculado a uma
Norma (NormaId nulo = grupo "global"). Fonte: aba "Configuração de Templates" (ControlPanel.tsx) e seu
upload multipart de pacote (metadados JSON + arquivos).

IMPORTANTE — arquivo físico do template: esta migration guarda SÓ METADADOS (Arquivo_Referencia é uma
string livre — nome de arquivo, caminho ou identificador de item ACC, a definir na implementação do
backend). NÃO cria coluna de BLOB. Onde o arquivo físico realmente será armazenado (pasta ACC dedicada,
disco local do servidor, blob storage) é uma decisão de infraestrutura que esta migration não assume —
ver AUDITORIA_FORGESYNC_NA/16-*.md §6, marcada como pendente antes da implementação do endpoint de
upload.

ON DELETE de FK_Template_Norma — reauditado em 18/09/2026 (checkpoint de revisão): a UI
(ControlPanel.tsx) agrupa e gerencia templates POR Norma (seção "Gerenciar Pacote" por grupo de norma,
função openPackageEditor) e o botão de excluir Norma (triggerDeleteDialog/handleConfirmDelete) NÃO avisa
o usuário sobre os templates associados nem pergunta o que fazer com eles. SET NULL faria os templates
de uma norma excluída se tornarem silenciosamente "globais" (NormaId nulo) sem o usuário perceber ou
decidir isso — mudança de categorização não solicitada. Trocado para NO ACTION: a exclusão de uma Norma
com templates associados falha explicitamente, obrigando quem for implementar o backend a decidir
(reatribuir os templates a outra Norma, torná-los explicitamente globais, ou excluí-los) ANTES de
excluir a Norma — nunca de forma implícita pela FK.

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_config.sql, 01_criar_config_norma.sql.

============================================================
PRÉ-CHECK + CREATE + ÍNDICES (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config')
BEGIN
    RAISERROR('Schema [config] não existe — execute 00_criar_schema_config.sql primeiro.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'Norma')
BEGIN
    RAISERROR('Tabela [config].Norma não existe — execute 01_criar_config_norma.sql primeiro.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'Template')
BEGIN
    PRINT 'Tabela [config].Template já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE config.Template
    (
        TemplateId           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NormaId               INT            NULL,
        Sigla_Doc             NVARCHAR(50)   NOT NULL,
        Template_Nome         NVARCHAR(300)  NOT NULL,
        Tipo_Doc              NVARCHAR(200)  NULL,
        Arquivo_Referencia    NVARCHAR(500)  NULL,   -- ver nota de OBJETIVO — local físico ainda não decidido
        Data_Criacao          DATETIME2      NOT NULL CONSTRAINT DF_Template_Data_Criacao DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_Template_Norma FOREIGN KEY (NormaId)
            REFERENCES config.Norma (NormaId) ON DELETE NO ACTION
    );

    CREATE INDEX IX_Template_NormaId ON config.Template (NormaId);

    PRINT 'Tabela [config].Template e índice criados.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM config.Template; -- deve devolver 0 logo após a criação.
SELECT * FROM sys.foreign_keys WHERE name = 'FK_Template_Norma';

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE config.Template;
*/
