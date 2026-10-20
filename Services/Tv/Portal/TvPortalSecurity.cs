using APS.WebApp.Services.Permissions;

namespace APS.WebApp.Services.Tv.Portal;

public sealed record TvPortalIdentity(int UserId, bool Restricted, bool CanView, bool CanConfigure);
public sealed record TvPortalGrant(string PresentationId, int[] UserIds);
public sealed record TvPortalAssignments(long Revision, TvPortalGrant[] Grants);
public sealed record TvReadScope(IReadOnlySet<string> ProjectIds, IReadOnlyList<string> Keys);

public static class TvPortalPolicy
{
    public const int ProfileId = 7;
    public const string ScopeItem = "TvPortal:ReadScope";
    public static TvPortalIdentity Identity(int userId, UserPermissionData data, bool designatedTv = false)
    {
        var master = PermissionResolver.IsMasterAdmin(data);
        // An administrative role must be separate from TV; TV's historical settings grant is not an exemption.
        var admin = master || data.Assignments.Any(a => a.ProfileId != ProfileId && a.ProjetoId is null
            && a.Permissions.Contains("permissions.manage")) && PermissionResolver.Decide(data, "permissions.manage", null).Allowed;
        var restricted = (designatedTv || data.Assignments.Any(a => a.ProfileId == ProfileId)) && !admin;
        return new(userId, restricted, PermissionResolver.EffectiveAnyProject(data).Contains("tv.view"),
            !restricted && PermissionResolver.Decide(data, "settings.tv.configure", null).Allowed);
    }
    public static bool Assigned(TvPortalAssignments assignments, string presentationId, int userId) =>
        assignments.Grants.Any(g => g.PresentationId == presentationId && g.UserIds.Contains(userId));
    // A permissão TV é GLOBAL, mas só vale pelo gateway de apresentação atribuída.
    // Respeitar DENYs explícitos, sem herdar o requisito de vínculo em perm.UserProjects.
    public static bool CanReadAssignedSlideProject(UserPermissionData data, int projectId) =>
        PermissionResolver.Decide(data, "tv.view", null).Allowed
        && !data.Overrides.Any(o => o.PermissionKey == "tv.view" && o.Effect == PermissionEffects.Deny
            && (o.ProjetoId is null || o.ProjetoId == projectId));
    public static bool LocalIntervalValid(int seconds) => seconds is >= 10 and <= 300;
}

public sealed class TvPortalIdentityService(IPermissionService permissions, IPermissionRepository repository,
    ITvPortalRoleDesignation? designation = null, ITvPortalProjectRegistry? projectRegistry = null)
{
    private TvPortalIdentity? cached;
    private UserPermissionData? loaded;
    public async Task<TvPortalIdentity?> ReadAsync(CancellationToken ct)
    {
        if (cached is not null) return cached;
        var user = await permissions.GetCurrentUserAsync(ct);
        if (user is null) return null;
        if (user.UserId is not int id) return user.IsMasterAdmin ? new(0, false, true, true) : null;
        // Always load real assignments, including when Permissions:Enforce=false.
        var data = await repository.CarregarDadosAsync(id, user.IsMasterAdmin, ct);
        loaded = data;
        return cached = TvPortalPolicy.Identity(id, data, designation is not null && await designation.IsTvAsync(id, ct));
    }
    public async Task<bool> CanViewProjectAsync(int userId, string projectId, CancellationToken ct)
    {
        var identity = await ReadAsync(ct);
        if (identity?.UserId != userId || !identity.Restricted || !identity.CanView || loaded is null) return false;
        var internalId = await repository.ResolverProjetoIdAsync(projectId, ct);
        if (internalId is not int id || !TvPortalPolicy.CanReadAssignedSlideProject(loaded, id)) return false;
        // Em produção validar se projeto segue ativo, independentemente de vínculo do usuário.
        return projectRegistry is null || await projectRegistry.ExistsActiveAsync(projectId, ct);
    }
}

/// <summary>Only the gateway can create ScopeItem. It never exists for a normal report request.</summary>
public sealed class TvPortalPermissionService(PermissionService inner, IHttpContextAccessor http) : IPermissionService, IProjectAccessService
{
    private TvReadScope? Scope => http.HttpContext?.Items[TvPortalPolicy.ScopeItem] as TvReadScope;
    public bool Enforced => Scope is not null || inner.Enforced;
    public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => inner.GetCurrentUserAsync(ct);
    public Task<int?> ResolverProjetoIdAsync(string? id, CancellationToken ct) => inner.ResolverProjetoIdAsync(id, ct);
    public Task<EffectivePermissions> GetEffectiveAsync(string? id, CancellationToken ct) => inner.GetEffectiveAsync(id, ct);
    public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys, string? project, CancellationToken ct, bool anyProject = false) =>
        Scope is { } s ? Task.FromResult(project is not null && s.ProjectIds.Contains(project) && keys.Any(s.Keys.Contains) ? PermissionCheck.Allowed : PermissionCheck.Denied)
            : inner.CheckAsync(keys, project, ct, anyProject);
    public Task<PermissionCheck> CheckProjectAccessAsync(string? project, CancellationToken ct) =>
        Scope is { } s ? Task.FromResult(project is not null && s.ProjectIds.Contains(project) ? PermissionCheck.Allowed : PermissionCheck.Denied)
            : inner.CheckProjectAccessAsync(project, ct);
}
