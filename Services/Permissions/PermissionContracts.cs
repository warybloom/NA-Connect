namespace APS.WebApp.Services.Permissions;

// Contratos do serviço de permissões SEM dependências (usados pelo [RequirePermission] e pelos testes que linkam fontes).

public enum PermissionCheck { Allowed, Unauthenticated, Denied, Unavailable }

public sealed record CurrentPermissionUser(int? UserId, string AccUserId, string Email, string Nome, bool IsMasterAdmin);

public sealed record EffectivePermissions(bool Enforced, bool SchemaInstalled, bool IsMasterAdmin, string? Email, string? ProjectIdAcc,
    IReadOnlyCollection<string> Permissions, IReadOnlyCollection<string> AnyProject);

public interface IPermissionService
{
    bool Enforced { get; }
    /// <summary>
    /// Permitido se o usuário tiver QUALQUER uma das keys no contexto (projeto ACC opcional). anyProject=true (sem
    /// projeto): basta tê-la globalmente ou em algum projeto — só para listagens sem projeto no request.
    /// </summary>
    Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false);
    Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct);
    Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct);
    Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct);
}

/// <summary>Gate positivo de entrada no projeto, independente das permissões funcionais.</summary>
public interface IProjectAccessService
{
    Task<PermissionCheck> CheckProjectAccessAsync(string? projectIdAcc, CancellationToken ct);
}
