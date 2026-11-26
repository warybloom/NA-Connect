/*
============================================================
OBJETIVO
============================================================
Criar a tabela física gestao.CustoProfissionalRealHistorico — grão: 1 VERSÃO de custo por
Project_ID_ACC + NomeProfissional + vigência (SCD tipo 2, nunca UPSERT cego). Fonte real: aba
"Custo Real" (oculta) da LDP .xlsx — ver AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 2 e
GESTAO_VISTA_MODELO_DADOS.md.

Regra de histórico (obrigatória pelo pedido do usuário): custo de uma pessoa pode variar por projeto E ao
longo do tempo. Nunca sobrescrever uma linha existente — ao detectar um RowHash diferente para a mesma
chave (Project_ID_ACC + NomeProfissional), o repositório de importação deve fechar VigenciaFim da linha
ativa anterior e inserir uma nova linha. A DATA de vigência nunca é inventada: usa-se a data de
sincronização da LDP em que o valor foi OBSERVADO (nunca uma data retroativa suposta).

Esta migration cria SÓ A ESTRUTURA — nenhuma linha é inserida aqui.

============================================================
DEPENDÊNCIAS
============================================================
01_criar_schema_gestao.sql.

============================================================
PRÉ-CHECK + CREATE + ÍNDICES (mesmo lote)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    RAISERROR('Schema [gestao] não existe — execute 01_criar_schema_gestao.sql primeiro.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'CustoProfissionalRealHistorico')
BEGIN
    PRINT 'Tabela [gestao].CustoProfissionalRealHistorico já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE gestao.CustoProfissionalRealHistorico
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Project_ID_ACC      NVARCHAR(100)  NOT NULL,
        NomeProfissional    NVARCHAR(300)  NOT NULL,
        Funcao              NVARCHAR(200)  NULL,
        Disciplina          NVARCHAR(200)  NULL,
        Remuneracao         DECIMAL(18,6)  NULL,
        Encargos            DECIMAL(18,6)  NULL,
        Softwares           DECIMAL(18,6)  NULL,
        Computadores        DECIMAL(18,6)  NULL,
        CustoTotalMensal    DECIMAL(18,6)  NULL,
        CustoHora           DECIMAL(18,6)  NOT NULL,
        VigenciaInicio      DATETIME2      NOT NULL,   -- = Data_Sincronizacao em que o valor foi observado pela 1a vez
        VigenciaFim         DATETIME2      NULL,       -- NULL = ainda vigente
        Ativo               BIT            NOT NULL CONSTRAINT DF_CustoProfissionalRealHistorico_Ativo DEFAULT 1,
        RowHash             BINARY(32)     NOT NULL,   -- hash dos campos de valor (Remuneracao..Computadores)

        CONSTRAINT CK_CustoProfissionalRealHistorico_Vigencia CHECK (VigenciaFim IS NULL OR VigenciaFim >= VigenciaInicio)
    );

    CREATE INDEX IX_CustoProfissionalRealHistorico_Chave
        ON gestao.CustoProfissionalRealHistorico (Project_ID_ACC, NomeProfissional, VigenciaInicio);

    -- índice filtrado: consulta de "custo vigente" (gestao.vw_Custo_Real_Vigente) é o caminho mais quente.
    CREATE INDEX IX_CustoProfissionalRealHistorico_Vigente
        ON gestao.CustoProfissionalRealHistorico (Project_ID_ACC, NomeProfissional)
        WHERE Ativo = 1 AND VigenciaFim IS NULL;

    PRINT 'Tabela [gestao].CustoProfissionalRealHistorico e índices criados.';
END
GO

/*
============================================================
CREATE — view de leitura (custo vigente), mesmo objetivo de VALIDAÇÃO/uso pelo extrator
============================================================
*/
IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'CustoProfissionalRealHistorico')
BEGIN
    EXEC('
        CREATE OR ALTER VIEW gestao.vw_Custo_Real_Vigente AS
        SELECT *
        FROM gestao.CustoProfissionalRealHistorico
        WHERE Ativo = 1 AND VigenciaFim IS NULL;
    ');
    PRINT 'View gestao.vw_Custo_Real_Vigente criada/atualizada.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM gestao.CustoProfissionalRealHistorico; -- 0 logo após a criação.
SELECT * FROM gestao.vw_Custo_Real_Vigente WHERE Project_ID_ACC = '<guid-de-teste>';

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP VIEW IF EXISTS gestao.vw_Custo_Real_Vigente;
-- DROP TABLE gestao.CustoProfissionalRealHistorico;
*/
