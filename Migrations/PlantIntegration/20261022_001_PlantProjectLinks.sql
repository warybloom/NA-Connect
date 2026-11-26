/*
   Plant Dashboard integration -- persistent N&A Connect to Dashboard BIM binding.
   Execute manually against DB_NA_CONNECT only. This script is intentionally idempotent.
   No FK is created because historical installations do not guarantee a compatible unique
   key/type on dbo.Projetos.Project_ID_ACC; the API validates the project before writing.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PlantProjectLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantProjectLinks
    (
        ProjectIdAcc       nvarchar(100) NOT NULL,
        DashboardProjectId uniqueidentifier NOT NULL,
        CreatedAtUtc       datetime2(3) NOT NULL
            CONSTRAINT DF_PlantProjectLinks_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc       datetime2(3) NOT NULL
            CONSTRAINT DF_PlantProjectLinks_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantProjectLinks PRIMARY KEY (ProjectIdAcc)
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.PlantProjectLinks')
      AND name = N'UX_PlantProjectLinks_DashboardProjectId'
)
    CREATE UNIQUE INDEX UX_PlantProjectLinks_DashboardProjectId
        ON dbo.PlantProjectLinks (DashboardProjectId);
GO

/* Per-project comparison configuration.  This is N&A Connect data only. */
IF OBJECT_ID(N'dbo.PlantCategoryMappings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantCategoryMappings
    (
        ProjectIdAcc nvarchar(100) NOT NULL,
        MappingJson  nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_PlantCategoryMappings_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_PlantCategoryMappings_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantCategoryMappings PRIMARY KEY (ProjectIdAcc),
        CONSTRAINT CK_PlantCategoryMappings_MappingJson CHECK (ISJSON(MappingJson) = 1)
    );
END;
GO
