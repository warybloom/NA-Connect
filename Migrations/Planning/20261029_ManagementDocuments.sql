/*
  Documentos de Gestão — classificação do planejador (29/10/2026).

  NÃO EXECUTADA AUTOMATICAMENTE. Execução manual no SQL Server (LocalConnection) após revisão.

  Regra de negócio:
  - O planejador (Painel de Planejamento > Painel do Planejador) marca quais documentos do projeto são Documentos de Gestão.
  - Todo documento elegível NÃO marcado aqui é Engenharia. Não há heurística de título/disciplina/pessoa/AFE.
  - Identidade estável entre cargas da LDP: Project_ID_ACC + DocumentKey, onde DocumentKey = UPPER(LTRIM(RTRIM(NA_Document_Number)))
    (mesma regra de PmoExecutiveRules.Chave). Nunca ID_Registro/Data_Sincronizacao — uma nova carga da LDP não apaga a marcação.
  - Retornar para Engenharia = remover a linha (o histórico fica em planning.ManagementDocumentAudit).

  Também registra a key de ação planning.planner.management-documents.edit (escopo PROJETO) e concede somente ao perfil
  Administrador. Planejadores recebem a key pela tela de permissões, por projeto.
  Idempotente; sem DROP/TRUNCATE/ALTER em dbo.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'planning') IS NULL EXEC(N'CREATE SCHEMA planning');
GO

IF OBJECT_ID(N'planning.ManagementDocument', N'U') IS NULL
CREATE TABLE planning.ManagementDocument (
    ProjectIdAcc nvarchar(100) NOT NULL,
    DocumentKey nvarchar(200) NOT NULL,
    DocumentCodeOriginal nvarchar(200) NOT NULL,
    ClassifiedBy nvarchar(256) NULL,
    ClassifiedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_ManagementDocument_ClassifiedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ManagementDocument PRIMARY KEY (ProjectIdAcc, DocumentKey),
    CONSTRAINT CK_ManagementDocument_Key CHECK (LEN(DocumentKey) > 0 AND DocumentKey = UPPER(LTRIM(RTRIM(DocumentKey))))
);
GO

IF OBJECT_ID(N'planning.ManagementDocumentAudit', N'U') IS NULL
CREATE TABLE planning.ManagementDocumentAudit (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ManagementDocumentAudit PRIMARY KEY,
    ProjectIdAcc nvarchar(100) NOT NULL,
    DocumentKey nvarchar(200) NOT NULL,
    Target varchar(12) NOT NULL CONSTRAINT CK_ManagementDocumentAudit_Target CHECK (Target IN ('GESTAO', 'ENGENHARIA')),
    ChangedBy nvarchar(256) NULL,
    ChangedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_ManagementDocumentAudit_ChangedAtUtc DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'planning.ManagementDocumentAudit') AND name = N'IX_ManagementDocumentAudit_Project')
CREATE INDEX IX_ManagementDocumentAudit_Project ON planning.ManagementDocumentAudit(ProjectIdAcc, ChangedAtUtc);
GO

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'perm.PermissionResources', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM perm.PermissionResources WHERE PermissionKey = N'planning.planner.view')
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM perm.PermissionResources WHERE PermissionKey = N'planning.planner.management-documents.edit')
            INSERT INTO perm.PermissionResources (PermissionKey, ParentKey, Label, ResourceType, ScopeType, Route, SortOrder, Ativo)
            VALUES (N'planning.planner.management-documents.edit', N'planning.planner.view', N'Classificar Documentos de Gestão',
                    N'ACTION', N'PROJECT', NULL, 765, 1);

        INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey)
        SELECT p.ProfileId, N'planning.planner.management-documents.edit'
        FROM perm.PermissionProfiles p
        WHERE p.Ativo = 1 AND p.Name = N'Administrador'
          AND NOT EXISTS (SELECT 1 FROM perm.ProfilePermissions e
                          WHERE e.ProfileId = p.ProfileId AND e.PermissionKey = N'planning.planner.management-documents.edit');
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
