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
BEGIN TRANSACTION;
GO

/* 09B - Custos do Projeto.
   Corrigido para dbo.Projetos.Project_ID_ACC = nvarchar(255).
   Script idempotente; executar em janela controlada. */

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.Projetos')
      AND i.is_unique = 1
      AND NOT EXISTS (
          SELECT 1
          FROM sys.index_columns ic
          JOIN sys.columns c
            ON c.object_id = ic.object_id
           AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND c.name <> N'Project_ID_ACC'
      )
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns ic
          JOIN sys.columns c
            ON c.object_id = ic.object_id
           AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND c.name = N'Project_ID_ACC'
      )
)
BEGIN
    RAISERROR('dbo.Projetos.Project_ID_ACC precisa de indice UNIQUE antes da 09B.',16,1);
    RETURN;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'costs')
    EXEC(N'CREATE SCHEMA costs AUTHORIZATION dbo');
GO

IF OBJECT_ID(N'costs.ProjectProfessional','U') IS NULL
BEGIN
    CREATE TABLE costs.ProjectProfessional(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_ProjectProfessional PRIMARY KEY,
        Project_ID_ACC nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        Nome nvarchar(200) NOT NULL,
        Empresa nvarchar(200) NULL,
        AliasTimesheet nvarchar(200) NULL,
        HHBaseMensal decimal(18,6) NOT NULL CONSTRAINT CK_costs_Professional_HH CHECK(HHBaseMensal > 0),
        Ativo bit NOT NULL CONSTRAINT DF_costs_Professional_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Professional_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Professional_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_Professional_Project
            FOREIGN KEY(Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC)
    );
END;
GO

IF OBJECT_ID(N'costs.ProfessionalCostItem','U') IS NULL
BEGIN
    CREATE TABLE costs.ProfessionalCostItem(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_ProfessionalCostItem PRIMARY KEY,
        ProfessionalId int NOT NULL,
        Categoria varchar(30) NOT NULL,
        Descricao nvarchar(300) NULL,
        Valor decimal(18,6) NOT NULL CONSTRAINT CK_costs_Item_Valor CHECK(Valor >= 0),
        Ativo bit NOT NULL CONSTRAINT DF_costs_Item_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Item_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Item_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_Item_Professional
            FOREIGN KEY(ProfessionalId) REFERENCES costs.ProjectProfessional(Id)
    );
END;
GO

IF OBJECT_ID(N'costs.ProjectExpense','U') IS NULL
BEGIN
    CREATE TABLE costs.ProjectExpense(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_ProjectExpense PRIMARY KEY,
        Project_ID_ACC nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        Categoria varchar(40) NOT NULL,
        Descricao nvarchar(300) NOT NULL,
        Valor decimal(18,6) NOT NULL CONSTRAINT CK_costs_Expense_Valor CHECK(Valor >= 0),
        Competencia date NOT NULL,
        Tipo varchar(20) NOT NULL CONSTRAINT CK_costs_Expense_Tipo CHECK(Tipo IN('UNICA','RECORRENTE')),
        Observacao nvarchar(1000) NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_Expense_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Expense_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Expense_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_Expense_Project
            FOREIGN KEY(Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC)
    );
END;
GO

IF OBJECT_ID(N'costs.Package','U') IS NULL
BEGIN
    CREATE TABLE costs.Package(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_Package PRIMARY KEY,
        Project_ID_ACC nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        Empresa nvarchar(200) NULL,
        NomeReferencia nvarchar(200) NOT NULL,
        AliasTimesheet nvarchar(200) NULL,
        Descricao nvarchar(500) NULL,
        HHBase decimal(18,6) NOT NULL CONSTRAINT CK_costs_Package_HH CHECK(HHBase >= 0),
        CustoBase decimal(18,6) NOT NULL CONSTRAINT CK_costs_Package_Custo CHECK(CustoBase >= 0),
        Ativo bit NOT NULL CONSTRAINT DF_costs_Package_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Package_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Package_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_Package_Project
            FOREIGN KEY(Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC)
    );
END;
GO

