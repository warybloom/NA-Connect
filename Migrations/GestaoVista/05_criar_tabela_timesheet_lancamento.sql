/*
============================================================
OBJETIVO
============================================================
Criar a tabela física gestao.TimesheetLancamento — grão: 1 lançamento (pessoa x documento, ou pessoa x
documento x dia SE a LDP importada tiver coluna "Data" na aba Timesheet; a LDP de referência auditada
NÃO tem essa coluna). Fonte real: aba "Timesheet" (oculta) da LDP .xlsx, ver
AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 1 e GESTAO_VISTA_MODELO_DADOS.md.

Esta migration cria SÓ A ESTRUTURA. A alimentação é responsabilidade do extrator/repositório
(Services/GestaoVista/*, fora desta migration) — read-only sobre o workbook, nunca escreve na LDP.

============================================================
DEPENDÊNCIAS
============================================================
01_criar_schema_gestao.sql.

============================================================
PRÉ-CHECK + CREATE + ÍNDICES (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    RAISERROR('Schema [gestao] não existe — execute 01_criar_schema_gestao.sql primeiro.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'TimesheetLancamento')
BEGIN
    PRINT 'Tabela [gestao].TimesheetLancamento já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE gestao.TimesheetLancamento
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Project_ID_ACC      NVARCHAR(100)  NOT NULL,
        NomeProfissional    NVARCHAR(300)  NOT NULL,
        HorasPlanejadas     DECIMAL(18,6)  NULL,
        HorasTrabalhadas    DECIMAL(18,6)  NULL,
        Projeto             NVARCHAR(300)  NULL,
        Funcao              NVARCHAR(200)  NULL,
        Disciplina          NVARCHAR(200)  NULL,
        DocumentoCodigo     NVARCHAR(300)  NULL,
        Documento           NVARCHAR(500)  NULL,
        Descricao           NVARCHAR(1000) NULL,
        DataLancamento      DATE           NULL,   -- só preenchido se a coluna "Data" existir na aba Timesheet importada
        CustoCalculado      DECIMAL(18,6)  NULL,    -- HorasTrabalhadas x CustoHora vigente (nunca copiado da planilha)
        RowHash             BINARY(32)     NOT NULL,
        Data_Sincronizacao  DATETIME2      NOT NULL CONSTRAINT DF_TimesheetLancamento_Data_Sincronizacao DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_TimesheetLancamento_Hash UNIQUE (Project_ID_ACC, RowHash)
    );

    CREATE INDEX IX_TimesheetLancamento_Documento
        ON gestao.TimesheetLancamento (Project_ID_ACC, DocumentoCodigo);

    CREATE INDEX IX_TimesheetLancamento_Profissional
        ON gestao.TimesheetLancamento (Project_ID_ACC, NomeProfissional);

    PRINT 'Tabela [gestao].TimesheetLancamento e índices criados.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM gestao.TimesheetLancamento; -- deve devolver 0 logo após a criação.
SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('gestao.TimesheetLancamento');

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE gestao.TimesheetLancamento;
*/
