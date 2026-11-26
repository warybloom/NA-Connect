namespace APS.WebApp.Models.WeeklyPlanning;

public enum SubmissionState { RASCUNHO, ENVIADO, DEVOLVIDO, APROVADO }
// Integration vocabulary only. These are not authentication claims or an authorization mechanism.
public enum PlanningRole { DISCIPLINA, JOB_LEADER, PLANEJADOR }
public enum PlanningEventKind
{
    PROGRAMACAO_ABERTA, PRAZO_SE_APROXIMANDO, PROGRAMACAO_ATRASADA,
    PROGRAMACAO_ENVIADA, PROGRAMACAO_DEVOLVIDA, PROGRAMACAO_APROVADA
}
public sealed record PlanningEvent(Guid Id, PlanningEventKind Kind, Guid WeekId,
    string? Discipline, DateTimeOffset OccurredAt, string Actor);
public sealed record SubmissionHistory(Guid Id, SubmissionState From, SubmissionState To,
    string Actor, string? Comment, DateTimeOffset OccurredAt, int Revision,
    IReadOnlyList<PlannedDocument> Documents);
// The canonical key must be supplied by a future identity resolver. Never use a Weekly snapshot ID.
public sealed record PlannedDocument(string CanonicalDocumentKey, DateOnly PlannedDate,
    string Responsible, decimal? WeeklyPlannedHours, string? Note);

public sealed class PlanningWeek
{
    public Guid Id { get; } = Guid.NewGuid();
    public string ProjectId { get; }
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public DateTimeOffset SubmissionDeadline { get; }
    public string TimeZoneId { get; }
    public IReadOnlyList<string> ExpectedDisciplines { get; }
    public PlanningEvent Opened { get; }

    public PlanningWeek(string projectId, DateOnly start, DateOnly end, DateTimeOffset deadline,
        string timeZoneId, IEnumerable<string> disciplines, string actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (end < start || end.DayNumber - start.DayNumber > 6)
            throw new ArgumentException("A semana deve ter entre um e sete dias.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(deadline, zone).DateTime) > end)
            throw new ArgumentException("Prazo posterior ao fim da semana.");
        var expected = disciplines.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (expected.Length == 0 || expected.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Informe as disciplinas esperadas.");
        ProjectId = projectId; Start = start; End = end; SubmissionDeadline = deadline;
        TimeZoneId = timeZoneId; ExpectedDisciplines = Array.AsReadOnly(expected);
        Opened = new(Guid.NewGuid(), PlanningEventKind.PROGRAMACAO_ABERTA, Id, null, now, actor);
    }
}

/// <summary>Pure domain; no writes, notifications, authentication or in-memory production database.</summary>
public sealed class DisciplineSubmission
{
    private readonly List<SubmissionHistory> history = [];
    private readonly List<PlanningEvent> events = [];
    private PlannedDocument[] documents = [];
    public PlanningWeek Week { get; }
    public string Discipline { get; }
    public SubmissionState State { get; private set; } = SubmissionState.RASCUNHO;
    public int Revision { get; private set; }
    public string? ReturnReason { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public IReadOnlyList<PlannedDocument> Documents => Array.AsReadOnly(documents);
    public IReadOnlyList<SubmissionHistory> History => history.AsReadOnly();
    public IReadOnlyList<PlanningEvent> Events => events.AsReadOnly();
    public bool IsEditable => State is SubmissionState.RASCUNHO or SubmissionState.DEVOLVIDO;
    public bool IsOverdue(DateTimeOffset now) => State == SubmissionState.RASCUNHO && now > Week.SubmissionDeadline;
    public string DisplayStatus(DateTimeOffset now) => IsOverdue(now) ? "ATRASADO PARA SUBMISSÃO"
        : State == SubmissionState.RASCUNHO ? "PENDENTE" : State.ToString();

    public DisciplineSubmission(PlanningWeek week, string discipline)
    {
        Week = week;
        Discipline = week.ExpectedDisciplines.FirstOrDefault(x => string.Equals(x, discipline, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Disciplina não prevista nesta semana.");
    }
    public void SaveDraft(IEnumerable<PlannedDocument> rows, int expectedRevision)
    {
        CheckRevision(expectedRevision);
        if (!IsEditable) throw new InvalidOperationException("Submissão congelada para edição.");
        var next = rows.ToArray();
        if (next.Any(x => string.IsNullOrWhiteSpace(x.CanonicalDocumentKey) || x.CanonicalDocumentKey.Length > 450 ||
            x.PlannedDate < Week.Start || x.PlannedDate > Week.End || x.WeeklyPlannedHours < 0 ||
            x.WeeklyPlannedHours > 999999999999.999999m ||
            (x.WeeklyPlannedHours.HasValue && decimal.Round(x.WeeklyPlannedHours.Value, 6) != x.WeeklyPlannedHours.Value) ||
            x.Responsible.Length > 200 || x.Note?.Length > 2000))
            throw new ArgumentException("Documento, data, responsável ou HH semanal inválido.");
        if (next.Select(x => x.CanonicalDocumentKey).Distinct(StringComparer.Ordinal).Count() != next.Length)
            throw new ArgumentException("Documento repetido na disciplina/semana.");
        documents = next; Revision++;
    }
    public void Submit(string actor, DateTimeOffset now, int expectedRevision)
    {
        CheckRevision(expectedRevision);
        if (!IsEditable || documents.Length == 0 || documents.Any(x => string.IsNullOrWhiteSpace(x.Responsible)))
            throw new InvalidOperationException("Envie um rascunho editável com documentos e responsáveis.");
        // Late submissions remain possible and auditable. The deadline drives status, not a silent lock.
        Move(SubmissionState.ENVIADO, PlanningEventKind.PROGRAMACAO_ENVIADA, actor, null, now);
        SubmittedAt = now;
    }
    public void Approve(string actor, string? comment, DateTimeOffset now, int expectedRevision)
    {
        CheckRevision(expectedRevision);
        if (State != SubmissionState.ENVIADO) throw new InvalidOperationException("Aprovação exige envio.");
        Move(SubmissionState.APROVADO, PlanningEventKind.PROGRAMACAO_APROVADA, actor, comment, now);
    }
    public void Return(string actor, string reason, DateTimeOffset now, int expectedRevision)
    {
        CheckRevision(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (State != SubmissionState.ENVIADO) throw new InvalidOperationException("Devolução exige envio.");
        Move(SubmissionState.DEVOLVIDO, PlanningEventKind.PROGRAMACAO_DEVOLVIDA, actor, reason.Trim(), now);
        ReturnReason = reason.Trim();
    }
    public void Comment(string actor, string comment, DateTimeOffset now, int expectedRevision)
    {
        CheckRevision(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        if (comment.Length > 2000) throw new ArgumentException("Comentário muito longo.");
        history.Add(new(Guid.NewGuid(), State, State, actor, comment.Trim(), now, ++Revision, Array.AsReadOnly(documents.ToArray())));
    }
    private void Move(SubmissionState next, PlanningEventKind kind, string actor, string? comment, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (comment?.Length > 2000) throw new ArgumentException("Comentário muito longo.");
        history.Add(new(Guid.NewGuid(), State, next, actor, comment, now, ++Revision, Array.AsReadOnly(documents.ToArray())));
        State = next;
        events.Add(new(Guid.NewGuid(), kind, Week.Id, Discipline, now, actor));
    }
    private void CheckRevision(int expected)
    {
        if (expected != Revision) throw new InvalidOperationException("Versão desatualizada; recarregue a submissão.");
    }
}
