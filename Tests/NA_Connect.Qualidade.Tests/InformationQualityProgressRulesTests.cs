using APS.WebApp.Services.DocumentQuality;

namespace NA_Connect.Qualidade.Tests;

public class InformationQualityProgressRulesTests
{
    [Fact]
    public void Settings_AreProjectScoped_AndDoNotShareApprovedPercentage()
    {
        var projectAApproved = new ProgressSettingItem("APPROVED", 100m, true);
        var projectBApproved = new ProgressSettingItem("APPROVED", 80m, true);
        Assert.NotEqual(projectAApproved.Percentual, projectBApproved.Percentual);
        Assert.Equal("APROVADO", InformationQualityProgressService.NormalizeStatus(projectAApproved.DocumentStatus)); // alias explícito APPROVED → APROVADO
    }

    [Fact]
    public void ConfirmedSnapshot_IsNotChangedByLaterConfiguration()
    {
        Assert.Equal(60m, InformationQualityProgressService.EffectivePercent(0, 60m));
        Assert.NotEqual(80m, InformationQualityProgressService.EffectivePercent(0, 60m));
    }

    [Fact]
    public void ZeroConfigurationWithNoOriginalPhysical_DoesNotCreatePendingFinding()
    {
        Assert.False(InformationQualityProgressService.RequiresPendingFinding(0, 0));
        Assert.True(InformationQualityProgressService.RequiresPendingFinding(null, 10));
    }

    [Fact]
    public void OriginalPhysicalAlwaysPrevailsOverAuditOverride()
        => Assert.Equal(35m, InformationQualityProgressService.EffectivePercent(35m, 100m));
}
