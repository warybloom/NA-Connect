-- Normalizador de Siglas de Disciplina — UP (idempotente). Aplicação MANUAL, somente após backup verificado
-- (ver README.md). Este arquivo não é executado pelo aplicativo.
-- ADITIVO: cria APENAS o schema [normalizador] e 3 tabelas próprias. Não altera, não lê e não apaga tabela existente.
USE [DB_NA_CONNECT];
GO
SET XACT_ABORT ON;
GO
IF SCHEMA_ID(N'normalizador') IS NULL EXEC(N'CREATE SCHEMA normalizador AUTHORIZATION dbo;');
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'normalizador.Importacao', N'U') IS NULL
BEGIN
    CREATE TABLE normalizador.Importacao (
        Id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NormalizadorImportacao PRIMARY KEY,
        NomeArquivo    nvarchar(260) NOT NULL,
        HashSha256     char(64)      NOT NULL,
        Aba            nvarchar(128) NOT NULL,
        MapeamentoJson nvarchar(max) NOT NULL,
        Incluidas      int NOT NULL,
        Alteradas      int NOT NULL,
        Inalteradas    int NOT NULL,
        Rejeitadas     int NOT NULL,
        Duplicadas     int NOT NULL,
        RelatorioJson  nvarchar(max) NOT NULL,
        ImportadoEm    datetime2(0)  NOT NULL,
        ImportadoPor   nvarchar(320) NOT NULL,
        CONSTRAINT CK_NormalizadorImportacao_Mapeamento CHECK (ISJSON(MapeamentoJson) = 1),
        CONSTRAINT CK_NormalizadorImportacao_Relatorio CHECK (ISJSON(RelatorioJson) = 1),
        CONSTRAINT CK_NormalizadorImportacao_Contagens CHECK (Incluidas >= 0 AND Alteradas >= 0 AND Inalteradas >= 0 AND Rejeitadas >= 0 AND Duplicadas >= 0)
    );
END;

IF OBJECT_ID(N'normalizador.SiglaDisciplina', N'U') IS NULL
BEGIN
    CREATE TABLE normalizador.SiglaDisciplina (
        Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NormalizadorSiglaDisciplina PRIMARY KEY,
        SiglaOriginal      nvarchar(50)  NOT NULL,   -- como informada (espaços colapsados, Unicode NFC)
        SiglaChave         nvarchar(50)  NOT NULL,   -- comparação: maiúsculas, espaços colapsados (gerada pela aplicação)
        DisciplinaCanonica nvarchar(200) NOT NULL,   -- nome canônico PT-BR, Unicode preservado
        ContextoTipo       varchar(10)   NULL,       -- NULL = global | PROJETO | FONTE
        ContextoValor      nvarchar(200) NULL,
        ContextoChave      nvarchar(220) NOT NULL CONSTRAINT DF_NormalizadorSiglaDisciplina_ContextoChave DEFAULT (N''),
        Ativo              bit           NOT NULL,
        Origem             varchar(12)   NOT NULL,
        ImportacaoId       bigint        NULL CONSTRAINT FK_NormalizadorSiglaDisciplina_Importacao REFERENCES normalizador.Importacao (Id),
        Versao             int           NOT NULL CONSTRAINT DF_NormalizadorSiglaDisciplina_Versao DEFAULT (1),
        CriadoEm           datetime2(0)  NOT NULL,
        CriadoPor          nvarchar(320) NOT NULL,
        AlteradoEm         datetime2(0)  NOT NULL,
        AlteradoPor        nvarchar(320) NOT NULL,
        -- Uma regra por sigla + contexto: conflito nunca é resolvido em silêncio no banco.
        CONSTRAINT UQ_NormalizadorSiglaDisciplina_Chave UNIQUE (SiglaChave, ContextoChave),
        CONSTRAINT CK_NormalizadorSiglaDisciplina_ContextoTipo CHECK (ContextoTipo IS NULL OR ContextoTipo IN ('PROJETO', 'FONTE')),
        CONSTRAINT CK_NormalizadorSiglaDisciplina_Contexto CHECK (
            (ContextoTipo IS NULL AND ContextoValor IS NULL AND ContextoChave = N'')
            OR (ContextoTipo IS NOT NULL AND ContextoValor IS NOT NULL AND LEN(ContextoValor) > 0 AND ContextoChave <> N'')),
        CONSTRAINT CK_NormalizadorSiglaDisciplina_Textos CHECK (LEN(SiglaChave) > 0 AND LEN(DisciplinaCanonica) > 0),
        CONSTRAINT CK_NormalizadorSiglaDisciplina_Origem CHECK (Origem IN ('MANUAL', 'IMPORTACAO')),
        CONSTRAINT CK_NormalizadorSiglaDisciplina_Versao CHECK (Versao > 0)
    );
END;

IF OBJECT_ID(N'normalizador.SiglaDisciplinaAuditoria', N'U') IS NULL
BEGIN
    -- Sem FK para a regra: o histórico sobrevive à exclusão (AntesJson guarda o retrato completo).
    CREATE TABLE normalizador.SiglaDisciplinaAuditoria (
        Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NormalizadorSiglaDisciplinaAuditoria PRIMARY KEY,
        RegraId      bigint        NOT NULL,
        Acao         varchar(12)   NOT NULL,
        AntesJson    nvarchar(max) NULL,
        DepoisJson   nvarchar(max) NULL,
        ImportacaoId bigint        NULL CONSTRAINT FK_NormalizadorAuditoria_Importacao REFERENCES normalizador.Importacao (Id),
        Usuario      nvarchar(320) NOT NULL,
        DataHora     datetime2(0)  NOT NULL,
        CONSTRAINT CK_NormalizadorAuditoria_Acao CHECK (Acao IN ('INCLUIR', 'ALTERAR', 'ATIVAR', 'DESATIVAR', 'EXCLUIR')),
        CONSTRAINT CK_NormalizadorAuditoria_Json CHECK ((AntesJson IS NULL OR ISJSON(AntesJson) = 1) AND (DepoisJson IS NULL OR ISJSON(DepoisJson) = 1))
    );
    CREATE INDEX IX_NormalizadorAuditoria_Regra ON normalizador.SiglaDisciplinaAuditoria (RegraId, DataHora);
END;

-- Nenhum seed: nenhuma sigla é cadastrada por script (as regras vêm da planilha oficial ou da tela).
COMMIT TRANSACTION;
GO
