-- Exceções pontuais de posição da sigla de disciplina — UP (idempotente). Aplicação MANUAL, somente após backup verificado
-- (ver README.md). Este arquivo não é executado pelo aplicativo.
-- ADITIVO: cria APENAS 2 tabelas no schema [normalizador] (já existente desde 20261019_001). Não altera, não lê e não
-- apaga tabela existente: LDP, Timesheet, Norma de Coordenação, projetos e números de documento ficam intocados.
-- Sem exceção ativa para o projeto = DOC3, exatamente o comportamento anterior. NÃO existe configuração "DOC4 do projeto":
-- cada exceção vale só para um número de documento exato (CODIGO) ou para um padrão limitado DOC3 + sigla exata (PADRAO).
USE [DB_NA_CONNECT];
GO
SET XACT_ABORT ON;
GO
IF SCHEMA_ID(N'normalizador') IS NULL EXEC(N'CREATE SCHEMA normalizador AUTHORIZATION dbo;');
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplina', N'U') IS NULL
BEGIN
    CREATE TABLE normalizador.ExcecaoPosicaoDisciplina (
        Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NormalizadorExcecaoPosicaoDisciplina PRIMARY KEY,
        Project_ID_ACC       nvarchar(255) NOT NULL,
        Criterio             varchar(10)   NOT NULL,   -- CODIGO | PADRAO
        CodigoDocumento      nvarchar(255) NULL,       -- CODIGO: número exato (maiúsculas, sem espaços nas pontas)
        CodigoChave          nvarchar(255) NOT NULL CONSTRAINT DF_NormalizadorExcecaoPosicao_CodigoChave DEFAULT (N''),
        SegmentoDoc3         nvarchar(50)  NULL,       -- PADRAO: valor exato que ocupa o DOC3 (ex.: DE)
        SegmentoChave        nvarchar(50)  NOT NULL CONSTRAINT DF_NormalizadorExcecaoPosicao_SegmentoChave DEFAULT (N''),
        SiglaEsperada        nvarchar(50)  NOT NULL,   -- sigla exata exigida na posição (ex.: TU3)
        SiglaChave           nvarchar(50)  NOT NULL,
        Posicao              tinyint       NOT NULL,   -- 2, 4 ou 5 (DOC3 é o padrão, nunca exceção)
        DisciplinaNoCadastro nvarchar(200) NOT NULL,   -- retrato da disciplina resolvida na criação (auditoria)
        Motivo               nvarchar(500) NULL,
        Ativo                bit           NOT NULL,
        Versao               int           NOT NULL CONSTRAINT DF_NormalizadorExcecaoPosicao_Versao DEFAULT (1),
        CriadoEm             datetime2(0)  NOT NULL,
        CriadoPor            nvarchar(320) NOT NULL,
        AlteradoEm           datetime2(0)  NOT NULL,
        AlteradoPor          nvarchar(320) NOT NULL,
        CONSTRAINT UQ_NormalizadorExcecaoPosicao_Chave UNIQUE (Project_ID_ACC, Criterio, CodigoChave, SegmentoChave, SiglaChave, Posicao),
        CONSTRAINT CK_NormalizadorExcecaoPosicao_Criterio CHECK (
            (Criterio = 'CODIGO' AND CodigoDocumento IS NOT NULL AND LEN(CodigoChave) > 0 AND SegmentoDoc3 IS NULL AND SegmentoChave = N'')
            OR (Criterio = 'PADRAO' AND SegmentoDoc3 IS NOT NULL AND LEN(SegmentoChave) > 0 AND CodigoDocumento IS NULL AND CodigoChave = N'')),
        CONSTRAINT CK_NormalizadorExcecaoPosicao_Posicao CHECK (Posicao IN (2, 4, 5)),
        CONSTRAINT CK_NormalizadorExcecaoPosicao_Textos CHECK (LEN(SiglaChave) > 0 AND LEN(DisciplinaNoCadastro) > 0),
        CONSTRAINT CK_NormalizadorExcecaoPosicao_Versao CHECK (Versao > 0)
    );
    CREATE INDEX IX_NormalizadorExcecaoPosicao_Projeto ON normalizador.ExcecaoPosicaoDisciplina (Project_ID_ACC, Ativo);
END;

IF OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplinaAuditoria', N'U') IS NULL
BEGIN
    -- Sem FK: o histórico é independente (AntesJson/DepoisJson guardam o retrato completo).
    CREATE TABLE normalizador.ExcecaoPosicaoDisciplinaAuditoria (
        Id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NormalizadorExcecaoPosicaoAuditoria PRIMARY KEY,
        ExcecaoId      bigint        NOT NULL,
        Project_ID_ACC nvarchar(255) NOT NULL,
        Acao           varchar(12)   NOT NULL,
        AntesJson      nvarchar(max) NULL,
        DepoisJson     nvarchar(max) NULL,
        Usuario        nvarchar(320) NOT NULL,
        DataHora       datetime2(0)  NOT NULL,
        CONSTRAINT CK_NormalizadorExcecaoPosicaoAuditoria_Acao CHECK (Acao IN ('INCLUIR', 'ATIVAR', 'DESATIVAR')),
        CONSTRAINT CK_NormalizadorExcecaoPosicaoAuditoria_Json CHECK (
            (AntesJson IS NULL OR ISJSON(AntesJson) = 1) AND (DepoisJson IS NULL OR ISJSON(DepoisJson) = 1))
    );
    CREATE INDEX IX_NormalizadorExcecaoPosicaoAuditoria_Projeto
        ON normalizador.ExcecaoPosicaoDisciplinaAuditoria (Project_ID_ACC, DataHora);
END;

-- Nenhum seed: as exceções da BYD002 são criadas pela tela, depois da prévia por documento.
COMMIT TRANSACTION;
GO
