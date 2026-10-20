SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'gestao') IS NULL
    EXEC(N'CREATE SCHEMA gestao');
GO

IF OBJECT_ID(N'gestao.AuditoriaRedistribuicaoDocumento', N'U') IS NULL
BEGIN
    CREATE TABLE gestao.AuditoriaRedistribuicaoDocumento
    (
        Id uniqueidentifier NOT NULL CONSTRAINT DF_AuditoriaRedistribuicaoDocumento_Id DEFAULT NEWSEQUENTIALID(),
        Project_ID_ACC nvarchar(100) NOT NULL,
        Documento_Origem nvarchar(255) NOT NULL,
        Disciplina nvarchar(255) NOT NULL,
        HH_Origem decimal(18,6) NOT NULL,
        Custo_Origem decimal(18,6) NOT NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_AuditoriaRedistribuicaoDocumento_Status DEFAULT N'CONFIRMADO',
        Status_Documento nvarchar(30) NOT NULL CONSTRAINT DF_AuditoriaRedistribuicaoDocumento_StatusDocumento DEFAULT N'CANCELADO',
        ConfirmadoPor nvarchar(255) NOT NULL,
        ConfirmadoEm datetime2(3) NOT NULL CONSTRAINT DF_AuditoriaRedistribuicaoDocumento_ConfirmadoEm DEFAULT SYSUTCDATETIME(),
        Ativo bit NOT NULL CONSTRAINT DF_AuditoriaRedistribuicaoDocumento_Ativo DEFAULT 1,
        Motivo nvarchar(500) NULL,
        CONSTRAINT PK_AuditoriaRedistribuicaoDocumento PRIMARY KEY (Id)
    );
END
GO

IF COL_LENGTH(N'gestao.AuditoriaRedistribuicaoDocumento', N'Status_Documento') IS NULL
BEGIN
    ALTER TABLE gestao.AuditoriaRedistribuicaoDocumento
        ADD Status_Documento nvarchar(30) NOT NULL
            CONSTRAINT DF_AuditoriaRedistribuicaoDocumento_StatusDocumento DEFAULT N'CANCELADO';
END
GO

IF OBJECT_ID(N'gestao.AuditoriaRedistribuicaoItem', N'U') IS NULL
BEGIN
    CREATE TABLE gestao.AuditoriaRedistribuicaoItem
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        Redistribuicao_Id uniqueidentifier NOT NULL,
        Documento_Destino nvarchar(255) NOT NULL,
        Titulo_Destino nvarchar(500) NULL,
        Planned_HH_Destino decimal(18,6) NOT NULL,
        Peso decimal(18,12) NOT NULL,
        HH_Redistribuida decimal(18,6) NOT NULL,
        Custo_Redistribuido decimal(18,6) NOT NULL,
        CONSTRAINT PK_AuditoriaRedistribuicaoItem PRIMARY KEY (Id),
        CONSTRAINT FK_AuditoriaRedistribuicaoItem_Documento
            FOREIGN KEY (Redistribuicao_Id)
            REFERENCES gestao.AuditoriaRedistribuicaoDocumento(Id)
    );
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'gestao.AuditoriaRedistribuicaoDocumento')
      AND name = N'UX_AuditoriaRedistribuicaoDocumento_Ativa'
)
BEGIN
    CREATE UNIQUE INDEX UX_AuditoriaRedistribuicaoDocumento_Ativa
        ON gestao.AuditoriaRedistribuicaoDocumento(Project_ID_ACC, Documento_Origem)
        WHERE Ativo = 1;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'gestao.AuditoriaRedistribuicaoItem')
      AND name = N'IX_AuditoriaRedistribuicaoItem_Redistribuicao'
)
BEGIN
    CREATE INDEX IX_AuditoriaRedistribuicaoItem_Redistribuicao
        ON gestao.AuditoriaRedistribuicaoItem(Redistribuicao_Id, Documento_Destino);
END
GO

