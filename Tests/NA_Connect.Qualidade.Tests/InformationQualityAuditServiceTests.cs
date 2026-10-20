using APS.WebApp.Services.DocumentQuality;

namespace NA_Connect.Qualidade.Tests;

public class InformationQualityAuditServiceTests
{
    private static InformationQualityLdpRow Ldp(
        string code,
        decimal actualHours = 0,
        string? status = null,
        string? client = null,
        string? title = "Documento",
        string? discipline = "PROCESS",
        string area = "000",
        string activity = "PX1",
        string docType = "DR",
        string seq = "001",
        decimal? plannedPhysical = 0,
        decimal? actualPhysical = 0,
        string? situation = "A Emitir")
        => new(
            code,
            client,
            title,
            discipline,
            area,
            activity,
            docType,
            seq,
            0,
            actualHours,
            plannedPhysical,
            actualPhysical,
            status,
            situation);

    private static InformationQualityTimesheetRow Ts(string code, decimal hours, string? discipline = "PROCESS")
        => new(code, discipline, hours);

    private static InformationQualityScheduleRow Schedule(
        string? code,
        decimal? actual = 0,
        string? task = "Task",
        DateTime? syncedAt = null)
        => new(code, task, actual, syncedAt ?? new DateTime(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc));

    private static IReadOnlyList<InformationQualityScheduleRow> HealthySchedule(string code)
        => Enumerable.Range(1, 20).Select(i => Schedule(code, 50, $"Task {i}")).ToList();

    [Fact]
    public void DuplicateNaDocument_IsCriticalAndDecisionRequired()
    {
        var code = "BML011.019-000-ME7-DS-001";
        var ldp = new[]
        {
            Ldp(code, client: "P-0303.1-510-ME-TS-0001", title: "GENERAL ARRANGEMENT", discipline: "MECHANICAL", activity: "ME7", docType: "DS"),
            Ldp(code, client: "P-0303.1-000-IA-DR-0001", title: "PRELIMINARY NETWORK ARCHITECTURE", discipline: "INSTRUMENTATION AND AUTOMATION", activity: "ME7", docType: "DS")
        };

        var result = InformationQualityAuditService.BuildFindings(ldp, [], HealthySchedule(code));

        var finding = Assert.Single(result.Where(x => x.Rule == "NA_DOCUMENT_DUPLICADO"));
        Assert.Equal("CRITICO", finding.Severity);
        Assert.True(finding.DecisionRequired);
        Assert.Equal(2, finding.Details.Count);
        Assert.Contains("GENERAL ARRANGEMENT", finding.Details[0].Value + finding.Details[1].Value);
        Assert.Contains("PRELIMINARY NETWORK ARCHITECTURE", finding.Details[0].Value + finding.Details[1].Value);
        Assert.Contains("Diferenças no grupo: cliente, título, disciplina", finding.Details[0].Value);
    }

    [Fact]
    public void TimesheetWithoutLdp_RequiresDecision()
    {
        var code = "BML011.016-000-3D0-AT-001";

        var result = InformationQualityAuditService.BuildFindings(
            [],
            [Ts(code, 38)],
            []);

        var finding = Assert.Single(result.Where(x => x.Rule == "TIMESHEET_SEM_LDP"));
        Assert.Equal("ATENCAO", finding.Severity);
        Assert.True(finding.DecisionRequired);
        Assert.Equal(38, finding.TimesheetHours);
    }

