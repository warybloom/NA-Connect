using APS.WebApp.Services.DocumentQuality;

namespace NA_Connect.Qualidade.Tests;

public class InformationQualityRedistributionServiceTests
{
    [Fact]
    public void CalculateAllocations_UsesSameWeightForHoursAndCost_AndClosesTotals()
    {
        var targets = new[]
        {
            new RedistributionTargetSource("DOC-A", "A", 60m),
            new RedistributionTargetSource("DOC-B", "B", 30m),
            new RedistributionTargetSource("DOC-C", "C", 10m)
        };

        var result = InformationQualityRedistributionService.CalculateAllocations(40m, 4000m, targets);

        Assert.Equal(3, result.Count);
        Assert.Equal(0.6m, result[0].Weight);
        Assert.Equal(24m, result[0].Hours);
        Assert.Equal(2400m, result[0].Cost);
        Assert.Equal(0.3m, result[1].Weight);
        Assert.Equal(12m, result[1].Hours);
        Assert.Equal(1200m, result[1].Cost);
        Assert.Equal(0.1m, result[2].Weight);
        Assert.Equal(4m, result[2].Hours);
        Assert.Equal(400m, result[2].Cost);
        Assert.Equal(40m, result.Sum(x => x.Hours));
        Assert.Equal(4000m, result.Sum(x => x.Cost));
        Assert.Equal(1m, result.Sum(x => x.Weight));
    }

    [Fact]
    public void CalculateAllocations_IgnoresTargetsWithoutPlannedHours()
    {
        var targets = new[]
        {
            new RedistributionTargetSource("DOC-ZERO", "Zero", 0m),
            new RedistributionTargetSource("DOC-A", "A", 25m)
        };

        var result = InformationQualityRedistributionService.CalculateAllocations(10m, 123.45m, targets);

        var item = Assert.Single(result);
        Assert.Equal("DOC-A", item.DocumentCode);
        Assert.Equal(1m, item.Weight);
        Assert.Equal(10m, item.Hours);
        Assert.Equal(123.45m, item.Cost);
    }

    [Fact]
    public void CalculateAllocations_WithNoPositivePlannedHours_IsBlockedByEmptyResult()
    {
        var targets = new[]
        {
            new RedistributionTargetSource("DOC-ZERO", "Zero", 0m),
            new RedistributionTargetSource("DOC-NEG", "Neg", -1m)
        };

        var result = InformationQualityRedistributionService.CalculateAllocations(10m, 100m, targets);

        Assert.Empty(result);
    }

    [Fact]
    public void CalculateAllocations_RoundingRemainderGoesToLastDocumentAndPreservesTotals()
    {
        var targets = new[]
        {
            new RedistributionTargetSource("A", "A", 1m),
            new RedistributionTargetSource("B", "B", 1m),
            new RedistributionTargetSource("C", "C", 1m)
        };

        var result = InformationQualityRedistributionService.CalculateAllocations(1m, 1m, targets);

        Assert.Equal(1m, result.Sum(x => x.Hours));
        Assert.Equal(1m, result.Sum(x => x.Cost));
        Assert.Equal(1m, result.Sum(x => x.Weight));
        Assert.Equal(0.333334m, result[^1].Hours);
        Assert.Equal(0.333334m, result[^1].Cost);
    }

    [Fact]
    public void ApplyRedistributions_RemovesPendingSourceFindingAndAddsInformationalHistory()
    {
        var code = "BML011.014-000-ME7-DS-001";
        var findings = new List<InformationQualityFinding>
        {
            new(
                $"TS_ONLY:{code}",
                "ATENCAO",
                "TIMESHEET",
                "TIMESHEET_SEM_LDP",
                "Documento do Timesheet não existe na LDP",
                "Pendência",
                code,
                "MECHANICAL",
                0,
                10,
                true,
                [])
        };

        var decision = new RedistributionDecision(
            Guid.NewGuid(),
            "b.92c9d539-6092-4a08-a812-72801dc15fe7",
            code,
            "MECHANICAL",
            10m,
            1000m,
            "CONFIRMADO",
            "Christian",
            DateTime.UtcNow,
            [
                new RedistributionDestination("DOC-A", "A", 60m, 0.6m, 6m, 600m),
                new RedistributionDestination("DOC-B", "B", 40m, 0.4m, 4m, 400m)
            ]);

        InformationQualityAuditService.ApplyRedistributions(findings, [decision]);

        Assert.DoesNotContain(findings, x => x.Rule == "TIMESHEET_SEM_LDP" && x.DocumentCode == code);
        var info = Assert.Single(findings.Where(x => x.Rule == "REDISTRIBUICAO_CONFIRMADA"));
        Assert.False(info.DecisionRequired);
        Assert.Equal("INFORMATIVO", info.Severity);
        Assert.Contains(info.Details, x => x.Label == "DOC-A");
        Assert.Contains(info.Details, x => x.Label == "DOC-B");
    }
}
