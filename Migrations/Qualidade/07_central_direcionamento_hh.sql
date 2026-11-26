/*
  Central de Direcionamento de HH — migration ADITIVA e idempotente.
  NÃO é executada automaticamente. Não altera Timesheet/LDP/HH/custo nem remove decisões.
*/
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'qualidade') IS NULL EXEC(N'CREATE SCHEMA qualidade');
GO

IF OBJECT_ID(N'gestao.TimesheetDocumentoDePara_Espelho', N'U') IS NOT NULL
   AND COL_LENGTH(N'gestao.TimesheetDocumentoDePara_Espelho', N'RowVersion') IS NULL
    ALTER TABLE gestao.TimesheetDocumentoDePara_Espelho ADD RowVersion rowversion NOT NULL;
GO

IF OBJECT_ID(N'gestao.AuditoriaRedistribuicaoDocumento', N'U') IS NOT NULL
   AND COL_LENGTH(N'gestao.AuditoriaRedistribuicaoDocumento', N'RowVersion') IS NULL
    ALTER TABLE gestao.AuditoriaRedistribuicaoDocumento ADD RowVersion rowversion NOT NULL;
GO

IF OBJECT_ID(N'qualidade.DirecionamentoHHAuditoria', N'U') IS NULL
BEGIN
    CREATE TABLE qualidade.DirecionamentoHHAuditoria
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DirecionamentoHHAuditoria PRIMARY KEY,
        Project_ID_ACC nvarchar(100) NOT NULL,
        Documento_Origem nvarchar(300) NOT NULL,
        Metodo nvarchar(30) NOT NULL,
        Operacao nvarchar(40) NOT NULL,
        AntesJson nvarchar(max) NOT NULL,
        DepoisJson nvarchar(max) NOT NULL,
        Motivo nvarchar(1000) NOT NULL,
        AlteradoPor nvarchar(255) NOT NULL,
        AlteradoEm datetime2(3) NOT NULL CONSTRAINT DF_DirecionamentoHHAuditoria_AlteradoEm DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_DirecionamentoHHAuditoria_Metodo CHECK (Metodo IN (N'DE_PARA',N'REDISTRIBUICAO')),
        CONSTRAINT CK_DirecionamentoHHAuditoria_Operacao CHECK (Operacao IN (N'EDIT_DE_PARA',N'RESET_DE_PARA',N'RESET_REDISTRIBUICAO')),
        CONSTRAINT CK_DirecionamentoHHAuditoria_AntesJson CHECK (ISJSON(AntesJson)=1),
        CONSTRAINT CK_DirecionamentoHHAuditoria_DepoisJson CHECK (ISJSON(DepoisJson)=1)
    );
    CREATE INDEX IX_DirecionamentoHHAuditoria_Origem
        ON qualidade.DirecionamentoHHAuditoria(Project_ID_ACC, Documento_Origem, AlteradoEm DESC);
END
GO
