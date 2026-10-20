USE [DB_NA_CONNECT];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

/* 09C incremental migration. Review counts/back up before manual execution.
   No DROP and no edits to legacy finance/Timesheet/LDP source objects. */
IF OBJECT_ID(N'costs.ProjectProfessional','U') IS NULL OR OBJECT_ID(N'costs.ProfessionalCostItem','U') IS NULL
    THROW 51000, 'Apply/verify the existing 09B costs schema before 09C.', 1;
GO

IF OBJECT_ID(N'costs.ProjectProfessionalCostPeriod','U') IS NULL
BEGIN
    CREATE TABLE costs.ProjectProfessionalCostPeriod(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_ProjectProfessionalCostPeriod PRIMARY KEY,
        ProfessionalId int NOT NULL,
        VigenciaInicio date NOT NULL,
        VigenciaFim date NULL,
        HHBaseMensal decimal(18,6) NOT NULL,
        RemuneracaoBase decimal(18,6) NOT NULL CONSTRAINT DF_costs_ProfessionalPeriod_Remuneracao DEFAULT(0),
        CustoHoraManual decimal(18,6) NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_ProfessionalPeriod_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_ProfessionalPeriod_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_ProfessionalPeriod_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT CK_costs_ProfessionalPeriod_Dates CHECK(VigenciaFim IS NULL OR VigenciaFim>=VigenciaInicio),
        CONSTRAINT CK_costs_ProfessionalPeriod_HH CHECK(HHBaseMensal>0),
        CONSTRAINT CK_costs_ProfessionalPeriod_Remuneracao CHECK(RemuneracaoBase>=0),
        CONSTRAINT CK_costs_ProfessionalPeriod_ManualRate CHECK(CustoHoraManual IS NULL OR CustoHoraManual>=0),
        CONSTRAINT FK_costs_ProfessionalPeriod_Professional FOREIGN KEY(ProfessionalId) REFERENCES costs.ProjectProfessional(Id),
        CONSTRAINT UQ_costs_ProfessionalPeriod_IdOwner UNIQUE(Id,ProfessionalId)
    );
END;
GO

IF COL_LENGTH(N'costs.ProfessionalCostItem',N'PeriodId') IS NULL
    ALTER TABLE costs.ProfessionalCostItem ADD PeriodId int NULL;
IF COL_LENGTH(N'costs.ProfessionalCostItem',N'TipoCalculo') IS NULL
    ALTER TABLE costs.ProfessionalCostItem ADD TipoCalculo varchar(12) NOT NULL CONSTRAINT DF_costs_Item_TipoCalculo DEFAULT('FIXO') WITH VALUES;
IF COL_LENGTH(N'costs.ProfessionalCostItem',N'Percentual') IS NULL
    ALTER TABLE costs.ProfessionalCostItem ADD Percentual decimal(18,6) NULL;
IF COL_LENGTH(N'costs.ProfessionalCostItem',N'BaseCalculo') IS NULL
    ALTER TABLE costs.ProfessionalCostItem ADD BaseCalculo varchar(24) NOT NULL CONSTRAINT DF_costs_Item_BaseCalculo DEFAULT('REMUNERACAO') WITH VALUES;
IF COL_LENGTH(N'costs.ProjectProfessional',N'DesativadoEm') IS NULL
    ALTER TABLE costs.ProjectProfessional ADD DesativadoEm datetime2(3) NULL;
GO

UPDATE costs.ProjectProfessional SET DesativadoEm=AtualizadoEm WHERE Ativo=0 AND DesativadoEm IS NULL;
GO

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_costs_Item_PeriodOwner' AND parent_object_id=OBJECT_ID(N'costs.ProfessionalCostItem'))
    ALTER TABLE costs.ProfessionalCostItem WITH CHECK ADD CONSTRAINT FK_costs_Item_PeriodOwner
        FOREIGN KEY(PeriodId,ProfessionalId) REFERENCES costs.ProjectProfessionalCostPeriod(Id,ProfessionalId);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_costs_Item_Calculation' AND parent_object_id=OBJECT_ID(N'costs.ProfessionalCostItem'))
    ALTER TABLE costs.ProfessionalCostItem WITH CHECK ADD CONSTRAINT CK_costs_Item_Calculation CHECK(
       (TipoCalculo='FIXO' AND Percentual IS NULL) OR
       (TipoCalculo='PERCENTUAL' AND Percentual IS NOT NULL AND Percentual>=0));
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_costs_Item_Base' AND parent_object_id=OBJECT_ID(N'costs.ProfessionalCostItem'))
    ALTER TABLE costs.ProfessionalCostItem WITH CHECK ADD CONSTRAINT CK_costs_Item_Base CHECK(BaseCalculo IN('REMUNERACAO','VALOR_FIXO'));
