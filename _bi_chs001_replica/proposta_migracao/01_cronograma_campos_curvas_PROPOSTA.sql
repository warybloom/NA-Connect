/* =====================================================================================================================
   PROPOSTA — NÃO APLICADA. Requer aprovação explícita do usuário antes de qualquer execução em banco de produção.
   Objetivo: persistir, na leitura oficial do .mpp (CronogramaTipo2), os campos que as curvas S / ACC / BASE SEM / matriz
   do BI CHS001 usam e que hoje o importador lê mas descarta.
   Evidência (15/10/2026, _bi_chs001_replica/STATUS_CORRECAO_TELAS_20261015.md): extrator MPXJ somente leitura sobre cópia do
   .mpp CHS001 → Início LB 711/711, Término LB 711/711, Trabalho 693/711 e trabalho de LB semanal 2315/2315 iguais ao PBIX.
   Regras: somente ADIÇÕES nullable; nenhum UPDATE/DELETE de linhas antigas; snapshots históricos ficam NULL (fonte
   indisponível — sem retropreenchimento); a view atual usa C.* → sp_refreshview para expor as colunas novas.
   Rollback ao final (comentado).
   ===================================================================================================================== */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* 1) Campos por tarefa, mesmos nomes do padrão Tipo 2 (Termino / Termino_Linha_Base já existem).
      Não reaproveita PLANNED_DATE_START / PLANNED_DATA_BASELINE / REPLANNED_DATE (semântica do Tipo 1 não validada). */
IF COL_LENGTH('dbo.Cronograma_Projetos', 'Inicio') IS NULL
    ALTER TABLE dbo.Cronograma_Projetos ADD Inicio datetime2(7) NULL;                     -- MPXJ Task.getStart()
IF COL_LENGTH('dbo.Cronograma_Projetos', 'Inicio_Linha_Base') IS NULL
    ALTER TABLE dbo.Cronograma_Projetos ADD Inicio_Linha_Base datetime2(7) NULL;          -- Task.getBaselineStart()
IF COL_LENGTH('dbo.Cronograma_Projetos', 'Trabalho_Linha_Base_Horas') IS NULL
    ALTER TABLE dbo.Cronograma_Projetos ADD Trabalho_Linha_Base_Horas decimal(18, 6) NULL; -- Task.getBaselineWork() em horas
IF COL_LENGTH('dbo.Cronograma_Projetos', 'Trabalho_Real_Horas') IS NULL
    ALTER TABLE dbo.Cronograma_Projetos ADD Trabalho_Real_Horas decimal(18, 6) NULL;       -- Task.getActualWork() em horas
IF COL_LENGTH('dbo.Cronograma_Projetos', 'UID_Tarefa_Pai') IS NULL
    ALTER TABLE dbo.Cronograma_Projetos ADD UID_Tarefa_Pai nvarchar(100) NULL;            -- Task.getParentTask().getUniqueID()
IF COL_LENGTH('dbo.Cronograma_Projetos', 'Nivel_Estrutura') IS NULL
    ALTER TABLE dbo.Cronograma_Projetos ADD Nivel_Estrutura int NULL;                      -- Task.getOutlineLevel()
/* Trabalho da tarefa: já gravado em Campos_Customizados_JSON.__NA_CONNECT_WORK_V1.ItemHoras (o BI lê de lá). */

/* 2) Trabalho por tarefa × dia (série do .mpp, soma das atribuições da tarefa no dia; calendário efetivo da atribuição).
      Mesmo Data_Sincronizacao do snapshot de tarefas, gravado na MESMA transação do importador. */
IF OBJECT_ID('dbo.Cronograma_Tarefa_Dia', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cronograma_Tarefa_Dia
    (
        ID_Tarefa_Dia               bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Cronograma_Tarefa_Dia PRIMARY KEY,
        Project_ID_ACC              nvarchar(100)  NOT NULL,
        Data_Sincronizacao          datetime       NOT NULL,
        UID_Tarefa                  nvarchar(100)  NOT NULL,
        Data                        date           NOT NULL,
        Trabalho_Real_Horas         decimal(18, 6) NULL,   -- ResourceAssignment.getTimephasedActualWork()
        Trabalho_Linha_Base_Horas   decimal(18, 6) NULL,   -- getTimephasedBaselineWork(0)
        Trabalho_Horas              decimal(18, 6) NULL,   -- getTimephasedWork() (restante planejado)
        Arquivo_Origem              nvarchar(400)  NULL
    );
    CREATE INDEX IX_Cronograma_Tarefa_Dia_Snapshot
        ON dbo.Cronograma_Tarefa_Dia (Project_ID_ACC, Data_Sincronizacao, UID_Tarefa, Data);
END;

EXEC sp_refreshview N'dbo.VW_Cronograma_Tipo2_Ultima_Versao';
COMMIT TRANSACTION;
GO

/* 3) Última versão da série, amarrada ao MESMO snapshot da view de tarefas (lida por GET api/bi-chs001/cronograma-diario). */
CREATE OR ALTER VIEW dbo.VW_Cronograma_Tarefa_Dia_Ultima_Versao
AS
SELECT D.Project_ID_ACC, D.Data_Sincronizacao, D.UID_Tarefa, D.Data,
       D.Trabalho_Real_Horas, D.Trabalho_Linha_Base_Horas, D.Trabalho_Horas
FROM dbo.Cronograma_Tarefa_Dia AS D
INNER JOIN (SELECT DISTINCT V.Project_ID_ACC, V.Data_Sincronizacao FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao AS V) AS U
    ON U.Project_ID_ACC = D.Project_ID_ACC AND U.Data_Sincronizacao = D.Data_Sincronizacao;
GO

/* Importador (código, depende da mesma aprovação): CronogramaTipo2Parser preenche os 6 campos acima por tarefa e gera a série
   diária (referência: _bi_chs001_replica/tools/extrator_mpp/Program.cs); CronogramaTipo2Repository insere tarefas + série na
   mesma transação (série em lote, ~14 mil linhas por snapshot). Validar antes de ativar: 883 tarefas / 755 folhas, somas de
   trabalho (atual, LB, real) × .mpp, hierarquia sem ciclos, relatórios atuais (ScheduleReportRepository, LDP, HH) inalterados.

   ROLLBACK:
   DROP VIEW IF EXISTS dbo.VW_Cronograma_Tarefa_Dia_Ultima_Versao;
   DROP TABLE IF EXISTS dbo.Cronograma_Tarefa_Dia;
   ALTER TABLE dbo.Cronograma_Projetos DROP COLUMN Inicio, Inicio_Linha_Base, Trabalho_Linha_Base_Horas, Trabalho_Real_Horas,
       UID_Tarefa_Pai, Nivel_Estrutura;
   EXEC sp_refreshview N'dbo.VW_Cronograma_Tipo2_Ultima_Versao';
*/
