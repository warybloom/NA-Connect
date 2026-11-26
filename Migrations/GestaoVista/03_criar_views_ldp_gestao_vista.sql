/*
============================================================
OBJETIVO
============================================================
Criar 2 views semânticas em [gestao], baseadas 100% em dbo.VW_Ldp_Unificada (fonte já comprovada e em
uso na ETAPA A por Produção/Documentos, Mapa de Calor e LDP) — SEM depender de nenhuma fonte não
confirmada. Espelham exatamente as fórmulas reais encontradas no ForgeSync (GetGestaoAVista), documentadas
em AUDITORIA_FORGESYNC_NA/MATRIZ_GESTAO_A_VISTA.md — nenhuma fórmula nova foi inventada aqui.

Estas views NÃO são conectadas a nenhum endpoint/frontend nesta sessão (instrução explícita: só migrar/
projetar, nunca ligar indicador novo antes do usuário executar). Ficam prontas para uso futuro.

============================================================
DEPENDÊNCIAS
============================================================
01_criar_schema_gestao.sql. Lê de dbo.VW_Ldp_Unificada (leitura apenas — SELECT, nunca escreve nela).

============================================================
PRÉ-CHECK + CREATE (mesmo lote, via SQL dinâmico — CREATE OR ALTER VIEW precisa ser o único statement
do seu lote, por isso vai dentro de EXEC(); nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    RAISERROR('Schema [gestao] não existe — execute 01_criar_schema_gestao.sql primeiro.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.views WHERE name = 'VW_Ldp_Unificada')
BEGIN
    RAISERROR('dbo.VW_Ldp_Unificada não existe neste banco — as views desta migration dependem dela. Abortando.', 16, 1);
END
ELSE
BEGIN
    -- Cards de documentos (Total/Emitidos/Atrasados/NoPrazo/NãoIniciados) — fórmula real do ForgeSync
    -- (GetGestaoAVista), 1:1.
    EXEC('
        CREATE OR ALTER VIEW gestao.vw_GestaoAVista_CardsDocumentos AS
        SELECT
            Project_ID_ACC,
            COUNT(*) AS TotalDocumentos,
            SUM(CASE WHEN ISNULL(Percent_Physical_Completed, 0) >= 100 THEN 1 ELSE 0 END) AS Emitidos,
            SUM(CASE WHEN Planned_Date_Baseline < CAST(GETDATE() AS DATE) AND ISNULL(Percent_Physical_Completed, 0) < 100 THEN 1 ELSE 0 END) AS Atrasados,
            SUM(CASE WHEN Planned_Date_Baseline >= CAST(GETDATE() AS DATE) AND ISNULL(Percent_Physical_Completed, 0) < 100 THEN 1 ELSE 0 END) AS NoPrazo,
            SUM(CASE WHEN ISNULL(Percent_Physical_Completed, 0) = 0 THEN 1 ELSE 0 END) AS NaoIniciados,
            SUM(ISNULL(Planned_HH, 0)) AS HH_Previsto,
            SUM(ISNULL(HH_TimeSheet, 0)) AS HH_Realizado
        FROM dbo.VW_Ldp_Unificada
        GROUP BY Project_ID_ACC;
    ');

    -- Foco da Semana (programação semanal) — fórmula real do ForgeSync (GetGestaoAVista), 1:1 (sem o
    -- TOP 15/filtro de 7 dias — aplicado pelo consumidor, não fixado dentro da view persistida).
    -- Consumidor aplica: WHERE Project_ID_ACC=@p AND DataAlvo<=DATEADD(DAY,7,GETDATE())
    --                    ORDER BY DataAlvo ASC OFFSET 0 ROWS FETCH NEXT 15 ROWS ONLY;
    EXEC('
        CREATE OR ALTER VIEW gestao.vw_GestaoAVista_FocoDocumentos AS
        SELECT
            Project_ID_ACC,
            NA_Document_Number AS Codigo,
            Document_Title AS Titulo,
            Discipline AS Disciplina,
            Planned_Date_Baseline AS DataAlvo,
            CAST(ISNULL(Percent_Physical_Completed, 0) AS INT) AS AvancoFisico,
            CASE WHEN Planned_Date_Baseline < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END AS IsAtrasado
        FROM dbo.VW_Ldp_Unificada
        WHERE ISNULL(Percent_Physical_Completed, 0) < 100
          AND Planned_Date_Baseline IS NOT NULL;
    ');

    PRINT 'Views gestao.vw_GestaoAVista_CardsDocumentos e gestao.vw_GestaoAVista_FocoDocumentos criadas/atualizadas.';
END
GO

/*
============================================================
ÍNDICES
============================================================
Não aplicável — são views sobre uma view existente (sem materialização física nesta migration).

============================================================
VALIDAÇÃO
============================================================
SELECT * FROM gestao.vw_GestaoAVista_CardsDocumentos WHERE Project_ID_ACC = 'b.<guid-de-teste>';
SELECT TOP 15 * FROM gestao.vw_GestaoAVista_FocoDocumentos
  WHERE Project_ID_ACC = 'b.<guid-de-teste>' AND DataAlvo <= DATEADD(DAY, 7, GETDATE())
  ORDER BY DataAlvo ASC;

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP VIEW IF EXISTS gestao.vw_GestaoAVista_FocoDocumentos;
-- DROP VIEW IF EXISTS gestao.vw_GestaoAVista_CardsDocumentos;
*/
