using System.Text.RegularExpressions;
using APS.WebApp.Services;
using APS.WebApp.Services.RobotRun;
using Microsoft.Extensions.Logging;

namespace NA_Connect.Workflow.Tests;

public class WorkerStageExecutionTests
{
    private static readonly string[] Order = ["00", "01", "02", "03", "04", "05", "05B", "06", "07", "09", "10", "11", "12", "13", "14", "15", "16", "17"];

    [Fact]
    public void WorkerExecutaNaOrdemOperacionalSemEtapa08OuPersistenciaDentroDoEspelho()
    {
        var worker = Source("Services/GrdAutomationWorker.cs");
        var calls = Regex.Matches(worker, "await ExecutarEtapaAsync\\(\"(?<id>[^\"]+)\"|\\[ETAPA (?<disabled>02)\\]\\[PULADA\\]")
            .Select(m => m.Groups["disabled"].Success ? "02" : m.Groups["id"].Value)
            .Where(id => id != "GESTAO_VISTA").Select(id => id == "5B" ? "05B" : id);
        Assert.Equal(Order, calls);
        int start = worker.IndexOf("await ExecutarEtapaAsync(\"00\"", StringComparison.Ordinal);
        int end = worker.IndexOf("// Resultado geral do PROJETO", start, StringComparison.Ordinal);
        Assert.DoesNotContain("continue;", worker[start..end]);
        Assert.DoesNotContain("RegistrarEquivalenteTipo2Async", worker);
        Assert.DoesNotContain("RunApprovalVerdictSyncAsync(", worker);
        Assert.Contains("[ETAPA 02][PULADA] Motivo=DESABILITADA_TEMPORARIAMENTE", worker);
        int stage11 = worker.IndexOf("await ExecutarEtapaAsync(\"11\"", StringComparison.Ordinal);
        int stage12 = worker.IndexOf("await ExecutarEtapaAsync(\"12\"", StringComparison.Ordinal);
        Assert.Contains("GetItemDetailsAsync", worker[stage11..stage12]);
        Assert.Contains("parser.ProcessarAsync", worker[stage11..stage12]);
        Assert.Contains("persistence.PersistirAsync", worker[stage11..stage12]);
        var mirror = Source("Services/LdpTipo2/LdpTipo2EspelhoService.cs");
        Assert.DoesNotContain("PersistirAsync", mirror);
        Assert.DoesNotContain("SqlConnection", mirror);
        Assert.DoesNotContain("DataSincronizacao", mirror);
        Assert.Contains("CreateNewVersionAsync(projectId, mirrorId, folder, mirrorName", mirror);
        Assert.Contains("@\"-\\d{2}\\.xlsx$\", RegexOptions.IgnoreCase", mirror);
    }

    [Fact]
    public async Task FalhaNoEspelhoNaoImpedePersistenciaFinalEAsEtapasSeguintes()
    {
        var logger = new CaptureLogger();
        var executed = new List<string>();
        foreach (string id in Order.SkipWhile(id => id != "10"))
        {
            var result = await GrdAutomationWorker.ExecutarEtapaIsoladaAsync(id, () =>
            {
                executed.Add(id);
                if (id == "10") throw new InvalidOperationException("Falha ACC no espelho");
                return Task.FromResult((0, "Concluído"));
            }, logger, CancellationToken.None, tipo: "Tipo2");
            Assert.Equal(id == "10" ? 3 : 0, result.Gravidade);
        }
        Assert.Equal(new[] { "10", "11", "12", "13", "14", "15", "16", "17" }, executed);
        int error = logger.Messages.FindIndex(m => m.Contains("[ETAPA 10][ERRO]"));
        int persist = logger.Messages.FindIndex(m => m.Contains("[ETAPA 11][INICIO] Tipo2"));
        Assert.True(error >= 0 && persist > error);
        Assert.Contains("[ETAPA 17][FIM] Tipo2", logger.Messages);
    }

    [Fact]
    public async Task ResultadoDeFalhaSemExceptionTambemGeraErroECancelamentoNaoESilenciado()
    {
        var logger = new CaptureLogger();
        var result = await GrdAutomationWorker.ExecutarEtapaIsoladaAsync("09",
            () => Task.FromResult((3, "Router falhou")), logger, CancellationToken.None);
        Assert.Equal(3, result.Gravidade);
        Assert.Contains(logger.Messages, m => m.Contains("[ETAPA 09][ERRO] Router falhou"));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GrdAutomationWorker.ExecutarEtapaIsoladaAsync("10", () =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }, logger, cancellation.Token));
    }

    [Fact]
    public void CatalogoSegueOrdemEPermiteEtapasTipo2Independentes()
    {
        var catalog = RobotStageCatalog.Stages.Where(s => s.Id != "GESTAO_VISTA" && s.Id != "08")
            .OrderBy(s => s.Order).Select(s => s.Id == "5B" ? "05B" : s.Id);
        Assert.Equal(Order, catalog);
        foreach (string id in new[] { "00", "06", "09", "10", "11" })
            Assert.Equal(new[] { id }, RobotStageCatalog.Validate([id], 2));
        Assert.False(RobotStageCatalog.Stages.Single(s => s.Id == "02").Available);
        Assert.False(RobotStageCatalog.Stages.Single(s => s.Id == "08").Available);
    }

    private static string Source(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(path);
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
