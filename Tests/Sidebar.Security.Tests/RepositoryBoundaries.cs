// SQL-free boundaries for compiling the production controller and rules in isolation.
// The tested implementations are linked directly from production source by the project.
namespace APS.WebApp.Services.Sidebar
{
    public interface ISidebarStore
    {
        Task<SidebarConfiguration> ReadAsync(CancellationToken ct);
        Task<IReadOnlyList<SidebarHistory>> HistoryAsync(CancellationToken ct);
        Task<SidebarConfiguration?> RevisionAsync(long revision, CancellationToken ct);
        Task<SidebarConfiguration> PublishAsync(IReadOnlyList<SidebarItem> items, long expectedRevision, string author, long? restoredFrom, CancellationToken ct);
    }
}
namespace APS.WebApp.Services.Permissions
{
    public interface IPermissionRepository
    {
        Task<UserPermissionData> CarregarDadosAsync(int userId, bool masterPorConfiguracao, CancellationToken ct);
        Task<bool> UsuarioPossuiAcessoAtivoProjetoAsync(int userId, string projectIdAcc, CancellationToken ct);
        Task<int?> ResolverProjetoIdAsync(string projectIdAcc, CancellationToken ct);
    }
}
