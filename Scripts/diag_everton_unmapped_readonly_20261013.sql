SET NOCOUNT ON;
PRINT '== EVERTON SEM PROJETO CANONICO - CR/OS,PROJETO,DOC3 ==';
SELECT TOP(30) ISNULL(Projeto,N'(sem projeto)') AS Projeto,ISNULL(CR_OS_Origem,N'(sem CR)') AS CR,
ISNULL(LEFT(DocumentoCodigo,12),N'(sem documento)') AS PrefDoc,ISNULL(Status_Mapeamento_CR_OS,N'(sem status)') AS Status,
COUNT(*) AS Linhas,SUM(Trabalhadas) AS HH
FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto
WHERE Nome LIKE '%EVERTON%' AND Project_ID_ACC_Canonico IS NULL
GROUP BY Projeto,CR_OS_Origem,LEFT(DocumentoCodigo,12),Status_Mapeamento_CR_OS ORDER BY HH DESC;
PRINT '== POSSIVEIS 016 SEM MATCH ==';
SELECT COUNT(*) AS Linhas,SUM(Trabalhadas) AS HH FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Nome LIKE '%EVERTON%' AND Project_ID_ACC_Canonico IS NULL AND (DocumentoCodigo LIKE 'BML011.016%' OR Projeto LIKE '%BML011.016%');
