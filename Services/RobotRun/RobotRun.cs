namespace APS.WebApp.Services.RobotRun;

public sealed record RobotHistoricoEntry(string Projeto, string Etapa, string Status, string Mensagem)
{
    public DateTime At { get; init; } = DateTime.Now;
}

/// <summary>Estado REAL de uma etapa planejada do projeto (PENDING → RUNNING → COMPLETED/ERROR; SKIPPED = o Worker não a executou).</summary>
public sealed record RobotStageProgress(string Id, string Name, string State, DateTime? StartedAt = null, DateTime? FinishedAt = null, string? Message = null)
{
    public const string Pending = "PENDING", Running = "RUNNING", Completed = "COMPLETED", Error = "ERROR", Skipped = "SKIPPED";
    public bool Finished => State is Completed or Error or Skipped;
}
/// <summary>Evento recente do histórico (mesmo conteúdo do Logs_Historico, só os últimos N; mensagem resumida).</summary>
public sealed record RobotRecentEntry(DateTime At, string Project, string Stage, string Status, string? Message);

public sealed record RobotRunStatus(string State, bool Manual, string? ProjectId, string? ProjectName, DateTime? StartedAt, DateTime? FinishedAt,
    string Message)
{
    public IReadOnlyList<string> ProjectIds { get; init; } = Array.Empty<string>();
    public string? RobotId { get; init; }
    /// <summary>Projeto X de Y (1-based; 0 = nenhum projeto iniciado ainda).</summary>
    public int QueuePosition { get; init; }
    public int QueueTotal { get; init; }
    public string? CurrentStage { get; init; }
    public string? NextProject { get; init; }
    public IReadOnlyList<RobotQueueProjectResult> Projects { get; init; } = Array.Empty<RobotQueueProjectResult>();
    /// <summary>Plano de etapas do projeto atual, com o estado real de cada uma.</summary>
    public IReadOnlyList<RobotStageProgress> Stages { get; init; } = Array.Empty<RobotStageProgress>();
    public string? CurrentStageId { get; init; }
    /// <summary>Etapa X de Y do projeto atual (X = etapa em execução, ou a quantidade já processada).</summary>
    public int StagePosition { get; init; }
    public int StageTotal { get; init; }
    /// <summary>Etapas já processadas (concluídas, com erro ou não executadas pelo Worker) somando todos os projetos da execução.</summary>
    public int OverallCompletedStages { get; init; }
    public int OverallTotalStages { get; init; }
    public int OverallFailedStages { get; init; }
    public int OverallSkippedStages { get; init; }
    /// <summary>OverallCompletedStages / OverallTotalStages × 100 (0–100). null = execução sem plano conhecido (ciclo automático legado).</summary>
    public int? ProgressPercent { get; init; }
    public IReadOnlyList<RobotRecentEntry> RecentEntries { get; init; } = Array.Empty<RobotRecentEntry>();
    public const string Idle = "IDLE", Running = "RUNNING", Completed = "COMPLETED", Error = "ERROR";
    public static RobotRunStatus Ocioso { get; } = new(Idle, false, null, null, null, null, "Nenhuma execução registrada neste processo.");
}

/// <summary>Projeto da execução manual. TipoLdp (do cadastro) só define o plano de progresso; o hub é resolvido no cadastro (dbo.Projetos/Hubs_Conta).</summary>
public sealed record ManualRobotProject(string ProjectId, string? Name)
{
    public int TipoLdp { get; init; } = 1;
}

public sealed record RobotRunStartResult(bool Started, RobotRunStatus Status);
public sealed record RobotCycleOutcome(bool Erro, string Message);

/// <summary>
/// Portão ÚNICO de execução do robô, compartilhado pelo ciclo automático e pelo disparo manual: o pipeline (ProcessarCicloCompleto) usa estado global
/// (Console.SetOut, contadores da API Autodesk, arquivo de log da execução) e não é reentrante — nunca há dois ciclos ao mesmo tempo, nem do mesmo projeto
/// nem de projetos diferentes.
/// Também mantém, EM MEMÓRIA, o progresso real da execução: plano de etapas por projeto (RobotStageCatalog) atualizado pelos registros que o Worker já
/// grava (Record ← RegistrarLogHistorico). Nada de timer: o percentual só anda quando uma etapa termina.
/// </summary>
public sealed class RobotRunGate
{
    public const int RecentLimit = 20;
    private readonly object _lock = new();
    private RobotRunStatus _status = RobotRunStatus.Ocioso;
    private readonly List<RobotHistoricoEntry> _entries = new();
    private sealed class ProjectPlan(string projectId, RobotStageProgress[] stages)
    {
        public string ProjectId { get; } = projectId;
        public RobotStageProgress[] Stages { get; } = stages;
    }
    private readonly List<ProjectPlan> _plans = new();
    private int _current = -1;

