-- RELEASE GATE: execute first in a restored disposable DB. Never run before deploying/testing the TV barrier.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM perm.PermissionProfiles WHERE ProfileId=7 AND Name=N'TV' AND Ativo=1 AND IsMasterAdmin=0)
    THROW 51000, 'TV profile identity mismatch; no change made.', 1;
IF NOT EXISTS(SELECT 1 FROM perm.PermissionResources WHERE PermissionKey=N'tv.view')
    THROW 51000, 'tv.view missing.', 1;
IF OBJECT_ID(N'perm.TvProfile7Backup20261019',N'U') IS NULL
BEGIN
    CREATE TABLE perm.TvProfile7Backup20261019(PermissionKey nvarchar(120) NOT NULL PRIMARY KEY);
    INSERT perm.TvProfile7Backup20261019 SELECT PermissionKey FROM perm.ProfilePermissions WITH(UPDLOCK,HOLDLOCK) WHERE ProfileId=7;
END;
DELETE FROM perm.ProfilePermissions WHERE ProfileId=7 AND PermissionKey<>N'tv.view';
IF NOT EXISTS(SELECT 1 FROM perm.ProfilePermissions WHERE ProfileId=7 AND PermissionKey=N'tv.view')
    INSERT perm.ProfilePermissions(ProfileId,PermissionKey) VALUES(7,N'tv.view');
IF (SELECT COUNT(*) FROM perm.ProfilePermissions WHERE ProfileId=7)<>1
    THROW 51000, 'TV grants did not reconcile.', 1;
COMMIT;
