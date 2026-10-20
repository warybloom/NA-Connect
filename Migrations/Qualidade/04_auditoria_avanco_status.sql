SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'qualidade') IS NULL EXEC(N'CREATE SCHEMA qualidade');
GO

IF OBJECT_ID(N'qualidade.DocumentStatusAvancoConfig', N'U') IS NULL
CREATE TABLE qualidade.DocumentStatusAvancoConfig
(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentStatusAvancoConfig PRIMARY KEY,
    Project_ID_ACC nvarchar(100) NOT NULL,
    Document_Status_Normalizado nvarchar(255) NOT NULL,
    Document_Status_Exibicao nvarchar(255) NOT NULL,
    Percentual decimal(9,4) NOT NULL CONSTRAINT CK_DocumentStatusAvancoConfig_Percentual CHECK (Percentual BETWEEN 0 AND 100),
    Ativo bit NOT NULL CONSTRAINT DF_DocumentStatusAvancoConfig_Ativo DEFAULT 1,
    AtualizadoPor nvarchar(255) NULL,
    AtualizadoEm datetime2(3) NULL,
    CONSTRAINT UQ_DocumentStatusAvancoConfig_Project_Status UNIQUE(Project_ID_ACC, Document_Status_Normalizado)
);
GO

IF OBJECT_ID(N'qualidade.AuditoriaAvancoDocumento', N'U') IS NULL
CREATE TABLE qualidade.AuditoriaAvancoDocumento
(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_AuditoriaAvancoDocumento PRIMARY KEY,
    Project_ID_ACC nvarchar(100) NOT NULL,
    Documento nvarchar(255) NOT NULL,
    Document_Status_Origem nvarchar(255) NULL,
    Config_Id bigint NULL,
    Percentual_Original decimal(9,4) NULL,
    Percentual_Validado decimal(9,4) NOT NULL CONSTRAINT CK_AuditoriaAvancoDocumento_Validado CHECK (Percentual_Validado BETWEEN 0 AND 100),
    Status_Decisao nvarchar(30) NOT NULL CONSTRAINT DF_AuditoriaAvancoDocumento_Status DEFAULT N'CONFIRMADO',
    ConfirmadoPor nvarchar(255) NOT NULL,
    ConfirmadoEm datetime2(3) NOT NULL CONSTRAINT DF_AuditoriaAvancoDocumento_ConfirmadoEm DEFAULT SYSUTCDATETIME(),
    Ativo bit NOT NULL CONSTRAINT DF_AuditoriaAvancoDocumento_Ativo DEFAULT 1,
    Motivo nvarchar(500) NULL,
    CONSTRAINT FK_AuditoriaAvancoDocumento_Config FOREIGN KEY(Config_Id) REFERENCES qualidade.DocumentStatusAvancoConfig(Id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'qualidade.AuditoriaAvancoDocumento') AND name=N'UX_AuditoriaAvancoDocumento_Ativa')
CREATE UNIQUE INDEX UX_AuditoriaAvancoDocumento_Ativa ON qualidade.AuditoriaAvancoDocumento(Project_ID_ACC, Documento) WHERE Ativo=1;
GO

CREATE OR ALTER VIEW qualidade.VW_Ldp_AvancoGerencial
AS
WITH LdpAtual AS
(
    SELECT L.*
    FROM dbo.Ldp_Unificada L
    WHERE L.Project_ID_ACC IS NOT NULL
      AND ((EXISTS(SELECT 1 FROM dbo.Projetos P WHERE P.Project_ID_ACC=L.Project_ID_ACC AND P.Tipo_LDP=2)
            AND L.Data_Sincronizacao=(SELECT MAX(T2.Data_Sincronizacao) FROM dbo.Ldp_Unificada T2 WHERE T2.Project_ID_ACC=L.Project_ID_ACC))
        OR (NOT EXISTS(SELECT 1 FROM dbo.Projetos P WHERE P.Project_ID_ACC=L.Project_ID_ACC AND P.Tipo_LDP=2)
            AND L.Data_Sincronizacao >= ISNULL((SELECT MAX(G.Data_Sincronizacao) FROM
                (SELECT X.Project_ID_ACC,X.Data_Sincronizacao,DATEDIFF(SECOND,LAG(X.Data_Sincronizacao) OVER(PARTITION BY X.Project_ID_ACC ORDER BY X.Data_Sincronizacao,X.ID_Registro),X.Data_Sincronizacao) Gap
                 FROM dbo.Ldp_Unificada X) G WHERE G.Project_ID_ACC=L.Project_ID_ACC AND G.Gap>30),CAST('19000101' AS datetime))))
)
SELECT L.Project_ID_ACC, UPPER(LTRIM(RTRIM(L.NA_Document_Number))) AS Documento, L.Document_Status,
       CAST(L.Percent_Physical_Completed AS decimal(9,4)) AS Percentual_Original,
       D.Percentual_Validado AS Percentual_Override,
       CAST(CASE WHEN ISNULL(L.Percent_Physical_Completed,0)>0 THEN L.Percent_Physical_Completed WHEN D.Id IS NOT NULL THEN D.Percentual_Validado ELSE L.Percent_Physical_Completed END AS decimal(9,4)) AS Percentual_Gerencial,
       CASE WHEN ISNULL(L.Percent_Physical_Completed,0)>0 THEN N'ORIGINAL' WHEN D.Id IS NOT NULL THEN N'AUDITORIA_STATUS' ELSE N'SEM_AVANCO' END AS Fonte_Avanco
FROM LdpAtual L
LEFT JOIN qualidade.AuditoriaAvancoDocumento D ON D.Project_ID_ACC=L.Project_ID_ACC AND D.Documento=UPPER(LTRIM(RTRIM(L.NA_Document_Number))) AND D.Ativo=1;
GO
