SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

IF SCHEMA_ID(N'coordenacao') IS NULL
    EXEC(N'CREATE SCHEMA coordenacao');
GO

IF OBJECT_ID(N'coordenacao.AtividadeDisciplinaProjeto', N'U') IS NULL
BEGIN
    CREATE TABLE coordenacao.AtividadeDisciplinaProjeto
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AtividadeDisciplinaProjeto PRIMARY KEY,
        Project_ID_ACC nvarchar(100) NOT NULL,
        Atividade nvarchar(255) NOT NULL,
        Tipo nvarchar(10) NOT NULL
            CONSTRAINT DF_AtividadeDisciplinaProjeto_Tipo DEFAULT N'AT',
        Disciplina nvarchar(255) NOT NULL,
        Ativo bit NOT NULL
            CONSTRAINT DF_AtividadeDisciplinaProjeto_Ativo DEFAULT 1,
        AlteradoPor nvarchar(255) NOT NULL,
        AlteradoEm datetime2(3) NOT NULL
            CONSTRAINT DF_AtividadeDisciplinaProjeto_AlteradoEm DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_AtividadeDisciplinaProjeto_ProjetoAtividade
        ON coordenacao.AtividadeDisciplinaProjeto(Project_ID_ACC, Atividade);
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'coordenacao.AtividadeDisciplinaProjeto')
      AND name = N'IX_AtividadeDisciplinaProjeto_Disciplina'
)
BEGIN
    CREATE INDEX IX_AtividadeDisciplinaProjeto_Disciplina
        ON coordenacao.AtividadeDisciplinaProjeto(Project_ID_ACC, Disciplina, Ativo);
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'coordenacao.AtividadeDisciplinaProjeto')
      AND name = N'CK_AtividadeDisciplinaProjeto_Tipo_AT'
)
BEGIN
    ALTER TABLE coordenacao.AtividadeDisciplinaProjeto WITH CHECK
        ADD CONSTRAINT CK_AtividadeDisciplinaProjeto_Tipo_AT CHECK (Tipo = N'AT');
END
GO