    public RobotRunStatus Snapshot { get { lock (_lock) return Compose(); } }

    public bool TryBegin(bool manual, string? projectId, string? projectName, DateTime now, out RobotRunStatus status)
    {
        lock (_lock)
        {
            if (_status.State == RobotRunStatus.Running) { status = Compose(); return false; }
            _entries.Clear(); _plans.Clear(); _current = -1;
            _status = new(RobotRunStatus.Running, manual, projectId, projectName, now, null,
                manual ? (projectId is null ? "Execução manual para todos os projetos ativos em andamento." : $"Execução manual do projeto {projectName ?? projectId} em andamento.")
                       : "Ciclo automático do robô em andamento.");
            status = Compose(); return true;
        }
    }

    public bool TryBeginManualProjects(IReadOnlyList<ManualRobotProject> projects, DateTime now, out RobotRunStatus status)
    {
        lock (_lock)
        {
            var single = projects.Count == 1 ? projects[0] : null;
            if (!TryBegin(true, single?.ProjectId, single?.Name, now, out status)) return false;
            foreach (var p in projects) _plans.Add(NewPlan(p.ProjectId, RobotStageCatalog.FullPlan(p.TipoLdp)));
            _status = _status with { ProjectIds = Array.AsReadOnly(projects.Select(p => p.ProjectId).ToArray()), QueueTotal = projects.Count,
                Projects = Array.AsReadOnly(projects.Select(p => new RobotQueueProjectResult(p.ProjectId, p.Name ?? p.ProjectId, "PENDING", null)).ToArray()),
                NextProject = projects[0].Name ?? projects[0].ProjectId,
                Message = projects.Count == 1 ? _status.Message : $"Execução manual de {projects.Count} projetos em andamento." };
            status = Compose();
            return true;
        }
    }

    /// <summary>Registro do histórico do ciclo em curso (espelha RegistrarLogHistorico do Worker) — base do resultado real e do progresso da UI.</summary>
    public void Record(RobotHistoricoEntry entry)
    {
        lock (_lock)
        {
            if (_status.State != RobotRunStatus.Running) return;
            _entries.Add(entry);
            _status = _status with { CurrentStage = entry.Etapa };
            if (_current < 0 || _current >= _plans.Count) return;
            var plan = _plans[_current];
            var stageId = StageIdOf(entry.Etapa);
            var index = stageId == null ? -1 : Array.FindIndex(plan.Stages, s => s.Id == stageId);
            var status = (entry.Status ?? "").ToUpperInvariant();
            if (index >= 0)
            {
                var s = plan.Stages[index];
                plan.Stages[index] = status.Contains("ERRO") ? s with { State = RobotStageProgress.Error, StartedAt = s.StartedAt ?? entry.At, FinishedAt = entry.At, Message = Resumo(entry.Mensagem) }
                    : status.StartsWith("CONCLU") ? s with { State = RobotStageProgress.Completed, StartedAt = s.StartedAt ?? entry.At, FinishedAt = entry.At }
                    : status.StartsWith("INICIA") ? s with { State = RobotStageProgress.Running, StartedAt = entry.At }
                    : s;
            }
            else if (entry.Etapa == "PROJETO") CloseProject(plan, null, entry.At);
            else if (status.Contains("ERRO"))
            {
                // Erro fora de uma etapa numerada (ex.: descoberta/download da LDP): a etapa em curso, se houver, termina com erro.
                for (int i = 0; i < plan.Stages.Length; i++)
                    if (plan.Stages[i].State == RobotStageProgress.Running)
                        plan.Stages[i] = plan.Stages[i] with { State = RobotStageProgress.Error, FinishedAt = entry.At, Message = Resumo(entry.Mensagem) };
            }
        }
    }
    public IReadOnlyList<RobotHistoricoEntry> Entries() { lock (_lock) return _entries.ToArray(); }

    public bool TryBeginQueue(string robotId, IReadOnlyList<RobotQueueItem> queue, out RobotRunStatus status)
    {
        lock (_lock)
        {
            if (!TryBegin(true, null, null, DateTime.Now, out status)) return false;
            foreach (var item in queue) _plans.Add(NewPlan(item.ProjectId, RobotStageCatalog.Plan(item.Stages, item.TipoLdp)));
            _status = _status with { RobotId = robotId, QueueTotal = queue.Count,
                Projects = Array.AsReadOnly(queue.Select(p => new RobotQueueProjectResult(p.ProjectId, p.Name, "PENDING", null)).ToArray()),
                Message = "Fila configurada iniciada.", NextProject = queue[0].Name };
            status = Compose(); return true;
        }
    }
    public void QueueProjectStarted(int position, RobotQueueItem item, string? next) => ProjectStarted(position, item.ProjectId, item.Name, next);
    public void QueueProjectFinished(RobotQueueItem item, RobotCycleOutcome outcome) => ProjectFinished(item.ProjectId, outcome);

