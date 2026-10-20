using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

/// <summary>CDE / Lista de Documentos: mesmo contrato de segurança dos demais relatórios.</summary>
public class CdeReportsControllerTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    private static (CdeReportsController C, Access A, Repo R) Criar(bool autenticado = true, PermissionCheck decision = PermissionCheck.Allowed, bool throws = false)
    {
        var context = new DefaultHttpContext { Session = new MemorySession() };
        var access = new Access { Decision = autenticado ? decision : PermissionCheck.Unauthenticated }; var repo = new Repo { Throws = throws };
        return (new CdeReportsController(access, new Perm(), repo, NullLogger<CdeReportsController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } }, access, repo);
    }

    [Fact]
    public async Task SemSessao_NuncaChamaDependencias()
    {
        var (c, a, r) = Criar(autenticado: false);
        Assert.IsType<UnauthorizedObjectResult>(await c.DocumentList(Hub, Project));
        Assert.Equal(1, a.Calls); Assert.Equal(0, r.Calls);
    }

    [Theory]
    [InlineData(Hub, "' OR 1=1 --", null, 1, 50)]
    [InlineData(Hub, Project, "sideways", 1, 50)]
    [InlineData(Hub, Project, null, 0, 50)]
    [InlineData(Hub, Project, null, 1, 101)]
    public async Task EntradaInvalida_400SemChamarDependencias(string hub, string project, string? dir, int page, int pageSize)
    {
        var (c, a, r) = Criar();
        Assert.IsType<BadRequestObjectResult>(await c.DocumentList(hub, project, dir: dir, page: page, pageSize: pageSize));
        Assert.Equal(0, a.Calls); Assert.Equal(0, r.Calls);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task AcessoNegado_NuncaLeRepository(PermissionCheck decision, int status)
    {
        var (c, _, r) = Criar(decision: decision);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await c.DocumentList(Hub, Project)).StatusCode);
        Assert.Equal(0, r.Calls);
    }

    [Fact]
    public async Task Autorizado_RepassaProjetoFiltrosEOrdenacao()
    {
        var (c, _, r) = Criar();
        Assert.IsType<OkObjectResult>(await c.DocumentList(Hub, Project, "MECÂNICA", "NA-1", "revision", "desc", 2, 25));
        Assert.Equal(Project, r.Project); Assert.Equal("MECÂNICA", r.Disciplina); Assert.Equal("NA-1", r.Search);
        Assert.Equal("revision", r.Sort); Assert.True(r.Desc); Assert.Equal((2, 25), r.Pagina);
    }

    [Fact]
    public async Task FalhaDoRepository_503SemDetalheInterno()
    {
        var (c, _, _) = Criar(throws: true);
        var result = Assert.IsType<ObjectResult>(await c.DocumentList(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    private sealed class Access : IProjectAccessService
    {
        public int Calls; public PermissionCheck Decision = PermissionCheck.Allowed;
        public Task<PermissionCheck> CheckProjectAccessAsync(string? projectId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Decision); }
    }

    private sealed class Perm : IPermissionService
    {
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> keys, string? projectId, CancellationToken ct, bool anyProject = false) => Task.FromResult(PermissionCheck.Allowed);
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> ResolverProjetoIdAsync(string? projectId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Repo : ICdeDocumentListRepository
    {
        public int Calls; public bool Throws; public string? Project, Disciplina, Search, Sort; public bool Desc; public (int, int) Pagina;
        public Task<CdeDocumentListReport> ReadAsync(string projectId, string? disciplina, string? search, string? sortKey, bool sortDesc, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId; Disciplina = disciplina; Search = search; Sort = sortKey; Desc = sortDesc; Pagina = (page, pageSize);
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new CdeDocumentListReport(1, CdeDocumentListDefinitions.Tipo1, 0, 0, null, page, pageSize, []));
        }
    }

    private sealed class MemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsAvailable => true; public string Id => "test"; public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear(); public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
