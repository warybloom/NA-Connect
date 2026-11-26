USE [DB_NA_CONNECT];
GO
IF OBJECT_ID(N'dbo.SidebarOfficial', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SidebarOfficial (
  SingletonId int NOT NULL CONSTRAINT PK_SidebarOfficial PRIMARY KEY,
  Revision bigint NOT NULL,
  ItemsJson nvarchar(max) NOT NULL,
  UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_SidebarOfficial_UpdatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedBy nvarchar(320) NOT NULL,
  CONSTRAINT CK_SidebarOfficial_Singleton CHECK (SingletonId=1),
  CONSTRAINT CK_SidebarOfficial_Json CHECK (ISJSON(ItemsJson)=1)
 );
END;
GO
IF OBJECT_ID(N'dbo.SidebarOfficialHistory', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SidebarOfficialHistory (
  Revision bigint NOT NULL CONSTRAINT PK_SidebarOfficialHistory PRIMARY KEY,
  ItemsJson nvarchar(max) NOT NULL,
  UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_SidebarOfficialHistory_UpdatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedBy nvarchar(320) NOT NULL,
  RestoredFrom bigint NULL,
  CONSTRAINT CK_SidebarOfficialHistory_Json CHECK (ISJSON(ItemsJson)=1)
 );
END;
GO
-- Nenhum INSERT inicial: fallback em código preserva o sidebar existente até a primeira publicação.
