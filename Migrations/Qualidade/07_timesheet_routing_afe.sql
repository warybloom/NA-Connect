SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF SCHEMA_ID(N'qualidade') IS NULL
    EXEC(N'CREATE SCHEMA qualidade');

IF OBJECT_ID(N'qualidade.TimesheetRoutingConfig', N'U') IS NULL
BEGIN
    CREATE TABLE qualidade.TimesheetRoutingConfig
    (
        Project_ID_ACC   nvarchar(100) NOT NULL
            CONSTRAINT PK_TimesheetRoutingConfig PRIMARY KEY,
        BiProjectCode    nvarchar(100) NOT NULL,
        RoutingMode      varchar(20) NOT NULL,
        AfeCode          nvarchar(100) NULL,
        Ativo            bit NOT NULL
            CONSTRAINT DF_TimesheetRoutingConfig_Ativo DEFAULT(1),
        AlteradoPor      nvarchar(300) NULL,
        AlteradoEm       datetime2(7) NOT NULL
            CONSTRAINT DF_TimesheetRoutingConfig_AlteradoEm DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT CK_TimesheetRoutingConfig_Mode
            CHECK (RoutingMode IN ('PROJECT','PROJECT_AFE')),
        CONSTRAINT CK_TimesheetRoutingConfig_Afe
            CHECK (
                (RoutingMode='PROJECT' AND AfeCode IS NULL)
                OR
                (RoutingMode='PROJECT_AFE' AND NULLIF(LTRIM(RTRIM(AfeCode)),'') IS NOT NULL)
            )
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'qualidade.TimesheetRoutingConfig')
      AND name=N'UX_TimesheetRoutingConfig_BiProject_Afe'
)
BEGIN
    CREATE UNIQUE INDEX UX_TimesheetRoutingConfig_BiProject_Afe
        ON qualidade.TimesheetRoutingConfig(BiProjectCode,AfeCode)
        WHERE Ativo=1 AND RoutingMode='PROJECT_AFE';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'qualidade.TimesheetRoutingConfig')
      AND name=N'IX_TimesheetRoutingConfig_BiProject'
)
BEGIN
    CREATE INDEX IX_TimesheetRoutingConfig_BiProject
        ON qualidade.TimesheetRoutingConfig(BiProjectCode,Ativo,RoutingMode)
        INCLUDE(Project_ID_ACC,AfeCode,AlteradoEm);
END;
