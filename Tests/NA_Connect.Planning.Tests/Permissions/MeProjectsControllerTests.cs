using APS.WebApp.Controllers;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace NA_Connect.Planning.Tests.Permissions;

public sealed class MeProjectsControllerTests
{
    private static (MeProjectsController C, FakePermissionRepository R, FakeNaCurrentUserAccessor Na) Criar(bool ativo = true)
    {
        var na = new FakeNaCurrentUserAccessor { Identidade = new NaCurrentUser(42, "interno@na.com", "Interno", "N&A") };
        var repo = new FakePermissionRepository();
        repo.Usuarios[42] = new PermUser(42, "acc-42", "interno@na.com", "Interno", ativo, null);
        return (new MeProjectsController(na, repo), repo, na);
    }

    [Fact]
    public async Task RetornaSomenteProjetosComVinculoEProjetoAtivos()
    {
        var (controller, repo, _) = Criar();
        repo.ProjetosPermitidos.Add(new(1, "b.one", "Projeto 1", 1, true));
        repo.ProjetosPermitidos.Add(new(2, "b.two", "Projeto 2", 2, true));
        repo.ProjetosPermitidos.Add(new(3, "b.inativo", "Inativo", 1, false));
        repo.UserProjects[(42, "b.one")] = true;
        repo.UserProjects[(42, "b.two")] = true;
        repo.UserProjects[(42, "b.inativo")] = true;

        var ok = Assert.IsType<OkObjectResult>((await controller.Projects(default)).Result);
        var projetos = Assert.IsType<MeProjectsController.MeProjectDto[]>(ok.Value);
        Assert.Equal(new[] { "b.one", "b.two" }, projetos.Select(p => p.ProjectIdAcc));
    }

    [Fact]
    public async Task SemUserProjects_RetornaArrayVazio()
    {
        var (controller, repo, _) = Criar();
        repo.ProjetosPermitidos.Add(new(1, "b.one", "Projeto 1", 1, true));

        var ok = Assert.IsType<OkObjectResult>((await controller.Projects(default)).Result);
        Assert.Empty(Assert.IsType<MeProjectsController.MeProjectDto[]>(ok.Value));
    }

    [Fact]
    public async Task UsuarioInexistenteOuInativo_Retorna401()
    {
        var (inativo, _, _) = Criar(ativo: false);
        Assert.IsType<UnauthorizedResult>((await inativo.Projects(default)).Result);

        var na = new FakeNaCurrentUserAccessor();
        var repo = new FakePermissionRepository();
        var inexistente = new MeProjectsController(na, repo);
        Assert.IsType<UnauthorizedResult>((await inexistente.Projects(default)).Result);
    }

    [Fact]
    public async Task MasterAdminRecebeSomenteSeusVinculos()
    {
        var (controller, repo, _) = Criar();
        repo.ProjetosPermitidos.Add(new(1, "b.one", "Projeto 1", 1, true));
        repo.ProjetosPermitidos.Add(new(2, "b.two", "Projeto 2", 1, true));
        repo.UserProjects[(42, "b.one")] = true;

        var ok = Assert.IsType<OkObjectResult>((await controller.Projects(default)).Result);
        var projetos = Assert.IsType<MeProjectsController.MeProjectDto[]>(ok.Value);
        Assert.Single(projetos);
        Assert.Equal("b.one", projetos[0].ProjectIdAcc);
    }

    [Fact]
    public async Task FuncionaSemAccessTokenEUsaSomenteAccessorNa()
    {
        var (controller, repo, na) = Criar();
        repo.ProjetosPermitidos.Add(new(1, "b.one", "Projeto 1", null, true));
        repo.UserProjects[(42, "b.one")] = true;

        var ok = Assert.IsType<OkObjectResult>((await controller.Projects(default)).Result);
        Assert.Single(Assert.IsType<MeProjectsController.MeProjectDto[]>(ok.Value));
        Assert.Equal(1, na.Chamadas);
    }
}
