SET XACT_ABORT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID(N'coordenacao.DisciplinaPadrao'))
BEGIN
    CREATE TABLE coordenacao.DisciplinaPadrao(
        DisciplinaPadraoId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DisciplinaPadrao PRIMARY KEY,
        Nome_Padrao NVARCHAR(200) NOT NULL,
        Nome_PT NVARCHAR(200) NULL,
        Nome_EN NVARCHAR(200) NULL,
        Ativa BIT NOT NULL CONSTRAINT DF_DisciplinaPadrao_Ativa DEFAULT(1),
        Data_Criacao DATETIME2(0) NOT NULL CONSTRAINT DF_DisciplinaPadrao_Criacao DEFAULT(SYSUTCDATETIME()),
        Data_Atualizacao DATETIME2(0) NOT NULL CONSTRAINT DF_DisciplinaPadrao_Atualizacao DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_DisciplinaPadrao_NomePadrao
      ON coordenacao.DisciplinaPadrao(Nome_Padrao);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID(N'coordenacao.DisciplinaAlias'))
BEGIN
    CREATE TABLE coordenacao.DisciplinaAlias(
        DisciplinaAliasId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DisciplinaAlias PRIMARY KEY,
        DisciplinaPadraoId INT NOT NULL,
        Alias NVARCHAR(200) NOT NULL,
        Data_Criacao DATETIME2(0) NOT NULL CONSTRAINT DF_DisciplinaAlias_Criacao DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_DisciplinaAlias_DisciplinaPadrao FOREIGN KEY(DisciplinaPadraoId)
            REFERENCES coordenacao.DisciplinaPadrao(DisciplinaPadraoId) ON DELETE CASCADE
    );
    CREATE INDEX IX_DisciplinaAlias_Alias ON coordenacao.DisciplinaAlias(Alias);
END;

;WITH Pares AS (
    SELECT
        Disciplina_EN_Padrao AS EN,
        MIN(Disciplina_PT_Padrao) AS PT
    FROM dbo.VW_Ldp_Unificada
    WHERE NULLIF(LTRIM(RTRIM(Disciplina_EN_Padrao)), '') IS NOT NULL
      AND UPPER(LTRIM(RTRIM(Disciplina_EN_Padrao))) NOT LIKE 'NOT MAPPED%'
    GROUP BY Disciplina_EN_Padrao
)
INSERT INTO coordenacao.DisciplinaPadrao(Nome_Padrao, Nome_PT, Nome_EN)
SELECT LTRIM(RTRIM(P.EN)), NULLIF(LTRIM(RTRIM(P.PT)), ''), LTRIM(RTRIM(P.EN))
FROM Pares P
WHERE NOT EXISTS (
    SELECT 1 FROM coordenacao.DisciplinaPadrao D
    WHERE UPPER(LTRIM(RTRIM(D.Nome_Padrao))) COLLATE Latin1_General_100_CI_AI =
          UPPER(LTRIM(RTRIM(P.EN))) COLLATE Latin1_General_100_CI_AI
);

-- Disciplinas existentes nas Normas de Coordenação que ainda não têm grupo conhecido.
INSERT INTO coordenacao.DisciplinaPadrao(Nome_Padrao, Nome_EN)
SELECT DISTINCT LTRIM(RTRIM(M.Disciplina)), LTRIM(RTRIM(M.Disciplina))
FROM coordenacao.NormaMapeamento M
WHERE NULLIF(LTRIM(RTRIM(M.Disciplina)), '') IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM coordenacao.DisciplinaPadrao D
      WHERE UPPER(LTRIM(RTRIM(D.Nome_Padrao))) COLLATE Latin1_General_100_CI_AI =
            UPPER(LTRIM(RTRIM(M.Disciplina))) COLLATE Latin1_General_100_CI_AI
  );

-- Alias = nome padrão/PT/EN.
INSERT INTO coordenacao.DisciplinaAlias(DisciplinaPadraoId, Alias)
SELECT D.DisciplinaPadraoId, X.Alias
FROM coordenacao.DisciplinaPadrao D
CROSS APPLY (VALUES (D.Nome_Padrao),(D.Nome_PT),(D.Nome_EN)) X(Alias)
WHERE NULLIF(LTRIM(RTRIM(X.Alias)), '') IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM coordenacao.DisciplinaAlias A
      WHERE UPPER(LTRIM(RTRIM(A.Alias))) COLLATE Latin1_General_100_CI_AI =
            UPPER(LTRIM(RTRIM(X.Alias))) COLLATE Latin1_General_100_CI_AI
  );

