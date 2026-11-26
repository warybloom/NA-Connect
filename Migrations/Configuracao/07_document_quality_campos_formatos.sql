/*
============================================================
OBJETIVO (01/10/2026 — Auditoria de Qualidade de Documentos, rodada corretiva da Fase 2)
============================================================
Separar dois conceitos que hoje moram juntos em config.PacoteOcrCoordenada:

  1) QUAIS CAMPOS o template audita  → pertence ao TEMPLATE        → config.PacoteOcrCampo   (NOVA)
  2) ONDE cada campo está na folha    → TEMPLATE + FORMATO + CAMPO → config.PacoteOcrCoordenada (inalterada)

e registrar o MAPEAMENTO DE FORMATOS da LDP por template (valor da coluna Format da LDP → perfil
calibrado / detectar pela dimensão / ignorar)                       → config.PacoteOcrFormato (NOVA)

Motivo: o conjunto de campos era derivado das regiões existentes; remover um campo num formato não
o removia de outro, e o Detalhe da auditoria mostrava campos "antigos". E "D" na LDP não significa
ANSI D em todo projeto (pode ser ARCH D ou código do cliente) — a tradução é do template.

NADA é apagado: config.PacoteOcrCoordenada não é alterada. Os campos ativos de cada template são
semeados a partir das regiões já existentes (uma vez, só se o template ainda não tiver campos).

============================================================
DEPENDÊNCIAS
============================================================
03_criar_config_pacote_ocr.sql (config.PacoteOcr / config.PacoteOcrCoordenada) e 06_document_quality_mvp.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada objeto é criado só se não existir; a semeadura só insere campos para templates sem nenhum campo.
Tudo numa transação com TRY/CATCH (rollback completo em caso de erro).
NÃO É EXECUTADO PELA APLICAÇÃO. Execução manual, após aprovação.
============================================================
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcrCoordenada')
BEGIN
    RAISERROR('config.PacoteOcrCoordenada não existe — execute 03_criar_config_pacote_ocr.sql primeiro.', 16, 1);
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcrCampo')
    BEGIN
        CREATE TABLE config.PacoteOcrCampo
        (
            PacoteOcrCampoId  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            PacoteOcrId       INT            NOT NULL,
            CampoDestino      NVARCHAR(100)  NOT NULL,   -- nome REAL da coluna da LDP (validado contra o schema antes de gravar)
            Ordem             INT            NOT NULL CONSTRAINT DF_PacoteOcrCampo_Ordem DEFAULT (0),
            Ativo             BIT            NOT NULL CONSTRAINT DF_PacoteOcrCampo_Ativo DEFAULT (1),
            Data_Atualizacao  DATETIME2      NOT NULL CONSTRAINT DF_PacoteOcrCampo_Data DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_PacoteOcrCampo_Pacote FOREIGN KEY (PacoteOcrId) REFERENCES config.PacoteOcr (PacoteOcrId) ON DELETE CASCADE,
            CONSTRAINT UQ_PacoteOcrCampo_Campo UNIQUE (PacoteOcrId, CampoDestino)
        );
        PRINT 'Tabela [config].PacoteOcrCampo criada.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcrFormato')
    BEGIN
        CREATE TABLE config.PacoteOcrFormato
        (
            PacoteOcrFormatoId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            PacoteOcrId        INT            NOT NULL,
            ValorFormatoLdp    NVARCHAR(50)   NOT NULL,  -- valor da coluna Format da LDP (ex.: D, A1, AT)
            Modo               NVARCHAR(20)   NOT NULL,  -- PERFIL | DIMENSAO | IGNORAR
            TipoFolha          NVARCHAR(50)   NULL,      -- perfil calibrado (config.PacoteOcrCoordenada.TipoFolha) quando Modo = PERFIL
            Data_Atualizacao   DATETIME2      NOT NULL CONSTRAINT DF_PacoteOcrFormato_Data DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_PacoteOcrFormato_Pacote FOREIGN KEY (PacoteOcrId) REFERENCES config.PacoteOcr (PacoteOcrId) ON DELETE CASCADE,
            CONSTRAINT UQ_PacoteOcrFormato_Valor UNIQUE (PacoteOcrId, ValorFormatoLdp),
            CONSTRAINT CK_PacoteOcrFormato_Modo CHECK (Modo IN (N'PERFIL', N'DIMENSAO', N'IGNORAR')),
            CONSTRAINT CK_PacoteOcrFormato_Perfil CHECK (Modo <> N'PERFIL' OR TipoFolha IS NOT NULL)
        );
        PRINT 'Tabela [config].PacoteOcrFormato criada.';
    END

    -- Semeadura (compatibilidade): campos ativos = campos que o template já tem região em QUALQUER formato.
    -- Só para templates que ainda não têm nenhum campo. Ordem = primeira região cadastrada do campo.
    INSERT INTO config.PacoteOcrCampo (PacoteOcrId, CampoDestino, Ordem, Ativo)
    SELECT C.PacoteOcrId, C.CampoDestino, ROW_NUMBER() OVER (PARTITION BY C.PacoteOcrId ORDER BY MIN(C.PacoteOcrCoordenadaId)), 1
    FROM config.PacoteOcrCoordenada C
    WHERE NOT EXISTS (SELECT 1 FROM config.PacoteOcrCampo F WHERE F.PacoteOcrId = C.PacoteOcrId)
    GROUP BY C.PacoteOcrId, C.CampoDestino;
    PRINT CONCAT('Campos semeados a partir das regiões existentes: ', @@ROWCOUNT);

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
SELECT PacoteOcrId, CampoDestino, Ordem, Ativo FROM config.PacoteOcrCampo ORDER BY PacoteOcrId, Ordem;
SELECT COUNT(*) FROM config.PacoteOcrCoordenada;   -- deve ser IGUAL ao valor antes da migration (nada apagado)
SELECT * FROM config.PacoteOcrFormato;              -- vazio: nenhum mapeamento é assumido (Format "D" NÃO vira ANSI_D sozinho)

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE config.PacoteOcrFormato;
-- DROP TABLE config.PacoteOcrCampo;
*/
