using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;

namespace NA_Connect.Planning.Tests.Permissions;

internal sealed class MemorySessionPerm : ISession
{
    private readonly Dictionary<string, byte[]> _v = new();
    public bool IsAvailable => true;
    public string Id => "t";
    public IEnumerable<string> Keys => _v.Keys;
    public void Clear() => _v.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => _v.Remove(key);
    public void Set(string key, byte[] value) => _v[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _v.TryGetValue(key, out value!);
}

internal sealed class FakeUserAccessor : ICurrentUserAccessor
{
    public AutodeskUserInfo? Identidade { get; set; } = new("acc-1", "user@na.com", "Usuário");
    /// <summary>Simula falha da API Autodesk (UserInfo).</summary>
    public int Chamadas { get; private set; }
    public bool Lanca { get; set; }
    /// <summary>Simula token sem o scope user-profile:read (UserInfo 403).</summary>
    public bool SemScope { get; set; }
    public Task<AutodeskUserInfo?> GetAsync(CancellationToken ct)
    {
        Chamadas++;
        return
        SemScope ? throw new AutodeskUserInfoException(403)
        : Lanca ? throw new HttpRequestException("Autodesk indisponível")
        : Task.FromResult(Identidade);
    }
}

internal sealed class FakeNaCurrentUserAccessor : INaCurrentUserAccessor
{
    public NaCurrentUser? Identidade { get; set; }
    public bool Lanca { get; set; }
    public int Chamadas { get; private set; }

    public Task<NaCurrentUser?> GetAsync(CancellationToken ct = default)
    {
        Chamadas++;
        return Lanca ? throw new InvalidOperationException("sessão N&A indisponível") : Task.FromResult(Identidade);
    }
}

/// <summary>Repositório em memória: só o necessário para PermissionService e PermissionsController.</summary>
internal sealed class FakePermissionRepository : IPermissionRepository
{
    public bool Schema { get; set; } = true;
    /// <summary>Simula banco fora do ar: toda chamada lança.</summary>
    public bool Lanca { get; set; }
    public Dictionary<int, UserPermissionData> Dados { get; } = new();
    public Dictionary<int, PermUser> Usuarios { get; } = new();
    public Dictionary<string, int> Projetos { get; } = new(StringComparer.Ordinal);
    public Dictionary<(int UserId, string ProjectIdAcc), bool> UserProjects { get; } = new();
    public HashSet<string> ProjetosInativos { get; } = new(StringComparer.Ordinal);
    public bool AcessoProjetoPadrao { get; set; } = true;
    public List<PermittedProject> ProjetosPermitidos { get; } = new();
    public List<PermProfile> Perfis { get; } = new();
    public int ProximoUserId { get; set; } = 1;
    public List<string> Escritas { get; } = new();