    public void ProjectStarted(int position, string projectId, string? name, string? next)
    {
        lock (_lock)
        {
            _current = _plans.FindIndex(p => p.ProjectId == projectId);
            _status = _status with { QueuePosition = position, ProjectId = projectId, ProjectName = name,
                CurrentStage = null, NextProject = next, Projects = Array.AsReadOnly(_status.Projects.Select(p => p.ProjectId == projectId ? p with { State = "RUNNING" } : p).ToArray()) };
        }
    }
    public void ProjectFinished(string projectId, RobotCycleOutcome outcome)
    {
        lock (_lock)
        {
            var plan = _plans.FirstOrDefault(p => p.ProjectId == projectId);
            if (plan != null) CloseProject(plan, outcome.Erro ? outcome.Message : null, DateTime.Now);
            _status = _status with { Projects = Array.AsReadOnly(_status.Projects.Select(p => p.ProjectId == projectId
                ? p with { State = outcome.Erro ? "ERROR" : "COMPLETED", Message = outcome.Message } : p).ToArray()) };
        }
    }

    public RobotRunStatus Finish(RobotCycleOutcome outcome, DateTime now)
    {
        lock (_lock)
        {
            foreach (var plan in _plans) CloseProject(plan, outcome.Erro ? outcome.Message : null, now);
            _status = _status with { State = outcome.Erro ? RobotRunStatus.Error : RobotRunStatus.Completed, FinishedAt = now, Message = outcome.Message };
            return Compose();
        }
    }

