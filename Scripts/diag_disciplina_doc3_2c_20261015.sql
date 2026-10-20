-- SOMENTE LEITURA (15/10/2026). Auditoria da regra de disciplina = 2 primeiros caracteres do DOC3.
-- Usa apenas tabelas temporárias (#canon, #ts, #ldp); nenhuma tabela do banco é alterada.
-- Rodar: sqlcmd -S .\SQLEXPRESS -d DB_NA_CONNECT -E -W -s "|" -i Scripts\diag_disciplina_doc3_2c_20261015.sql
SET NOCOUNT ON;
IF OBJECT_ID('tempdb..#canon') IS NOT NULL DROP TABLE #canon;
IF OBJECT_ID('tempdb..#ts') IS NOT NULL DROP TABLE #ts;
-- canônico global com prioridade Nome_Padrao > alias > PT/EN (mesma regra do CanonicalNameMap novo)
;WITH n AS (
  SELECT 1 nivel, UPPER(LTRIM(RTRIM(Nome_Padrao))) COLLATE Latin1_General_100_CI_AI k, Nome_Padrao nome FROM coordenacao.DisciplinaPadrao WHERE Ativa=1
  UNION ALL SELECT 2, UPPER(LTRIM(RTRIM(a.Alias))) COLLATE Latin1_General_100_CI_AI, p.Nome_Padrao FROM coordenacao.DisciplinaAlias a JOIN coordenacao.DisciplinaPadrao p ON p.DisciplinaPadraoId=a.DisciplinaPadraoId AND p.Ativa=1
  UNION ALL SELECT 3, UPPER(LTRIM(RTRIM(Nome_PT))) COLLATE Latin1_General_100_CI_AI, Nome_Padrao FROM coordenacao.DisciplinaPadrao WHERE Ativa=1 AND NULLIF(LTRIM(Nome_PT),'') IS NOT NULL
  UNION ALL SELECT 3, UPPER(LTRIM(RTRIM(Nome_EN))) COLLATE Latin1_General_100_CI_AI, Nome_Padrao FROM coordenacao.DisciplinaPadrao WHERE Ativa=1 AND NULLIF(LTRIM(Nome_EN),'') IS NOT NULL
), g AS (SELECT k, nivel, COUNT(DISTINCT nome) nn, MIN(nome) nome FROM n GROUP BY k, nivel),
 f AS (SELECT k, MIN(nivel) nivel FROM g GROUP BY k)
SELECT f.k, CASE WHEN g.nn=1 THEN g.nome END nome INTO #canon FROM f JOIN g ON g.k=f.k AND g.nivel=f.nivel;
-- Timesheet corrigido com DOC3 do código corrigido
SELECT v.Id, v.Project_ID_ACC, v.HorasTrabalhadas HH, v.CustoCalculado Custo, v.Tipo_Correcao, v.Disciplina DiscOrigem,
       v.DocumentoCodigo_Corrigido Cod, pn.NormaId,
       CAST(NULL AS nvarchar(50)) Doc3, CAST(NULL AS nvarchar(300)) Antiga, CAST(NULL AS nvarchar(300)) Nova, CAST(NULL AS nvarchar(300)) NovaNorma
INTO #ts
FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial v
LEFT JOIN (SELECT pn.Project_ID_ACC, pn.NormaId FROM coordenacao.ProjetoNorma pn JOIN coordenacao.Norma n ON n.NormaId=pn.NormaId AND n.Ativa=1) pn ON pn.Project_ID_ACC=v.Project_ID_ACC;
UPDATE #ts SET Cod = NULLIF(LTRIM(RTRIM(Cod)),'');
UPDATE t SET Cod = NULLIF(CONCAT_WS('-',NULLIF(LTRIM(RTRIM(v.Doc1)),''),NULLIF(LTRIM(RTRIM(v.Doc2)),''),NULLIF(LTRIM(RTRIM(v.Doc3)),''),NULLIF(LTRIM(RTRIM(v.Doc4)),''),NULLIF(LTRIM(RTRIM(v.Doc5)),'')),'')
FROM #ts t JOIN gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial v ON v.Id=t.Id WHERE t.Cod IS NULL;
UPDATE #ts SET Doc3 = UPPER(LTRIM(RTRIM(
   CASE WHEN CHARINDEX('-',Cod)>0 AND CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)>0 THEN
     SUBSTRING(Cod, CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)+1,
        CASE WHEN CHARINDEX('-',Cod,CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)+1)>0
             THEN CHARINDEX('-',Cod,CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)+1) - CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1) - 1
             ELSE 400 END) END)))
