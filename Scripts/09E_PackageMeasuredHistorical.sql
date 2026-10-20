-- DB_NA_CONNECT only. No Timesheet/LDP writes. This script does not confirm any package.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
IF DB_NAME() <> N'DB_NA_CONNECT' THROW 51000, 'Wrong database for historical measurements.', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'costs.PackageMeasuredHistorical',N'U') IS NULL
BEGIN
    CREATE TABLE costs.PackageMeasuredHistorical(
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PackageMeasuredHistorical PRIMARY KEY,
        Project_ID_ACC nvarchar(255) NOT NULL,
        PackageId int NOT NULL CONSTRAINT FK_MeasuredHistorical_Package REFERENCES costs.Package(Id),
        ProfessionalName nvarchar(200) NOT NULL,
        MeasurementType varchar(40) NOT NULL CONSTRAINT DF_MeasuredHistorical_Type DEFAULT 'MEDICAO_HISTORICA_CONFIRMADA',
        ConfirmedHours decimal(18,6) NOT NULL,
        ConfirmedCost decimal(18,6) NOT NULL,
        ConfirmedAtUtc datetime2(3) NOT NULL,
        ConfirmedBy nvarchar(255) NOT NULL,
        Status varchar(20) NOT NULL,
        Origin nvarchar(2000) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_MeasuredHistorical_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_MeasuredHistorical_Type CHECK(MeasurementType='MEDICAO_HISTORICA_CONFIRMADA'),
        CONSTRAINT CK_MeasuredHistorical_Amounts CHECK(ConfirmedHours>=0 AND ConfirmedCost>=0),
        CONSTRAINT CK_MeasuredHistorical_Status CHECK(Status IN ('CONFIRMADA','REVOGADA'))
    );
    CREATE UNIQUE INDEX UX_MeasuredHistorical_Active ON costs.PackageMeasuredHistorical(PackageId) WHERE Status='CONFIRMADA';
    CREATE INDEX IX_MeasuredHistorical_Project ON costs.PackageMeasuredHistorical(Project_ID_ACC,Status);
END;
IF OBJECT_ID(N'costs.PackageMeasuredHistoricalDiscipline',N'U') IS NULL
BEGIN
    CREATE TABLE costs.PackageMeasuredHistoricalDiscipline(
        ConfirmationId bigint NOT NULL CONSTRAINT FK_MeasuredHistoricalDiscipline_Confirmation REFERENCES costs.PackageMeasuredHistorical(Id),
        Discipline nvarchar(200) NOT NULL,
        ConfirmedHours decimal(18,6) NOT NULL,
        ConfirmedCost decimal(18,6) NOT NULL,
        CONSTRAINT PK_MeasuredHistoricalDiscipline PRIMARY KEY(ConfirmationId,Discipline),
        CONSTRAINT CK_MeasuredHistoricalDiscipline_Amounts CHECK(ConfirmedHours>=0 AND ConfirmedCost>=0)
    );
END;
IF OBJECT_ID(N'costs.PackageMeasuredHistoricalHistory',N'U') IS NULL
BEGIN
    CREATE TABLE costs.PackageMeasuredHistoricalHistory(
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MeasuredHistoricalHistory PRIMARY KEY,
        Entity varchar(30) NOT NULL, ChangedAtUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        ChangedBy nvarchar(255) NOT NULL, BeforeJson nvarchar(max) NULL, AfterJson nvarchar(max) NULL
    );
END;
COMMIT;
GO
CREATE OR ALTER TRIGGER costs.TR_MeasuredHistorical_Audit ON costs.PackageMeasuredHistorical
AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted h JOIN costs.Package p ON p.Id=h.PackageId
              WHERE p.Project_ID_ACC<>h.Project_ID_ACC OR p.ModeloCadastro<>'DISCIPLINA')
        THROW 51001,'Measurement identity does not match discipline package.',1;
    INSERT costs.PackageMeasuredHistoricalHistory(Entity,ChangedBy,BeforeJson,AfterJson)
    SELECT 'CONFIRMATION',COALESCE(CONVERT(nvarchar(255),SESSION_CONTEXT(N'MeasuredHistoricalAuthor')),SUSER_SNAME()),
           (SELECT * FROM deleted FOR JSON PATH),(SELECT * FROM inserted FOR JSON PATH);
END;
GO
CREATE OR ALTER TRIGGER costs.TR_MeasuredHistoricalDiscipline_Audit ON costs.PackageMeasuredHistoricalDiscipline
AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    INSERT costs.PackageMeasuredHistoricalHistory(Entity,ChangedBy,BeforeJson,AfterJson)
    SELECT 'DISCIPLINE',COALESCE(CONVERT(nvarchar(255),SESSION_CONTEXT(N'MeasuredHistoricalAuthor')),SUSER_SNAME()),
           (SELECT * FROM deleted FOR JSON PATH),(SELECT * FROM inserted FOR JSON PATH);
END;
GO
