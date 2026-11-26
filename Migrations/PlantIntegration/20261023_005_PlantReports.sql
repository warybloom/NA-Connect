/*
  N&A Connect Plant 3D -- Central de Relatorios (F6): origem dos relatorios por projeto e preferencias por usuario.
  Execute manually ONLY against DB_NA_CONNECT after a verified backup and outside business hours.
  Idempotent. Nunca altera DB_DASHBOARD_BIM nem tabelas existentes.
*/
IF DB_NAME() <> N'DB_NA_CONNECT'
    THROW 53000, 'Migration apenas para DB_NA_CONNECT.', 1;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Origem de cada relatorio (categoria do DE/PARA OU tabelas P&ID/3D), por projeto.
IF OBJECT_ID(N'dbo.PlantReportConfigs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantReportConfigs
    (
        ProjectIdAcc nvarchar(100) NOT NULL,
        ConfigJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantReportConfigs_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantReportConfigs_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantReportConfigs PRIMARY KEY (ProjectIdAcc),
        CONSTRAINT CK_PlantReportConfigs_Json CHECK (ISJSON(ConfigJson)=1)
    );
END;
GO

-- Campos selecionados e filtros escolhidos por cada usuario em cada relatorio (preferencia, nao dado de negocio).
IF OBJECT_ID(N'dbo.PlantReportUserPreferences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantReportUserPreferences
    (
        ProjectIdAcc nvarchar(100) NOT NULL,
        UserId int NOT NULL,
        ReportKind nvarchar(40) NOT NULL,
        PreferencesJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantReportUserPreferences_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_PlantReportUserPreferences_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlantReportUserPreferences PRIMARY KEY (ProjectIdAcc, UserId, ReportKind),
        CONSTRAINT CK_PlantReportUserPreferences_Json CHECK (ISJSON(PreferencesJson)=1),
        CONSTRAINT CK_PlantReportUserPreferences_Kind CHECK (ReportKind IN (N'lines',N'equipment',N'instruments',N'valves',N'tie-ins',N'materials',N'supports'))
    );
END;
GO
