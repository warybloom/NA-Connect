/*
============================================================
OBJETIVO
============================================================
Criar a tabela física gestao.CustoProfissionalPrevisto — grão: 1 linha por Project_ID_ACC + Funcao +
Disciplina por sincronização (snapshot, SEM vigência própria — a planilha não expõe histórico desse
valor, só o estado atual). Fonte real: aba "Custo profissionais" (oculta) da LDP .xlsx — ver
AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 3 e GESTAO_VISTA_MODELO_DADOS.md.

IMPORTANTE: esta tabela é CUSTO PREVISTO/REFERÊNCIA (planejamento), nunca deve ser confundida ou cruzada
com gestao.CustoProfissionalRealHistorico (custo real por pessoa). Nomes deliberadamente distintos
("...Real..." vs. "...Previsto") para evitar join acidental.

O bloco "MIX" (percentuais Sênior/Pleno/Júnior por função, presente na mesma aba com layout não
tabular-padrão) NÃO está incluído nesta migration — registrado como candidato futuro em
GESTAO_VISTA_MODELO_DADOS.md, precisa de parser dedicado.

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
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'CustoProfissionalPrevisto')
BEGIN
    PRINT 'Tabela [gestao].CustoProfissionalPrevisto já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE gestao.CustoProfissionalPrevisto
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Project_ID_ACC      NVARCHAR(100)  NOT NULL,
        Funcao              NVARCHAR(200)  NOT NULL,
        GrupoFuncao         NVARCHAR(200)  NULL,   -- coluna B da aba (sem cabeçalho textual, mas populada; agrupamento tipo "ENGINEER"/"DESIGNER")
        Disciplina          NVARCHAR(200)  NULL,
        Remuneracao         DECIMAL(18,6)  NULL,
        Encargos            DECIMAL(18,6)  NULL,
        Softwares           DECIMAL(18,6)  NULL,
        Computadores        DECIMAL(18,6)  NULL,
        CustoTotalMensal    DECIMAL(18,6)  NULL,
        CustoHora           DECIMAL(18,6)  NOT NULL,
        Data_Sincronizacao  DATETIME2      NOT NULL CONSTRAINT DF_CustoProfissionalPrevisto_Data_Sincronizacao DEFAULT SYSUTCDATETIME(),
        RowHash             BINARY(32)     NOT NULL,

        CONSTRAINT UQ_CustoProfissionalPrevisto_Hash UNIQUE (Project_ID_ACC, RowHash)
    );

    CREATE INDEX IX_CustoProfissionalPrevisto_FuncaoDisciplina
        ON gestao.CustoProfissionalPrevisto (Project_ID_ACC, Funcao, Disciplina);

    PRINT 'Tabela [gestao].CustoProfissionalPrevisto e índice criados.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM gestao.CustoProfissionalPrevisto; -- 0 logo após a criação.

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE gestao.CustoProfissionalPrevisto;
*/