GO

/* Only legacy composition requires a migration period. A ProjectProfessional with no
   ProfessionalCostItem stays without a period, so the planner can enter the real
   historical start date later. The true historical date cannot be inferred from CriadoEm. */
INSERT costs.ProjectProfessionalCostPeriod(ProfessionalId,VigenciaInicio,VigenciaFim,HHBaseMensal,RemuneracaoBase,CustoHoraManual,Ativo)
SELECT p.Id,CONVERT(date,COALESCE(p.CriadoEm,SYSUTCDATETIME())),NULL,p.HHBaseMensal,0,NULL,1
FROM costs.ProjectProfessional p
WHERE NOT EXISTS(SELECT 1 FROM costs.ProjectProfessionalCostPeriod h WHERE h.ProfessionalId=p.Id)
  AND EXISTS(SELECT 1 FROM costs.ProfessionalCostItem i WHERE i.ProfessionalId=p.Id AND i.PeriodId IS NULL);
GO

/* Attach only migrated legacy items to their opening period. No amount/category is rewritten. */
UPDATE i SET PeriodId=h.Id
FROM costs.ProfessionalCostItem i
CROSS APPLY(
    SELECT TOP(1) p.Id
    FROM costs.ProjectProfessionalCostPeriod p
    WHERE p.ProfessionalId=i.ProfessionalId
    ORDER BY CASE WHEN CAST(i.CriadoEm AS date)>=p.VigenciaInicio AND (p.VigenciaFim IS NULL OR CAST(i.CriadoEm AS date)<=p.VigenciaFim) THEN 0 ELSE 1 END,
             CASE WHEN p.VigenciaInicio<=CAST(i.CriadoEm AS date) THEN 0 ELSE 1 END,
             p.VigenciaInicio DESC,p.Id DESC
) h
WHERE i.PeriodId IS NULL
  AND EXISTS(SELECT 1 FROM costs.ProjectProfessionalCostPeriod p WHERE p.ProfessionalId=i.ProfessionalId);
GO

IF EXISTS(
    SELECT 1 FROM costs.ProjectProfessionalCostPeriod a
    JOIN costs.ProjectProfessionalCostPeriod b ON b.ProfessionalId=a.ProfessionalId AND b.Id>a.Id
      AND a.VigenciaInicio<=ISNULL(b.VigenciaFim,CONVERT(date,'99991231'))
      AND b.VigenciaInicio<=ISNULL(a.VigenciaFim,CONVERT(date,'99991231'))
) THROW 51004, 'Existem vigencias profissionais sobrepostas; saneie antes de continuar a migration 09C.', 1;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProjectProfessionalCostPeriod') AND name=N'IX_costs_ProfessionalPeriod_Date')
    CREATE INDEX IX_costs_ProfessionalPeriod_Date ON costs.ProjectProfessionalCostPeriod(ProfessionalId,Ativo,VigenciaInicio,VigenciaFim) INCLUDE(HHBaseMensal,RemuneracaoBase,CustoHoraManual);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProjectProfessionalCostPeriod') AND name=N'UX_costs_ProfessionalPeriod_Open')
    CREATE UNIQUE INDEX UX_costs_ProfessionalPeriod_Open ON costs.ProjectProfessionalCostPeriod(ProfessionalId) WHERE Ativo=1 AND VigenciaFim IS NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProfessionalCostItem') AND name=N'IX_costs_Item_Period')
    CREATE INDEX IX_costs_Item_Period ON costs.ProfessionalCostItem(PeriodId,Ativo) INCLUDE(Categoria,TipoCalculo,Percentual,BaseCalculo,Valor);
GO

CREATE OR ALTER TRIGGER costs.TR_ProjectProfessionalCostPeriod_NoOverlap
ON costs.ProjectProfessionalCostPeriod AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1 FROM inserted i
        JOIN costs.ProjectProfessionalCostPeriod p WITH(UPDLOCK,HOLDLOCK)
          ON p.ProfessionalId=i.ProfessionalId AND p.Id<>i.Id
         AND i.VigenciaInicio<=ISNULL(p.VigenciaFim,CONVERT(date,'99991231'))
         AND p.VigenciaInicio<=ISNULL(i.VigenciaFim,CONVERT(date,'99991231'))
    ) THROW 51001, 'Periodos de custo profissional nao podem se sobrepor.', 1;
