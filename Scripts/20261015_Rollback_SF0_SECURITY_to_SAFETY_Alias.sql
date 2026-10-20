-- ROLLBACK MANUAL do alias SECURITY -> SAFETY, executar somente se necessario.
-- Nao modifica Norma de Coordenacao nem lancamentos.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE FROM coordenacao.DisciplinaAlias
 WHERE DisciplinaPadraoId=26 AND Alias=N'SECURITY'
 AND EXISTS (SELECT 1 FROM coordenacao.DisciplinaPadrao
             WHERE DisciplinaPadraoId=26 AND Nome_Padrao=N'SAFETY');
SELECT @@ROWCOUNT AS AliasesRemovidos;
-- Se aprovado explicitamente, substitua ROLLBACK por COMMIT:
ROLLBACK TRANSACTION;
