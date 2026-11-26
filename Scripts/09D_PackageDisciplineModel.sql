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

/* 09D - Pacotes: modelo de cadastro Por Documento / Por Disciplina.
   SOMENTE ADITIVO e idempotente: nao altera nem converte pacotes existentes (todos ficam DOCUMENTO pelo DEFAULT),
   nao toca Timesheet, LDP ou tabelas financeiras. Pode ser executado mais de uma vez.
   - costs.Package.ModeloCadastro ............ DOCUMENTO (padrao, comportamento atual) | DISCIPLINA
   - costs.PackageDiscipline .................. totais por disciplina (HH/custo do pacote + snapshot da LDP + justificativa)
   - costs.PackageDisciplineDocument .......... documentos escolhidos quando Abrangencia = SELECIONADOS
   - costs.PackageDisciplineAllocationHistory . fotografia do rateio igualitario a cada gravacao (historico preservado) */

IF OBJECT_ID(N'costs.Package','U') IS NULL
BEGIN
    RAISERROR('costs.Package nao existe. Execute a 09B antes da 09D.',16,1);
    RETURN;
END;
GO

IF COL_LENGTH(N'costs.Package',N'ModeloCadastro') IS NULL
    ALTER TABLE costs.Package ADD ModeloCadastro varchar(12) NOT NULL
        CONSTRAINT DF_costs_Package_ModeloCadastro DEFAULT('DOCUMENTO') WITH VALUES;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_costs_Package_ModeloCadastro')
    EXEC(N'ALTER TABLE costs.Package ADD CONSTRAINT CK_costs_Package_ModeloCadastro CHECK (ModeloCadastro IN (''DOCUMENTO'',''DISCIPLINA''));');
GO

IF OBJECT_ID(N'costs.PackageDiscipline','U') IS NULL
BEGIN
    CREATE TABLE costs.PackageDiscipline(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_PackageDiscipline PRIMARY KEY,
        PackageId int NOT NULL CONSTRAINT FK_costs_PackageDiscipline_Package REFERENCES costs.Package(Id),
        Disciplina nvarchar(150) NOT NULL,
        Abrangencia varchar(12) NOT NULL CONSTRAINT CK_costs_PackageDiscipline_Abrangencia CHECK (Abrangencia IN ('TODOS','SELECIONADOS')),
        HHPrevisto decimal(18,6) NOT NULL CONSTRAINT CK_costs_PackageDiscipline_HH CHECK (HHPrevisto >= 0),
        CustoPrevisto decimal(18,6) NOT NULL CONSTRAINT CK_costs_PackageDiscipline_Custo CHECK (CustoPrevisto >= 0),
        HHPrevistoLdp decimal(18,6) NULL,
        CustoPrevistoLdp decimal(18,6) NULL,
        DocumentosLdp int NULL,
        Justificativa nvarchar(1000) NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_PackageDiscipline_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_PackageDiscipline_CriadoEm DEFAULT(SYSUTCDATETIME()),
        AtualizadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_PackageDiscipline_AtualizadoEm DEFAULT(SYSUTCDATETIME()),
        CriadoPor nvarchar(320) NULL
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_costs_PackageDiscipline_Ativa' AND object_id = OBJECT_ID(N'costs.PackageDiscipline'))
    CREATE UNIQUE INDEX UX_costs_PackageDiscipline_Ativa ON costs.PackageDiscipline(PackageId, Disciplina) WHERE Ativo = 1;
GO

IF OBJECT_ID(N'costs.PackageDisciplineDocument','U') IS NULL
BEGIN
    CREATE TABLE costs.PackageDisciplineDocument(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_PackageDisciplineDocument PRIMARY KEY,
        PackageDisciplineId int NOT NULL CONSTRAINT FK_costs_PackageDisciplineDocument_Discipline REFERENCES costs.PackageDiscipline(Id),
        DocumentoCodigo nvarchar(255) NOT NULL,
        Ativo bit NOT NULL CONSTRAINT DF_costs_PackageDisciplineDocument_Ativo DEFAULT(1),
        CriadoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_PackageDisciplineDocument_CriadoEm DEFAULT(SYSUTCDATETIME()),
        DesativadoEm datetime2(3) NULL
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_costs_PackageDisciplineDocument_Ativo' AND object_id = OBJECT_ID(N'costs.PackageDisciplineDocument'))
    CREATE UNIQUE INDEX UX_costs_PackageDisciplineDocument_Ativo ON costs.PackageDisciplineDocument(PackageDisciplineId, DocumentoCodigo) WHERE Ativo = 1;
GO

IF OBJECT_ID(N'costs.PackageDisciplineAllocationHistory','U') IS NULL
BEGIN
    CREATE TABLE costs.PackageDisciplineAllocationHistory(
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_costs_PackageDisciplineAllocationHistory PRIMARY KEY,
        PackageDisciplineId int NOT NULL CONSTRAINT FK_costs_PackageDisciplineAllocationHistory_Discipline REFERENCES costs.PackageDiscipline(Id),
        DocumentoCodigo nvarchar(255) NOT NULL,
        HHPrevisto decimal(18,6) NOT NULL,
        CustoPrevisto decimal(18,6) NOT NULL,
        DocumentosElegiveis int NOT NULL,
        Motivo varchar(30) NOT NULL,
        RegistradoEm datetime2(3) NOT NULL CONSTRAINT DF_costs_PackageDisciplineAllocationHistory_Em DEFAULT(SYSUTCDATETIME()),
        RegistradoPor nvarchar(320) NULL
    );
    CREATE INDEX IX_costs_PackageDisciplineAllocationHistory_Discipline ON costs.PackageDisciplineAllocationHistory(PackageDisciplineId, RegistradoEm);
END;
GO

/* Conferencia (somente leitura) */
EXEC(N'SELECT ModeloCadastro, COUNT(*) AS Pacotes FROM costs.Package GROUP BY ModeloCadastro;');
SELECT OBJECT_ID(N'costs.PackageDiscipline','U') AS PackageDiscipline,
       OBJECT_ID(N'costs.PackageDisciplineDocument','U') AS PackageDisciplineDocument,
       OBJECT_ID(N'costs.PackageDisciplineAllocationHistory','U') AS AllocationHistory;
GO
