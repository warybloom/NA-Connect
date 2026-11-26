/*
  N&A Connect Plant 3D -- standards catalog and phase-scoped line weights.
  Execute manually ONLY against DB_NA_CONNECT after a verified backup and outside
  business hours.  This idempotent script never changes DB_DASHBOARD_BIM.
*/
IF DB_NAME() <> N'DB_NA_CONNECT'
    THROW 53000, 'Migration apenas para DB_NA_CONNECT.', 1;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.PlantIntegrationStandards', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantIntegrationStandards
    (
        ProjectIdAcc nvarchar(100) NOT NULL,
        StandardsJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantIntegrationStandards_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantIntegrationStandards_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantIntegrationStandards PRIMARY KEY (ProjectIdAcc),
        CONSTRAINT CK_PlantIntegrationStandards_Json CHECK (ISJSON(StandardsJson)=1)
    );
END;
GO

IF OBJECT_ID(N'dbo.PlantLineWeights', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantLineWeights
    (
        ProjectIdAcc nvarchar(100) NOT NULL,
        WeightsJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantLineWeights_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantLineWeights_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantLineWeights PRIMARY KEY (ProjectIdAcc),
        CONSTRAINT CK_PlantLineWeights_Json CHECK (ISJSON(WeightsJson)=1)
    );
END;
GO