END;
GO

IF OBJECT_ID(N'costs.ProjectTax','U') IS NULL
BEGIN
    CREATE TABLE costs.ProjectTax(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_ProjectTax PRIMARY KEY,
        Project_ID_ACC nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        Nome nvarchar(200) NOT NULL,
        Tipo nvarchar(60) NOT NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_ProjectTax_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_ProjectTax_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_ProjectTax_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_ProjectTax_Project FOREIGN KEY(Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC)
    );
END;
GO

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_costs_ProjectTax_Project' AND parent_object_id=OBJECT_ID(N'costs.ProjectTax'))
    ALTER TABLE costs.ProjectTax WITH CHECK ADD CONSTRAINT FK_costs_ProjectTax_Project FOREIGN KEY(Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC);
GO

IF OBJECT_ID(N'costs.ProjectTaxPeriod','U') IS NULL
BEGIN
    CREATE TABLE costs.ProjectTaxPeriod(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_ProjectTaxPeriod PRIMARY KEY,
        ProjectTaxId int NOT NULL,
        VigenciaInicio date NOT NULL,
        VigenciaFim date NULL,
        TipoCalculo varchar(12) NOT NULL,
        Percentual decimal(18,6) NULL,
        ValorFixo decimal(18,6) NULL,
        BaseCalculo varchar(24) NOT NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_ProjectTaxPeriod_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_ProjectTaxPeriod_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_ProjectTaxPeriod_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT CK_costs_ProjectTaxPeriod_Dates CHECK(VigenciaFim IS NULL OR VigenciaFim>=VigenciaInicio),
        CONSTRAINT CK_costs_ProjectTaxPeriod_Calc CHECK((TipoCalculo='PERCENTUAL' AND Percentual IS NOT NULL AND Percentual>=0 AND ValorFixo IS NULL AND BaseCalculo IN('RECEITA','CUSTO','OUTRO')) OR (TipoCalculo='FIXO' AND ValorFixo IS NOT NULL AND ValorFixo>=0 AND Percentual IS NULL AND BaseCalculo='VALOR_FIXO')),
        CONSTRAINT CK_costs_ProjectTaxPeriod_Base CHECK(BaseCalculo IN('RECEITA','CUSTO','VALOR_FIXO','OUTRO')),
        CONSTRAINT FK_costs_ProjectTaxPeriod_Tax FOREIGN KEY(ProjectTaxId) REFERENCES costs.ProjectTax(Id)
    );
END;
GO

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_costs_ProjectTaxPeriod_Tax' AND parent_object_id=OBJECT_ID(N'costs.ProjectTaxPeriod'))
    ALTER TABLE costs.ProjectTaxPeriod WITH CHECK ADD CONSTRAINT FK_costs_ProjectTaxPeriod_Tax FOREIGN KEY(ProjectTaxId) REFERENCES costs.ProjectTax(Id);
GO

