-- Etapa 15 only. Additive, idempotent. Does not rewrite historical columns or rows.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Item') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Item] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Phase') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Phase] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Progress_Status') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Progress_Status] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Baseline_Date') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Baseline_Date] date NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Replanned_Date') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Replanned_Date] date NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Preparation_Completed') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Preparation_Completed] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Verification') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Verification] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Approval') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Approval] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_HH_Previsto') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Previsto] decimal(18,6) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_HH_Verificacao') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Verificacao] decimal(18,6) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Responsible_Verification') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Responsible_Verification] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_HH_Aprovacao') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_HH_Aprovacao] decimal(18,6) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Responsible_Approval') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Responsible_Approval] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Liberado_Execucao') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Liberado_Execucao] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.tb_WeeklyEngineeringSchedule', N'Weekly_Observacao_Dia') IS NULL
    ALTER TABLE dbo.tb_WeeklyEngineeringSchedule ADD [Weekly_Observacao_Dia] nvarchar(max) NULL;
COMMIT TRANSACTION;
