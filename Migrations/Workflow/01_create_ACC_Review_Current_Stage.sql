/*
    ============================================================================================
    ACC_REVIEW_STAGE — PASSO 1: CRIAÇÃO DA TABELA [dbo].[ACC_Review_Current_Stage]
    ============================================================================================
    NÃO EXECUTADO AUTOMATICAMENTE — revise e rode manualmente.

    O que este script faz:
      1) Verifica se [dbo].[ACC_Review_Current_Stage] já existe (pré-condição — aborta sem
         alterar nada se já existir, para nunca sobrescrever uma execução anterior).
      2) Cria a tabela com as colunas da seção 10 da funcionalidade "ACC_REVIEW_STAGE" (snapshot
         da etapa atual dos documentos em ACC Reviews).
      3) Cria um índice NÃO ÚNICO por (Project_ID_ACC, ReviewId, NumeroDocumentoNormalizado).

    O que este script NUNCA faz:
      - Alterar qualquer tabela existente (LDP, Cronograma, Qualidade, cache de atributos, etc.).
      - Criar índice/constraint UNIQUE (fora de escopo nesta primeira versão).
      - DROP TABLE / DELETE.

    Roda em UMA transação com SET XACT_ABORT ON: qualquer erro desfaz tudo.
    ============================================================================================
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION CriarAccReviewCurrentStage;

BEGIN TRY

    IF OBJECT_ID(N'[dbo].[ACC_Review_Current_Stage]', N'U') IS NOT NULL
    BEGIN
        RAISERROR(N'Pré-condição falhou: [dbo].[ACC_Review_Current_Stage] já existe. Script abortado, nada foi alterado.', 16, 1);
    END

    CREATE TABLE [dbo].[ACC_Review_Current_Stage]
    (
        [Id]                            BIGINT IDENTITY(1,1) NOT NULL,
        [Project_ID_ACC]                NVARCHAR(200)  NOT NULL,

        [ReviewId]                      NVARCHAR(150)  NOT NULL,
        [ReviewNumber]                  NVARCHAR(50)   NULL,
        [ReviewName]                    NVARCHAR(500)  NULL,
        [ReviewStatus]                  NVARCHAR(100)  NULL,

        [WorkflowId]                    NVARCHAR(150)  NULL,
        [CurrentStepId]                 NVARCHAR(200)  NULL,
        [EtapaAtual]                    NVARCHAR(250)  NULL,

        [ResponsavelEtapa]              NVARCHAR(1000) NULL,
        [ResponsavelEtapaIds]           NVARCHAR(1000) NULL,
        [OrigemResponsavel]             NVARCHAR(100)  NULL,

        [DataVencimentoEtapa]           DATE           NULL,
        [OrigemVencimento]              NVARCHAR(300)  NULL,

        [NumeroDocumento]               NVARCHAR(300)  NOT NULL,
        [NumeroDocumentoNormalizado]    NVARCHAR(300)  NOT NULL,

        [ArquivosFisicos]               NVARCHAR(MAX)  NULL,
        [ItemUrns]                      NVARCHAR(MAX)  NULL,
        [VersionUrns]                   NVARCHAR(MAX)  NULL,
        [PastasEncontradas]             NVARCHAR(MAX)  NULL,

        [StatusNoFluxo]                 NVARCHAR(250)  NULL,
        [DataAtualizacaoReview]         DATETIME2(0)   NULL,
        [DataSincronizacao]             DATETIME2(0)   NOT NULL,

        [DataCriacao]                   DATETIME2(0)   NOT NULL CONSTRAINT DF_AccReviewCurrentStage_DataCriacao DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_ACC_Review_Current_Stage PRIMARY KEY CLUSTERED ([Id])
    );

    CREATE NONCLUSTERED INDEX IX_AccReviewCurrentStage_Projeto_Review_Documento
        ON [dbo].[ACC_Review_Current_Stage] ([Project_ID_ACC], [ReviewId], [NumeroDocumentoNormalizado]);

    COMMIT TRANSACTION CriarAccReviewCurrentStage;

    PRINT N'[dbo].[ACC_Review_Current_Stage] criada com sucesso, com índice não único por (Project_ID_ACC, ReviewId, NumeroDocumentoNormalizado).';

END TRY
BEGIN CATCH

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION CriarAccReviewCurrentStage;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrState INT = ERROR_STATE();

    RAISERROR(N'Criação de [dbo].[ACC_Review_Current_Stage] ABORTADA (rollback executado, nenhuma alteração foi aplicada): %s', @ErrSeverity, @ErrState, @ErrMsg);

END CATCH;