    /// <summary>"ETAPA 05B - WORKFLOW" → "5B"; demais registros (INICIALIZAÇÃO, DESCOBERTA, PROJETO) → null.</summary>
    public static string? StageIdOf(string? etapa)
    {
        if (string.IsNullOrWhiteSpace(etapa) || !etapa.StartsWith("ETAPA ", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = etapa[6..];
        var cut = rest.IndexOf(" - ", StringComparison.Ordinal);
        var id = (cut < 0 ? rest : rest[..cut]).Trim();
        return id.Length == 0 ? null : RobotStageCatalog.Normalize(id);
    }

    private static ProjectPlan NewPlan(string projectId, IEnumerable<string> stages) =>
        new(projectId, stages.Select(id => new RobotStageProgress(id, RobotStageCatalog.NameOf(id), RobotStageProgress.Pending)).ToArray());

    /// <summary>Projeto encerrado: o que não rodou deixa de ser trabalho pendente (SKIPPED); etapa interrompida termina com erro.</summary>
    private static void CloseProject(ProjectPlan plan, string? error, DateTime now)
    {
        for (int i = 0; i < plan.Stages.Length; i++)
        {
            var s = plan.Stages[i];
            if (s.State == RobotStageProgress.Running)
                plan.Stages[i] = s with { State = RobotStageProgress.Error, FinishedAt = now, Message = Resumo(error ?? "Etapa interrompida sem conclusão registrada.") };
            else if (s.State == RobotStageProgress.Pending)
                plan.Stages[i] = s with { State = RobotStageProgress.Skipped, FinishedAt = now };
        }
    }

    private static string? Resumo(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var line = s.Split('\n')[0].Trim();
        return line.Length <= 300 ? line : line[..300] + "…";
    }

    private RobotRunStatus Compose()
    {
        var recent = _entries.Skip(Math.Max(0, _entries.Count - RecentLimit))
            .Select(e => new RobotRecentEntry(e.At, e.Projeto, e.Etapa, e.Status, Resumo(e.Mensagem))).ToArray();
        if (_plans.Count == 0) return _status with { RecentEntries = recent, ProgressPercent = _status.State == RobotRunStatus.Completed ? 100 : null };
        var all = _plans.SelectMany(p => p.Stages).ToArray();
        int total = all.Length, done = all.Count(s => s.Finished);
        var stages = _current >= 0 && _current < _plans.Count ? _plans[_current].Stages.ToArray() : Array.Empty<RobotStageProgress>();
        var running = Array.FindIndex(stages, s => s.State == RobotStageProgress.Running);
        return _status with
        {
            RecentEntries = recent, Stages = Array.AsReadOnly(stages), CurrentStageId = running >= 0 ? stages[running].Id : null,
            StagePosition = running >= 0 ? running + 1 : stages.Count(s => s.Finished), StageTotal = stages.Length,
            OverallTotalStages = total, OverallCompletedStages = done,
            OverallFailedStages = all.Count(s => s.State == RobotStageProgress.Error), OverallSkippedStages = all.Count(s => s.State == RobotStageProgress.Skipped),
            ProgressPercent = total == 0 ? (_status.State == RobotRunStatus.Completed ? 100 : 0) : Math.Clamp((int)Math.Floor(done * 100.0 / total), 0, 100),
        };
    }
}

public static class RobotCycleOutcomeRules
{
    /// <summary>Resultado real do ciclo a partir do histórico que o Worker já grava e do erro fatal capturado (nunca "sucesso" por padrão).</summary>
    public static RobotCycleOutcome Evaluate(IReadOnlyList<RobotHistoricoEntry> entries, string? fatalError)
    {
        if (!string.IsNullOrWhiteSpace(fatalError)) return new(true, $"Erro fatal no robô: {Truncate(fatalError)}");
        var erros = entries.Where(e => e.Status.Contains("ERRO", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (erros.Length > 0)
        {
            var first = erros[0];
            var detalhe = string.IsNullOrWhiteSpace(first.Mensagem) ? "" : $" — {Truncate(first.Mensagem.Split('\n')[0])}";
            return new(true, $"{erros.Length} registro(s) com erro (primeiro: {first.Projeto} / {first.Etapa}){detalhe}");
        }
        var finais = entries.Where(e => e.Etapa == "PROJETO").Select(e => $"{e.Projeto}: {e.Status}").ToArray();
        if (finais.Length == 0)
            return new(false, "Ciclo finalizado sem resultado consolidado do projeto (ex.: LDP não encontrada). Consulte o log da execução.");
        return new(false, string.Join(" | ", finais));
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}

/// <summary>
/// Disparo manual do MESMO pipeline do Worker: o runner só decide (portão, projeto, estado) e delega a execução ao delegate, que é o próprio
/// GrdAutomationWorker.ExecutarCicloManualAsync → ProcessarCicloCompleto. Não há segundo Worker nem cópia de etapas.
/// </summary>
public sealed class ManualRobotRunner(RobotRunGate gate, Func<string?, CancellationToken, Task<RobotCycleOutcome>> pipeline, CancellationToken appStopping,
    Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.Now);
    public RobotRunStatus Status => gate.Snapshot;

    public RobotRunStartResult TryStart(string? projectId, string? projectName)
    {
        if (!gate.TryBegin(true, projectId, projectName, _now(), out var status)) return new(false, status);
        _ = Task.Run(async () =>
        {
            RobotCycleOutcome outcome;
            try { outcome = await pipeline(projectId, appStopping); }
            catch (Exception ex) { outcome = new(true, $"Erro ao executar o robô: {ex.Message}"); }
            gate.Finish(outcome, _now());
        });
        return new(true, status);
    }
    public Task Completion { get; private set; } = Task.CompletedTask;

    public RobotRunStartResult TryStartProjects(IReadOnlyList<ManualRobotProject> projects)
    {
        if (projects.Count == 0 || projects.Any(p => string.IsNullOrWhiteSpace(p.ProjectId)))
            throw new ArgumentException("Selecione ao menos um projeto válido.", nameof(projects));
        var selected = projects.DistinctBy(p => p.ProjectId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!gate.TryBeginManualProjects(selected, _now(), out var status)) return new(false, status);
        Completion = Task.Run(async () =>
        {
            var failed = false;
            var completed = 0;
            RobotCycleOutcome? last = null;
            try
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    var project = selected[i];
                    appStopping.ThrowIfCancellationRequested();
                    gate.ProjectStarted(i + 1, project.ProjectId, project.Name ?? project.ProjectId,
                        i + 1 < selected.Length ? selected[i + 1].Name ?? selected[i + 1].ProjectId : null);
                    try { last = await pipeline(project.ProjectId, appStopping); }
                    catch (Exception ex) { last = new(true, $"Erro ao executar o robô: {ex.Message}"); }
                    gate.ProjectFinished(project.ProjectId, last);
                    failed |= last.Erro;
                    completed++;
                }
            }
            catch (Exception ex) { failed = true; last = new(true, ex.Message); }
            finally
            {
                gate.Finish(selected.Length == 1 && last != null ? last :
                    new(failed, $"Execução manual finalizada: {completed}/{selected.Length} projetos processados. " +
                        (failed ? "Ocorreram erros; consulte o histórico." : "Concluído.")), _now());
            }
        });
        return new(true, status);
    }

}
