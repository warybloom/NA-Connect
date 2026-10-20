using APS.WebApp.Controllers;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

public sealed class ProductionAuditControllerTests
{
    private const string Project="b.22222222-2222-2222-2222-222222222222";
    private static ProductionAuditController Create(PermissionCheck access=PermissionCheck.Allowed, PermissionCheck permission=PermissionCheck.Allowed, ProductionAuditScope? scope=null)
        => new(new Access{Value=access},new Perm{Value=permission},new Repo{Scope=scope??new(ProductionAuditScopeStatus.Ok,"BML011.013",null)},NullLogger<ProductionAuditController>.Instance)
        {ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};

    [Fact] public async Task SemSessao_Retorna401(){var r=await Create(PermissionCheck.Unauthenticated).Entries(Project,null,null,null,null,null);Assert.IsType<UnauthorizedObjectResult>(r);}
    [Fact] public async Task SemUserProjects_Retorna403(){var r=await Create(PermissionCheck.Denied).Entries(Project,null,null,null,null,null);Assert.Equal(403,Assert.IsType<ObjectResult>(r).StatusCode);}
    [Fact] public async Task Permitido_ConsultaSomenteProjectId(){var r=await Create().Entries(Project,null,null,null,null,null);Assert.IsType<OkObjectResult>(r);}
    [Fact] public async Task CrAusente_RetornaConflitoClaro(){var r=await Create(scope:new(ProductionAuditScopeStatus.CrNotConfigured,null,"CR/OS não configurado para este projeto.")).Entries(Project,null,null,null,null,null);Assert.Equal(409,Assert.IsAssignableFrom<ObjectResult>(r).StatusCode);}