IF OBJECT_ID(N'costs.PackageDocument','U') IS NULL
BEGIN
    CREATE TABLE costs.PackageDocument(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_PackageDocument PRIMARY KEY,
        PackageId int NOT NULL,
        DocumentoCodigo nvarchar(300) NOT NULL,
        Disciplina nvarchar(150) NULL,
        HHAlocada decimal(18,6) NOT NULL CONSTRAINT DF_costs_PackageDocument_HH DEFAULT(0)
            CONSTRAINT CK_costs_PackageDocument_HH CHECK(HHAlocada >= 0),
        CustoAlocado decimal(18,6) NOT NULL CONSTRAINT DF_costs_PackageDocument_Custo DEFAULT(0)
            CONSTRAINT CK_costs_PackageDocument_Custo CHECK(CustoAlocado >= 0),
        ModoHH varchar(10) NOT NULL CONSTRAINT CK_costs_PackageDocument_ModoHH CHECK(ModoHH IN('AUTO','MANUAL')),
        ModoCusto varchar(10) NOT NULL CONSTRAINT CK_costs_PackageDocument_ModoCusto CHECK(ModoCusto IN('AUTO','MANUAL')),
        Ativo bit NOT NULL CONSTRAINT DF_costs_PackageDocument_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_PackageDocument_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_PackageDocument_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_PackageDocument_Package
            FOREIGN KEY(PackageId) REFERENCES costs.Package(Id)
    );
END;
GO