WHERE ISNULL(Tipo_Correcao,'')<>'AT_MANUAL';
UPDATE #ts SET Doc3=NULL WHERE Doc3='';
-- ANTIGA: match exato, senão maior prefixo cadastrado
UPDATE t SET Antiga = (SELECT TOP 1 m.Disciplina FROM coordenacao.NormaMapeamento m WHERE m.NormaId=t.NormaId AND t.Doc3 LIKE m.Sigla+'%' ORDER BY LEN(m.Sigla) DESC)
FROM #ts t WHERE t.Doc3 IS NOT NULL;
-- NOVA: sigla = 2 primeiros caracteres; senão consenso das siglas com o mesmo prefixo
UPDATE t SET NovaNorma = COALESCE(
   (SELECT m.Disciplina FROM coordenacao.NormaMapeamento m WHERE m.NormaId=t.NormaId AND m.Sigla=LEFT(t.Doc3,2)),
   (SELECT MIN(LTRIM(RTRIM(m.Disciplina))) FROM coordenacao.NormaMapeamento m WHERE m.NormaId=t.NormaId AND m.Sigla LIKE LEFT(t.Doc3,2)+'%'
      HAVING COUNT(DISTINCT UPPER(LTRIM(RTRIM(m.Disciplina))))=1))
FROM #ts t WHERE LEN(t.Doc3)>=2;
-- canônico (antiga usava mapa SEM prioridade: ambíguo => SEM DISCIPLINA)
UPDATE t SET Nova = c.nome FROM #ts t JOIN #canon c ON c.k = UPPER(LTRIM(RTRIM(t.NovaNorma))) COLLATE Latin1_General_100_CI_AI;
UPDATE t SET Antiga = CASE WHEN x.nn=1 THEN x.nome END FROM #ts t OUTER APPLY (
  SELECT COUNT(DISTINCT nome) nn, MIN(nome) nome FROM (
    SELECT Nome_Padrao nome, UPPER(LTRIM(RTRIM(Nome_Padrao))) k FROM coordenacao.DisciplinaPadrao WHERE Ativa=1
    UNION ALL SELECT Nome_Padrao, UPPER(LTRIM(RTRIM(Nome_PT))) FROM coordenacao.DisciplinaPadrao WHERE Ativa=1
    UNION ALL SELECT Nome_Padrao, UPPER(LTRIM(RTRIM(Nome_EN))) FROM coordenacao.DisciplinaPadrao WHERE Ativa=1
    UNION ALL SELECT p.Nome_Padrao, UPPER(LTRIM(RTRIM(a.Alias))) FROM coordenacao.DisciplinaAlias a JOIN coordenacao.DisciplinaPadrao p ON p.DisciplinaPadraoId=a.DisciplinaPadraoId AND p.Ativa=1) z
  WHERE z.k COLLATE Latin1_General_100_CI_AI = UPPER(LTRIM(RTRIM(t.Antiga))) COLLATE Latin1_General_100_CI_AI) x WHERE t.Antiga IS NOT NULL;
