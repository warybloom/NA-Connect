/*
    N&A Connect — Credenciais locais (login N&A por e-mail + senha) — ETAPA 06A
    Versão: 20261005_001

    • ADITIVO e IDEMPOTENTE: somente CREATE SCHEMA / CREATE TABLE quando ausentes. Sem DROP, sem ALTER, sem DELETE.
    • Não altera perm.Users nem UserProjects; a credencial referencia perm.Users(UserId) por FK.
    • PasswordHash = saída do Microsoft.AspNetCore.Identity.PasswordHasher (PBKDF2, salt embutido no próprio valor).
      A senha nunca é armazenada; não existe coluna de salt separada.
    • Datas em UTC.
    • Pré-requisito: Migrations/Permissions/20260929_001_Permissions.sql aplicado (perm.Users).
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'perm.Users', N'U') IS NULL
    THROW 50001, N'perm.Users não existe: aplique Migrations/Permissions/20260929_001_Permissions.sql antes.', 1;
GO

IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA auth AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'auth.UserCredentials', N'U') IS NULL
CREATE TABLE auth.UserCredentials (
    UserId              INT            NOT NULL CONSTRAINT PK_AuthUserCredentials PRIMARY KEY,
    PasswordHash        NVARCHAR(512)  NOT NULL,
    FailedAttempts      INT            NOT NULL CONSTRAINT DF_AuthUserCredentials_Failed DEFAULT (0),
    LockoutUntil        DATETIME2(0)   NULL,
    PasswordUpdatedAt   DATETIME2(0)   NOT NULL CONSTRAINT DF_AuthUserCredentials_Updated DEFAULT (SYSUTCDATETIME()),
    MustChangePassword  BIT            NOT NULL CONSTRAINT DF_AuthUserCredentials_MustChange DEFAULT (0),
    CONSTRAINT FK_AuthUserCredentials_PermUsers FOREIGN KEY (UserId) REFERENCES perm.Users (UserId),
    CONSTRAINT CK_AuthUserCredentials_Failed CHECK (FailedAttempts >= 0)
);
GO