/* Ownership keys let SQL Server reject cross-project/cross-package recognition references. */
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProjectProfessional') AND name=N'UX_costs_Professional_IdProject')
    CREATE UNIQUE INDEX UX_costs_Professional_IdProject ON costs.ProjectProfessional(Id,Project_ID_ACC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.Package') AND name=N'UX_costs_Package_IdProject')
    CREATE UNIQUE INDEX UX_costs_Package_IdProject ON costs.Package(Id,Project_ID_ACC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.PackageDocument') AND name=N'UX_costs_PackageDocument_IdPackage')
    CREATE UNIQUE INDEX UX_costs_PackageDocument_IdPackage ON costs.PackageDocument(Id,PackageId);
GO

IF EXISTS(
    SELECT 1 FROM costs.TimesheetRecognition r
    LEFT JOIN costs.Package p ON p.Id=r.PackageId AND p.Project_ID_ACC=r.Project_ID_ACC
    LEFT JOIN costs.ProjectProfessional pr ON pr.Id=r.ProfessionalId AND pr.Project_ID_ACC=r.Project_ID_ACC
    LEFT JOIN costs.PackageDocument d ON d.Id=r.PackageDocumentId AND d.PackageId=r.PackageId
    WHERE (r.PackageId IS NOT NULL AND p.Id IS NULL)
       OR (r.ProfessionalId IS NOT NULL AND pr.Id IS NULL)
       OR (r.PackageDocumentId IS NOT NULL AND d.Id IS NULL)
)
    THROW 51003, 'TimesheetRecognition possui ownership inconsistente; corrija os dados antes da migration 09C.', 1;
GO

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_costs_Recognition_PackageProject' AND parent_object_id=OBJECT_ID(N'costs.TimesheetRecognition'))
    ALTER TABLE costs.TimesheetRecognition WITH CHECK ADD CONSTRAINT FK_costs_Recognition_PackageProject
        FOREIGN KEY(PackageId,Project_ID_ACC) REFERENCES costs.Package(Id,Project_ID_ACC);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_costs_Recognition_ProfessionalProject' AND parent_object_id=OBJECT_ID(N'costs.TimesheetRecognition'))
    ALTER TABLE costs.TimesheetRecognition WITH CHECK ADD CONSTRAINT FK_costs_Recognition_ProfessionalProject
        FOREIGN KEY(ProfessionalId,Project_ID_ACC) REFERENCES costs.ProjectProfessional(Id,Project_ID_ACC);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_costs_Recognition_DocumentPackage' AND parent_object_id=OBJECT_ID(N'costs.TimesheetRecognition'))
    ALTER TABLE costs.TimesheetRecognition WITH CHECK ADD CONSTRAINT FK_costs_Recognition_DocumentPackage
        FOREIGN KEY(PackageDocumentId,PackageId) REFERENCES costs.PackageDocument(Id,PackageId);
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProjectTax') AND name=N'IX_costs_ProjectTax_Project')
    CREATE INDEX IX_costs_ProjectTax_Project ON costs.ProjectTax(Project_ID_ACC,Ativo,Nome);
IF EXISTS(
    SELECT 1 FROM costs.ProjectTaxPeriod a
    JOIN costs.ProjectTaxPeriod b ON b.ProjectTaxId=a.ProjectTaxId AND b.Id>a.Id
      AND a.VigenciaInicio<=ISNULL(b.VigenciaFim,CONVERT(date,'99991231'))
      AND b.VigenciaInicio<=ISNULL(a.VigenciaFim,CONVERT(date,'99991231'))
) THROW 51005, 'Existem vigencias de impostos sobrepostas; saneie antes de continuar a migration 09C.', 1;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProjectTaxPeriod') AND name=N'IX_costs_ProjectTaxPeriod_Date')
    CREATE INDEX IX_costs_ProjectTaxPeriod_Date ON costs.ProjectTaxPeriod(ProjectTaxId,Ativo,VigenciaInicio,VigenciaFim) INCLUDE(TipoCalculo,Percentual,ValorFixo,BaseCalculo);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'costs.ProjectTaxPeriod') AND name=N'UX_costs_ProjectTaxPeriod_Open')
    CREATE UNIQUE INDEX UX_costs_ProjectTaxPeriod_Open ON costs.ProjectTaxPeriod(ProjectTaxId) WHERE Ativo=1 AND VigenciaFim IS NULL;
GO

CREATE OR ALTER TRIGGER costs.TR_ProjectTaxPeriod_NoOverlap
ON costs.ProjectTaxPeriod AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1 FROM inserted i
        JOIN costs.ProjectTaxPeriod p WITH(UPDLOCK,HOLDLOCK)
          ON p.ProjectTaxId=i.ProjectTaxId AND p.Id<>i.Id
         AND i.VigenciaInicio<=ISNULL(p.VigenciaFim,CONVERT(date,'99991231'))
         AND p.VigenciaInicio<=ISNULL(i.VigenciaFim,CONVERT(date,'99991231'))
    ) THROW 51002, 'Vigencias de imposto do projeto nao podem se sobrepor.', 1;
END;
GO

/* Match aliases must belong to exactly one cost owner. */
IF EXISTS(SELECT 1 FROM costs.TimesheetMatchAlias WHERE (ProfessionalId IS NULL AND PackageId IS NULL) OR (ProfessionalId IS NOT NULL AND PackageId IS NOT NULL))
    THROW 51006, 'TimesheetMatchAlias possui ownership ausente/duplicado; saneie antes de adicionar a constraint 09C.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'costs.TimesheetMatchAlias') AND name IN(N'CK_costs_Alias_Owner',N'CK_costs_TimesheetMatchAlias_ExactlyOneOwner'))
    ALTER TABLE costs.TimesheetMatchAlias WITH CHECK ADD CONSTRAINT CK_costs_TimesheetMatchAlias_ExactlyOneOwner CHECK((ProfessionalId IS NOT NULL AND PackageId IS NULL) OR (ProfessionalId IS NULL AND PackageId IS NOT NULL));
GO
