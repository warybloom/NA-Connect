-- NA Connect | Central de notificacoes v1 | executar uma unica vez apos backup
-- Somente estado de leitura, segregado por usuario. Os eventos permanecem nos logs do robo.
IF OBJECT_ID(N'dbo.NA_NotificationReads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NA_NotificationReads (
        UserId INT NOT NULL,
        NotificationKey CHAR(64) NOT NULL,
        ReadAt DATETIME2(3) NOT NULL CONSTRAINT DF_NA_NotificationReads_ReadAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_NA_NotificationReads PRIMARY KEY(UserId, NotificationKey)
    );
END;
