using APS.WebApp.Services.Permissions;

namespace NA_Connect.Planning.Tests.Permissions;

/// <summary>Precedência: MasterAdmin &gt; DENY individual &gt; ALLOW individual &gt; perfis (união) &gt; negado por padrão.</summary>
public class PermissionResolverTests
{
    private const int ProjA = 10, ProjB = 20;

    internal static ProfileAssignment Perfil(string nome, int? projeto, params string[] keys) =>
        new(nome.GetHashCode(), nome, projeto, false, keys.ToHashSet(StringComparer.Ordinal));

    internal static UserPermissionData Usuario(IEnumerable<ProfileAssignment>? perfis = null, IEnumerable<PermissionOverride>? overrides = null,
        bool masterConfig = false, IEnumerable<int>? inativos = null) =>
        new(masterConfig, (perfis ?? Array.Empty<ProfileAssignment>()).ToList(), (overrides ?? Array.Empty<PermissionOverride>()).ToList(),
            (inativos ?? Array.Empty<int>()).ToHashSet());

    private static bool Pode(UserPermissionData u, string key, int? projeto = null) => PermissionResolver.Decide(u, key, projeto).Allowed;

    [Fact]
    public void MasterAdmin_PorConfiguracao_TemTudo_MesmoComDeny()
    {
        var u = Usuario(overrides: new[] { new PermissionOverride(null, "robot.run", PermissionEffects.Deny) }, masterConfig: true);
        Assert.True(Pode(u, "robot.run"));
        Assert.True(Pode(u, "permissions.manage", ProjA));
        Assert.Equal(PermissionCatalog.Keys.Count, PermissionResolver.Effective(u, null).Count);
    }

    [Fact]
    public void MasterAdmin_PorPerfilGlobal_TemTudo_MasPerfilDeProjetoNaoPromove()
    {
        var global = new ProfileAssignment(1, "MasterAdmin", null, true, new HashSet<string>());
        Assert.True(PermissionResolver.IsMasterAdmin(Usuario(new[] { global })));
        var soNoProjeto = new ProfileAssignment(1, "MasterAdmin", ProjA, true, new HashSet<string>());
        Assert.False(PermissionResolver.IsMasterAdmin(Usuario(new[] { soNoProjeto })));
        Assert.False(Pode(Usuario(new[] { soNoProjeto }), "cde.view", ProjA));
    }

    [Fact]
    public void UmPerfil_ConcedeSomenteSuasKeys()
    {
        var u = Usuario(new[] { Perfil("Leitor CDE", null, "cde.view", "cde.document-list.view") });
        Assert.True(Pode(u, "cde.view"));
        Assert.True(Pode(u, "cde.document-list.view", ProjA));
        Assert.False(Pode(u, "cde.docs.view"));
        Assert.Equal(PermissionDecisionSource.Perfil, PermissionResolver.Decide(u, "cde.view", null).Source);
    }

    [Fact]
    public void VariosPerfis_SaoUniao()
    {
        var u = Usuario(new[] { Perfil("A", null, "cde.view"), Perfil("B", null, "production.view", "production.managerial.rnc.view") });
        Assert.True(Pode(u, "cde.view"));
        Assert.True(Pode(u, "production.managerial.rnc.view"));
        Assert.False(Pode(u, "robot.view"));
    }

    [Fact]
    public void SemPermissao_NegadoPorPadrao()
    {
        var u = Usuario();
        foreach (var k in PermissionCatalog.Keys) Assert.False(Pode(u, k), k);
        Assert.Empty(PermissionResolver.Effective(u, ProjA));
        Assert.Equal(PermissionDecisionSource.NegadoPorPadrao, PermissionResolver.Decide(u, "home.view", null).Source);
    }

    [Fact]
    public void KeyForaDoCatalogo_SempreNegada_MesmoComPerfilOuAllow()
    {
        var u = Usuario(new[] { Perfil("X", null, "inventada.view") }, new[] { new PermissionOverride(null, "inventada.view", PermissionEffects.Allow) });
        Assert.False(Pode(u, "inventada.view"));
    }

    [Fact]
    public void AllowIndividual_ConcedeSemPerfil()
    {
        var u = Usuario(overrides: new[] { new PermissionOverride(null, "tv.view", PermissionEffects.Allow) });
        Assert.True(Pode(u, "tv.view"));
        Assert.Equal(PermissionDecisionSource.AllowIndividual, PermissionResolver.Decide(u, "tv.view", null).Source);
    }

    [Fact]
    public void DenyIndividual_NegaMesmoComPerfil()
    {
        var u = Usuario(new[] { Perfil("Produção", null, "production.view", "production.managerial.rnc.view") },
            new[] { new PermissionOverride(null, "production.managerial.rnc.view", PermissionEffects.Deny) });
        Assert.True(Pode(u, "production.view"));
        Assert.False(Pode(u, "production.managerial.rnc.view"));
        Assert.False(Pode(u, "production.managerial.rnc.view", ProjA));
        Assert.Equal(PermissionDecisionSource.DenyIndividual, PermissionResolver.Decide(u, "production.managerial.rnc.view", ProjA).Source);
    }

