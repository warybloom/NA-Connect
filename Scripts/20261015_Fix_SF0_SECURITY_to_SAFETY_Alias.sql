-- N&A Connect | SF0 BML011.013 | 2026-10-15
-- Motivo: Norma 4 contem SF -> SECURITY, mas cadastro global nao tem alias SECURITY.
-- Alteracao restrita: adicionar alias SECURITY ao padrao 26 SAFETY; nenhuma Norma/HH/custo alterados.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 IF NOT EXISTS (SELECT 1 FROM coordenacao.DisciplinaPadrao WITH (UPDLOCK, HOLDLOCK)
                WHERE DisciplinaPadraoId=26 AND Nome_Padrao=N'SAFETY' AND Ativa=1)
    THROW 51001, 'Padrao SAFETY ID 26 ausente, diferente ou inativo', 1;
 IF NOT EXISTS (SELECT 1 FROM coordenacao.ProjetoNorma pn JOIN coordenacao.NormaMapeamento m ON m.NormaId=pn.NormaId
                WHERE pn.Project_ID_ACC=N'b.c3e20d9d-779e-423c-9381-abec38cf11cb'
                AND m.Sigla=N'SF' AND m.Disciplina=N'SECURITY')
    THROW 51002, 'Mapeamento SF -> SECURITY alterado desde auditoria; abortando', 1;
 IF EXISTS (SELECT 1 FROM coordenacao.DisciplinaAlias WITH (UPDLOCK,HOLDLOCK)
            WHERE Alias COLLATE Latin1_General_100_CI_AI=N'SECURITY')
 BEGIN
   IF NOT EXISTS (SELECT 1 FROM coordenacao.DisciplinaAlias WHERE DisciplinaPadraoId=26 AND Alias COLLATE Latin1_General_100_CI_AI=N'SECURITY')
     THROW 51003, 'Alias SECURITY existe associado a outro padrao; abortando', 1;
   PRINT 'NOOP: SECURITY ja associado a SAFETY; nenhuma alteracao.';
 END
 ELSE
 BEGIN
   INSERT INTO coordenacao.DisciplinaAlias(DisciplinaPadraoId,Alias)
   VALUES(26,N'SECURITY');
   PRINT 'OK: SECURITY cadastrado como alias de SAFETY ID 26.';
 END;
 SELECT a.DisciplinaAliasId,a.DisciplinaPadraoId,a.Alias,d.Nome_Padrao
 FROM coordenacao.DisciplinaAlias a JOIN coordenacao.DisciplinaPadrao d ON d.DisciplinaPadraoId=a.DisciplinaPadraoId
 WHERE a.DisciplinaPadraoId=26 AND a.Alias=N'SECURITY';
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
