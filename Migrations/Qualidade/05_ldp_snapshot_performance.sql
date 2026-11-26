SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Ldp_Unificada')
      AND name = N'IX_Ldp_Unificada_Project_Sync_Id'
)
BEGIN
    CREATE INDEX IX_Ldp_Unificada_Project_Sync_Id
        ON dbo.Ldp_Unificada(Project_ID_ACC, Data_Sincronizacao, ID_Registro)
        INCLUDE
        (
            NA_Document_Number,
            Document_Status,
            Planned_HH,
            Doc_Type_Code,
            Discipline_Activity,
            Document_Title
        );
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Ldp_Unificada')
      AND name = N'IX_Ldp_Unificada_Project_Document_Sync'
)
BEGIN
    CREATE INDEX IX_Ldp_Unificada_Project_Document_Sync
        ON dbo.Ldp_Unificada(Project_ID_ACC, NA_Document_Number, Data_Sincronizacao)
        INCLUDE
        (
            Document_Status,
            Planned_HH,
            Doc_Type_Code,
            Discipline_Activity,
            Document_Title
        );
END
GO
