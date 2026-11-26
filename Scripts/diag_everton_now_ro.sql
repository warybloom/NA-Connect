SET NOCOUNT ON;
SELECT GETDATE() AS AGORA,COUNT_BIG(*) AS TOTAL, SUM(CASE WHEN Nome LIKE '%EVERTON%' THEN 1 ELSE 0 END) AS EVERTON FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto;
SELECT TOP(12) Project_ID_ACC_Canonico,COUNT(*) AS Linhas,SUM(Trabalhadas) AS HH FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Nome LIKE '%EVERTON%' GROUP BY Project_ID_ACC_Canonico ORDER BY HH DESC;
SELECT Id,Project_ID_ACC,NomeReferencia,AliasTimesheet FROM costs.Package WHERE Id=2;