IF OBJECT_ID(N'costs.PackageAddendum','U') IS NULL
BEGIN
    CREATE TABLE costs.PackageAddendum(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_PackageAddendum PRIMARY KEY,
        PackageId int NOT NULL,
        HHAdicional decimal(18,6) NOT NULL CONSTRAINT CK_costs_Addendum_HH CHECK(HHAdicional >= 0),
        CustoAdicional decimal(18,6) NOT NULL CONSTRAINT CK_costs_Addendum_Custo CHECK(CustoAdicional >= 0),
        Data date NOT NULL,
        Descricao nvarchar(500) NOT NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_Addendum_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Addendum_Criado DEFAULT(sysutcdatetime()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Addendum_Atualizado DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_costs_Addendum_Package
            FOREIGN KEY(PackageId) REFERENCES costs.Package(Id)
    );
END;
GO

IF OBJECT_ID(N'costs.TimesheetMatchAlias','U') IS NULL
BEGIN
    CREATE TABLE costs.TimesheetMatchAlias(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_TimesheetMatchAlias PRIMARY KEY,
        ProfessionalId int NULL,
        PackageId int NULL,
        TextoMatch nvarchar(200) NOT NULL,
        TipoMatch varchar(10) NOT NULL CONSTRAINT CK_costs_Alias_Tipo CHECK(TipoMatch IN('EXATO','CONTEM')),
        Ativo bit NOT NULL CONSTRAINT DF_costs_Alias_Ativo DEFAULT(1),
        CONSTRAINT CK_costs_Alias_Owner CHECK(
            (ProfessionalId IS NOT NULL AND PackageId IS NULL)
            OR
            (ProfessionalId IS NULL AND PackageId IS NOT NULL)
        ),
        CONSTRAINT FK_costs_Alias_Professional
            FOREIGN KEY(ProfessionalId) REFERENCES costs.ProjectProfessional(Id),
        CONSTRAINT FK_costs_Alias_Package
            FOREIGN KEY(PackageId) REFERENCES costs.Package(Id)
    );
END;
GO

IF OBJECT_ID(N'costs.TimesheetRecognition','U') IS NULL
BEGIN
    CREATE TABLE costs.TimesheetRecognition(
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_TimesheetRecognition PRIMARY KEY,
        Project_ID_ACC nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        PackageId int NULL,
        ProfessionalId int NULL,
        PackageDocumentId int NULL,
        DocumentoCodigo nvarchar(300) NULL,
        HHTimesheet decimal(18,6) NOT NULL,
        HHReconhecida decimal(18,6) NOT NULL,
        HHExcedente decimal(18,6) NOT NULL,
        CustoTeorico decimal(18,6) NOT NULL,
        CustoReconhecido decimal(18,6) NOT NULL,
        CustoExcedente decimal(18,6) NOT NULL,
        CalculadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_Recognition_Calculado DEFAULT(sysutcdatetime()),
        CONSTRAINT CK_costs_Recognition_NonNegative CHECK(
            HHTimesheet >= 0 AND HHReconhecida >= 0 AND HHExcedente >= 0
            AND CustoTeorico >= 0 AND CustoReconhecido >= 0 AND CustoExcedente >= 0
        ),
        CONSTRAINT CK_costs_Recognition_Owner CHECK(
            (PackageId IS NOT NULL AND ProfessionalId IS NULL)
            OR
            (PackageId IS NULL AND ProfessionalId IS NOT NULL)
        ),
        CONSTRAINT CK_costs_Recognition_PackageDocument CHECK(
            PackageDocumentId IS NULL OR PackageId IS NOT NULL
        ),
        CONSTRAINT FK_costs_Recognition_Project
            FOREIGN KEY(Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC),
        CONSTRAINT FK_costs_Recognition_Package
            FOREIGN KEY(PackageId) REFERENCES costs.Package(Id),
        CONSTRAINT FK_costs_Recognition_Professional
            FOREIGN KEY(ProfessionalId) REFERENCES costs.ProjectProfessional(Id),
        CONSTRAINT FK_costs_Recognition_Document
            FOREIGN KEY(PackageDocumentId) REFERENCES costs.PackageDocument(Id)
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Professional_Project'
      AND object_id = OBJECT_ID(N'costs.ProjectProfessional')
)
    CREATE INDEX IX_costs_Professional_Project
        ON costs.ProjectProfessional(Project_ID_ACC,Ativo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Item_Professional'
      AND object_id = OBJECT_ID(N'costs.ProfessionalCostItem')
)
    CREATE INDEX IX_costs_Item_Professional
        ON costs.ProfessionalCostItem(ProfessionalId,Ativo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Expense_Project'
      AND object_id = OBJECT_ID(N'costs.ProjectExpense')
)
    CREATE INDEX IX_costs_Expense_Project
        ON costs.ProjectExpense(Project_ID_ACC,Ativo,Competencia);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Package_Project'
      AND object_id = OBJECT_ID(N'costs.Package')
)
    CREATE INDEX IX_costs_Package_Project
        ON costs.Package(Project_ID_ACC,Ativo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Document_Package'
      AND object_id = OBJECT_ID(N'costs.PackageDocument')
)
    CREATE INDEX IX_costs_Document_Package
        ON costs.PackageDocument(PackageId,Ativo,DocumentoCodigo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Addendum_Package'
      AND object_id = OBJECT_ID(N'costs.PackageAddendum')
)
    CREATE INDEX IX_costs_Addendum_Package
        ON costs.PackageAddendum(PackageId,Ativo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Alias_Package'
      AND object_id = OBJECT_ID(N'costs.TimesheetMatchAlias')
)
    CREATE INDEX IX_costs_Alias_Package
        ON costs.TimesheetMatchAlias(PackageId,Ativo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Alias_Professional'
      AND object_id = OBJECT_ID(N'costs.TimesheetMatchAlias')
)
    CREATE INDEX IX_costs_Alias_Professional
        ON costs.TimesheetMatchAlias(ProfessionalId,Ativo);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_costs_Recognition_Project'
      AND object_id = OBJECT_ID(N'costs.TimesheetRecognition')
)
    CREATE INDEX IX_costs_Recognition_Project
        ON costs.TimesheetRecognition(Project_ID_ACC,CalculadoEm);
GO

IF OBJECT_ID(N'costs.PackageDocument','U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_costs_PackageDocument_ActiveCode'
      AND object_id = OBJECT_ID(N'costs.PackageDocument')
)
    CREATE UNIQUE INDEX UX_costs_PackageDocument_ActiveCode
        ON costs.PackageDocument(PackageId,DocumentoCodigo)
        WHERE Ativo = 1;
GO


IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
GO
PRINT 'VALIDACAO_09B_OK_ROLLBACK';
GO

