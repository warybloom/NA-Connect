namespace APS.WebApp.Services.RobotRun;

public sealed record RobotQueueProjectResult(string ProjectId, string Name, string State, string? Message);

/// <summary>Holds the existing global gate across ALL projects. Calls the same worker, never copies stages.</summary>
public sealed class RobotQueueRunner(RobotRunGate gate, IRobotProjectDirectory directory,
    Func<string, IReadOnlyList<string>, CancellationToken, Task<RobotCycleOutcome>> pipeline, CancellationToken stopping)
{
    public Task Completion { get; private set; } = Task.CompletedTask;
    public RobotRunStartResult TryStart(string robotId, IReadOnlyList<RobotQueueItem> selected)
    {
        if (string.IsNullOrWhiteSpace(robotId) || selected.Count == 0) throw new ArgumentException("Fila vazia.");
        var queue = selected.OrderBy(p => p.Order).Select(p => p with { Stages = RobotStageCatalog.Validate(p.Stages) }).ToArray();
        if (queue.Select(p => p.ProjectId).Distinct().Count() != queue.Length) throw new ArgumentException("Projeto repetido.");
        if (!gate.TryBeginQueue(robotId, queue, out var status)) return new(false, status);
        Completion = Task.Run(async () =>
        {
            int errors = 0;
            try
            {
                for (int i = 0; i < queue.Length; i++)
                {
                    var item = queue[i];
                    gate.QueueProjectStarted(i + 1, item, i + 1 < queue.Length ? queue[i + 1].Name : null);
                    RobotCycleOutcome outcome;
                    try
                    {
                        stopping.ThrowIfCancellationRequested();
                        var projects = await directory.ReadAsync(stopping);
                        var project = projects.SingleOrDefault(p => p.ProjectId == item.ProjectId);
                        if (project is not { Active: true }) throw new InvalidOperationException("Projeto ficou inativo ou deixou de existir; execução bloqueada.");
                        var stages = RobotStageCatalog.Validate(item.Stages, project.TipoLdp);
                        outcome = await pipeline(project.ProjectId, stages, stopping);
                    }
                    catch (Exception ex) { outcome = new(true, ex is OperationCanceledException ? "Aplicação encerrando; projeto não executado." : ex.Message); }
                    if (outcome.Erro) errors++;
                    gate.QueueProjectFinished(item, outcome);
                }
            }
            catch (Exception) { errors++; }
            finally { gate.Finish(new(errors > 0, errors > 0 ? $"CONCLUÍDO COM ERROS: {errors} projeto(s)/falha(s)." : "CONCLUÍDO COM SUCESSO"), DateTime.Now); }
        });
        return new(true, status);
    }
}