-- AT manual: disciplina informada
UPDATE #ts SET Antiga = DiscOrigem, Nova = DiscOrigem WHERE Tipo_Correcao='AT_MANUAL' AND NULLIF(LTRIM(DiscOrigem),'') IS NOT NULL;
UPDATE #ts SET Antiga = ISNULL(Antiga,'SEM DISCIPLINA'), Nova = ISNULL(Nova,'SEM DISCIPLINA');
PRINT '=== Totais por projeto: antes x depois (HH/custo nunca mudam)';
SELECT Project_ID_ACC, CAST(SUM(HH) AS decimal(18,2)) HH, CAST(SUM(Custo) AS decimal(18,2)) Custo,
  CAST(SUM(CASE WHEN Antiga='SEM DISCIPLINA' THEN HH END) AS decimal(18,2)) HH_SemDisc_Antes,
  CAST(SUM(CASE WHEN Nova='SEM DISCIPLINA' THEN HH END) AS decimal(18,2)) HH_SemDisc_Depois,
  CAST(SUM(CASE WHEN Antiga<>Nova THEN HH END) AS decimal(18,2)) HH_MudouDisciplina
FROM #ts GROUP BY Project_ID_ACC ORDER BY 1;
PRINT '=== Movimentos antes -> depois (todos os projetos)';
SELECT Project_ID_ACC, Antiga, Nova, LEFT(Doc3,2) Chave, COUNT(*) Lanc, CAST(SUM(HH) AS decimal(18,2)) HH, CAST(SUM(Custo) AS decimal(18,2)) Custo
FROM #ts WHERE Antiga<>Nova GROUP BY Project_ID_ACC, Antiga, Nova, LEFT(Doc3,2) ORDER BY 1, HH DESC;
PRINT '=== BML011.013 por disciplina (depois)';
SELECT Nova, COUNT(*) Lanc, CAST(SUM(HH) AS decimal(18,2)) HH, CAST(SUM(Custo) AS decimal(18,2)) Custo FROM #ts WHERE Project_ID_ACC='b.c3e20d9d-779e-423c-9381-abec38cf11cb' GROUP BY Nova ORDER BY 1;
PRINT '=== BML011.013 DOC3 3D0 por disciplina de origem e Tipo_Correcao';
SELECT DiscOrigem, ISNULL(Tipo_Correcao,'(nulo)') Tipo_Correcao, Nova, COUNT(*) Lanc, CAST(SUM(HH) AS decimal(18,2)) HH FROM #ts WHERE Project_ID_ACC='b.c3e20d9d-779e-423c-9381-abec38cf11cb' AND Doc3='3D0' GROUP BY DiscOrigem, Tipo_Correcao, Nova;
PRINT '=== Itens que continuam SEM DISCIPLINA (regra nova), agrupados por código';
SELECT t.Project_ID_ACC, p.Nome_Projeto, ISNULL(CAST(t.NormaId AS varchar),'SEM NORMA') Norma, ISNULL(t.Cod,'(sem documento)') Cod, ISNULL(t.Doc3,'-') Doc3,
  ISNULL(t.Tipo_Correcao,'(nulo)') Tipo, ISNULL(t.DiscOrigem,'(nula)') DiscOrigem, COUNT(*) Lanc, CAST(SUM(t.HH) AS decimal(18,2)) HH, CAST(SUM(t.Custo) AS decimal(18,2)) Custo
FROM #ts t LEFT JOIN dbo.Projetos p ON p.Project_ID_ACC=t.Project_ID_ACC
WHERE t.Nova='SEM DISCIPLINA' GROUP BY t.Project_ID_ACC, p.Nome_Projeto, t.NormaId, t.Cod, t.Doc3, t.Tipo_Correcao, t.DiscOrigem ORDER BY 1, HH DESC;
DECLARE @projectId nvarchar(100)='b.c3e20d9d-779e-423c-9381-abec38cf11cb';
IF OBJECT_ID('tempdb..#ldp') IS NOT NULL DROP TABLE #ldp;
SELECT L.NA_Document_Number Cod, L.Discipline, CAST(L.Planned_HH AS decimal(18,6)) HH, CAST(L.Planned_Cost AS decimal(18,6)) Custo, L.Doc_Type_Code,
  CAST(NULL AS nvarchar(50)) Doc3, CAST(NULL AS nvarchar(300)) Nova, CAST(NULL AS nvarchar(300)) Antiga
