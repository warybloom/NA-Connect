-- Exceções pontuais de posição da sigla de disciplina — DOWN (rollback, idempotente). Aplicação MANUAL.
-- Remove SOMENTE as 2 tabelas criadas pelo UP 20261026_002. Não toca nas tabelas do 20261019_001 nem em tabela legada.
-- Proteção: se houver exceção ou auditoria gravada, o rollback é RECUSADO até que @ConfirmoExportacao = 1
-- (exporte antes: SELECT * FROM normalizador.ExcecaoPosicaoDisciplina / ExcecaoPosicaoDisciplinaAuditoria).
-- Para só reverter o efeito, prefira DESATIVAR as exceções pela tela. Sem as tabelas, todos os projetos voltam ao DOC3.
USE [DB_NA_CONNECT];
GO
SET XACT_ABORT ON;
DECLARE @ConfirmoExportacao bit = 0;

IF @ConfirmoExportacao = 0 AND (
       (OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplina', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM normalizador.ExcecaoPosicaoDisciplina))
    OR (OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplinaAuditoria', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM normalizador.ExcecaoPosicaoDisciplinaAuditoria)))
BEGIN
    RAISERROR(N'Rollback recusado: há exceções/auditoria de posição da disciplina. Exporte-as e defina @ConfirmoExportacao = 1.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;
IF OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplinaAuditoria', N'U') IS NOT NULL DROP TABLE normalizador.ExcecaoPosicaoDisciplinaAuditoria;
IF OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplina', N'U') IS NOT NULL DROP TABLE normalizador.ExcecaoPosicaoDisciplina;
COMMIT TRANSACTION;
GO
