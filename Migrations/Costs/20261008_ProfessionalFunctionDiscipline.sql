SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'costs.ProjectProfessional', N'Funcao') IS NULL
    ALTER TABLE costs.ProjectProfessional ADD Funcao nvarchar(200) NULL;

IF COL_LENGTH(N'costs.ProjectProfessional', N'Disciplina') IS NULL
    ALTER TABLE costs.ProjectProfessional ADD Disciplina nvarchar(200) NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'costs.ProjectProfessional')
      AND name = N'IX_ProjectProfessional_Project_Ativo_Nome'
)
BEGIN
    CREATE INDEX IX_ProjectProfessional_Project_Ativo_Nome
        ON costs.ProjectProfessional(Project_ID_ACC, Ativo, Nome)
        INCLUDE (Funcao, Disciplina, HHBaseMensal, AliasTimesheet);
END;