CREATE OR ALTER VIEW gestao.VW_TimesheetDocumentoGerencial
AS
WITH Base AS
(
    SELECT
        Project_ID_ACC,
        UPPER(LTRIM(RTRIM(DocumentoCodigo_Corrigido))) AS DocumentoCodigo,
        MAX(NULLIF(LTRIM(RTRIM(Disciplina)), N'')) AS Disciplina,
        CAST(SUM(ISNULL(HorasTrabalhadas, 0)) AS decimal(18,6)) AS HH_Original,
        CAST(SUM(ISNULL(CustoCalculado, 0)) AS decimal(18,6)) AS Custo_Original
    FROM gestao.VW_TimeSheet_Corrigido_Espelho
    WHERE NULLIF(LTRIM(RTRIM(DocumentoCodigo_Corrigido)), N'') IS NOT NULL
    GROUP BY Project_ID_ACC, UPPER(LTRIM(RTRIM(DocumentoCodigo_Corrigido)))
),
Saidas AS
(
    SELECT
        Project_ID_ACC,
        UPPER(LTRIM(RTRIM(Documento_Origem))) AS DocumentoCodigo,
        MAX(Disciplina) AS Disciplina,
        CAST(SUM(HH_Origem) AS decimal(18,6)) AS HH_Redistribuida_Saida,
        CAST(SUM(Custo_Origem) AS decimal(18,6)) AS Custo_Redistribuido_Saida
    FROM gestao.AuditoriaRedistribuicaoDocumento
    WHERE Ativo = 1 AND Status = N'CONFIRMADO'
    GROUP BY Project_ID_ACC, UPPER(LTRIM(RTRIM(Documento_Origem)))
),
Entradas AS
(
    SELECT
        h.Project_ID_ACC,
        UPPER(LTRIM(RTRIM(i.Documento_Destino))) AS DocumentoCodigo,
        MAX(h.Disciplina) AS Disciplina,
        CAST(SUM(i.HH_Redistribuida) AS decimal(18,6)) AS HH_Redistribuida_Entrada,
        CAST(SUM(i.Custo_Redistribuido) AS decimal(18,6)) AS Custo_Redistribuido_Entrada
    FROM gestao.AuditoriaRedistribuicaoDocumento h
    INNER JOIN gestao.AuditoriaRedistribuicaoItem i
        ON i.Redistribuicao_Id = h.Id
    WHERE h.Ativo = 1 AND h.Status = N'CONFIRMADO'
    GROUP BY h.Project_ID_ACC, UPPER(LTRIM(RTRIM(i.Documento_Destino)))
),
Chaves AS
(
    SELECT Project_ID_ACC, DocumentoCodigo FROM Base
    UNION
    SELECT Project_ID_ACC, DocumentoCodigo FROM Saidas
    UNION
    SELECT Project_ID_ACC, DocumentoCodigo FROM Entradas
)
SELECT
    k.Project_ID_ACC,
    k.DocumentoCodigo,
    COALESCE(s.Disciplina, e.Disciplina, b.Disciplina) AS Disciplina,
    CAST(ISNULL(b.HH_Original, 0) AS decimal(18,6)) AS HH_Original,
    CAST(ISNULL(b.Custo_Original, 0) AS decimal(18,6)) AS Custo_Original,
    CAST(ISNULL(e.HH_Redistribuida_Entrada, 0) AS decimal(18,6)) AS HH_Redistribuida_Entrada,
    CAST(ISNULL(e.Custo_Redistribuido_Entrada, 0) AS decimal(18,6)) AS Custo_Redistribuido_Entrada,
    CAST(ISNULL(s.HH_Redistribuida_Saida, 0) AS decimal(18,6)) AS HH_Redistribuida_Saida,
    CAST(ISNULL(s.Custo_Redistribuido_Saida, 0) AS decimal(18,6)) AS Custo_Redistribuido_Saida,
    CAST(ISNULL(b.HH_Original, 0) - ISNULL(s.HH_Redistribuida_Saida, 0) + ISNULL(e.HH_Redistribuida_Entrada, 0) AS decimal(18,6)) AS HH_Gerencial,
    CAST(ISNULL(b.Custo_Original, 0) - ISNULL(s.Custo_Redistribuido_Saida, 0) + ISNULL(e.Custo_Redistribuido_Entrada, 0) AS decimal(18,6)) AS Custo_Gerencial
FROM Chaves k
LEFT JOIN Base b
    ON b.Project_ID_ACC = k.Project_ID_ACC
   AND b.DocumentoCodigo = k.DocumentoCodigo
LEFT JOIN Saidas s
    ON s.Project_ID_ACC = k.Project_ID_ACC
   AND s.DocumentoCodigo = k.DocumentoCodigo
LEFT JOIN Entradas e
    ON e.Project_ID_ACC = k.Project_ID_ACC
   AND e.DocumentoCodigo = k.DocumentoCodigo;
GO