INTO #ldp FROM dbo.Ldp_Unificada L
WHERE L.Project_ID_ACC=@projectId AND L.Data_Sincronizacao >= ISNULL((SELECT MAX(G.Data_Sincronizacao) FROM (SELECT X.Data_Sincronizacao,
   DATEDIFF(SECOND, LAG(X.Data_Sincronizacao) OVER (ORDER BY X.Data_Sincronizacao, X.ID_Registro), X.Data_Sincronizacao) AS Gap
   FROM dbo.Ldp_Unificada X WHERE X.Project_ID_ACC=@projectId) G WHERE G.Gap>30), CAST('19000101' AS DATETIME))
 AND NOT (LTRIM(RTRIM(ISNULL(L.NA_Document_Number,'')))='' AND LTRIM(RTRIM(ISNULL(L.Client_Document_Number,'')))='' AND LTRIM(RTRIM(ISNULL(L.Document_Title,'')))='');
UPDATE #ldp SET Doc3 = UPPER(LTRIM(RTRIM(CASE WHEN CHARINDEX('-',Cod)>0 AND CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)>0 THEN
     SUBSTRING(Cod, CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)+1,
        CASE WHEN CHARINDEX('-',Cod,CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)+1)>0
             THEN CHARINDEX('-',Cod,CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1)+1) - CHARINDEX('-',Cod,CHARINDEX('-',Cod)+1) - 1 ELSE 400 END) END)));
UPDATE l SET Nova=c.nome FROM #ldp l JOIN coordenacao.NormaMapeamento m ON m.NormaId=2 AND m.Sigla=LEFT(l.Doc3,2) JOIN #canon c ON c.k=UPPER(LTRIM(RTRIM(m.Disciplina))) COLLATE Latin1_General_100_CI_AI;
UPDATE l SET Antiga=COALESCE(p.Nome_Padrao, UPPER(LTRIM(RTRIM(l.Discipline)))) FROM #ldp l
 LEFT JOIN coordenacao.DisciplinaAlias a ON UPPER(LTRIM(RTRIM(a.Alias))) COLLATE Latin1_General_100_CI_AI = UPPER(LTRIM(RTRIM(l.Discipline))) COLLATE Latin1_General_100_CI_AI
 LEFT JOIN coordenacao.DisciplinaPadrao p ON p.DisciplinaPadraoId=a.DisciplinaPadraoId AND p.Ativa=1;
PRINT '=== LDP BML011.013: total';
SELECT COUNT(*) Linhas, CAST(SUM(HH) AS decimal(18,2)) HH, CAST(SUM(Custo) AS decimal(18,2)) Custo, SUM(CASE WHEN Nova IS NULL THEN 1 ELSE 0 END) SemDoc3Resolvido FROM #ldp;
PRINT '=== LDP: previsto por disciplina — antes (texto Discipline) x depois (DOC3 2 caracteres; fallback texto)';
SELECT ISNULL(Nova,Antiga) Disciplina, CAST(SUM(HH) AS decimal(18,2)) HH_Depois, CAST(SUM(Custo) AS decimal(18,2)) Custo_Depois FROM #ldp GROUP BY ISNULL(Nova,Antiga) ORDER BY 1;
SELECT Antiga Disciplina_Antes, CAST(SUM(HH) AS decimal(18,2)) HH_Antes FROM #ldp GROUP BY Antiga ORDER BY 1;
PRINT '=== LDP: linhas cuja disciplina muda (texto LDP <> DOC3)';
SELECT Cod, Discipline LDP_Texto, Antiga, Nova, Doc_Type_Code, CAST(HH AS decimal(18,2)) HH FROM #ldp WHERE Nova IS NOT NULL AND Nova<>Antiga ORDER BY HH DESC;
PRINT '=== LDP: linhas sem DOC3 resolvido';
SELECT Cod, Discipline, Doc3, CAST(HH AS decimal(18,2)) HH FROM #ldp WHERE Nova IS NULL;
PRINT '=== DOC3 3D/CI no previsto';
SELECT Cod, Discipline, Doc3, Nova, CAST(HH AS decimal(18,2)) HH FROM #ldp WHERE LEFT(Doc3,2) IN ('3D','CI') ORDER BY Doc3;
