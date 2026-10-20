using APS.WebApp.Services.Permissions;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>Impede que um perfil antigo em projeto sem vinculo ativo apareca no menu de outro projeto.</summary>
public sealed class PermissionScopePositiveTests
{
    [Fact]
    public void AnyProject_OnlyIncludesProjectsWithPositiveActiveLink()
    {
        var permissions = new UserPermissionData(
            false,
            new[]
            {
                new ProfileAssignment(3, "Somente Leitura", 232, false, new HashSet<string> { "home.view" }),
                new ProfileAssignment(4, "adm", 244, false, new HashSet<string> { "production.view", "robot.run" })
            },
            Array.Empty<PermissionOverride>(),
            new HashSet<int>(),
            new HashSet<int> { 232 });

        var effective = PermissionResolver.EffectiveAnyProject(permissions);

        Assert.Contains("home.view", effective);
        Assert.DoesNotContain("production.view", effective);
        Assert.DoesNotContain("robot.run", effective);
        Assert.False(PermissionResolver.Decide(permissions, "production.view", 232).Allowed);
    }

    [Fact]
    public void AnyProject_RespectsExplicitActiveSecondProject()
    {
        var permissions = new UserPermissionData(
            false,
            new[]
            {
                new ProfileAssignment(3, "Somente Leitura", 232, false, new HashSet<string> { "home.view" }),
                new ProfileAssignment(4, "adm", 244, false, new HashSet<string> { "production.view" })
            },
            Array.Empty<PermissionOverride>(),
            new HashSet<int>(),
            new HashSet<int> { 232, 244 });

        Assert.Contains("production.view", PermissionResolver.EffectiveAnyProject(permissions));
        Assert.False(PermissionResolver.Decide(permissions, "production.view", 232).Allowed);
    }
}
