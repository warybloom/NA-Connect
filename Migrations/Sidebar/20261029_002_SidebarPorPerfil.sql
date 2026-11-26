SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.SidebarProfileConfiguration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SidebarProfileConfiguration
    (
        ProfileId INT NOT NULL,
        Revision BIGINT NOT NULL CONSTRAINT DF_SidebarProfileConfiguration_Revision DEFAULT (0),
        ItemsJson NVARCHAR(MAX) NOT NULL,
        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SidebarProfileConfiguration_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedBy NVARCHAR(320) NOT NULL,
        CONSTRAINT PK_SidebarProfileConfiguration PRIMARY KEY (ProfileId),
        CONSTRAINT CK_SidebarProfileConfiguration_Revision CHECK (Revision >= 0),
        CONSTRAINT CK_SidebarProfileConfiguration_ItemsJson CHECK (ISJSON(ItemsJson) = 1),
        -- NO ACTION é intencional: um perfil configurado não pode ser apagado silenciosamente.
        CONSTRAINT FK_SidebarProfileConfiguration_Profile FOREIGN KEY (ProfileId)
            REFERENCES perm.PermissionProfiles(ProfileId)
    );
END;

IF OBJECT_ID(N'dbo.SidebarProfileConfigurationHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SidebarProfileConfigurationHistory
    (
        ProfileId INT NOT NULL,
        Revision BIGINT NOT NULL,
        ItemsJson NVARCHAR(MAX) NOT NULL,
        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SidebarProfileConfigurationHistory_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedBy NVARCHAR(320) NOT NULL,
        Reason NVARCHAR(80) NOT NULL,
        CONSTRAINT PK_SidebarProfileConfigurationHistory PRIMARY KEY (ProfileId, Revision),
        CONSTRAINT CK_SidebarProfileConfigurationHistory_Revision CHECK (Revision > 0),
        CONSTRAINT CK_SidebarProfileConfigurationHistory_ItemsJson CHECK (ISJSON(ItemsJson) = 1),
        CONSTRAINT FK_SidebarProfileConfigurationHistory_Profile FOREIGN KEY (ProfileId)
            REFERENCES perm.PermissionProfiles(ProfileId)
    );
END;

COMMIT TRANSACTION;
