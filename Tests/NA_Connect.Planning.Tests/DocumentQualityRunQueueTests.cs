using APS.WebApp.Services.DocumentQuality;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>"Auditar agora" fora da requisição HTTP: enfileira, mostra progresso e termina em COMPLETED/FAILED.</summary>
public class DocumentQualityRunQueueTests
{
    private const string Proj = "b.11111111-1111-1111-1111-111111111111", Proj2 = "b.22222222-2222-2222-2222-222222222222";
    private static DocumentQualityRunRequest Req(string projeto = Proj) => new("b.hub", projeto, "token", "user@na");

    private static DocumentQualityRunResult Resultado(Guid id, int total) => new(id, DateTime.UtcNow, DateTime.UtcNow, Proj, "CHS001", "Project Files/PDF", 1, "Pacote",
        new DocumentQualitySummary(total, total, 0, 0, 0, 0, 0), Array.Empty<DocumentQualityDocumentResult>(), true, null);

    private static DocumentQualityRunQueue Fila(DocumentQualityRunPipeline pipeline, int maxConcurrent = 1, CancellationToken stopping = default) =>
        new(pipeline, NullLogger<DocumentQualityRunQueue>.Instance, stopping, maxConcurrent);

    [Fact]
    public async Task Start_RespondeNaHora_ComProgresso_ETerminaComResultado()
    {
        var liberar = new TaskCompletionSource();
        var lendo = new TaskCompletionSource();
        var fila = Fila(async (req, id, progresso, ct) =>
        {
            progresso(new(184, 37, "CHS001-PR-PID-001.pdf"));
            lendo.SetResult();
            await liberar.Task;
            return Resultado(id, 184);
        });

        var start = fila.Start(Req());
        Assert.True(start.Started);
        Assert.Equal(DocumentQualityRunState.Queued, start.Status.Status); // o POST não espera o processamento

        await lendo.Task;
        var rodando = fila.Get(start.Status.ExecutionId)!;
        Assert.Equal(DocumentQualityRunState.Running, rodando.Status);
        Assert.Equal((184, 37, "CHS001-PR-PID-001.pdf"), (rodando.Total!.Value, rodando.Processed, rodando.CurrentFile));

        liberar.SetResult();
        await fila.Completion;
        var fim = fila.Get(start.Status.ExecutionId)!;
        Assert.Equal(DocumentQualityRunState.Completed, fim.Status);
        Assert.Equal(start.Status.ExecutionId, fim.Result!.ExecutionId); // relatório/OperationalAudit usam o mesmo id
        Assert.Equal(184, fim.Processed);
    }

    [Fact]
    public async Task SegundoClique_NoMesmoProjeto_DevolveAMesmaExecucao()
    {
        var liberar = new TaskCompletionSource();
        var fila = Fila(async (req, id, p, ct) => { await liberar.Task; return Resultado(id, 1); });
        var a = fila.Start(Req());
        var b = fila.Start(Req());
        Assert.False(b.Started);
        Assert.Equal(a.Status.ExecutionId, b.Status.ExecutionId);
        liberar.SetResult();
        await fila.Completion;
    }

    [Fact]
    public async Task UmaAuditoriaPorVez_OutroProjetoFicaNaFila()
    {
        var liberar = new TaskCompletionSource();
        var iniciadas = 0;
        var fila = Fila(async (req, id, p, ct) => { Interlocked.Increment(ref iniciadas); await liberar.Task; return Resultado(id, 1); });
        var a = fila.Start(Req(Proj));
        var b = fila.Start(Req(Proj2));
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref iniciadas));
        Assert.Equal(DocumentQualityRunState.Queued, fila.Get(b.Status.ExecutionId)!.Status);
        liberar.SetResult();
        await fila.Completion;
        Assert.Equal(DocumentQualityRunState.Completed, fila.Get(a.Status.ExecutionId)!.Status);
        Assert.Equal(DocumentQualityRunState.Completed, fila.Get(b.Status.ExecutionId)!.Status);
    }

    [Fact]
    public async Task Falha_ViraFailed_ComErroReal()
    {
        var fila = Fila((req, id, p, ct) => throw new DocumentQualityConfigurationException("O template selecionado não possui regiões calibradas."));
        var start = fila.Start(Req());
        await fila.Completion;
        var fim = fila.Get(start.Status.ExecutionId)!;
        Assert.Equal(DocumentQualityRunState.Failed, fim.Status);
        Assert.Equal("O template selecionado não possui regiões calibradas.", fim.Error);
        // Depois de terminar, um novo clique cria outra execução.
        Assert.True(fila.Start(Req()).Started);
        await fila.Completion;
    }

    [Fact]
    public void MensagemDeErro_TraduzFalhasDeIntegracao()
    {
        Assert.Equal("Não foi possível ler a pasta ou os PDFs no ACC.",
            DocumentQualityRunQueue.MensagemDeErro(new HttpRequestException("x", null, System.Net.HttpStatusCode.BadGateway), false));
        Assert.Contains("sessão expirada", DocumentQualityRunQueue.MensagemDeErro(new HttpRequestException("x", null, System.Net.HttpStatusCode.Unauthorized), false));
        Assert.Contains("tempo máximo", DocumentQualityRunQueue.MensagemDeErro(new OperationCanceledException(), false));
        Assert.Contains("reiniciado", DocumentQualityRunQueue.MensagemDeErro(new OperationCanceledException(), true));
    }
}
