SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF SCHEMA_ID(N'gestao') IS NULL
    EXEC(N'CREATE SCHEMA gestao');

IF OBJECT_ID(N'gestao.TimesheetSemDocumentoTratamento', N'U') IS NULL
BEGIN
    CREATE TABLE gestao.TimesheetSemDocumentoTratamento
    (
        Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TimesheetSemDocumentoTratamento PRIMARY KEY,
        Project_ID_ACC      nvarchar(100) NOT NULL,
        RowKey              char(64) NOT NULL,
        NomeProfissional    nvarchar(300) NULL,
        DataLancamento      date NULL,
        DescricaoSnapshot   nvarchar(2000) NULL,
        HHOriginal          decimal(18,6) NOT NULL,
        DisciplinaDestino   nvarchar(200) NULL,
        EhPacote            bit NOT NULL CONSTRAINT DF_TimesheetSemDocumentoTratamento_EhPacote DEFAULT(0),
        TipoTratamento      varchar(30) NOT NULL CONSTRAINT DF_TimesheetSemDocumentoTratamento_Tipo DEFAULT('PENDENTE'),
        CustoHoraAplicado   decimal(18,6) NULL,
        CustoCalculado      decimal(18,6) NULL,
        Ativo               bit NOT NULL CONSTRAINT DF_TimesheetSemDocumentoTratamento_Ativo DEFAULT(1),
        AlteradoPor         nvarchar(300) NULL,
        AlteradoEm          datetime2(7) NOT NULL CONSTRAINT DF_TimesheetSemDocumentoTratamento_AlteradoEm DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT CK_TimesheetSemDocumentoTratamento_Tipo CHECK (TipoTratamento IN ('PENDENTE','AT'))
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'gestao.TimesheetSemDocumentoTratamento')
      AND name = N'UX_TimesheetSemDocumentoTratamento_Project_RowKey'
)
BEGIN
    CREATE UNIQUE INDEX UX_TimesheetSemDocumentoTratamento_Project_RowKey
        ON gestao.TimesheetSemDocumentoTratamento(Project_ID_ACC, RowKey)
        WHERE Ativo = 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'gestao.TimesheetSemDocumentoTratamento')
      AND name = N'IX_TimesheetSemDocumentoTratamento_Project_Tipo'
)
BEGIN
    CREATE INDEX IX_TimesheetSemDocumentoTratamento_Project_Tipo
        ON gestao.TimesheetSemDocumentoTratamento(Project_ID_ACC, TipoTratamento, EhPacote)
        INCLUDE (DisciplinaDestino, HHOriginal, CustoCalculado, NomeProfissional, DataLancamento);
END;