    [Fact]
    public void DenyVenceAllow_NoMesmoNivel()
    {
        var u = Usuario(overrides: new[]
        {
            new PermissionOverride(null, "robot.run", PermissionEffects.Allow),
            new PermissionOverride(null, "robot.run", PermissionEffects.Deny),
        });
        Assert.False(Pode(u, "robot.run"));
    }

    [Fact]
    public void OverrideDoProjeto_VenceOverrideGlobal()
    {
        var u = Usuario(new[] { Perfil("P", null, "cde.view") }, new[]
        {
            new PermissionOverride(null, "cde.view", PermissionEffects.Deny),
            new PermissionOverride(ProjA, "cde.view", PermissionEffects.Allow),
        });
        Assert.True(Pode(u, "cde.view", ProjA));   // exceção do projeto A libera
        Assert.False(Pode(u, "cde.view", ProjB));  // nos demais vale o DENY global
        Assert.False(Pode(u, "cde.view"));
    }

    [Fact]
    public void TrocaDeProjeto_PerfilDeProjetoValeSoNoProjeto()
    {
        var u = Usuario(new[] { Perfil("Produção A", ProjA, "production.view", "production.managerial.ldp.view") });
        Assert.True(Pode(u, "production.managerial.ldp.view", ProjA));
        Assert.False(Pode(u, "production.managerial.ldp.view", ProjB));
        Assert.False(Pode(u, "production.managerial.ldp.view"));            // sem projeto: só globais
        Assert.Contains("production.managerial.ldp.view", PermissionResolver.EffectiveAnyProject(u)); // menu aparece
        Assert.DoesNotContain("production.managerial.ldp.view", PermissionResolver.Effective(u, ProjB));
    }

    [Fact]
    public void UsuarioSemVinculo_NaoTemNada_NemNoProjeto()
    {
        var u = Usuario(new[] { Perfil("Produção A", ProjA, "production.view") });
        Assert.Empty(PermissionResolver.Effective(u, ProjB));
        Assert.Empty(PermissionResolver.Effective(Usuario(), ProjA));
    }

    [Fact]
    public void UsuarioInativoNoProjeto_PerdeInclusiveGlobaisNesseProjeto()
    {
        var u = Usuario(new[] { Perfil("Global", null, "cde.view") }, inativos: new[] { ProjA });
        Assert.False(Pode(u, "cde.view", ProjA));
        Assert.True(Pode(u, "cde.view", ProjB));
    }

    [Fact]
    public void Robo_ViewSemConfigure_ConfigureSemRun()
    {
        var soVer = Usuario(new[] { Perfil("Observador", null, "robot.view") });
        Assert.True(Pode(soVer, "robot.view"));
        Assert.False(Pode(soVer, "robot.configure"));
        Assert.False(Pode(soVer, "robot.run"));

        var configura = Usuario(new[] { Perfil("Configurador", null, "robot.view", "robot.configure") });
        Assert.True(Pode(configura, "robot.configure"));
        Assert.False(Pode(configura, "robot.run"));
        Assert.False(Pode(configura, "robot.stop"));
    }

    [Fact]
    public void Catalogo_KeysUnicas_PaisExistem_DozeDashboards()
    {
        var keys = PermissionCatalog.Todos.Select(r => r.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(PermissionCatalog.Todos.Where(r => r.ParentKey != null), r => Assert.True(PermissionCatalog.Existe(r.ParentKey!), r.Key));
        Assert.Equal(12, PermissionCatalog.DashboardsProducao.Count);
        // Cada dashboard tem key Gerencial e Operacional, ambas descendentes do módulo Controle de Produção.
        Assert.All(PermissionCatalog.DashboardsProducao.Values.SelectMany(k => k), k =>
        {
            Assert.True(PermissionCatalog.Existe(k), k);
            Assert.Contains(k, PermissionCatalog.ComDescendentes("production.view"));
        });
        // Telas de custo absoluto só existem no Gerencial.
        Assert.DoesNotContain(PermissionCatalog.ComDescendentes("production.operational.view"), k => k.Contains("hh-custo") || k.Contains("financial"));
        Assert.All(keys, k => Assert.Matches(@"^[a-z0-9-]+(\.[a-z0-9-]+)+$", k));
        foreach (var r in PermissionCatalog.Todos)
        {
            Assert.Contains(r.Type, new[] { "MODULE", "PAGE", "TAB", "REPORT", "DASHBOARD", "SETTING", "ACTION" });
            Assert.Contains(r.Scope, new[] { "GLOBAL", "PROJECT" });
        }
    }

    [Fact]
    public void ComDescendentes_IncluiFilhosENetos()
    {
        var d = PermissionCatalog.ComDescendentes("cde.view");
        Assert.Contains("cde.docs.view", d);
        Assert.Contains("cde.docs.download", d); // neto
        Assert.DoesNotContain("production.view", d);
    }
}
