SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION AddPlannedPhysical;

BEGIN TRY

    IF OBJECT_ID(N'[dbo].[Ldp_Documento_Gates]', N'U') IS NULL
    BEGIN
        RAISERROR(
            N'Pré-condição falhou: [dbo].[Ldp_Documento_Gates] não existe.',
            16,
            1
        );
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[Ldp_Documento_Gates]')
          AND name = N'Planned_Physical'
    )
    BEGIN
        RAISERROR(
            N'Pré-condição falhou: [Planned_Physical] já existe.',
            16,
            1
        );
    END;

    ALTER TABLE [dbo].[Ldp_Documento_Gates]
        ADD [Planned_Physical] DECIMAL(18,6) NULL;

    COMMIT TRANSACTION AddPlannedPhysical;

    PRINT N'[dbo].[Ldp_Documento_Gates].[Planned_Physical] criada com sucesso.';

END TRY
BEGIN CATCH

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION AddPlannedPhysical;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();

    RAISERROR(
        N'Criação de [Planned_Physical] abortada: %s',
        16,
        1,
        @ErrMsg
    );

END CATCH;