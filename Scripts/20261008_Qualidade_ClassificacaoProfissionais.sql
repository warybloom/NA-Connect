USE [DB_NA_CONNECT];
GO
-- Migração idempotente: pessoas fora dos indicadores de engenharia por projeto.
-- O Timesheet bruto e a LDP NÃO são alterados.
IF OBJECT_ID(N'qualidade.ProfissionalEscopoEngenharia', N'U') IS NULL
BEGIN
    CREATE TABLE qualidade.ProfissionalEscopoEngenharia
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProfissionalEscopoEngenharia PRIMARY KEY,
        Project_ID_ACC NVARCHAR(255) NOT NULL,
        NomeNormalizado NVARCHAR(250) NOT NULL,
        NomeExibicao NVARCHAR(250) NOT NULL,
        ExcluirEngenharia BIT NOT NULL CONSTRAINT DF_ProfEscopo_Excl DEFAULT (0),
        AtualizadoPor NVARCHAR(300) NULL,
        AtualizadoEm DATETIME2 NOT NULL CONSTRAINT DF_ProfEscopo_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_ProfEscopo_ProjectName UNIQUE (Project_ID_ACC, NomeNormalizado),
        CONSTRAINT FK_ProfEscopo_Project FOREIGN KEY (Project_ID_ACC) REFERENCES dbo.Projetos(Project_ID_ACC)
    );
END;
GO