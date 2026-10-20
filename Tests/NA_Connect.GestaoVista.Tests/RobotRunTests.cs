using APS.WebApp.Services;
using APS.WebApp.Services.RobotRun;
using Microsoft.Extensions.Hosting;
namespace NA_Connect.GestaoVista.Tests;

public class RobotRunTests
{
    private static async Task<RobotRunStatus> WaitFinished(RobotRunGate gate)
    {
        for (var i = 0; i < 200 && gate.Snapshot.State == RobotRunStatus.Running; i++) await Task.Delay(10);
        return gate.Snapshot;
    }

    [Fact]
    public async Task CliqueDisparaOPipelineComOProjetoCorretoEConclusaoAtualizaOEstado()
    {
        var gate = new RobotRunGate(); string? recebido = "nao-chamado"; var chamadas = 0;
        var runner = new ManualRobotRunner(gate, (p, ct) => { recebido = p; chamadas++; return Task.FromResult(new RobotCycleOutcome(false, "P1: CONCLUIDO COM SUCESSO")); }, CancellationToken.None);
        var r = runner.TryStart("b.11111111-1111-1111-1111-111111111111", "BML011.013");
        Assert.True(r.Started);
        Assert.Equal(RobotRunStatus.Running, r.Status.State);
        Assert.Equal("BML011.013", r.Status.ProjectName); Assert.NotNull(r.Status.StartedAt); Assert.True(r.Status.Manual);
        var fim = await WaitFinished(gate);
        Assert.Equal(1, chamadas); Assert.Equal("b.11111111-1111-1111-1111-111111111111", recebido);
        Assert.Equal(RobotRunStatus.Completed, fim.State); Assert.NotNull(fim.FinishedAt); Assert.Equal("P1: CONCLUIDO COM SUCESSO", fim.Message);
    }

    [Fact]
    public async Task SegundoDisparoConcorrenteEBloqueadoEDepoisDaConclusaoLiberaNovamente()
    {
        var gate = new RobotRunGate(); var solta = new TaskCompletionSource<RobotCycleOutcome>(); var chamadas = 0;
        var runner = new ManualRobotRunner(gate, (p, ct) => { Interlocked.Increment(ref chamadas); return solta.Task; }, CancellationToken.None);
        Assert.True(runner.TryStart("A", "A").Started);
        var segundo = runner.TryStart("B", "B");
        Assert.False(segundo.Started);                       // → HTTP 409 "Robô já está em execução."
        Assert.Equal(RobotRunStatus.Running, segundo.Status.State); Assert.Equal("A", segundo.Status.ProjectId);
        Assert.False(runner.TryStart("A", "A").Started);      // mesmo projeto também bloqueado
        for (var i = 0; i < 200 && Volatile.Read(ref chamadas) == 0; i++) await Task.Delay(10);
        Assert.Equal(1, Volatile.Read(ref chamadas));       // o pipeline foi chamado UMA vez
        solta.SetResult(new(false, "ok"));
        await WaitFinished(gate);
        Assert.True(runner.TryStart("B", "B").Started);
    }

    [Fact]
    public async Task ErroDoPipelineChegaNaUiComMensagemRealENaoTravaOPortao()
    {
        var gate = new RobotRunGate();
        var runner = new ManualRobotRunner(gate, (p, ct) => throw new InvalidOperationException("LDP inacessível"), CancellationToken.None);
        Assert.True(runner.TryStart("A", "A").Started);
        var fim = await WaitFinished(gate);
        Assert.Equal(RobotRunStatus.Error, fim.State); Assert.Contains("LDP inacessível", fim.Message);
        Assert.True(runner.TryStart("A", "A").Started);
    }

    [Fact]
    public void CicloAutomaticoOcupaOMesmoPortaoEBloqueiaOManualEViceVersa()
    {
        var gate = new RobotRunGate();
        Assert.True(gate.TryBegin(false, null, null, DateTime.Now, out var auto));
        Assert.False(auto.Manual);
        var runner = new ManualRobotRunner(gate, (p, ct) => Task.FromResult(new RobotCycleOutcome(false, "x")), CancellationToken.None);
        Assert.False(runner.TryStart("A", "A").Started);
        gate.Finish(new(false, "auto ok"), DateTime.Now);
        Assert.True(runner.TryStart("A", "A").Started);
        Assert.False(gate.TryBegin(false, null, null, DateTime.Now, out _));
    }

    [Fact]
    public void ResultadoRealNuncaEhSucessoPorPadrao()
    {
        Assert.True(RobotCycleOutcomeRules.Evaluate([], "timeout SQL").Erro);
        var comErro = RobotCycleOutcomeRules.Evaluate([new("P", "Etapa 4", "ERRO", "System.Exception: falhou\n  at ...")], null);
        Assert.True(comErro.Erro); Assert.Contains("P / Etapa 4", comErro.Message); Assert.Contains("falhou", comErro.Message);
        Assert.True(RobotCycleOutcomeRules.Evaluate([new("P", "Etapa 17", "ERRO", "x")], null).Erro);
        var ok = RobotCycleOutcomeRules.Evaluate([new("P", "PROJETO", "CONCLUIDO COM ALERTAS", "")], null);
        Assert.False(ok.Erro); Assert.Equal("P: CONCLUIDO COM ALERTAS", ok.Message);
        var vazio = RobotCycleOutcomeRules.Evaluate([], null);
        Assert.False(vazio.Erro); Assert.Contains("sem resultado consolidado", vazio.Message);
    }

    [Fact]
    public void HistoricoSoEhCapturadoDuranteExecucaoEReiniciaACadaCiclo()
    {
        var gate = new RobotRunGate();
        gate.Record(new("P", "PROJETO", "X", ""));
        Assert.Empty(gate.Entries());
        Assert.True(gate.TryBegin(true, "A", "A", DateTime.Now, out _));
        gate.Record(new("P", "PROJETO", "CONCLUIDO COM SUCESSO", ""));
        Assert.Single(gate.Entries());
        gate.Finish(new(false, "ok"), DateTime.Now);
        Assert.True(gate.TryBegin(true, "A", "A", DateTime.Now, out _));
        Assert.Empty(gate.Entries());
    }

    [Fact]
    public void WorkerAutomaticoContinuaSendoOBackgroundServiceQueExpoeOMesmoPipelineAoDisparoManual()
    {
        Assert.True(typeof(BackgroundService).IsAssignableFrom(typeof(GrdAutomationWorker)));
        var manual = typeof(GrdAutomationWorker).GetMethod(nameof(GrdAutomationWorker.ExecutarCicloManualAsync));
        Assert.NotNull(manual);
        Assert.Equal(typeof(RobotRunGate), typeof(GrdAutomationWorker).GetProperty(nameof(GrdAutomationWorker.Gate))!.PropertyType);
        var cycle = typeof(GrdAutomationWorker).GetMethod("ProcessarCicloCompleto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(cycle);
        Assert.Equal(new[] { "stoppingToken", "projetoManual", "manual" }, cycle!.GetParameters().Select(p => p.Name));
    }
}