-- Aliases brutos observados na LDP. Só semeia automaticamente quando TODOS os registros desse
-- nome apontam para o mesmo padrão. Divergência entre projetos fica deliberadamente NÃO MAPEADA para decisão humana.
;WITH AliasCandidato AS (
    SELECT
        LTRIM(RTRIM(V.Discipline)) AS Alias,
        MIN(D.DisciplinaPadraoId) AS DisciplinaPadraoId,
        COUNT(DISTINCT D.DisciplinaPadraoId) AS QtdPadroes
    FROM dbo.VW_Ldp_Unificada V
    JOIN coordenacao.DisciplinaPadrao D
      ON UPPER(LTRIM(RTRIM(D.Nome_EN))) COLLATE Latin1_General_100_CI_AI =
         UPPER(LTRIM(RTRIM(V.Disciplina_EN_Padrao))) COLLATE Latin1_General_100_CI_AI
    WHERE NULLIF(LTRIM(RTRIM(V.Discipline)), '') IS NOT NULL
      AND UPPER(LTRIM(RTRIM(V.Disciplina_EN_Padrao))) NOT LIKE 'NOT MAPPED%'
    GROUP BY LTRIM(RTRIM(V.Discipline))
)
INSERT INTO coordenacao.DisciplinaAlias(DisciplinaPadraoId, Alias)
SELECT C.DisciplinaPadraoId, C.Alias
FROM AliasCandidato C
WHERE C.QtdPadroes = 1
  AND NOT EXISTS (
      SELECT 1 FROM coordenacao.DisciplinaAlias A
      WHERE UPPER(LTRIM(RTRIM(A.Alias))) COLLATE Latin1_General_100_CI_AI =
            UPPER(LTRIM(RTRIM(C.Alias))) COLLATE Latin1_General_100_CI_AI
  );

COMMIT;

-- 2026-10-06: alias é GLOBAL e determinístico.
-- Se o mesmo nome apareceu associado a mais de uma disciplina nos dados legados, não escolhemos silenciosamente:
-- removemos esse alias para que a tela de Padronização o marque como NÃO MAPEADO e o usuário decida uma única vez.
;WITH Ambiguos AS (
    SELECT UPPER(LTRIM(RTRIM(Alias))) COLLATE Latin1_General_100_CI_AI AS AliasKey
    FROM coordenacao.DisciplinaAlias
    GROUP BY UPPER(LTRIM(RTRIM(Alias))) COLLATE Latin1_General_100_CI_AI
    HAVING COUNT(DISTINCT DisciplinaPadraoId) > 1
)
DELETE A
FROM coordenacao.DisciplinaAlias A
JOIN Ambiguos X
  ON UPPER(LTRIM(RTRIM(A.Alias))) COLLATE Latin1_General_100_CI_AI = X.AliasKey;

;WITH DuplicadosMesmoPadrao AS (
    SELECT DisciplinaAliasId,
           ROW_NUMBER() OVER (
               PARTITION BY DisciplinaPadraoId,
                            UPPER(LTRIM(RTRIM(Alias))) COLLATE Latin1_General_100_CI_AI
               ORDER BY DisciplinaAliasId
           ) AS rn
    FROM coordenacao.DisciplinaAlias
)
DELETE A
FROM coordenacao.DisciplinaAlias A
JOIN DuplicadosMesmoPadrao D ON D.DisciplinaAliasId=A.DisciplinaAliasId
WHERE D.rn > 1;

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'coordenacao.DisciplinaAlias')
      AND name=N'IX_DisciplinaAlias_Alias'
)
    DROP INDEX IX_DisciplinaAlias_Alias ON coordenacao.DisciplinaAlias;

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id=c.object_id
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE s.name='coordenacao' AND t.name='DisciplinaAlias' AND c.name='Alias'
      AND c.collation_name <> 'Latin1_General_100_CI_AI'
)
    ALTER TABLE coordenacao.DisciplinaAlias
      ALTER COLUMN Alias NVARCHAR(200) COLLATE Latin1_General_100_CI_AI NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'coordenacao.DisciplinaAlias')
      AND name=N'UX_DisciplinaAlias_Alias'
)
    CREATE UNIQUE INDEX UX_DisciplinaAlias_Alias
      ON coordenacao.DisciplinaAlias(Alias);
