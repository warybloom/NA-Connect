using APS.WebApp.Models.WeeklyPlanning;
using APS.WebApp.Services.WeeklyPlanning;
using OfficeOpenXml;

namespace NA_Connect.WeeklyPlanning.Tests;

public sealed class DomainTests
{
    private static readonly DateOnly Start = new(2026, 9, 21);
    private static readonly DateTimeOffset Deadline = new(2026, 9, 18, 15, 0, 0, TimeSpan.FromHours(-3));
    private static PlanningWeek Week() => new("project", Start, Start.AddDays(4), Deadline,
        "America/Sao_Paulo", ["PROCESSO", "ELÉTRICA"], "planner", Deadline.AddDays(-1));
    private static PlannedDocument Doc(decimal? hours = 20) => new("canonical:doc1", Start, "leader", hours, null);

    [Fact] public void CompleteReturnResubmitApproveFlowFreezesDocumentsAndRetainsHistory()
    {
        var submission = new DisciplineSubmission(Week(), "PROCESSO");
        submission.SaveDraft([Doc()], 0);
        submission.Submit("leader", Deadline, 1);
        Assert.Throws<InvalidOperationException>(() => submission.SaveDraft([Doc(10)], 2));
        submission.Return("planner", "Revisar responsável", Deadline.AddMinutes(1), 2);
        submission.SaveDraft([Doc(25)], 3);
        submission.Submit("leader", Deadline.AddMinutes(2), 4);
        submission.Approve("planner", "Conferido", Deadline.AddMinutes(3), 5);
        Assert.Equal(SubmissionState.APROVADO, submission.State);
        Assert.Equal(25m, submission.Documents.Single().WeeklyPlannedHours);
        Assert.Equal("Revisar responsável", submission.ReturnReason);
        Assert.Equal(4, submission.History.Count);
        Assert.Equal(20m, submission.History[0].Documents.Single().WeeklyPlannedHours);
        Assert.Equal(25m, submission.History[3].Documents.Single().WeeklyPlannedHours);
        Assert.Equal(4, submission.Events.Count);
        Assert.Throws<InvalidOperationException>(() => submission.SaveDraft([], 6));
        Assert.Throws<InvalidOperationException>(() => submission.Return("planner", "reabrir", Deadline, 6));
        submission.Comment("planner", "Nota adicional sem mudar programação", Deadline, 6);
        Assert.Equal(SubmissionState.APROVADO, submission.State);
    }
    [Fact] public void DeadlineHasStrictBoundaryAndReturnedRemainsReturned()
    {
        var submission = new DisciplineSubmission(Week(), "PROCESSO");
        Assert.False(submission.IsOverdue(Deadline));
        Assert.True(submission.IsOverdue(Deadline.AddTicks(1)));
        Assert.Equal("PENDENTE", submission.DisplayStatus(Deadline));
        submission.SaveDraft([Doc()], 0);
        submission.Submit("leader", Deadline.AddMinutes(5), 1);
        Assert.False(submission.IsOverdue(Deadline.AddHours(1)));
        submission.Return("planner", "Ajustar", Deadline.AddHours(1), 2);
        Assert.Equal("DEVOLVIDO", submission.DisplayStatus(Deadline.AddDays(2)));
    }
    [Fact] public void RejectsDuplicateWrongDateNegativeHoursAndStaleChanges()
    {
        var s = new DisciplineSubmission(Week(), "PROCESSO");
        Assert.Throws<ArgumentException>(() => s.SaveDraft([Doc(), Doc()], 0));
        Assert.Throws<ArgumentException>(() => s.SaveDraft([Doc(-1)], 0));
        Assert.Throws<ArgumentException>(() => s.SaveDraft([Doc() with { PlannedDate = Start.AddDays(5) }], 0));
        s.SaveDraft([Doc(null)], 0);
        Assert.Null(s.Documents[0].WeeklyPlannedHours);
        Assert.Throws<InvalidOperationException>(() => s.SaveDraft([], 0));
        Assert.Single(s.Documents);
    }
    [Fact] public void RejectsEmptySubmissionAndInvalidTransitions()
    {
        var s = new DisciplineSubmission(Week(), "PROCESSO");
        Assert.Throws<InvalidOperationException>(() => s.Submit("leader", Deadline, 0));
        Assert.Throws<InvalidOperationException>(() => s.Approve("planner", null, Deadline, 0));
        s.SaveDraft([Doc() with { Responsible = " " }], 0);
        Assert.Throws<InvalidOperationException>(() => s.Submit("leader", Deadline, 1));
        s.SaveDraft([Doc()], 1); s.Submit("leader", Deadline, 2);
        Assert.Throws<ArgumentException>(() => s.Return("planner", " ", Deadline, 3));
        Assert.Equal(SubmissionState.ENVIADO, s.State);
    }
    [Fact] public void ExpectedDisciplinesAreExplicitAndWeekIsBounded()
    {
        Assert.Throws<ArgumentException>(() => new DisciplineSubmission(Week(), "MECÂNICA"));
        Assert.Throws<ArgumentException>(() => new PlanningWeek("project", Start, Start.AddDays(7), Deadline,
            "America/Sao_Paulo", ["PROCESSO"], "planner", Deadline));
        Assert.Equal(PlanningEventKind.PROGRAMACAO_ABERTA, Week().Opened.Kind);
        Assert.Equal(2, Week().ExpectedDisciplines.Count);
    }
    [Fact] public void RejectsSqlPrecisionOverflowAndDeadlineAfterWeek()
    {
        var s = new DisciplineSubmission(Week(), "PROCESSO");
        Assert.Throws<ArgumentException>(() => s.SaveDraft([Doc(1000000000000m)], 0));
        Assert.Throws<ArgumentException>(() => s.SaveDraft([Doc(0.0000001m)], 0));
        Assert.Throws<ArgumentException>(() => new PlanningWeek("project", Start, Start.AddDays(4),
            Deadline.AddDays(10), "America/Sao_Paulo", ["PROCESSO"], "planner", Deadline));
    }
    [Fact] public void ExportIsAnActualWorkbookAndDoesNotTurnTextIntoFormula()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial; // Same project license configuration.
        var data = WeeklyPlanningExportService.Preview("project", Start, Start.AddDays(4));
        data = data with { Rows = [data.Rows[0] with { Note = "=HYPERLINK(\"https://example.invalid\")" }] };
        var bytes = new WeeklyPlanningExportService().Export(data);
        using var package = new ExcelPackage(new MemoryStream(bytes));
        var sheet = package.Workbook.Worksheets[0];
        Assert.Contains("PREVIEW", sheet.Name);
        Assert.Equal(20m, Convert.ToDecimal(sheet.Cells[6, 6].Value));
        Assert.Equal(data.Rows[0].Note, sheet.Cells[6, 7].Text);
        Assert.True(string.IsNullOrEmpty(sheet.Cells[6, 7].Formula));
        Assert.Throws<InvalidOperationException>(() => new WeeklyPlanningExportService().Export(data with { IsPreview = false }));
    }
}
