SET NOCOUNT ON;
DECLARE @p nvarchar(100)=N'b.9907135f-ff67-43bb-887b-20c3a7f75807';
PRINT '== LDP ULTIMA VERSAO POR DISCIPLINA ==';
SELECT ISNULL(Disciplina_PT_Padrao,N'(sem padrao)') AS DisciplinaPT,ISNULL(Discipline,N'(sem discipline)') AS Discipline, COUNT(*) AS Linhas,
COUNT(DISTINCT NA_Document_Number) AS Documentos,
SUM(Planned_HH) AS HH,SUM(Planned_Cost) AS Custo
FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@p
GROUP BY Disciplina_PT_Padrao,Discipline ORDER BY HH DESC;
PRINT '== DOC3 POR HH E CUSTO ==';
SELECT doc3,COUNT(*) AS Linhas,SUM(Planned_HH) AS HH,SUM(Planned_Cost) AS Custo
FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@p
GROUP BY doc3 ORDER BY HH DESC;
