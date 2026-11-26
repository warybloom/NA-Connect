SET NOCOUNT ON;
DECLARE @id nvarchar(100)=N'b.c3e20d9d-779e-423c-9381-abec38cf11cb';
SELECT TOP 18 Project_ID_ACC_Canonico,Nome,DocumentoCodigo,CR_OS_Canonico,Projeto,Trabalhadas FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Nome LIKE '%EVERTON%' AND Project_ID_ACC_Canonico=@id ORDER BY Trabalhadas DESC;
SELECT Project_ID_ACC_Canonico,CR_OS_Canonico,COUNT(*) AS N,SUM(Trabalhadas) AS HH FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto WHERE Nome LIKE '%EVERTON%' AND Project_ID_ACC_Canonico=@id GROUP BY Project_ID_ACC_Canonico,CR_OS_Canonico;
