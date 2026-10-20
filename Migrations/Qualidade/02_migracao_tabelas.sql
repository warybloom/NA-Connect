/*
    ============================================================================================
    QUALIDADE — PASSO 2 de 2: MIGRAÇÃO DAS TABELAS (BASE LANÇAMENTO / RNC / DESVIO DE QUALIDADE)
    ============================================================================================
    NÃO EXECUTADO AUTOMATICAMENTE — revise e rode manualmente, DEPOIS de revisar o resultado de
    01_verificar_dependencias.sql e atualizar (fora deste script) qualquer View/Procedure/Function
    listada que ainda aponte para o nome antigo assumindo o schema de BASE LANÇAMENTO.

    O que este script faz:
      1) Verifica se [dbo].[NCR NONCONFORMITY REPORT] existe e se [dbo].[NCR BASE LANCAMENTO]
         ainda NÃO existe (pré-condição — aborta sem alterar nada se falhar).
      2) Renomeia [dbo].[NCR NONCONFORMITY REPORT] -> [dbo].[NCR BASE LANCAMENTO] (sp_rename,
         preserva 100% dos dados e o schema de colunas exatamente como estão hoje — nenhuma
         coluna é adicionada/removida/alterada).
      3) Cria a NOVA [dbo].[NCR NONCONFORMITY REPORT], com o schema da aba RNC.
      4) Cria [dbo].[DESVIO DE QUALIDADE], com o schema da aba DESVIO DE QUALIDADE.
      5) Cria índices não-clusterizados por Project_ID_ACC nas três tabelas.

    O que este script NUNCA faz:
      - DROP TABLE (nenhuma tabela é apagada).
      - DELETE (nenhum registro é apagado — sp_rename preserva os dados da tabela renomeada).
      - Alterar tipos de coluna da tabela renomeada.

    Todo o script roda em UMA transação com SET XACT_ABORT ON: qualquer erro em qualquer etapa
    desfaz TODAS as anteriores (nunca fica com o rename feito mas as tabelas novas ausentes, nem
    vice-versa).
    ============================================================================================
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION MigracaoQualidade;

BEGIN TRY

    -- --------------------------------------------------------------------------------------
    -- 1) Pré-condições (seção 3.1/3.2 do pedido) — aborta sem alterar nada se não baterem.
    -- --------------------------------------------------------------------------------------
    IF OBJECT_ID(N'[dbo].[NCR NONCONFORMITY REPORT]', N'U') IS NULL
    BEGIN
        RAISERROR(N'Pré-condição falhou: [dbo].[NCR NONCONFORMITY REPORT] não existe. Migração abortada, nada foi alterado.', 16, 1);
    END

    IF OBJECT_ID(N'[dbo].[NCR BASE LANCAMENTO]', N'U') IS NOT NULL
    BEGIN
        RAISERROR(N'Pré-condição falhou: [dbo].[NCR BASE LANCAMENTO] já existe (migração provavelmente já foi executada antes). Migração abortada, nada foi alterado.', 16, 1);
    END

    IF OBJECT_ID(N'[dbo].[DESVIO DE QUALIDADE]', N'U') IS NOT NULL
    BEGIN
        RAISERROR(N'Pré-condição falhou: [dbo].[DESVIO DE QUALIDADE] já existe (migração provavelmente já foi executada antes). Migração abortada, nada foi alterado.', 16, 1);
    END

    -- --------------------------------------------------------------------------------------
    -- 2) Renomeia a tabela existente (estrutura de BASE LANÇAMENTO) — preserva 100% dos dados
    --    e o schema de colunas exatamente como estão hoje (seção 3.3 / seção 6 do pedido).
    -- --------------------------------------------------------------------------------------
    EXEC sp_rename N'[dbo].[NCR NONCONFORMITY REPORT]', N'NCR BASE LANCAMENTO';

    -- --------------------------------------------------------------------------------------
    -- 3) Cria a NOVA [dbo].[NCR NONCONFORMITY REPORT] — schema da aba RNC (seção 4 do pedido).
    --    Campos obrigatórios de negócio (NOT NULL) são exatamente os exigidos para localizar o
    --    cabeçalho da aba no código (CodigoFormulario, Projeto, CR, DescricaoSituacao) — mesma
    --    lista usada pelo preflight (QualidadeCabecalhos.RncObrigatorios), para nunca repetir o
    --    erro "Column '...' does not allow DBNull.Value" por divergência entre código e schema.
    -- --------------------------------------------------------------------------------------
    CREATE TABLE [dbo].[NCR NONCONFORMITY REPORT]
    (
        [Id]                  INT IDENTITY(1,1) NOT NULL,
        [Project_ID_ACC]      NVARCHAR(100)      NOT NULL,
        [Data_Sincronizacao]  DATETIME           NOT NULL,

        [DataRegistro]        DATETIME           NULL,
        [CodigoFormulario]    NVARCHAR(100)       NOT NULL,
        [Projeto]             NVARCHAR(200)       NOT NULL,
        [CR]                  NVARCHAR(100)       NOT NULL,
        [Disciplina]          NVARCHAR(100)       NULL,
        [Responsavel]         NVARCHAR(200)       NULL,
        [DescricaoSituacao]   NVARCHAR(MAX)       NOT NULL,
        [ImpactoProjeto]      NVARCHAR(MAX)       NULL,
        [TipoRNC]             NVARCHAR(100)       NULL,
        [TipoAcao]            NVARCHAR(100)       NULL,
        [ResponsavelAcao]     NVARCHAR(200)       NULL,
        [Acao]                NVARCHAR(MAX)       NULL,
        [Status]              NVARCHAR(100)       NULL,
        [PrazoConclusao]      DATETIME            NULL,
        [DataConclusao]       DATETIME            NULL,
        [DiasEmAberto]        INT                 NULL,

        [DataCriacao]         DATETIME            NOT NULL CONSTRAINT DF_NcrNonconformityReport_DataCriacao DEFAULT (GETDATE()),
        [DataAtualizacao]     DATETIME            NOT NULL CONSTRAINT DF_NcrNonconformityReport_DataAtualizacao DEFAULT (GETDATE()),

        CONSTRAINT PK_NcrNonconformityReport PRIMARY KEY CLUSTERED ([Id])
    );

    -- --------------------------------------------------------------------------------------
    -- 4) Cria [dbo].[DESVIO DE QUALIDADE] — schema da aba DESVIO DE QUALIDADE (seção 5 do
    --    pedido). Obrigatórios (NOT NULL) = QualidadeCabecalhos.DesvioQualidadeObrigatorios.
    -- --------------------------------------------------------------------------------------
    CREATE TABLE [dbo].[DESVIO DE QUALIDADE]
    (
        [Id]                       INT IDENTITY(1,1) NOT NULL,
        [Project_ID_ACC]           NVARCHAR(100)      NOT NULL,
        [Data_Sincronizacao]       DATETIME           NOT NULL,

        [Projeto]                  NVARCHAR(200)       NOT NULL,
        [CR]                       NVARCHAR(100)       NOT NULL,
        [Disciplina]               NVARCHAR(100)       NULL,
        [NumeroDocumento]          NVARCHAR(200)       NOT NULL,
        [QuantidadeDocumentos]     INT                 NULL,
        [DescricaoInconsistencia]  NVARCHAR(MAX)       NOT NULL,
        [TipoInconsistencia]       NVARCHAR(200)       NULL,
        [Responsavel]              NVARCHAR(200)       NULL,
        [DataConclusao]            DATETIME            NULL,

        [DataCriacao]              DATETIME            NOT NULL CONSTRAINT DF_DesvioQualidade_DataCriacao DEFAULT (GETDATE()),
        [DataAtualizacao]          DATETIME            NOT NULL CONSTRAINT DF_DesvioQualidade_DataAtualizacao DEFAULT (GETDATE()),

        CONSTRAINT PK_DesvioQualidade PRIMARY KEY CLUSTERED ([Id])
    );

    -- --------------------------------------------------------------------------------------
    -- 5) Índices por Project_ID_ACC nas três tabelas (seção 3.9 do pedido). Guardados com
    --    IF NOT EXISTS porque a tabela renomeada (antiga NCR NONCONFORMITY REPORT) pode já ter
    --    um índice equivalente — não recriamos se já existir.
    -- --------------------------------------------------------------------------------------
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[dbo].[NCR BASE LANCAMENTO]') AND name = N'IX_NcrBaseLancamento_ProjectIdAcc'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_NcrBaseLancamento_ProjectIdAcc ON [dbo].[NCR BASE LANCAMENTO] ([Project_ID_ACC]);
    END

    CREATE NONCLUSTERED INDEX IX_NcrNonconformityReport_ProjectIdAcc ON [dbo].[NCR NONCONFORMITY REPORT] ([Project_ID_ACC]);
    CREATE NONCLUSTERED INDEX IX_DesvioQualidade_ProjectIdAcc ON [dbo].[DESVIO DE QUALIDADE] ([Project_ID_ACC]);

    COMMIT TRANSACTION MigracaoQualidade;

    PRINT N'Migração de Qualidade concluída: [dbo].[NCR NONCONFORMITY REPORT] renomeada para [dbo].[NCR BASE LANCAMENTO] (dados preservados); ' +
          N'[dbo].[NCR NONCONFORMITY REPORT] (nova, schema RNC) e [dbo].[DESVIO DE QUALIDADE] criadas; índices por Project_ID_ACC criados nas três.';

END TRY
BEGIN CATCH

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION MigracaoQualidade;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrState INT = ERROR_STATE();

    RAISERROR(N'Migração de Qualidade ABORTADA (rollback executado, nenhuma tabela foi alterada): %s', @ErrSeverity, @ErrState, @ErrMsg);

END CATCH;
