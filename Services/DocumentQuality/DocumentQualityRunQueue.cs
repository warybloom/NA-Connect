using System.Collections.Concurrent;

namespace APS.WebApp.Services.DocumentQuality;

// =============================================================================================
// AUDITORIA DE QUALIDADE — EXECUÇÃO MANUAL EM SEGUNDO PLANO (01/10/2026).
// Antes: POST /api/document-quality/audits rodava a auditoria INTEIRA dentro da requisição (listar ACC, baixar e ler cada PDF
// em sequência, OperationalAudit, relatório) → passava do limite do proxy (Cloudflare 100 s → HTTP 524).
// Agora: o POST só enfileira e responde 202 com o executionId; o processamento segue aqui, fora da requisição, e a tela consulta
// GET /api/document-quality/runs/{id}. Mesmo padrão do disparo manual do robô (portão em memória + Task.Run com o token de parada
// da aplicação), mas SEPARADO do robô: não usa o Worker, o Agendador nem a antiga etapa OCR.
// A regra documental (DocumentQualityAuditService → ComparisonEngine, PdfPig, OperationalAudit, relatório) não muda.
// =============================================================================================

public static class DocumentQualityRunState
{
    public const string Queued = "QUEUED", Running = "RUNNING", Completed = "COMPLETED", Failed = "FAILED";
    public static bool Active(string s) => s is Queued or Running;
}

public sealed record DocumentQualityRunRequest(string HubId, string ProjectIdAcc, string AccessToken, string TriggeredBy);

/// <summary>Estado de uma execução (o resultado completo só aparece em COMPLETED).</summary>
public sealed record DocumentQualityRunStatus(Guid ExecutionId, string ProjectIdAcc, string Status, int? Total, int Processed, string? CurrentFile,
    DateTime QueuedAtUtc, DateTime? StartedAtUtc, DateTime? FinishedAtUtc, string? Error, string TriggeredBy, DocumentQualityRunResult? Result);

public sealed record DocumentQualityRunStart(bool Started, DocumentQualityRunStatus Status);

public delegate Task<DocumentQualityRunResult> DocumentQualityRunPipeline(DocumentQualityRunRequest request, Guid executionId,
    Action<DocumentQualityProgress> onProgress, CancellationToken cancellationToken);

/// <summary>
/// Fila/registro em memória das auditorias manuais. Uma execução ativa por projeto (um segundo clique devolve a mesma execução);
/// no máximo <c>maxConcurrent</c> auditorias ao mesmo tempo no servidor (as demais ficam QUEUED) — sem paralelismo agressivo contra o ACC.
/// O estado vive no processo (como o status do robô): após reiniciar o servidor a execução some, mas o que já foi gravado
/// (OperationalAudit e o relatório CSV) permanece.
/// </summary>
public sealed class DocumentQualityRunQueue(DocumentQualityRunPipeline pipeline, ILogger<DocumentQualityRunQueue> logger, CancellationToken appStopping,
    int maxConcurrent = 1, TimeSpan? maxDuration = null, Func<DateTime>? clock = null)
{
    private static readonly TimeSpan Retencao = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<Guid, DocumentQualityRunStatus> _runs = new();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, maxConcurrent), Math.Max(1, maxConcurrent));
    private readonly object _startLock = new();
    private readonly TimeSpan _maxDuration = maxDuration ?? TimeSpan.FromHours(2);
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);

    /// <summary>Tarefa da última execução disparada (testes).</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public DocumentQualityRunStatus? Get(Guid executionId) => _runs.TryGetValue(executionId, out var s) ? s : null;

    public DocumentQualityRunStart Start(DocumentQualityRunRequest request)
    {
        DocumentQualityRunStatus status;
        lock (_startLock)
        {
            Podar();
            var ativa = _runs.Values.Where(r => DocumentQualityRunState.Active(r.Status) && string.Equals(r.ProjectIdAcc, request.ProjectIdAcc, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.QueuedAtUtc).FirstOrDefault();
            if (ativa != null) return new(false, ativa);
            status = new(Guid.NewGuid(), request.ProjectIdAcc, DocumentQualityRunState.Queued, null, 0, null, _now(), null, null, null, request.TriggeredBy, null);
            _runs[status.ExecutionId] = status;
        }
        Completion = Task.Run(() => ExecutarAsync(request, status.ExecutionId));
        return new(true, status);
    }

    private void Atualizar(Guid id, Func<DocumentQualityRunStatus, DocumentQualityRunStatus> f)
    {
        while (_runs.TryGetValue(id, out var atual) && !_runs.TryUpdate(id, f(atual), atual)) { }
    }

    private async Task ExecutarAsync(DocumentQualityRunRequest request, Guid id)
    {
        var slot = false;
        try
        {
            await _slots.WaitAsync(appStopping);
            slot = true;
            Atualizar(id, s => s with { Status = DocumentQualityRunState.Running, StartedAtUtc = _now() });
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
            cts.CancelAfter(_maxDuration);
            var result = await pipeline(request, id,
                p => Atualizar(id, s => s with { Total = p.Total, Processed = p.Processed, CurrentFile = p.CurrentFile }), cts.Token);
            Atualizar(id, s => s with
            {
                Status = DocumentQualityRunState.Completed, FinishedAtUtc = _now(), CurrentFile = null,
                Total = result.Summary.Total, Processed = result.Summary.Total, Result = result,
            });
            logger.LogInformation("[DOC_QUALITY] Execução {ExecutionId} concluída para {ProjectId}: {Total} PDF(s).", id, request.ProjectIdAcc, result.Summary.Total);
        }
        catch (Exception ex)
        {
            var mensagem = MensagemDeErro(ex, appStopping.IsCancellationRequested);
            if (ex is DocumentQualityConfigurationException or InvalidOperationException)
                logger.LogWarning("[DOC_QUALITY] Execução {ExecutionId} recusada para {ProjectId}: {Mensagem}", id, request.ProjectIdAcc, ex.Message);
            else logger.LogError(ex, "[DOC_QUALITY] Execução {ExecutionId} falhou para {ProjectId}.", id, request.ProjectIdAcc);
            Atualizar(id, s => s with { Status = DocumentQualityRunState.Failed, FinishedAtUtc = _now(), CurrentFile = null, Error = mensagem });
        }
        finally
        {
            if (slot) _slots.Release();
        }
    }

    /// <summary>Erro REAL para o usuário (configuração, ACC, tempo esgotado), sem detalhes internos de SQL/token.</summary>
    public static string MensagemDeErro(Exception ex, bool aplicacaoParando) => ex switch
    {
        DocumentQualityConfigurationException or InvalidOperationException => ex.Message,
        HttpRequestException h => h.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? "O ACC recusou o acesso à pasta auditada (sessão expirada ou sem permissão). Entre novamente e repita a auditoria."
            : "Não foi possível ler a pasta ou os PDFs no ACC.",
        OperationCanceledException when aplicacaoParando => "A auditoria foi interrompida porque o servidor foi reiniciado.",
        OperationCanceledException => "A auditoria excedeu o tempo máximo de execução e foi interrompida.",
        Microsoft.Data.SqlClient.SqlException => "Falha ao acessar o banco de dados durante a auditoria.",
        _ => $"Erro inesperado na auditoria ({ex.GetType().Name}).",
    };

    private void Podar()
    {
        var limite = _now() - Retencao;
        foreach (var r in _runs.Values.Where(r => !DocumentQualityRunState.Active(r.Status) && (r.FinishedAtUtc ?? r.QueuedAtUtc) < limite).ToArray())
            _runs.TryRemove(r.ExecutionId, out _);
    }
}
