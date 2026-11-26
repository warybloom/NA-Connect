/*
  Fluxos de Revisão — permissão de leitura independente.

  Execução manual no SQL Server após revisão.
  - Registra planning.review-flows.view no catálogo persistido.
  - Concede somente leitura aos perfis PERFIL GESTÃO e PERFIL TV (aceita o nome legado TV).
  - Mantém acesso dos perfis Administrador e dos perfis que já possuíam a permissão de edição.
  - Não concede planning.coordination.edit nem qualquer outra permissão de escrita.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'perm.PermissionResources', N'U') IS NULL
        OR OBJECT_ID(N'perm.PermissionProfiles', N'U') IS NULL
        OR OBJECT_ID(N'perm.ProfilePermissions', N'U') IS NULL
        THROW 51000, 'Schema de permissões não instalado. Nenhuma alteração foi realizada.', 1;

    IF NOT EXISTS (SELECT 1 FROM perm.PermissionResources WHERE PermissionKey = N'planning.view')
        THROW 51000, 'Permissão pai planning.view não encontrada. Nenhuma alteração foi realizada.', 1;

    IF EXISTS (SELECT 1 FROM perm.PermissionResources WHERE PermissionKey = N'planning.review-flows.view')
    BEGIN
        UPDATE perm.PermissionResources
        SET ParentKey = N'planning.view',
            Label = N'Fluxos de Revisão — Visualizar',
            ResourceType = N'PAGE',
            ScopeType = N'PROJECT',
            Route = N'/planejamento/fluxos-de-revisao',
            SortOrder = 715,
            Ativo = 1,
            AtualizadoEm = SYSUTCDATETIME()
        WHERE PermissionKey = N'planning.review-flows.view';
    END
    ELSE
    BEGIN
        INSERT INTO perm.PermissionResources
            (PermissionKey, ParentKey, Label, ResourceType, ScopeType, Route, SortOrder, Ativo)
        VALUES
            (N'planning.review-flows.view', N'planning.view', N'Fluxos de Revisão — Visualizar',
             N'PAGE', N'PROJECT', N'/planejamento/fluxos-de-revisao', 715, 1);
    END;

    ;WITH PerfisAlvo AS
    (
        SELECT DISTINCT p.ProfileId
        FROM perm.PermissionProfiles p
        WHERE p.Ativo = 1
          AND
          (
              p.Name IN (N'PERFIL GESTÃO', N'PERFIL TV', N'TV', N'Administrador')
              OR EXISTS
              (
                  SELECT 1
                  FROM perm.ProfilePermissions existente
                  WHERE existente.ProfileId = p.ProfileId
                    AND existente.PermissionKey = N'planning.coordination.edit'
              )
          )
    )
    INSERT INTO perm.ProfilePermissions (ProfileId, PermissionKey)
    SELECT alvo.ProfileId, N'planning.review-flows.view'
    FROM PerfisAlvo alvo
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM perm.ProfilePermissions existente
        WHERE existente.ProfileId = alvo.ProfileId
          AND existente.PermissionKey = N'planning.review-flows.view'
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT p.ProfileId, p.Name, pp.PermissionKey
FROM perm.PermissionProfiles p
LEFT JOIN perm.ProfilePermissions pp
    ON pp.ProfileId = p.ProfileId
   AND pp.PermissionKey = N'planning.review-flows.view'
WHERE p.Name IN (N'PERFIL GESTÃO', N'PERFIL TV', N'TV', N'Administrador')
ORDER BY p.Name;
