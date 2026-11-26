namespace APS.WebApp.Services.Reports;

public sealed record WeeklyReportRow(int Id, string? Document, string? Title, string? Discipline,
    string? Type, string? Status, string? Diagnostic, string? File, string? Sheet, int? Line);

public sealed record WeeklyReport(long DataRows, long WarningRows, long SentinelRows,
    long UnclassifiedRows, long TotalRows, long FilteredRows, DateTime? LastSync,
    int Page, int PageSize, IReadOnlyList<WeeklyReportRow> Rows);

public interface IWeeklyReportRepository
{
    Task<WeeklyReport> ReadAsync(string projectId, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

public enum ProjectReportAccess { Allowed, Unauthenticated, Denied, Unavailable }
public interface IProjectReportAccess
{
    Task<ProjectReportAccess> CheckAsync(string hubId, string projectId, string accessToken, CancellationToken cancellationToken);
}
