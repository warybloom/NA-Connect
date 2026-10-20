using System.Reflection;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace NA_Connect.Planning.Tests.Permissions;

/// <summary>
/// Contrato: TODO endpoint do N&amp;A Connect declara [RequirePermission] (action ou classe) ou [PermissionExempt] com
/// motivo. Endpoint novo sem declaração quebra este teste — nada nasce liberado por esquecimento.
/// </summary>
public class EndpointPermissionContractTests
{
    private static readonly Assembly App = typeof(PermissionCatalog).Assembly;

    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints() =>
        App.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
                .Select(m => (t, m)));

    private static bool EhHttp(MethodInfo m) => m.GetCustomAttributes<HttpMethodAttribute>().Any();

    [Fact]
    public void TodaActionTemPermissaoOuIsencao()
    {
        var sem = Endpoints()
            .Where(e => e.Action.GetCustomAttribute<RequirePermissionAttribute>() is null
                && e.Action.GetCustomAttribute<PermissionExemptAttribute>() is null
                && e.Controller.GetCustomAttribute<RequirePermissionAttribute>() is null
                && e.Controller.GetCustomAttribute<PermissionExemptAttribute>() is null)
            .Select(e => $"{e.Controller.Name}.{e.Action.Name}")
            .ToList();
        Assert.True(sem.Count == 0, "Endpoints sem [RequirePermission]/[PermissionExempt]: " + string.Join(", ", sem));
    }

    [Fact]
    public void ExisteQuantidadeRelevanteDeEndpointsHttp() =>
        Assert.True(Endpoints().Count(e => EhHttp(e.Action)) >= 80, "A varredura deixou de enxergar os controllers.");

    [Fact]
    public void TodaKeyUsadaExisteNoCatalogo()
    {
        var atributos = Endpoints().Select(e => e.Action.GetCustomAttribute<RequirePermissionAttribute>())
            .Concat(App.GetTypes().Select(t => t.GetCustomAttribute<RequirePermissionAttribute>()))
            .OfType<RequirePermissionAttribute>().ToList();
        Assert.NotEmpty(atributos);
        foreach (var a in atributos)
        {
            Assert.NotEmpty(a.Keys);
            Assert.All(a.Keys, k => Assert.True(PermissionCatalog.Existe(k), $"Key '{k}' fora do catálogo."));
        }
    }

    [Fact]
    public void IsencoesSaoPoucas_EComMotivo()
    {
        var isentos = Endpoints()
            .Select(e => (Nome: $"{e.Controller.Name}.{e.Action.Name}",
                Attr: e.Action.GetCustomAttribute<PermissionExemptAttribute>() ?? e.Controller.GetCustomAttribute<PermissionExemptAttribute>()))
            .Where(x => x.Attr != null).ToList();
        Assert.All(isentos, x => Assert.True(x.Attr!.Motivo.Length >= 20, $"{x.Nome}: motivo vago."));
        var controllersIsentos = isentos.Select(x => x.Nome.Split('.')[0]).Distinct().OrderBy(x => x).ToArray();
        // Lista fechada: novos isentos precisam ser revisados conscientemente.
        Assert.Equal(new[] { "DataController", "HomeController", "NaAuthController", "OAuthController", "PermissionsController", "ProjectIdentityController", "SidebarController", "WebhooksController" }, controllersIsentos);
        // Sidebar GET: sessão + filtro estrito interno; Access retorna apenas a decisão do usuário.
        Assert.Equal(new[] { "SidebarController.Get", "SidebarController.GetAccess" },
            isentos.Where(x => x.Nome.StartsWith("SidebarController.")).Select(x => x.Nome).OrderBy(x => x).ToArray());
        // 05/10/2026 (06A): login N&A local por e-mail + senha — ocorre antes de existir identidade, como o OAuth.
        // ChangePassword: troca obrigatória da PRÓPRIA senha no primeiro acesso (usuário vem de Auth:UserId da sessão).
        Assert.Equal(new[] { "NaAuthController.ChangePassword", "NaAuthController.Login" },
            isentos.Where(x => x.Nome.StartsWith("NaAuthController.")).Select(x => x.Nome).OrderBy(x => x).ToArray());
        // 30/09/2026: leitura da Logo do Projeto (identidade visual, não é dado de negócio) — exige sessão no próprio
        // controller; ALTERAR a logo continua exigindo settings.projects.edit.
        Assert.Equal(new[] { "ProjectIdentityController.List", "ProjectIdentityController.Logo" },
            isentos.Where(x => x.Nome.StartsWith("ProjectIdentityController.")).Select(x => x.Nome).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "DataController.Hubs", "DataController.Projetos" },
            isentos.Where(x => x.Nome.StartsWith("DataController.")).Select(x => x.Nome).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "PermissionsController.Me" },
            isentos.Where(x => x.Nome.StartsWith("PermissionsController.")).Select(x => x.Nome).ToArray());
    }

    [Theory]
    [InlineData("RobotConfigurationController", "Get", "robot.view")]
    [InlineData("RobotConfigurationController", "Save", "robot.configure")]
    [InlineData("RobotConfigurationController", "Run", "robot.run")]
    [InlineData("RobotRunController", "Status", "robot.view")]
    [InlineData("RobotRunController", "Run", "robot.run")]
    [InlineData("DataController", "UploadNewVersion", "cde.docs.upload")]
    [InlineData("DataController", "CreateProjectFromTemplate", "projects.create")]
    [InlineData("PermissionsController", "PerfisUsuario", "permissions.manage")]
    public void AcoesSensiveis_TemAKeyEsperada(string controller, string action, string key)
    {
        var e = Endpoints().Single(x => x.Controller.Name == controller && x.Action.Name == action);
        var attr = e.Action.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(new[] { key }, attr!.Keys);
    }
}
