SET NOCOUNT ON;
DECLARE @p nvarchar(255)=N'b.9907135f-ff67-43bb-887b-20c3a7f75807';
PRINT '=== Vinculo do BML011.016 ===';
SELECT pn.Project_ID_ACC,n.NormaId,n.Nome,n.Origem,n.Ativa,n.Data_Atualizacao,COUNT(m.NormaMapeamentoId) AS Siglas FROM coordenacao.ProjetoNorma pn
JOIN coordenacao.Norma n ON n.NormaId=pn.NormaId LEFT JOIN coordenacao.NormaMapeamento m ON m.NormaId=n.NormaId
WHERE pn.Project_ID_ACC=@p GROUP BY pn.Project_ID_ACC,n.NormaId,n.Nome,n.Origem,n.Ativa,n.Data_Atualizacao;
PRINT '=== Norma BML BR mapeamentos ===';
SELECT n.Nome,m.Sigla,m.Disciplina FROM coordenacao.Norma n JOIN coordenacao.NormaMapeamento m ON m.NormaId=n.NormaId WHERE n.Nome LIKE N'%BML%' ORDER BY n.Nome,m.Sigla;
PRINT '=== docs amostras / codigo por DOC3 ===';
SELECT TOP (30) SUBSTRING(NA_Document_Number,CHARINDEX('-',NA_Document_Number,CHARINDEX('-',NA_Document_Number)+1)+1,3) as DOC3, Discipline, COUNT(*) N
FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@p AND NA_Document_Number LIKE '%-%-%'
GROUP BY SUBSTRING(NA_Document_Number,CHARINDEX('-',NA_Document_Number,CHARINDEX('-',NA_Document_Number)+1)+1,3),Discipline ORDER BY N DESC;
