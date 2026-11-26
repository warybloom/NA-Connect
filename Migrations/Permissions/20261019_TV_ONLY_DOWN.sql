-- Restores the first UP snapshot exactly; retain this backup table for audit/idempotence.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'perm.TvProfile7Backup20261019',N'U') IS NULL
    THROW 51000, 'No TV grant backup; refusing rollback.', 1;
IF NOT EXISTS(SELECT 1 FROM perm.PermissionProfiles WHERE ProfileId=7 AND Name=N'TV' AND IsMasterAdmin=0)
    THROW 51000, 'TV profile identity mismatch.', 1;
DELETE FROM perm.ProfilePermissions WHERE ProfileId=7;
INSERT perm.ProfilePermissions(ProfileId,PermissionKey)
SELECT 7,PermissionKey FROM perm.TvProfile7Backup20261019;
COMMIT;
