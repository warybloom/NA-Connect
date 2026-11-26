/*
    OperationalAuditSchema.sql
    ---------------------------------------------------------------------------
    Schema de persistência da Auditoria Operacional (subsistema paralelo e
    somente-leitura sobre os dados operacionais do N&A Connect).

    ESTE SCRIPT NUNCA É EXECUTADO AUTOMATICAMENTE PELA APLICAÇÃO. É um artefato
    de infraestrutura para execução manual, deliberada, por um administrador de
    banco de dados, na credencial de destino de "AuditRepositoryConnection".

    Cria exclusivamente:
      - o schema [audit];
      - 7 tabelas dentro de [audit];
      - 5 views de leitura (consumo por Power BI / relatórios) dentro de [audit].

    NÃO cria, altera nem referencia nenhum objeto em [dbo] ou em qualquer outro
    schema operacional. NÃO concede permissões — a permissão de leitura restrita
    (db_datareader) da credencial "AuditSourceConnection" sobre os dados
    operacionais é administrada separadamente, fora deste script.

    Idempotente: pode ser executado repetidamente sem erro e sem efeito
    colateral em uma instalação já provisionada (todo objeto é criado apenas
    se ainda não existir).
    ---------------------------------------------------------------------------
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- =============================================================================
-- 1. SCHEMA
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'audit')
BEGIN
    EXEC('CREATE SCHEMA [audit] AUTHORIZATION [dbo];');
END
GO

-- =============================================================================
-- 2. TABELAS (7)
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 2.1 audit.AuditRun — cabeçalho de uma execução completa da auditoria.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditRun')
BEGIN
    CREATE TABLE audit.AuditRun
    (
        AuditRunId                  BIGINT IDENTITY(1,1) NOT NULL,
        ExecutionId                 UNIQUEIDENTIFIER      NOT NULL,
        StartedAtUtc                DATETIME2(3)          NOT NULL,
        FinishedAtUtc                DATETIME2(3)          NULL,
        Status                      NVARCHAR(40)          NOT NULL,
        TriggeredBy                 NVARCHAR(200)         NULL,
        StagesRequested             NVARCHAR(400)         NULL,
        TotalProjects               INT                   NULL,
        TotalFindings               INT                   NULL,
        TotalRequirementsEvaluated  INT                   NULL,
        ErrorMessage                NVARCHAR(MAX)         NULL,

        CONSTRAINT PK_AuditRun PRIMARY KEY CLUSTERED (AuditRunId),
        CONSTRAINT UQ_AuditRun_ExecutionId UNIQUE (ExecutionId)
    );
END
GO

-- -----------------------------------------------------------------------------
-- 2.2 audit.AuditProjectRun — resultado da auditoria para UM projeto dentro de
--     uma execução (1 AuditRun : N AuditProjectRun).
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditProjectRun')
BEGIN
    CREATE TABLE audit.AuditProjectRun
    (
        AuditProjectRunId    BIGINT IDENTITY(1,1) NOT NULL,
        AuditRunId           BIGINT                NOT NULL,
        ProjectIdAcc         NVARCHAR(100)         NOT NULL,
        AccountIdAcc         NVARCHAR(100)         NULL,
        ProjectName          NVARCHAR(300)         NULL,
        StartedAtUtc         DATETIME2(3)          NOT NULL,
        FinishedAtUtc        DATETIME2(3)          NULL,
        Status               NVARCHAR(40)          NOT NULL,
        PersistenceStatus    NVARCHAR(40)          NOT NULL,

        CONSTRAINT PK_AuditProjectRun PRIMARY KEY CLUSTERED (AuditProjectRunId),
        CONSTRAINT FK_AuditProjectRun_AuditRun FOREIGN KEY (AuditRunId)
            REFERENCES audit.AuditRun (AuditRunId)
    );

    CREATE NONCLUSTERED INDEX IX_AuditProjectRun_AuditRunId ON audit.AuditProjectRun (AuditRunId);
    CREATE NONCLUSTERED INDEX IX_AuditProjectRun_ProjectIdAcc ON audit.AuditProjectRun (ProjectIdAcc);
END
GO

-- -----------------------------------------------------------------------------
-- 2.3 audit.AuditStageResult — resultado agregado de UMA etapa para UM projeto
--     dentro de uma execução (1 AuditProjectRun : N AuditStageResult).
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditStageResult')
BEGIN
    CREATE TABLE audit.AuditStageResult
    (
        AuditStageResultId    BIGINT IDENTITY(1,1) NOT NULL,
        AuditProjectRunId     BIGINT                NOT NULL,
        StageNumber           NVARCHAR(10)          NOT NULL,
        StageName             NVARCHAR(200)         NOT NULL,
        Result                NVARCHAR(40)          NOT NULL,
        TotalRequirements     INT                   NOT NULL,
        Approved              INT                   NOT NULL,
        Failed                INT                   NOT NULL,
        Warnings              INT                   NOT NULL,
        OperationalErrors     INT                   NOT NULL,
        AutomationErrors      INT                   NOT NULL,
        Inconclusive          INT                   NOT NULL,
        Blockers              INT                   NOT NULL,
        CompliancePercent     DECIMAL(5,2)          NULL,
        StartedAtUtc          DATETIME2(3)          NOT NULL,
        FinishedAtUtc         DATETIME2(3)          NULL,

        CONSTRAINT PK_AuditStageResult PRIMARY KEY CLUSTERED (AuditStageResultId),
        CONSTRAINT FK_AuditStageResult_AuditProjectRun FOREIGN KEY (AuditProjectRunId)
            REFERENCES audit.AuditProjectRun (AuditProjectRunId)
    );

    CREATE NONCLUSTERED INDEX IX_AuditStageResult_AuditProjectRunId ON audit.AuditStageResult (AuditProjectRunId);
    CREATE NONCLUSTERED INDEX IX_AuditStageResult_StageNumber ON audit.AuditStageResult (StageNumber);
END
GO

-- -----------------------------------------------------------------------------
-- 2.4 audit.AuditRequirementCatalog — catálogo estável (etapa, código de
--     requisito) -> descrição/se bloqueia a etapa. Populado sob demanda pelo
--     próprio repository (get-or-create), nunca via seed manual.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditRequirementCatalog')
BEGIN
    CREATE TABLE audit.AuditRequirementCatalog
    (
        AuditRequirementCatalogId    BIGINT IDENTITY(1,1) NOT NULL,
        StageNumber                  NVARCHAR(10)          NOT NULL,
        RequirementCode              NVARCHAR(60)          NOT NULL,
        Description                  NVARCHAR(400)         NULL,
        IsBlocking                   BIT                   NOT NULL,

        CONSTRAINT PK_AuditRequirementCatalog PRIMARY KEY CLUSTERED (AuditRequirementCatalogId),
        CONSTRAINT UQ_AuditRequirementCatalog_Stage_Code UNIQUE (StageNumber, RequirementCode)
    );
END
GO

-- -----------------------------------------------------------------------------
-- 2.5 audit.AuditRequirementResult — o resultado de UM requisito avaliado para
--     UM projeto dentro de uma execução (histórico completo, nunca sobrescrito
--     — cada execução grava sua própria linha).
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditRequirementResult')
BEGIN
    CREATE TABLE audit.AuditRequirementResult
    (
        AuditRequirementResultId      BIGINT IDENTITY(1,1) NOT NULL,
        AuditProjectRunId             BIGINT                NOT NULL,
        AuditRequirementCatalogId     BIGINT                NOT NULL,
        StageNumber                   NVARCHAR(10)          NOT NULL,
        RequirementCode               NVARCHAR(60)          NOT NULL,
        ObjectEvaluated                NVARCHAR(400)         NULL,
        Status                        NVARCHAR(40)          NOT NULL,
        ValueExpected                  NVARCHAR(400)         NULL,
        ValueFound                     NVARCHAR(400)         NULL,
        EvaluatedAtUtc                 DATETIME2(3)          NOT NULL,

        CONSTRAINT PK_AuditRequirementResult PRIMARY KEY CLUSTERED (AuditRequirementResultId),
        CONSTRAINT FK_AuditRequirementResult_AuditProjectRun FOREIGN KEY (AuditProjectRunId)
            REFERENCES audit.AuditProjectRun (AuditProjectRunId),
        CONSTRAINT FK_AuditRequirementResult_AuditRequirementCatalog FOREIGN KEY (AuditRequirementCatalogId)
            REFERENCES audit.AuditRequirementCatalog (AuditRequirementCatalogId)
    );

    CREATE NONCLUSTERED INDEX IX_AuditRequirementResult_AuditProjectRunId ON audit.AuditRequirementResult (AuditProjectRunId);
    CREATE NONCLUSTERED INDEX IX_AuditRequirementResult_CatalogId ON audit.AuditRequirementResult (AuditRequirementCatalogId);
END
GO

-- -----------------------------------------------------------------------------
-- 2.6 audit.AuditIssue — um problema lógico deduplicado por (ProjectIdAcc,
--     DedupHash). Estado mutável entre execuções (Aberto / PossivelmenteResolvido
--     / ResolvidoComEvidencia / Regressao) — NUNCA duplicado para o mesmo par.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditIssue')
BEGIN
    CREATE TABLE audit.AuditIssue
    (
        AuditIssueId              BIGINT IDENTITY(1,1) NOT NULL,
        ProjectIdAcc              NVARCHAR(100)         NOT NULL,
        DedupHash                 CHAR(64)              NOT NULL,
        StageNumber               NVARCHAR(10)          NOT NULL,
        ProblemCode               NVARCHAR(60)          NOT NULL,
        ErrorDomain               NVARCHAR(40)          NOT NULL,
        Classification            NVARCHAR(60)          NOT NULL,
        Severity                  NVARCHAR(20)          NOT NULL,
        Title                     NVARCHAR(300)         NOT NULL,
        Description               NVARCHAR(MAX)         NULL,
        ProbableOwner             NVARCHAR(100)         NULL,
        DocumentNA                NVARCHAR(300)         NULL,
        DocumentClient            NVARCHAR(300)         NULL,
        FileName                  NVARCHAR(300)         NULL,
        FoundFolder               NVARCHAR(400)         NULL,
        FoundWorksheet            NVARCHAR(200)         NULL,
        ExcelRow                  INT                   NULL,
        FieldName                 NVARCHAR(200)         NULL,
        CustomAttributeName       NVARCHAR(200)         NULL,
        CustomAttributeIdAcc      NVARCHAR(100)         NULL,
        CustomAttributeState      NVARCHAR(40)          NULL,
        IsCustomAttributeHidden   BIT                   NULL,
        ExpectedFolder            NVARCHAR(400)         NULL,
        ConfigurationKey          NVARCHAR(200)         NULL,
        BlocksStage               BIT                   NOT NULL,
        AutomationCanFix          BIT                   NOT NULL,
        CurrentStatus             NVARCHAR(40)          NOT NULL,
        FirstSeenAtUtc            DATETIME2(3)          NOT NULL,
        LastSeenAtUtc             DATETIME2(3)          NOT NULL,
        OccurrenceCount           INT                   NOT NULL,

        CONSTRAINT PK_AuditIssue PRIMARY KEY CLUSTERED (AuditIssueId),
        CONSTRAINT UQ_AuditIssue_Project_DedupHash UNIQUE (ProjectIdAcc, DedupHash)
    );

    CREATE NONCLUSTERED INDEX IX_AuditIssue_ProjectIdAcc_Status ON audit.AuditIssue (ProjectIdAcc, CurrentStatus);
    CREATE NONCLUSTERED INDEX IX_AuditIssue_StageNumber ON audit.AuditIssue (StageNumber);
END
GO

-- -----------------------------------------------------------------------------
-- 2.7 audit.AuditOccurrence — cada evidência individual de um AuditIssue,
--     amarrada à execução (AuditRun) que a detectou. (AuditIssueId,
--     OccurrenceHash) é único — a mesma evidência nunca é gravada duas vezes
--     na mesma execução (o repository trata a violação como no-op esperado).
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'audit' AND t.name = N'AuditOccurrence')
BEGIN
    CREATE TABLE audit.AuditOccurrence
    (
        AuditOccurrenceId    BIGINT IDENTITY(1,1) NOT NULL,
        AuditIssueId         BIGINT                NOT NULL,
        AuditRunId            BIGINT                NOT NULL,
        OccurrenceHash        CHAR(64)              NOT NULL,
        DetectedAtUtc         DATETIME2(3)          NOT NULL,
        -- Espelha OperationalFinding.FonteEvidencia ("fonte" no contrato do VW_AuditOccurrences_BI) —
        -- distinto de SourceLogFile ("arquivo"): a FONTE descreve ONDE a evidência foi observada
        -- (ex.: "Log de execução da Etapa 7", "Resposta da API ACC"), enquanto SourceLogFile e' o
        -- nome do ARQUIVO de log especifico.
        EvidenceSource        NVARCHAR(300)         NULL,
        SourceLogFile         NVARCHAR(300)         NULL,
        LogLineStart          INT                   NULL,
        LogLineEnd            INT                   NULL,
        ValueExpected         NVARCHAR(400)         NULL,
        ValueFound            NVARCHAR(400)         NULL,
        Evidence              NVARCHAR(MAX)         NULL,

        CONSTRAINT PK_AuditOccurrence PRIMARY KEY CLUSTERED (AuditOccurrenceId),
        CONSTRAINT FK_AuditOccurrence_AuditIssue FOREIGN KEY (AuditIssueId)
            REFERENCES audit.AuditIssue (AuditIssueId),
        CONSTRAINT FK_AuditOccurrence_AuditRun FOREIGN KEY (AuditRunId)
            REFERENCES audit.AuditRun (AuditRunId),
        CONSTRAINT UQ_AuditOccurrence_Issue_Hash UNIQUE (AuditIssueId, OccurrenceHash)
    );

    CREATE NONCLUSTERED INDEX IX_AuditOccurrence_AuditRunId ON audit.AuditOccurrence (AuditRunId);
END
GO

-- =============================================================================
-- 3. VIEWS (5) — contrato oficial de consumo pelo Power BI. Nomes e colunas
--    fixados pela correção de Checkpoint 1 (item 3); nenhuma coluna aqui é
--    inventada — todas vêm de colunas reais das 7 tabelas acima. Só
--    EvidenceSource (seção 2.7, "fonte" do contrato de VW_AuditOccurrences_BI)
--    e' uma coluna nova, adicionada porque a informação já existe no domínio
--    (OperationalFinding.FonteEvidencia) mas nunca tinha sido persistida.
--    CREATE OR ALTER é idempotente por natureza (T-SQL, SQL Server 2016+).
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 3.1 audit.VW_AuditIssues_BI — uma linha por audit.AuditIssue (estado atual
--     deduplicado do problema; não é histórico de ocorrências — isso é
--     VW_AuditOccurrences_BI).
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW audit.VW_AuditIssues_BI
AS
SELECT
    i.AuditIssueId,
    i.ProjectIdAcc,
    i.StageNumber                                          AS Etapa,
    i.ProblemCode,
    i.DocumentNA,
    i.DocumentClient,
    i.FileName,
    i.ErrorDomain,
    i.Classification,
    i.Severity,
    i.Title,
    i.Description,
    i.CurrentStatus,
    i.FirstSeenAtUtc,
    i.LastSeenAtUtc,
    i.OccurrenceCount,
    i.ProbableOwner,
    i.BlocksStage,
    i.AutomationCanFix,
    i.CustomAttributeName,
    i.CustomAttributeIdAcc,
    i.CustomAttributeState,
    i.IsCustomAttributeHidden
FROM audit.AuditIssue i;
GO

-- -----------------------------------------------------------------------------
-- 3.2 audit.VW_AuditOccurrences_BI — uma linha por audit.AuditOccurrence
--     (histórico completo de evidências, uma por execução em que o problema
--     foi observado), com projeto/etapa/documento resolvidos via AuditIssue e
--     execução resolvida via AuditRun — evita join manual no Power BI.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW audit.VW_AuditOccurrences_BI
AS
SELECT
    occ.AuditOccurrenceId,
    run.ExecutionId,
    i.ProjectIdAcc,
    i.StageNumber                                          AS Etapa,
    i.DocumentNA,
    i.DocumentClient,
    occ.EvidenceSource                                      AS Fonte,
    occ.SourceLogFile                                       AS Arquivo,
    occ.LogLineStart,
    occ.LogLineEnd,
    occ.DetectedAtUtc                                       AS DataDeteccao,
    occ.Evidence                                            AS Evidencia,
    occ.ValueExpected,
    occ.ValueFound
FROM audit.AuditOccurrence occ
INNER JOIN audit.AuditIssue i ON i.AuditIssueId = occ.AuditIssueId
INNER JOIN audit.AuditRun run ON run.AuditRunId = occ.AuditRunId;
GO

-- -----------------------------------------------------------------------------
-- 3.3 audit.VW_AuditStageSummary_BI — uma linha por audit.AuditStageResult
--     (uma etapa dentro de um projeto dentro de uma execução), com
--     execução/projeto resolvidos.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW audit.VW_AuditStageSummary_BI
AS
SELECT
    sr.AuditStageResultId,
    run.ExecutionId,
    pr.ProjectIdAcc,
    sr.StageNumber                                          AS Etapa,
    sr.StageName,
    sr.Result                                                AS ResultadoEtapa,
    sr.TotalRequirements                                     AS RequisitosAvaliados,
    sr.Approved                                              AS Aprovados,
    sr.Failed                                                AS Falhos,
    sr.Warnings                                              AS Avisos,
    sr.OperationalErrors                                     AS ErrosOperacionais,
    sr.AutomationErrors                                      AS ErrosAutomacao,
    sr.Inconclusive                                          AS Inconclusivos,
    sr.Blockers                                              AS Bloqueadores,
    sr.CompliancePercent                                     AS PercentualConformidade,
    sr.StartedAtUtc,
    sr.FinishedAtUtc
FROM audit.AuditStageResult sr
INNER JOIN audit.AuditProjectRun pr ON pr.AuditProjectRunId = sr.AuditProjectRunId
INNER JOIN audit.AuditRun run ON run.AuditRunId = pr.AuditRunId;
GO

-- -----------------------------------------------------------------------------
-- 3.4 audit.VW_AuditProjectSummary_BI — uma linha por audit.AuditProjectRun
--     (um projeto dentro de uma execução), com:
--       - "ResultadoGeral" = pior status entre as etapas desta execução deste
--         projeto (rollup de apresentação sobre um valor JÁ calculado por
--         RequirementStatusAggregator — nunca recalcula a classificação de
--         negócio a partir de findings/requisitos brutos, só agrega o texto
--         de Result já persistido por etapa);
--       - totais por domínio/severidade = contagem de AuditIssue distintos
--         cuja evidência (AuditOccurrence) pertence a ESTA execução deste
--         projeto (join via AuditOccurrence.AuditRunId, não um total
--         histórico do AuditIssue, que é mutável entre execuções).
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW audit.VW_AuditProjectSummary_BI
AS
WITH IssuesDaExecucao AS
(
    SELECT DISTINCT
        pr.AuditProjectRunId,
        i.AuditIssueId,
        i.ErrorDomain,
        i.Severity
    FROM audit.AuditProjectRun pr
    INNER JOIN audit.AuditOccurrence occ ON occ.AuditRunId = pr.AuditRunId
    INNER JOIN audit.AuditIssue i ON i.AuditIssueId = occ.AuditIssueId AND i.ProjectIdAcc = pr.ProjectIdAcc
)
SELECT
    pr.AuditProjectRunId,
    run.ExecutionId,
    pr.ProjectIdAcc,
    CASE
        WHEN SUM(CASE WHEN sr.Result LIKE 'Reprovada%' THEN 1 ELSE 0 END) > 0 THEN 'REPROVADO'
        WHEN SUM(CASE WHEN sr.Result = 'Inconclusiva' THEN 1 ELSE 0 END) > 0 THEN 'INCONCLUSIVO'
        WHEN SUM(CASE WHEN sr.Result = 'AprovadaComAlertas' THEN 1 ELSE 0 END) > 0 THEN 'APROVADO_COM_ALERTAS'
        WHEN COUNT(sr.AuditStageResultId) = 0 THEN 'SEM_ETAPAS_AVALIADAS'
        ELSE 'APROVADO'
    END                                                             AS ResultadoGeral,
    COUNT(DISTINCT CASE WHEN iss.ErrorDomain = 'Operacional' THEN iss.AuditIssueId END)             AS TotalIssuesDominioOperacional,
    COUNT(DISTINCT CASE WHEN iss.ErrorDomain = 'Automacao' THEN iss.AuditIssueId END)                AS TotalIssuesDominioAutomacao,
    COUNT(DISTINCT CASE WHEN iss.ErrorDomain = 'Inconclusivo' THEN iss.AuditIssueId END)             AS TotalIssuesDominioInconclusivo,
    COUNT(DISTINCT CASE WHEN iss.ErrorDomain = 'ComportamentoEsperado' THEN iss.AuditIssueId END)    AS TotalIssuesDominioComportamentoEsperado,
    COUNT(DISTINCT CASE WHEN iss.Severity = 'Critico' THEN iss.AuditIssueId END)                     AS TotalIssuesSeveridadeCritico,
    COUNT(DISTINCT CASE WHEN iss.Severity = 'Alto' THEN iss.AuditIssueId END)                        AS TotalIssuesSeveridadeAlto,
    COUNT(DISTINCT CASE WHEN iss.Severity = 'Medio' THEN iss.AuditIssueId END)                       AS TotalIssuesSeveridadeMedio,
    COUNT(DISTINCT CASE WHEN iss.Severity = 'Baixo' THEN iss.AuditIssueId END)                       AS TotalIssuesSeveridadeBaixo,
    COUNT(DISTINCT CASE WHEN iss.Severity = 'Informativo' THEN iss.AuditIssueId END)                 AS TotalIssuesSeveridadeInformativo,
    SUM(CASE WHEN sr.Result IN ('Aprovada', 'AprovadaComAlertas') THEN 1 ELSE 0 END)                 AS EtapasAprovadas,
    SUM(CASE WHEN sr.Result LIKE 'Reprovada%' THEN 1 ELSE 0 END)                                     AS EtapasReprovadas,
    AVG(sr.CompliancePercent)                                                                        AS PercentualGeralConformidade
FROM audit.AuditProjectRun pr
INNER JOIN audit.AuditRun run ON run.AuditRunId = pr.AuditRunId
LEFT JOIN audit.AuditStageResult sr ON sr.AuditProjectRunId = pr.AuditProjectRunId
LEFT JOIN IssuesDaExecucao iss ON iss.AuditProjectRunId = pr.AuditProjectRunId
GROUP BY
    pr.AuditProjectRunId, run.ExecutionId, pr.ProjectIdAcc;
GO

-- -----------------------------------------------------------------------------
-- 3.5 audit.VW_AuditRequirementsCompliance_BI — uma linha por
--     audit.AuditRequirementResult. Mapeamento de vocabulário (nenhuma coluna
--     inventada, apenas renomeada para o termo pedido no contrato):
--       "RegraId"    -> AuditRequirementCatalog.RequirementCode (identifica QUAL requisito)
--       "requisito"  -> AuditRequirementCatalog.Description (o requisito em si, por extenso)
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW audit.VW_AuditRequirementsCompliance_BI
AS
SELECT
    rr.AuditRequirementResultId,
    run.ExecutionId,
    pr.ProjectIdAcc,
    rr.StageNumber                                          AS Etapa,
    cat.RequirementCode                                      AS RegraId,
    cat.Description                                          AS Requisito,
    rr.ObjectEvaluated                                        AS ObjetoAvaliado,
    rr.Status,
    rr.ValueExpected                                          AS ValorEsperado,
    rr.ValueFound                                             AS ValorEncontrado,
    rr.EvaluatedAtUtc                                         AS DataAvaliacao
FROM audit.AuditRequirementResult rr
INNER JOIN audit.AuditRequirementCatalog cat ON cat.AuditRequirementCatalogId = rr.AuditRequirementCatalogId
INNER JOIN audit.AuditProjectRun pr ON pr.AuditProjectRunId = rr.AuditProjectRunId
INNER JOIN audit.AuditRun run ON run.AuditRunId = pr.AuditRunId;
GO
