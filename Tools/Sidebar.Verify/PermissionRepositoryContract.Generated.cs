// Extracted production repository contract.
#nullable enable



namespace APS.WebApp.Services.Permissions;

public sealed record PermUser(int UserId, string? AccUserId, string Email, string? DisplayName, bool Ativo, DateTime? UltimoAcessoEm);
public sealed record PermProfile(int ProfileId, string Name, string? Description, bool Ativo, bool IsSystem, bool IsMasterAdmin, IReadOnlyList<string> Permissions);
public sealed record UserProjectView(PermUser User, bool AtivoNoProjeto, IReadOnlyList<int> ProfileIds, IReadOnlyList<PermissionOverride> Overrides);
public sealed record PermittedProject(int ProjetoId, string ProjectIdAcc, string NomeProjeto, byte? TipoLdp, bool IsAtivo);
public sealed record PermissionAuditEntry(long Id, DateTime DataHora, string? AlteradoPorEmail, int? UsuarioAfetadoId, string? UsuarioAfetadoEmail,
    int? ProjetoId, string Alvo, string? Referencia, string? ValorAnterior, string? ValorNovo);

public interface IPermissionRepository
{
    Task<bool> SchemaInstaladoAsync(CancellationToken ct);
    Task<int?> ResolverProjetoIdAsync(string projectIdAcc, CancellationToken ct);
    Task<bool> UsuarioPossuiAcessoAtivoProjetoAsync(int userId, string projectIdAcc, CancellationToken ct);
    Task<int> RegistrarAcessoAsync(string? accUserId, string email, string? nome, CancellationToken ct);
    Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct);
    Task<IReadOnlyList<PermittedProject>> ListarProjetosPermitidosAsync(int userId, CancellationToken ct);
    Task<UserPermissionData> CarregarDadosAsync(int userId, bool masterPorConfiguracao, CancellationToken ct);

    Task<IReadOnlyList<PermProfile>> ListarPerfisAsync(CancellationToken ct);
    Task<int> CriarPerfilAsync(string nome, string? descricao, IEnumerable<string> permissoes, CancellationToken ct);
    Task AtualizarPerfilAsync(int profileId, string nome, string? descricao, bool ativo, CancellationToken ct);
    Task ExcluirPerfilAsync(int profileId, CancellationToken ct);
    Task DefinirPermissoesPerfilAsync(int profileId, IEnumerable<string> permissoes, CancellationToken ct);

    Task<IReadOnlyList<PermUser>> ListarUsuariosAsync(CancellationToken ct);
    Task<int> CadastrarUsuarioAsync(string email, string? nome, CancellationToken ct);
    Task<UserProjectView> ObterUsuarioNoProjetoAsync(int userId, int? projetoId, CancellationToken ct);
    Task DefinirPerfisUsuarioAsync(int userId, int? projetoId, IEnumerable<int> profileIds, CancellationToken ct);
    Task DefinirOverridesUsuarioAsync(int userId, int? projetoId, IEnumerable<PermissionOverride> overrides, CancellationToken ct);
    Task DefinirAtivoNoProjetoAsync(int userId, int projetoId, bool ativo, CancellationToken ct);

    Task AuditarAsync(int? alteradoPor, string? alteradoPorEmail, int? usuarioAfetado, int? projetoId, string alvo, string? referencia,
        string? anterior, string? novo, CancellationToken ct);
    Task<IReadOnlyList<PermissionAuditEntry>> LerAuditoriaAsync(int top, CancellationToken ct);
}

