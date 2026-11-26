SET NOCOUNT ON;
SELECT NormaId,Nome,Origem,Ativa,Data_Atualizacao FROM coordenacao.Norma WHERE Nome LIKE '%BML%';
PRINT '=== ID 5 UNIQUE NAMES ===';
SELECT Disciplina,COUNT(*) as Qtd,MIN(Sigla) AS SiglaExemplo FROM coordenacao.NormaMapeamento WHERE NormaId=5 GROUP BY Disciplina ORDER BY Disciplina;
PRINT '=== ID 5 specific ===';
SELECT Sigla,Disciplina FROM coordenacao.NormaMapeamento WHERE NormaId=5 AND Sigla IN ('TU','TU0','TU2','TU3','CI','CI4','ME','PR','PR0','PE','3D','3D0','DC','DC0','EM','ST','ES','EL') ORDER BY Sigla;
