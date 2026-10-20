using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Mesmo padrão de QualityReportsTests/ProductionDocumentsReportsTests/ProductionStatusReportsTests:
/// sessão ausente nunca chama dependências, 401/403/503 do gate nunca lê o repository, escopo inválido
/// nunca chama dependências, requisição autorizada preserva o projeto e devolve o relatório, falha do
/// repository nunca expõe detalhe interno. Um bloco por controller novo desta sessão (HOLD, Desvios de
/// Escopo, CAP Interno/Externo, Weekly detalhado, Cronograma, LDP).
/// </summary>
public class OperationalReportsControllersTests
{
    private const string Hub = "b.11111111-1111-1111-1111-111111111111";
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    private static DefaultHttpContext AuthenticatedContext()
    {
        var context = new DefaultHttpContext { Session = new MemorySession() };
        context.Session.SetString("AccessToken", "session-only");
        return context;
    }

    // ---------------------------------------------------------------- HOLD ----
    [Fact]
    public async Task Holds_MissingSessionNeverCallsDependencies()
    {
        var access = new Access { Decision = PermissionCheck.Unauthenticated }; var repository = new HoldRepo();
        var controller = new HoldReportsController(access, new Perm(), repository, NullLogger<HoldReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Holds(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Holds_AuthorizedRequestPreservesFiltersAndReturnsReport()
    {
        var access = new Access(); var repository = new HoldRepo();
        var controller = new HoldReportsController(access, new Perm(), repository, NullLogger<HoldReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.Holds(Hub, Project, "ABERTO", "Tubulação", "DOC"));
        var report = Assert.IsType<HoldReport>(result.Value);
        Assert.Equal(Project, repository.Project); Assert.Equal("ABERTO", repository.Status);
        Assert.Equal("Tubulação", repository.Disciplina); Assert.Equal("DOC", repository.Search);
        Assert.Equal(1, report.TotalRows);
    }

    [Theory]
    [InlineData(PermissionCheck.Denied, 403)]
    [InlineData(PermissionCheck.Unavailable, 503)]
    public async Task Holds_FailedAccessNeverReadsReport(PermissionCheck decision, int status)
    {
        var access = new Access { Decision = decision }; var repository = new HoldRepo();
        var controller = new HoldReportsController(access, new Perm(), repository, NullLogger<HoldReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Holds(Hub, Project));
        Assert.Equal(status, result.StatusCode); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Holds_RepositoryFailureIs503WithoutInternalDetails()
    {
        var access = new Access(); var repository = new HoldRepo { Throws = true };
        var controller = new HoldReportsController(access, new Perm(), repository, NullLogger<HoldReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<ObjectResult>(await controller.Holds(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    // ------------------------------------------------------ Desvios de Escopo ----
    [Fact]
    public async Task ScopeDeviations_MissingSessionNeverCallsDependencies()
    {
        var access = new Access { Decision = PermissionCheck.Unauthenticated }; var repository = new ScopeRepo();
        var controller = new ScopeDeviationReportsController(access, new Perm(), repository, NullLogger<ScopeDeviationReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.ScopeDeviations(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task ScopeDeviations_AuthorizedRequestReturnsReport()
    {
        var access = new Access(); var repository = new ScopeRepo();
        var controller = new ScopeDeviationReportsController(access, new Perm(), repository, NullLogger<ScopeDeviationReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.ScopeDeviations(Hub, Project));
        var report = Assert.IsType<ScopeDeviationReport>(result.Value);
        Assert.Equal(Project, repository.Project); Assert.Equal(1, report.TotalRows);
    }

    [Fact]
    public async Task ScopeDeviations_RepositoryFailureIs503WithoutInternalDetails()
    {
        var access = new Access(); var repository = new ScopeRepo { Throws = true };
        var controller = new ScopeDeviationReportsController(access, new Perm(), repository, NullLogger<ScopeDeviationReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<ObjectResult>(await controller.ScopeDeviations(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    // ---------------------------------------------------------- CAP Interno/Externo ----
    [Fact]
    public async Task Cap_MissingSessionNeverCallsDependencies()
    {
        var access = new Access { Decision = PermissionCheck.Unauthenticated }; var internalRepo = new ActionRepo(); var externalRepo = new ActionRepo();
        var controller = new CapReportsController(access, new Perm(), internalRepo, externalRepo, NullLogger<CapReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Internal(Hub, Project));
        Assert.IsType<UnauthorizedObjectResult>(await controller.External(Hub, Project));
        Assert.Equal(2, access.Calls); Assert.Equal(0, internalRepo.Calls); Assert.Equal(0, externalRepo.Calls);
    }

    [Fact]
    public async Task Cap_InternalAndExternalHitOnlyTheirOwnRepository()
    {
        var access = new Access(); var internalRepo = new ActionRepo(); var externalRepo = new ActionRepo();
        var controller = new CapReportsController(access, new Perm(), internalRepo, externalRepo, NullLogger<CapReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };

        Assert.IsType<OkObjectResult>(await controller.Internal(Hub, Project));
        Assert.Equal(1, internalRepo.Calls); Assert.Equal(0, externalRepo.Calls);

        Assert.IsType<OkObjectResult>(await controller.External(Hub, Project));
        Assert.Equal(1, internalRepo.Calls); Assert.Equal(1, externalRepo.Calls);
    }

    [Fact]
    public async Task Cap_RepositoryFailureIs503WithoutInternalDetails()
    {
        var access = new Access(); var internalRepo = new ActionRepo { Throws = true }; var externalRepo = new ActionRepo();
        var controller = new CapReportsController(access, new Perm(), internalRepo, externalRepo, NullLogger<CapReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<ObjectResult>(await controller.Internal(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    // ------------------------------------------------------------- Weekly detalhado ----
    [Fact]
    public async Task WeeklyDetails_MissingSessionNeverCallsDependencies()
    {
        var access = new Access { Decision = PermissionCheck.Unauthenticated }; var repository = new WeeklyDetailsRepo();
        var controller = new WeeklyDetailsController(access, new Perm(), repository, NullLogger<WeeklyDetailsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Details(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task WeeklyDetails_AuthorizedRequestReturnsReport()
    {
        var access = new Access(); var repository = new WeeklyDetailsRepo();
        var controller = new WeeklyDetailsController(access, new Perm(), repository, NullLogger<WeeklyDetailsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.Details(Hub, Project));
        Assert.Equal(Project, repository.Project);
        Assert.Equal(1, Assert.IsType<WeeklyDetailsReport>(result.Value).TotalRows);
    }

    // ------------------------------------------------------------------ Cronograma ----
    [Fact]
    public async Task Schedule_MissingSessionNeverCallsDependencies()
    {
        var access = new Access { Decision = PermissionCheck.Unauthenticated }; var repository = new ScheduleRepo();
        var controller = new ScheduleReportsController(access, new Perm(), repository, NullLogger<ScheduleReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Schedule(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Schedule_AuthorizedRequestReturnsReport()
    {
        var access = new Access(); var repository = new ScheduleRepo();
        var controller = new ScheduleReportsController(access, new Perm(), repository, NullLogger<ScheduleReportsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.Schedule(Hub, Project));
        Assert.Equal(Project, repository.Project);
        Assert.Equal(1, Assert.IsType<ScheduleReport>(result.Value).TotalRows);
    }

    // -------------------------------------------------------------------------- LDP ----
    [Fact]
    public async Task Ldp_MissingSessionNeverCallsDependencies()
    {
        var access = new Access { Decision = PermissionCheck.Unauthenticated }; var repository = new LdpRepo();
        var controller = new LdpDetailsController(access, new Perm(), repository, NullLogger<LdpDetailsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new MemorySession() } } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Ldp(Hub, Project));
        Assert.Equal(1, access.Calls); Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Ldp_AuthorizedRequestReturnsReportWithGates()
    {
        var access = new Access(); var repository = new LdpRepo();
        var controller = new LdpDetailsController(access, new Perm(), repository, NullLogger<LdpDetailsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.Ldp(Hub, Project));
        var report = Assert.IsType<LdpDetailsReport>(result.Value);
        Assert.Equal(Project, repository.Project);
        Assert.Equal(2, Assert.Single(report.Rows).Gates.Count);
    }

    [Fact]
    public async Task Ldp_RepositoryFailureIs503WithoutInternalDetails()
    {
        var access = new Access(); var repository = new LdpRepo { Throws = true };
        var controller = new LdpDetailsController(access, new Perm(), repository, NullLogger<LdpDetailsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = AuthenticatedContext() } };
        var result = Assert.IsType<ObjectResult>(await controller.Ldp(Hub, Project));
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("secret", result.Value!.ToString());
    }

    // ------------------------------------------------------------------------ fakes ----
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

    private sealed class HoldRepo : IHoldReportRepository
    {
        public int Calls; public string? Project, Status, Disciplina, Search; public bool Throws;
        public Task<HoldReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId; Status = status; Disciplina = disciplina; Search = search;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new HoldReport(1, 1, null, page, pageSize, new[] { new HoldRow(1, "1", "H-1", "DOC-1", "Tubulação", "Título", "0", "Motivo", "ABERTO", "Fulano") }));
        }
    }

    private sealed class ScopeRepo : IScopeDeviationReportRepository
    {
        public int Calls; public string? Project; public bool Throws;
        public Task<ScopeDeviationReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new ScopeDeviationReport(1, 1, null, page, pageSize, new[] { new ScopeDeviationRow(1, "1", "Descrição", "Tubulação", "D-1", "01/01/2026", "10", "PENDENTE", "SIM", "NAO") }));
        }
    }

    private sealed class ActionRepo : IInternalActionReportRepository, IExternalActionReportRepository
    {
        public int Calls; public bool Throws;
        public Task<ActionLogReport> ReadAsync(string projectId, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            return Task.FromResult(new ActionLogReport(1, 1, null, page, pageSize, new[] { new ActionLogRow(1, "1", "01/01/2026", "Nome", "Empresa", "1", "Frente", "AFE-1", "Descrição", null, null, "Prog", "Reprog", "Concl", "N", "ABERTO", "Comentário") }));
        }
    }

    private sealed class WeeklyDetailsRepo : IWeeklyDetailsReportRepository
    {
        public int Calls; public string? Project;
        public Task<WeeklyDetailsReport> ReadAsync(string projectId, string? disciplina, string? status, string? responsavel, string? fase, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId;
            return Task.FromResult(new WeeklyDetailsReport(1, 1, null, page, pageSize, new[] { new WeeklyDetailRow(1, "NA-1", "Tubulação", "Tarefa", "10%", "5%", "Fulano", "60%", "EM ANDAMENTO", null, null, "OK", "OK", "OK", 10m, 5m, 5m, "SIM", "obs", "DADO", "OK", null) }));
        }
    }

    private sealed class ScheduleRepo : IScheduleReportRepository
    {
        public int Calls; public string? Project;
        public Task<ScheduleReport> ReadAsync(string projectId, string? disciplina, string? fase, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId;
            return Task.FromResult(new ScheduleReport(1, 1, null, page, pageSize, new[] { new ScheduleRow(1, "NA-1", "Tarefa", "1.1", "Tubulação", "60%", 60, null, null, 50m, 40m, 100m, 40m, "VALIDO", "OK", "CR-1") }));
        }
    }

    private sealed class LdpRepo : ILdpDetailsReportRepository
    {
        public int Calls; public string? Project; public bool Throws;
        public Task<LdpDetailsReport> ReadAsync(string projectId, string? discipline, string? documentStatus, string? gateFase, string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; Project = projectId;
            if (Throws) throw new InvalidOperationException("secret SQL details");
            var gates = new[] {
                new LdpGateInfo(30, "30%", true, "30%", "EM ANDAMENTO", "APROVADO", null, null, 30m, 20m),
                new LdpGateInfo(60, "60%", true, "60%", "EM ANDAMENTO", "APROVADO", null, null, 60m, 10m),
            };
            return Task.FromResult(new LdpDetailsReport(1, 1, null, page, pageSize, new[] { new LdpDetailRow(1, "NA-1", "CLI-1", "Título", "Tubulação", "EM ANDAMENTO", "EMISSAO", "APROVADO", 50m, 50m, 30m, null, null, null, null, 10m, 5m, 100m, 50m, gates) }));
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