    [Fact]
    public void RetiredDocumentWithHours_IsFlaggedWithoutAutomaticCorrection()
    {
        var code = "BML011.017-211-CI4-DE-007";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, 1.83m, "Documento retirado da ldp", activity: "CI4", docType: "DE", seq: "007")],
            [Ts(code, 1.83m)],
            HealthySchedule(code));

        var finding = Assert.Single(result.Where(x => x.Rule == "DOCUMENTO_RETIRADO_COM_HH"));
        Assert.Equal("ATENCAO", finding.Severity);
        Assert.True(finding.DecisionRequired);
        Assert.Equal(1.83m, finding.LdpHours);
        Assert.Equal(1.83m, finding.TimesheetHours);
    }

    [Fact]
    public void LdpHoursWithoutTimesheet_IsFlagged()
    {
        var code = "BML011.024-000-IA0-AT-001";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, 2.5m, activity: "IA0", docType: "AT")],
            [],
            HealthySchedule(code));

        var finding = Assert.Single(result.Where(x => x.Rule == "LDP_SEM_TIMESHEET_COM_HH"));
        Assert.True(finding.DecisionRequired);
        Assert.Equal(2.5m, finding.LdpHours);
    }

    [Fact]
    public void MissingSchedule_IsWarningButDoesNotRequireDocumentDecision()
    {
        var code = "BML011.020-000-PX1-DR-001";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, area: "000", activity: "PX1", docType: "DR")],
            [],
            []);

        var finding = Assert.Single(result.Where(x => x.Rule == "CRONOGRAMA_AUSENTE"));
        Assert.Equal("ATENCAO", finding.Severity);
        Assert.False(finding.DecisionRequired);
    }

    [Fact]
    public void VeryLowScheduleDocumentCoverage_IsFlagged()
    {
        var code = "BML011.014-000-PM0-SC-001";
        var schedule = Enumerable.Range(1, 100)
            .Select(i => i <= 2 ? Schedule(code, 100, $"Doc task {i}") : Schedule(null, 100, $"Task {i}"))
            .ToList();

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, activity: "PM0", docType: "SC")],
            [],
            schedule);

        var finding = Assert.Single(result.Where(x => x.Rule == "CRONOGRAMA_COBERTURA_INSUFICIENTE"));
        Assert.False(finding.DecisionRequired);
        Assert.Contains(finding.Details, x => x.Label == "Tarefas com documento" && x.Value == "2");
    }

    [Fact]
    public void ConflictingScheduleActualForSameDocument_IsCritical()
    {
        var code = "BML011.015-000-PX2-FD-010";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, activity: "PX2", docType: "FD", seq: "010")],
            [],
            [
                Schedule(code, 100, "Entrega A"),
                Schedule(code, 40, "Entrega B")
            ]);

        var finding = Assert.Single(result.Where(x => x.Rule == "CRONOGRAMA_REALIZADO_CONFLITANTE"));
        Assert.Equal("CRITICO", finding.Severity);
        Assert.True(finding.DecisionRequired);
        Assert.Contains("40", finding.Description);
        Assert.Contains("100", finding.Description);
    }

    [Fact]
    public void CleanProject_ReturnsOnlyInformationalOk()
    {
        var code = "BML011.020-000-PX1-DR-001";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, actualHours: 4, area: "000", activity: "PX1", docType: "DR")],
            [Ts(code, 4)],
            HealthySchedule(code));

        var finding = Assert.Single(result);
        Assert.Equal("SEM_INCONSISTENCIAS", finding.Rule);
        Assert.Equal("INFORMATIVO", finding.Severity);
        Assert.False(finding.DecisionRequired);
    }

    [Fact]
    public void PhysicalPercentageOutsideRange_IsIgnoredForActivityTypeAt()
    {
        var code = "BML011.020-000-3D0-AT-001";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, docType: "AT", activity: "3D0", plannedPhysical: -100, actualPhysical: 150)],
            [],
            HealthySchedule(code));

        Assert.DoesNotContain(result, x => x.Rule == "PERCENTUAL_FORA_FAIXA");
    }

    [Fact]
    public void DivergentLdpAndTimesheetHours_RequiresDecision()
    {
        var code = "BML011.020-000-PX1-DR-001";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, actualHours: 4, area: "000", activity: "PX1", docType: "DR")],
            [Ts(code, 3)],
            HealthySchedule(code));

        var finding = Assert.Single(result.Where(x => x.Rule == "HH_LDP_TIMESHEET_DIVERGENTE"));
        Assert.True(finding.DecisionRequired);
        Assert.Equal(4, finding.LdpHours);
        Assert.Equal(3, finding.TimesheetHours);
    }

    [Fact]
    public void PhysicalPercentageOutsideRange_IsFlaggedForDocument()
    {
        var code = "BML011.020-000-PX1-DR-001";

        var result = InformationQualityAuditService.BuildFindings(
            [Ldp(code, area: "000", activity: "PX1", docType: "DR", plannedPhysical: -1, actualPhysical: 101)],
            [],
            HealthySchedule(code));

        var finding = Assert.Single(result.Where(x => x.Rule == "PERCENTUAL_FORA_FAIXA"));
        Assert.True(finding.DecisionRequired);
    }
}
