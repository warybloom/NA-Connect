/*
  N&A Connect Plant 3D — filtros centrais e metas por fase.
  Execute SOMENTE em DB_NA_CONNECT, após backup verificado.
  Histórico DCF pertence ao Dashboard BIM e NÃO é modificado.
*/
IF DB_NAME() <> N'DB_NA_CONNECT'
    THROW 53000, 'Migration apenas para DB_NA_CONNECT.', 1;
GO
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.PlantCategoryFilters',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantCategoryFilters(
        ProjectIdAcc nvarchar(100) NOT NULL,
        FilterJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantCategoryFilters_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantCategoryFilters_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantCategoryFilters PRIMARY KEY (ProjectIdAcc),
        CONSTRAINT CK_PlantCategoryFilters_Json CHECK (ISJSON(FilterJson)=1)
    );
END;
GO
IF OBJECT_ID(N'dbo.PlantMeasurementPhases',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantMeasurementPhases(
        ProjectIdAcc nvarchar(100) NOT NULL,
        PhasesJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantMeasurementPhases_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantMeasurementPhases_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantMeasurementPhases PRIMARY KEY (ProjectIdAcc),
        CONSTRAINT CK_PlantMeasurementPhases_Json CHECK (ISJSON(PhasesJson)=1)
    );
END;
GO
