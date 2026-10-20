using APS.WebApp.Controllers;
using APS.WebApp.Services.Costs;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

/// <summary>Custos do Projeto: erros de regra chegam à tela (400 com a mensagem) e falta de permissão é 403 (sem Forbid()).</summary>
public sealed class CostManagementControllerTests
{
    private const string Project = "b.22222222-2222-2222-2222-222222222222";

    private static CostManagementController Criar(ICostManagementService svc, PermissionCheck acesso = PermissionCheck.Allowed, PermissionCheck permissao = PermissionCheck.Allowed, bool masterAdmin = true) =>
        new(new Access(acesso), new Perm(permissao, masterAdmin), svc, NullLogger<CostManagementController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task RegraDeRateio_Viola_400ComMensagemDaRegra()
    {
        var r = await Criar(new Svc { Erro = new InvalidOperationException("Valores MANUAL excedem o total contratado.") })
            .AddDocumentsBatch(9, Project, new[] { new CostDocumentInput("DOC-1", null, 999, null, "MANUAL", "AUTO") }, default);
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        Assert.Contains("MANUAL excedem", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task SemProjetoOuSemPermissao_403Explicito()
    {
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Criar(new Svc(), acesso: PermissionCheck.Denied).Professionals(Project, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Criar(new Svc(), permissao: PermissionCheck.Denied).Professionals(Project, default)).StatusCode);
    }

    [Fact]
    public async Task HomologacaoDeCustos_ExigeMasterAdminMesmoComSafeMode()
    {
        var denied = Assert.IsType<ObjectResult>(await Criar(new Svc(), masterAdmin: false).Professionals(Project, default));
        Assert.Equal(403, denied.StatusCode);
        Assert.Contains("Custos do Projeto", System.Text.Json.JsonSerializer.Serialize(denied.Value));

        var allowed = await Criar(new Svc(), masterAdmin: true).Professionals(Project, default);
        Assert.IsType<OkObjectResult>(allowed);
    }

    [Fact]
    public void ControllersDeCustosExigemPermissaoExclusivaDeAnalise()
    {
        var controllers = new[] { typeof(CostManagementController), typeof(ProjectCostHistoryController) };
        foreach (var controller in controllers)
        {
            var attribute = Assert.Single(controller.GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: true).Cast<RequirePermissionAttribute>());
            Assert.Equal(new[] { "planning.costs.analysis" }, attribute.Keys);
        }
    }

    [Fact]
    public async Task FalhaInesperada_503SemDetalhe()
    {
        var r = Assert.IsType<ObjectResult>(await Criar(new Svc { Erro = new TimeoutException("sql interno") }).Professionals(Project, default));
        Assert.Equal(503, r.StatusCode);
        Assert.DoesNotContain("sql interno", System.Text.Json.JsonSerializer.Serialize(r.Value));
    }

    [Fact]
    public async Task EdicaoEListasNovas_ChegamAoServicoComProjeto()
    {
        var svc = new Svc();
        var c = Criar(svc);
        Assert.IsType<OkObjectResult>(await c.UpdateProfessional(7, Project, new CostProfessionalUpdate("Ana", null, "ana", 160, true), default));
        Assert.IsType<OkObjectResult>(await c.UpdateItem(7, 3, Project, new CostItemUpdate("INSS", 1200, true), default));
        Assert.IsType<OkObjectResult>(await c.Addenda(9, Project, default));
        Assert.IsType<OkObjectResult>(await c.Allocation(9, Project, default));
        Assert.Equal(new[] { "upd-prof:7", "upd-item:7/3", "addenda:9", "allocation:9" }, svc.Chamadas);
        Assert.IsType<BadRequestObjectResult>(await c.Addenda(9, "nao-e-projeto", default));
    }

    private sealed class Access(PermissionCheck v) : IProjectAccessService { public Task<PermissionCheck> CheckProjectAccessAsync(string? _, CancellationToken __) => Task.FromResult(v); }
    private sealed class Perm(PermissionCheck v, bool masterAdmin) : IPermissionService
    {
        public bool Enforced => false;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> _, string? __, CancellationToken ___, bool anyProject = false) => Task.FromResult(v);
        public Task<EffectivePermissions> GetEffectiveAsync(string? _, CancellationToken __) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken _) => Task.FromResult<CurrentPermissionUser?>(new CurrentPermissionUser(1, "", "master@na.local", "Master", masterAdmin));
        public Task<int?> ResolverProjetoIdAsync(string? _, CancellationToken __) => throw new NotSupportedException();
    }

    /// <summary>Serviço falso: registra as chamadas; Erro (se definido) é lançado por qualquer operação.</summary>
    private sealed class Svc : ICostManagementService
    {
        public Exception? Erro; public List<string> Chamadas { get; } = new();
        private Task<T> R<T>(string chamada, T valor) { if (Erro is not null) throw Erro; Chamadas.Add(chamada); return Task.FromResult(valor); }
        public Task<CostSummary> SummaryAsync(string p, CancellationToken ct) => R("summary", new CostSummary(p, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        public Task<IReadOnlyList<CostProfessional>> ProfessionalsAsync(string p, CancellationToken ct) => R<IReadOnlyList<CostProfessional>>("professionals", Array.Empty<CostProfessional>());
        public Task<IReadOnlyList<CostExpense>> ExpensesAsync(string p, CancellationToken ct) => R<IReadOnlyList<CostExpense>>("expenses", Array.Empty<CostExpense>());
        public Task<IReadOnlyList<CostPackage>> PackagesAsync(string p, CancellationToken ct) => R<IReadOnlyList<CostPackage>>("packages", Array.Empty<CostPackage>());
        public Task<IReadOnlyList<CostDocument>> DocumentsAsync(string p, int id, CancellationToken ct) => R<IReadOnlyList<CostDocument>>($"documents:{id}", Array.Empty<CostDocument>());
        public Task<IReadOnlyList<CostItem>> ItemsAsync(string p, int id, CancellationToken ct) => R<IReadOnlyList<CostItem>>($"items:{id}", Array.Empty<CostItem>());
        public Task<int> AddProfessionalAsync(string p, CostProfessionalInput i, CancellationToken ct) => R("add-prof", 1);
        public Task<int> AddExpenseAsync(string p, CostExpenseInput i, CancellationToken ct) => R("add-exp", 1);
        public Task<int> AddPackageAsync(string p, CostPackageInput i, CancellationToken ct) => R("add-pkg", 1);
        public Task<int> AddDocumentAsync(string p, int id, CostDocumentInput i, CancellationToken ct) => R("add-doc", 1);
        public Task<int> AddItemAsync(string p, int id, CostItemInput i, CancellationToken ct) => R("add-item", 1);
        public Task<int> AddDocumentsBatchAsync(string p, int id, IReadOnlyList<CostDocumentInput> i, CancellationToken ct) => R($"add-docs:{id}", i.Count);
        public Task<int> AddAddendumAsync(string p, int id, CostAddendumInput i, CancellationToken ct) => R("add-addendum", 1);
        public Task<bool> DeactivateAsync(string p, string e, int id, CancellationToken ct) => R($"deactivate:{e}/{id}", true);
        public Task<CostImportPreview> PreviewExcelAsync(string p, Stream f, CancellationToken ct) => R("preview", new CostImportPreview(Array.Empty<CostImportRow>(), 0, 0, 0));
        public Task<CostImportCommitResult> CommitExcelAsync(string p, Stream f, CancellationToken ct) => R("commit", new CostImportCommitResult(0, 0));
        public Task<IReadOnlyList<CostRecognitionLine>> RecognitionAsync(string p, CancellationToken ct) => R<IReadOnlyList<CostRecognitionLine>>("recognition", Array.Empty<CostRecognitionLine>());
        public Task<bool> UpdateProfessionalAsync(string p, int id, CostProfessionalUpdate i, CancellationToken ct) => R($"upd-prof:{id}", true);
        public Task<bool> UpdateItemAsync(string p, int pid, int id, CostItemUpdate i, CancellationToken ct) => R($"upd-item:{pid}/{id}", true);
        public Task<IReadOnlyList<CostAddendum>> AddendaAsync(string p, int id, CancellationToken ct) => R<IReadOnlyList<CostAddendum>>($"addenda:{id}", Array.Empty<CostAddendum>());
        public Task<CostPackageAllocation> AllocationAsync(string p, int id, CancellationToken ct) => R($"allocation:{id}", new CostPackageAllocation(id, 0, 0, Array.Empty<CostDocumentAllocation>(), null));
    }
}
