-- Projetos sem LDP pertencem ao cadastro PRINCIPAL, não a uma tabela paralela.
-- A antiga audit.LegacyProjectRegistry é apenas uma fonte temporária para migração.
SET XACT_ABORT ON;
BEGIN TRAN;
IF COL_LENGTH('dbo.Projetos','Sem_LDP') IS NULL
  ALTER TABLE dbo.Projetos ADD Sem_LDP bit NOT NULL CONSTRAINT DF_Projetos_Sem_LDP DEFAULT(0);
IF COL_LENGTH('dbo.Projetos','Timesheet_Projeto') IS NULL
  ALTER TABLE dbo.Projetos ADD Timesheet_Projeto nvarchar(100) NULL;
IF COL_LENGTH('dbo.Projetos','AFE_Historico') IS NULL
  ALTER TABLE dbo.Projetos ADD AFE_Historico nvarchar(100) NULL;
IF COL_LENGTH('dbo.Projetos','Sem_LDP_CriadoEm') IS NULL
  ALTER TABLE dbo.Projetos ADD Sem_LDP_CriadoEm datetime2(0) NULL;
IF COL_LENGTH('dbo.Projetos','Sem_LDP_AtualizadoEm') IS NULL
  ALTER TABLE dbo.Projetos ADD Sem_LDP_AtualizadoEm datetime2(0) NULL;
IF COL_LENGTH('dbo.Projetos','Sem_LDP_CriadoPor') IS NULL
  ALTER TABLE dbo.Projetos ADD Sem_LDP_CriadoPor nvarchar(255) NULL;
GO
IF OBJECT_ID('audit.LegacyProjectRegistry','U') IS NOT NULL
BEGIN
  IF EXISTS (
    SELECT 1 FROM audit.LegacyProjectRegistry r
    LEFT JOIN dbo.Hubs_Conta h ON h.Account_ID_ACC=r.HubIdAcc
    WHERE h.HubContaID IS NULL)
      THROW 51001,'Hub ACC sem correspondencia em Hubs_Conta; migração abortada.',1;
  IF EXISTS (
    SELECT 1 FROM audit.LegacyProjectRegistry r
    JOIN dbo.Projetos p ON p.Project_ID_ACC=r.ProjectIdAcc
    WHERE p.File_ID_LDP_ACC IS NOT NULL AND NULLIF(LTRIM(RTRIM(p.File_ID_LDP_ACC)), '') IS NOT NULL)
      THROW 51002,'Projeto possui LDP; conversão automática abortada.',1;

  UPDATE p SET
    HubContaID=h.HubContaID,
    Nome_Projeto=r.ProjectName,
    CR_OS=NULLIF(r.CrOs,''),
    Timesheet_Projeto=r.TimesheetProject,
    AFE_Historico=NULLIF(r.Afe,''),
    Sem_LDP=1,Is_Ativo=1,
    Sem_LDP_CriadoEm=r.CreatedAtUtc,
    Sem_LDP_AtualizadoEm=r.UpdatedAtUtc,
    Sem_LDP_CriadoPor=r.CreatedBy
  FROM dbo.Projetos p
  JOIN audit.LegacyProjectRegistry r ON r.ProjectIdAcc=p.Project_ID_ACC
  JOIN dbo.Hubs_Conta h ON h.Account_ID_ACC=r.HubIdAcc;

  INSERT dbo.Projetos
    (HubContaID,Nome_Projeto,Project_ID_ACC,Is_Ativo,Tipo_LDP,CR_OS,
    Timesheet_Projeto,AFE_Historico,Sem_LDP,Sem_LDP_CriadoEm,Sem_LDP_AtualizadoEm,Sem_LDP_CriadoPor)
  SELECT h.HubContaID,r.ProjectName,r.ProjectIdAcc,1,1,NULLIF(r.CrOs,''),
     r.TimesheetProject,NULLIF(r.Afe,''),1,r.CreatedAtUtc,r.UpdatedAtUtc,r.CreatedBy
  FROM audit.LegacyProjectRegistry r
  JOIN dbo.Hubs_Conta h ON h.Account_ID_ACC=r.HubIdAcc
  WHERE NOT EXISTS (SELECT 1 FROM dbo.Projetos p WITH(UPDLOCK,HOLDLOCK) WHERE p.Project_ID_ACC=r.ProjectIdAcc);

  -- O criador autorizado precisa acessar seu projeto na Home como os demais.
  UPDATE up SET Ativo=1,AtualizadoEm=SYSUTCDATETIME()
  FROM perm.UserProjects up
  JOIN dbo.Projetos p ON p.ProjetoID=up.ProjetoId
  JOIN audit.LegacyProjectRegistry r ON p.Project_ID_ACC=r.ProjectIdAcc
  JOIN perm.Users u ON u.UserId=up.UserId AND LOWER(u.Email)=LOWER(r.CreatedBy);

  INSERT perm.UserProjects(UserId,ProjetoId,Ativo)
  SELECT u.UserId,p.ProjetoID,1 FROM audit.LegacyProjectRegistry r
  JOIN dbo.Projetos p ON p.Project_ID_ACC=r.ProjectIdAcc
  JOIN perm.Users u ON LOWER(u.Email)=LOWER(r.CreatedBy) AND u.Ativo=1
  WHERE NOT EXISTS(SELECT 1 FROM perm.UserProjects up WITH(UPDLOCK,HOLDLOCK)
    WHERE up.UserId=u.UserId AND up.ProjetoId=p.ProjetoID);

  IF EXISTS (SELECT 1 FROM audit.LegacyProjectRegistry r
    LEFT JOIN dbo.Projetos p ON p.Project_ID_ACC=r.ProjectIdAcc
    WHERE p.ProjetoID IS NULL OR p.Sem_LDP=0
       OR ISNULL(p.Timesheet_Projeto,'')<>r.TimesheetProject
       OR ISNULL(p.CR_OS,'')<>r.CrOs OR ISNULL(p.AFE_Historico,'')<>r.Afe)
      THROW 51003,'Falha de reconciliação do registro legado; abortando.',1;
END
COMMIT;
