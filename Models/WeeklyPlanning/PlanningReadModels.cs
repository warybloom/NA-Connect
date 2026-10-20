namespace APS.WebApp.Models.WeeklyPlanning;

public sealed record PlanningDocumentCandidate(int SourceRecordId, string? DocumentNumber, string? Title,
    string? Discipline, DateTime? PlannedDate, decimal? DocumentTotalHours);
public sealed record PlanningDocumentPage(string Source, string IdentityStatus, long TotalRows,
    int Page, int PageSize, IReadOnlyList<PlanningDocumentCandidate> Rows, string Warning);
public sealed record LegacyWeeklyRow(int SourceRecordId, string? DocumentNumber, string? Title,
    string? Discipline, DateTime? PlannedDate, string? Responsible, decimal? ImportedHours,
    string? Note, DateTime? SynchronizedAt);
public sealed record LegacyWeeklyPage(string Source, long TotalRows, int Page, int PageSize,
    IReadOnlyList<LegacyWeeklyRow> Rows, string Warning);