    public Task<bool> SchemaInstaladoAsync(CancellationToken ct) => Lanca ? throw new InvalidOperationException("banco fora") : Task.FromResult(Schema);
    public Task<int?> ResolverProjetoIdAsync(string projectIdAcc, CancellationToken ct) =>
        Task.FromResult(Projetos.TryGetValue(projectIdAcc, out var id) ? id : (int?)null);
    public Task<bool> UsuarioPossuiAcessoAtivoProjetoAsync(int userId, string projectIdAcc, CancellationToken ct)
    {
        var projectId = projectIdAcc.StartsWith("b.", StringComparison.Ordinal) ? projectIdAcc : "b." + projectIdAcc;
        if (!Projetos.ContainsKey(projectId) || ProjetosInativos.Contains(projectId)) return Task.FromResult(false);
        return Task.FromResult(UserProjects.TryGetValue((userId, projectId), out var ativo) ? ativo : AcessoProjetoPadrao);
    }
    public Task<int> RegistrarAcessoAsync(string? accUserId, string email, string? nome, CancellationToken ct) => Task.FromResult(ProximoUserId);
    public Task<PermUser?> ObterUsuarioAsync(int userId, CancellationToken ct) =>
        Task.FromResult(Usuarios.TryGetValue(userId, out var usuario) ? usuario : null);
    public Task<IReadOnlyList<PermittedProject>> ListarProjetosPermitidosAsync(int userId, CancellationToken ct)
    {
        var resultado = ProjetosPermitidos.Where(p => p.IsAtivo
            && UserProjects.TryGetValue((userId, p.ProjectIdAcc), out var ativo) && ativo).ToList();
        return Task.FromResult<IReadOnlyList<PermittedProject>>(resultado);
    }
    public Task<UserPermissionData> CarregarDadosAsync(int userId, bool masterPorConfiguracao, CancellationToken ct)
    {
        if (Lanca) throw new InvalidOperationException("banco fora");
        var d = Dados.TryGetValue(userId, out var x) ? x : PermissionResolverTests.Usuario();
        return Task.FromResult(d with { IsConfiguredMasterAdmin = masterPorConfiguracao || d.IsConfiguredMasterAdmin });
    }
    public Task<IReadOnlyList<PermProfile>> ListarPerfisAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PermProfile>>(Perfis);
    public Task<int> CriarPerfilAsync(string nome, string? descricao, IEnumerable<string> permissoes, CancellationToken ct) { Escritas.Add("CriarPerfil"); return Task.FromResult(99); }
    public Task AtualizarPerfilAsync(int profileId, string nome, string? descricao, bool ativo, CancellationToken ct) { Escritas.Add("AtualizarPerfil"); return Task.CompletedTask; }
    public Task ExcluirPerfilAsync(int profileId, CancellationToken ct) { Escritas.Add("ExcluirPerfil"); Perfis.RemoveAll(p => p.ProfileId == profileId && !p.IsSystem); return Task.CompletedTask; }
    public Task DefinirPermissoesPerfilAsync(int profileId, IEnumerable<string> permissoes, CancellationToken ct) { Escritas.Add("PermissoesPerfil"); return Task.CompletedTask; }
    public Task<IReadOnlyList<PermUser>> ListarUsuariosAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PermUser>>(Array.Empty<PermUser>());
    public Task<int> CadastrarUsuarioAsync(string email, string? nome, CancellationToken ct) => Task.FromResult(50);
    public Task<UserProjectView> ObterUsuarioNoProjetoAsync(int userId, int? projetoId, CancellationToken ct)
    {
        var projectIdAcc = Projetos.FirstOrDefault(p => p.Value == projetoId).Key;
        var ativo = projetoId is null || (!string.IsNullOrEmpty(projectIdAcc)
            && UserProjects.TryGetValue((userId, projectIdAcc), out var vinculo) && vinculo);
        return Task.FromResult(new UserProjectView(new PermUser(userId, null, "x@na.com", null, true, null), ativo,
            Array.Empty<int>(), Array.Empty<PermissionOverride>()));
    }
    public Task DefinirPerfisUsuarioAsync(int userId, int? projetoId, IEnumerable<int> profileIds, CancellationToken ct) { Escritas.Add("PerfisUsuario"); return Task.CompletedTask; }
    public Task DefinirOverridesUsuarioAsync(int userId, int? projetoId, IEnumerable<PermissionOverride> overrides, CancellationToken ct) { Escritas.Add("OverridesUsuario"); return Task.CompletedTask; }
    public Task DefinirAtivoNoProjetoAsync(int userId, int projetoId, bool ativo, CancellationToken ct) { Escritas.Add("Ativo"); return Task.CompletedTask; }
    public Task AuditarAsync(int? alteradoPor, string? alteradoPorEmail, int? usuarioAfetado, int? projetoId, string alvo, string? referencia,
        string? anterior, string? novo, CancellationToken ct) { Escritas.Add("Audit:" + alvo); return Task.CompletedTask; }
    public Task<IReadOnlyList<PermissionAuditEntry>> LerAuditoriaAsync(int top, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PermissionAuditEntry>>(Array.Empty<PermissionAuditEntry>());
}
