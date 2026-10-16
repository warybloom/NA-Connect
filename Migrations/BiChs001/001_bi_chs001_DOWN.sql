/* =====================================================================================================================
   BI CHS001 (Original) — migração ISOLADA 001 (DOWN / rollback) — 15/10/2026
   Remove SOMENTE os objetos do schema bi_chs001 criados pelo UP. Não toca em dbo nem em nenhum outro schema.
   Idempotente. ATENÇÃO: descarta as versões do cronograma lidas pelo BI (são reproduzíveis a partir do .mpp).
   ===================================================================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'bi_chs001.VW_Trabalho_Semana', N'V') IS NOT NULL DROP VIEW bi_chs001.VW_Trabalho_Semana;
IF OBJECT_ID(N'bi_chs001.VW_Versao_Atual', N'V') IS NOT NULL DROP VIEW bi_chs001.VW_Versao_Atual;
IF OBJECT_ID(N'bi_chs001.VW_Tarefa', N'V') IS NOT NULL DROP VIEW bi_chs001.VW_Tarefa;
IF OBJECT_ID(N'bi_chs001.Trabalho_Tarefa_Dia', N'U') IS NOT NULL DROP TABLE bi_chs001.Trabalho_Tarefa_Dia;
IF OBJECT_ID(N'bi_chs001.Tarefa_Snapshot', N'U') IS NOT NULL DROP TABLE bi_chs001.Tarefa_Snapshot;
IF OBJECT_ID(N'bi_chs001.Versao_Cronograma', N'U') IS NOT NULL DROP TABLE bi_chs001.Versao_Cronograma;
IF OBJECT_ID(N'bi_chs001.Alias_Rotulo', N'U') IS NOT NULL DROP TABLE bi_chs001.Alias_Rotulo;
IF OBJECT_ID(N'bi_chs001.Migracao', N'U') IS NOT NULL DROP TABLE bi_chs001.Migracao;

/* O schema só é removido se estiver vazio (nunca apaga objeto que não seja desta migração). */
IF SCHEMA_ID(N'bi_chs001') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'bi_chs001'))
    EXEC(N'DROP SCHEMA bi_chs001');

COMMIT TRANSACTION;
