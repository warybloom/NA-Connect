/*
============================================================
OBJETIVO
============================================================
Criar uma TABELA FÍSICA de snapshot diário por documento LDP — grão: Project_ID_ACC + NA_Document_Number
+ Data_Snapshot (1 linha por documento por dia). Necessária porque dbo.VW_Ldp_Unificada e
dbo.VW_Cronograma_Tipo2_Ultima_Versao são views de ÚLTIMA VERSÃO (sem histórico) — sem uma tabela física
acumulando um snapshot por dia, NENHUMA Curva S de avanço físico (Percent_Physical_Completed ao longo do
tempo) é possível, porque o dado histórico simplesmente não existe em nenhum lugar hoje.

Esta migration cria SÓ A ESTRUTURA. Não popula nada, não cria job/trigger — a alimentação diária
(inserção de 1 linha por documento por dia) é responsabilidade de um processo separado, fora do escopo
desta sessão (nunca inventado aqui). Sem esse processo rodando, a tabela fica vazia e qualquer view que
dependa dela deve tratar "sem linhas" como "sem histórico ainda", nunca como erro.

============================================================
DEPENDÊNCIAS
============================================================
01_criar_schema_gestao.sql (schema [gestao] deve existir). Lê estruturalmente de dbo.VW_Ldp_Unificada
(só para os nomes/tipos de coluna espelhados — nenhuma FK real para uma view).

============================================================
PRÉ-CHECK + CREATE + ÍNDICES (mesmo lote — nunca depender de RETURN atravessar um GO: lotes
posteriores executam de qualquer forma mesmo depois de um RETURN/RAISERROR num lote anterior)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    RAISERROR('Schema [gestao] não existe — execute 01_criar_schema_gestao.sql primeiro.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'Snapshot_Diario_Documento')
BEGIN
    PRINT 'Tabela [gestao].Snapshot_Diario_Documento já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE gestao.Snapshot_Diario_Documento
    (
        Id                          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Data_Snapshot               DATE NOT NULL,                          -- dia do snapshot (chave, junto com os dois campos abaixo)
        Project_ID_ACC              NVARCHAR(100) NOT NULL,
        NA_Document_Number          NVARCHAR(300) NOT NULL,                 -- mesmo domínio de dbo.VW_Ldp_Unificada.NA_Document_Number
        Discipline                  NVARCHAR(200) NULL,
        Document_Status             NVARCHAR(200) NULL,
        Status_In_Flow              NVARCHAR(200) NULL,
        Situation                   NVARCHAR(200) NULL,
        Percent_Planned_Physical    DECIMAL(18,6) NULL,
        Percent_Physical_Completed  DECIMAL(18,6) NULL,
        Planned_HH                  DECIMAL(18,6) NULL,
        Actual_HH                   DECIMAL(18,6) NULL,                     -- coluna real já confirmada em VW_Ldp_Unificada
        HH_TimeSheet                DECIMAL(18,6) NULL,
        Planned_Cost                DECIMAL(18,6) NULL,
        Actual_Cost                 DECIMAL(18,6) NULL,
        Data_Insercao               DATETIME2 NOT NULL CONSTRAINT DF_Snapshot_Diario_Documento_Data_Insercao DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_Snapshot_Diario_Documento UNIQUE (Data_Snapshot, Project_ID_ACC, NA_Document_Number)
    );

    CREATE INDEX IX_Snapshot_Diario_Documento_Projeto_Data
        ON gestao.Snapshot_Diario_Documento (Project_ID_ACC, Data_Snapshot);

    PRINT 'Tabela [gestao].Snapshot_Diario_Documento e índice criados.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM gestao.Snapshot_Diario_Documento; -- deve devolver 0 logo após a criação.
SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('gestao.Snapshot_Diario_Documento'); -- deve listar PK + UQ + IX criados acima.

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE gestao.Snapshot_Diario_Documento;
*/
