SET NOCOUNT ON;
PRINT '=== 447.08: PROJETO, CODIGO, CR/OS ===';
SELECT TOP(35) ISNULL(LEFT(DocumentoCodigo,40),'(SEM CODIGO)') AS Codigo,
ISNULL(CR_OS_Canonico,'(SEM CR OS)') AS CR_OS,ISNULL(Projeto,'(SEM PROJETO)') AS Projeto,
COUNT(*) AS Lancamentos, SUM(Trabalhadas) AS HH
FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto
WHERE Nome LIKE '%EVERTON%' AND Project_ID_ACC_Canonico='b.c3e20d9d-779e-423c-9381-abec38cf11cb'
GROUP BY DocumentoCodigo,CR_OS_Canonico,Projeto ORDER BY HH DESC;
PRINT '=== 016: CR/OS ===';
SELECT ISNULL(CR_OS_Canonico,'(SEM CR OS)') AS CR_OS,COUNT(*) AS Lancamentos,SUM(Trabalhadas) AS HH
FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto
WHERE Nome LIKE '%EVERTON%' AND Project_ID_ACC_Canonico='b.9907135f-ff67-43bb-887b-20c3a7f75807'
GROUP BY CR_OS_Canonico;