CREATE OR ALTER VIEW gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial
AS
WITH Expandido AS
(
    SELECT
        t.Id AS Id_Origem,
        t.Project_ID_ACC,
        t.NomeProfissional,
        t.HorasPlanejadas,
        t.HorasTrabalhadas,
        t.Projeto,
        t.Funcao,
        t.Disciplina,
        t.DocumentoCodigo_Original,
        t.DocumentoCodigo_Corrigido,
        t.Documento,
        t.Descricao,
        t.Doc1,
        t.Doc2,
        t.Doc3,
        t.Doc4,
        t.Doc5,
        t.DataLancamento,
        t.CustoCalculado,
        t.Data_Sincronizacao,
        t.Tipo_Correcao,
        h.Id AS Redistribuicao_Id,
        i.Id AS Redistribuicao_Item_Id,
        i.Documento_Destino,
        i.Titulo_Destino,
        i.Peso
    FROM gestao.VW_TimeSheet_Corrigido_Espelho t
    LEFT JOIN gestao.AuditoriaRedistribuicaoDocumento h
        ON h.Project_ID_ACC = t.Project_ID_ACC
       AND UPPER(LTRIM(RTRIM(h.Documento_Origem)))
           = UPPER(LTRIM(RTRIM(t.DocumentoCodigo_Corrigido)))
       AND h.Ativo = 1
       AND h.Status = N'CONFIRMADO'
    LEFT JOIN gestao.AuditoriaRedistribuicaoItem i
        ON i.Redistribuicao_Id = h.Id
)
SELECT
    CAST(ROW_NUMBER() OVER
    (
        ORDER BY
            e.Project_ID_ACC,
            e.Id_Origem,
            ISNULL(e.Redistribuicao_Item_Id, 0)
    ) AS bigint) AS Id,
    e.Project_ID_ACC,
    e.NomeProfissional,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.HorasPlanejadas
        ELSE CAST(ISNULL(e.HorasPlanejadas, 0) * e.Peso AS decimal(38,18))
    END AS HorasPlanejadas,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.HorasTrabalhadas
        ELSE CAST(ISNULL(e.HorasTrabalhadas, 0) * e.Peso AS decimal(38,18))
    END AS HorasTrabalhadas,
    e.Projeto,
    e.Funcao,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.Disciplina
        ELSE COALESCE(ldp.Discipline, e.Disciplina)
    END AS Disciplina,
    e.DocumentoCodigo_Original,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.DocumentoCodigo_Corrigido
        ELSE e.Documento_Destino
    END AS DocumentoCodigo_Corrigido,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.Documento
        ELSE COALESCE(e.Titulo_Destino, ldp.Document_Title, e.Documento)
    END AS Documento,
    e.Descricao,
    CASE WHEN e.Redistribuicao_Id IS NULL THEN e.Doc1 ELSE COALESCE(ldp.CR_OS, e.Doc1) END AS Doc1,
    CASE WHEN e.Redistribuicao_Id IS NULL THEN e.Doc2 ELSE COALESCE(ldp.Area, e.Doc2) END AS Doc2,
    CASE WHEN e.Redistribuicao_Id IS NULL THEN e.Doc3 ELSE COALESCE(ldp.Discipline_Activity, e.Doc3) END AS Doc3,
    CASE WHEN e.Redistribuicao_Id IS NULL THEN e.Doc4 ELSE COALESCE(ldp.Doc_Type_Code, e.Doc4) END AS Doc4,
    CASE WHEN e.Redistribuicao_Id IS NULL THEN e.Doc5 ELSE COALESCE(ldp.Seq, e.Doc5) END AS Doc5,
    e.DataLancamento,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.CustoCalculado
        ELSE CAST(ISNULL(e.CustoCalculado, 0) * e.Peso AS decimal(38,18))
    END AS CustoCalculado,
    e.Data_Sincronizacao,
    CASE
        WHEN e.Redistribuicao_Id IS NULL THEN e.Tipo_Correcao
        ELSE N'REDISTRIBUICAO_AUDITORIA'
    END AS Tipo_Correcao
FROM Expandido e
LEFT JOIN dbo.VW_Ldp_Unificada ldp
    ON e.Redistribuicao_Id IS NOT NULL
   AND ldp.Project_ID_ACC = e.Project_ID_ACC
   AND UPPER(LTRIM(RTRIM(ldp.NA_Document_Number)))
       = UPPER(LTRIM(RTRIM(e.Documento_Destino)))
WHERE e.Redistribuicao_Id IS NULL
   OR e.Redistribuicao_Item_Id IS NOT NULL;
GO

-- Invariante global: antes e depois de redistribuições, os totais gerenciais
-- devem permanecer iguais aos totais originais por projeto.
-- SELECT Project_ID_ACC,
--        SUM(HH_Original) HH_Original, SUM(HH_Gerencial) HH_Gerencial,
--        SUM(Custo_Original) Custo_Original, SUM(Custo_Gerencial) Custo_Gerencial
-- FROM gestao.VW_TimesheetDocumentoGerencial
-- GROUP BY Project_ID_ACC
-- HAVING ABS(SUM(HH_Original)-SUM(HH_Gerencial)) > 0.000001
--     OR ABS(SUM(Custo_Original)-SUM(Custo_Gerencial)) > 0.000001;
