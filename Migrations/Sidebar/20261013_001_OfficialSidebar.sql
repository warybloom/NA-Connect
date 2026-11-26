-- Aplicação MANUAL. Este arquivo não é executado pelo aplicativo.
USE [DB_NA_CONNECT];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.SidebarOfficial', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SidebarOfficial (
        SingletonId tinyint NOT NULL CONSTRAINT PK_SidebarOfficial PRIMARY KEY,
        Revision bigint NOT NULL,
        ItemsJson nvarchar(max) NOT NULL,
        UpdatedAt datetime2(7) NOT NULL,
        UpdatedBy nvarchar(320) NOT NULL,
        CONSTRAINT CK_SidebarOfficial_Singleton CHECK (SingletonId = 1),
        CONSTRAINT CK_SidebarOfficial_Revision CHECK (Revision > 0),
        CONSTRAINT CK_SidebarOfficial_Json CHECK (ISJSON(ItemsJson) = 1)
    );
END;
IF OBJECT_ID(N'dbo.SidebarOfficialHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SidebarOfficialHistory (
        Revision bigint NOT NULL CONSTRAINT PK_SidebarOfficialHistory PRIMARY KEY,
        ItemsJson nvarchar(max) NOT NULL,
        UpdatedAt datetime2(7) NOT NULL,
        UpdatedBy nvarchar(320) NOT NULL,
        RestoredFrom bigint NULL,
        CONSTRAINT CK_SidebarOfficialHistory_Revision CHECK (Revision > 0),
        CONSTRAINT CK_SidebarOfficialHistory_Json CHECK (ISJSON(ItemsJson) = 1),
        CONSTRAINT CK_SidebarOfficialHistory_Restore CHECK (RestoredFrom IS NULL OR RestoredFrom >= 0 AND RestoredFrom < Revision)
    );
END;
-- Nenhum seed: ausência da linha = revisão 0, navegação oficial em código.
COMMIT TRANSACTION;
GO