    // ---------- Lançamentos (Pessoas / Disciplinas / Documentos): filtros chegam ao backend ----------
    private static (ProductionAuditController C, Repo R) ComRepo()
    {
        var repo=new Repo{Scope=new(ProductionAuditScopeStatus.Ok,"BML011.013",null)};
        return (new ProductionAuditController(new Access{Value=PermissionCheck.Allowed},new Perm{Value=PermissionCheck.Allowed},repo,NullLogger<ProductionAuditController>.Instance)
            {ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}},repo);
    }

    [Fact] public async Task Pessoas_RepassaPeriodoPessoaDisciplinaDocumentoEPagina()
    {
        var (c,r)=ComRepo();
        Assert.IsType<OkObjectResult>(await c.People(Project,new DateTime(2026,8,1),new DateTime(2026,8,31),"MECANICA","DOC-1","Sulivan",page:3,pageSize:25));
        Assert.Equal("people",r.UltimoMetodo);
        Assert.Equal(new ProductionAuditFilters(new DateTime(2026,8,1),new DateTime(2026,8,31),"MECANICA","DOC-1","Sulivan",3,25),r.Ultimo);
    }

    [Fact] public async Task Disciplinas_NaoIgnoramMaisOsFiltros()
    {
        var (c,r)=ComRepo();
        Assert.IsType<OkObjectResult>(await c.Disciplines(Project,new DateTime(2026,9,1),null,null,null,"Ana"));
        Assert.Equal(new DateTime(2026,9,1),r.Ultimo!.From); Assert.Equal("Ana",r.Ultimo.Person);
        // Endpoint antigo de estouro por disciplina também passa a repassar os filtros.
        var (c2,r2)=ComRepo();
        await c2.DisciplineOverruns(Project,null,new DateTime(2026,9,30),"CIVIL","DOC",null);
        Assert.Equal(new ProductionAuditFilters(null,new DateTime(2026,9,30),"CIVIL","DOC",null),r2.UltimoFiltroOverruns);
    }

    [Fact] public async Task Documentos_StatusValidoRepassado_StatusInvalido400()
    {
        var (c,r)=ComRepo();
        Assert.IsType<OkObjectResult>(await c.Documents(Project,null,null,null,null,"Ana"," ESTOURADO "));
        Assert.Equal("ESTOURADO",r.Ultimo!.Status); Assert.Equal("Ana",r.Ultimo.Person);
        var (c2,r2)=ComRepo();
        Assert.IsType<BadRequestObjectResult>(await c2.Documents(Project,null,null,null,null,null,"QUALQUER"));
        Assert.Null(r2.UltimoMetodo);
    }

    [Fact] public async Task Lancamentos_DrilldownExato_E_PaginaInvalida400()
    {
        var (c,r)=ComRepo();
        await c.Entries(Project,null,null,null,"BML-1",null,exact:true);
        Assert.True(r.UltimoFiltroEntries!.Exact); Assert.Equal("BML-1",r.UltimoFiltroEntries.Document);
        Assert.IsType<BadRequestObjectResult>(await ComRepo().C.People(Project,null,null,null,null,null,page:1,pageSize:500));
        Assert.IsType<BadRequestObjectResult>(await ComRepo().C.People("b.nao-e-guid",null,null,null,null,null));
    }

    [Fact] public async Task Lancamentos_SemUserProjects403_SemPermissao403()
    {
        Assert.Equal(403,Assert.IsType<ObjectResult>(await Create(PermissionCheck.Denied).People(Project,null,null,null,null,null)).StatusCode);
        Assert.Equal(403,Assert.IsType<ObjectResult>(await Create(permission:PermissionCheck.Denied).Documents(Project,null,null,null,null,null,null)).StatusCode);
    }

    private sealed class Access: IProjectAccessService { public PermissionCheck Value; public Task<PermissionCheck> CheckProjectAccessAsync(string? _,CancellationToken __)=>Task.FromResult(Value); }
    private sealed class Perm: IPermissionService { public PermissionCheck Value; public bool Enforced=>false; public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> _,string? __,CancellationToken ___,bool anyProject=false)=>Task.FromResult(Value); public Task<EffectivePermissions> GetEffectiveAsync(string? _,CancellationToken __)=>throw new NotImplementedException(); public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken _)=>throw new NotImplementedException(); public Task<int?> ResolverProjetoIdAsync(string? _,CancellationToken __)=>throw new NotImplementedException(); }
    private sealed class Repo: IProductionAuditRepository { public required ProductionAuditScope Scope; public Task<ProductionAuditScope> ResolveScopeAsync(string _,CancellationToken __)=>Task.FromResult(Scope); private void Check(){if(Scope.Status!=ProductionAuditScopeStatus.Ok)throw new ProductionAuditScopeException(Scope);} public ProductionAuditFilters? UltimoFiltroEntries, UltimoFiltroOverruns; public Task<ProductionAuditPage> ReadEntriesAsync(string _,ProductionAuditFilters __,CancellationToken ___){Check();UltimoFiltroEntries=__;return Task.FromResult(new ProductionAuditPage([],0,Scope.CrOs!));} public Task<IReadOnlyList<ProductionAuditOverrun>> ReadDocumentOverrunsAsync(string _,ProductionAuditFilters __,CancellationToken ___){Check();return Task.FromResult<IReadOnlyList<ProductionAuditOverrun>>([]);} public Task<IReadOnlyList<ProductionAuditDiscipline>> ReadDisciplineOverrunsAsync(string _,ProductionAuditFilters __,CancellationToken ___){Check();UltimoFiltroOverruns=__;return Task.FromResult<IReadOnlyList<ProductionAuditDiscipline>>([]);} public Task<ProductionAuditOverview> ReadReconciliationAsync(string _,ProductionAuditFilters __,CancellationToken ___){Check();return Task.FromResult(new ProductionAuditOverview(Scope.CrOs!,[],[],0));}
        public ProductionAuditFilters? Ultimo; public string? UltimoMetodo;
        public Task<ProductionAuditPeoplePage> ReadPeopleAsync(string _,ProductionAuditFilters f,CancellationToken ___){Check();Ultimo=f;UltimoMetodo="people";return Task.FromResult(new ProductionAuditPeoplePage([],0,Scope.CrOs!,0,0));}
        public Task<ProductionAuditDisciplinesResult> ReadDisciplinesAsync(string _,ProductionAuditFilters f,CancellationToken ___){Check();Ultimo=f;UltimoMetodo="disciplines";return Task.FromResult(new ProductionAuditDisciplinesResult([],Scope.CrOs!));}
        public Task<ProductionAuditDocumentsPage> ReadDocumentsAsync(string _,ProductionAuditFilters f,CancellationToken ___){Check();Ultimo=f;UltimoMetodo="documents";return Task.FromResult(new ProductionAuditDocumentsPage([],0,Scope.CrOs!,0,0,0));} }
}
