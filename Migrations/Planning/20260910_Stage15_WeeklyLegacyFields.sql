-- Etapa 15: campos distintos do layout legado; nenhuma equivalencia entre status ou HH.
-- Executar apos 20260909_Stage15_WeeklyOfficial.sql. Nao modifica colunas historicas.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_Client_Document_Number') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Client_Document_Number] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_Size') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Size] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_Pages') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Pages] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_Document_Status') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Document_Status] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_Current_Status') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Current_Status] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_HH_Previsto_30') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Previsto_30] decimal(18,6) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_HH_Previsto_60') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Previsto_60] decimal(18,6) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_HH_Previsto_90') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Previsto_90] decimal(18,6) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule',N'Weekly_HH_Realizado') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Realizado] decimal(18,6) NULL;
COMMIT TRANSACTION;
