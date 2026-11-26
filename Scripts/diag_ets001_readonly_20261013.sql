SET NOCOUNT ON;
PRINT '== ETS001 package ==';
SELECT Id,Project_ID_ACC,NomeReferencia,AliasTimesheet,HHBase,CustoBase,ModeloCadastro,Ativo FROM costs.Package WHERE NomeReferencia LIKE '%ETS001%';
PRINT '== Timesheet view columns ==';
SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE
 (TABLE_SCHEMA='gestao' AND TABLE_NAME IN ('VW_TimeSheet_Corrigido_Espelho_Gerencial','TimesheetSnapshotAtual'))
 OR (TABLE_SCHEMA='dbo' AND TABLE_NAME IN ('VW_TimeSheet_NA_Corrigida_Projeto','VW_Ldp_Unificada'))
 ORDER BY TABLE_SCHEMA,TABLE_NAME,ORDINAL_POSITION;
