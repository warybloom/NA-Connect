-- Normalizador de Siglas de Disciplina — DOWN (rollback, idempotente). Aplicação MANUAL.
-- Remove SOMENTE os objetos criados pelo UP (schema [normalizador]). Nenhuma tabela legada é tocada.
-- Proteção: se houver regras, auditoria ou importações gravadas, o rollback é RECUSADO até que @ConfirmoExportacao = 1
-- (exporte antes: SELECT * FROM normalizador.SiglaDisciplina / SiglaDisciplinaAuditoria / Importacao, ou use o backup do README).
USE [DB_NA_CONNECT];
GO
SET XACT_ABORT ON;
DECLARE @ConfirmoExportacao bit = 0;

IF @ConfirmoExportacao = 0 AND (
       (OBJECT_ID(N'normalizador.SiglaDisciplina', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM normalizador.SiglaDisciplina))
    OR (OBJECT_ID(N'normalizador.SiglaDisciplinaAuditoria', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM normalizador.SiglaDisciplinaAuditoria))
    OR (OBJECT_ID(N'normalizador.Importacao', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM normalizador.Importacao)))
BEGIN
    RAISERROR(N'Rollback recusado: o normalizador possui dados. Exporte-os e defina @ConfirmoExportacao = 1.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;
IF OBJECT_ID(N'normalizador.SiglaDisciplinaAuditoria', N'U') IS NOT NULL DROP TABLE normalizador.SiglaDisciplinaAuditoria;
IF OBJECT_ID(N'normalizador.SiglaDisciplina', N'U') IS NOT NULL DROP TABLE normalizador.SiglaDisciplina;
IF OBJECT_ID(N'normalizador.Importacao', N'U') IS NOT NULL DROP TABLE normalizador.Importacao;
COMMIT TRANSACTION;
GO
IF SCHEMA_ID(N'normalizador') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'normalizador'))
    EXEC(N'DROP SCHEMA normalizador;');
GO
