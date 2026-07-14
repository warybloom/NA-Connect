using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Handler global de mitigação de 429 (Too Many Requests) para a API da Autodesk.
    ///
    /// Por que um DelegatingHandler em vez de embrulhar cada chamada manualmente:
    /// - Cobre TODAS as chamadas feitas pelo HttpClient injetado (SendAsync direto,
    ///   uploads, downloads, etc.) sem precisar alterar cada método do AutodeskApiService.
    /// - Fica registrado uma única vez no pipeline do HttpClient (Program.cs), então
    ///   qualquer novo método que alguém adicionar no futuro já herda a proteção
    ///   automaticamente — não depende de lembrar de usar um wrapper específico.
    /// - Centraliza a lógica de throttle + backoff + respeito ao header Retry-After
    ///   num único lugar, testável isoladamente.
    /// </summary>
    public class RateLimitingHandler : DelegatingHandler
    {
        private readonly ILogger<RateLimitingHandler> _logger;

        // Limita quantas requisições concorrentes saem ao mesmo tempo para a Autodesk.
        // Ajuste esse número conforme o endpoint mais sensível do seu fluxo
        // (Data Management costuma tolerar menos concorrência que Admin/Account).
        private readonly SemaphoreSlim _throttleGate;

        private const int MaxRetries = 5;

        // Estatísticas simples, opcionais — substituem os contadores estáticos
        // espalhados hoje pelo AutodeskApiService (_totalCalls, TotalApiCallsInCurrentCycle etc.)
        private static int _totalCalls = 0;
        private static string _lastRateLimitRemaining = "N/A";

        public RateLimitingHandler(ILogger<RateLimitingHandler> logger, int maxConcurrentRequests = 3)
        {
            _logger = logger;
            _throttleGate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        }

        public static (int TotalCalls, string LastRateLimitRemaining) GetTelemetry()
            => (_totalCalls, _lastRateLimitRemaining);

        public static void ResetTelemetry()
        {
            _totalCalls = 0;
            _lastRateLimitRemaining = "N/A";
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _throttleGate.WaitAsync(cancellationToken);
            try
            {
                int attempt = 0;

                while (true)
                {
                    attempt++;

                    // Clona a request a cada tentativa: HttpRequestMessage não pode
                    // ser reenviado depois do primeiro SendAsync.
                    using var requestClone = await CloneRequestAsync(request);

                    HttpResponseMessage response;
                    try
                    {
                        response = await base.SendAsync(requestClone, cancellationToken);
                    }
                    catch (HttpRequestException ex) when (attempt <= MaxRetries)
                    {
                        // Falha de rede/conexão também merece retry com backoff.
                        _logger.LogWarning(ex, "Falha de rede na tentativa {Attempt}, tentando novamente...", attempt);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                        continue;
                    }

                    TraceHeaders(response);

                    if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    {
                        // Se o rate limit remanescente está baixo, dá uma folga
                        // proativa mesmo sem ter tomado 429 ainda.
                        if (int.TryParse(_lastRateLimitRemaining, out int remaining) && remaining < 5)
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                        return response;
                    }

                    if (attempt > MaxRetries)
                    {
                        _logger.LogError(
                            "🚫 429 persistente após {Attempts} tentativas para {Url}.",
                            attempt, request.RequestUri);
                        return response;
                    }

                    TimeSpan delay = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date.HasValue == true
                            ? response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow
                            : TimeSpan.FromSeconds(Math.Pow(2, attempt)));

                    if (delay < TimeSpan.Zero) delay = TimeSpan.FromSeconds(1);

                    _logger.LogWarning(
                        "⏳ 429 recebido para {Url} (tentativa {Attempt}/{Max}). Aguardando {Delay}s...",
                        request.RequestUri, attempt, MaxRetries, delay.TotalSeconds);

                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                }
            }
            finally
            {
                _throttleGate.Release();
            }
        }

        private void TraceHeaders(HttpResponseMessage response)
        {
            Interlocked.Increment(ref _totalCalls);
            if (response.Headers.TryGetValues("X-Ads-RateLimit-Remaining", out var values))
            {
                _lastRateLimitRemaining = values.FirstOrDefault() ?? "N/A";
            }
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri)
            {
                Version = original.Version
            };

            if (original.Content != null)
            {
                var contentBytes = await original.Content.ReadAsByteArrayAsync();
                var newContent = new ByteArrayContent(contentBytes);
                foreach (var header in original.Content.Headers)
                {
                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                clone.Content = newContent;
            }

            foreach (var header in original.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var prop in original.Options)
            {
                clone.Options.Set(new HttpRequestOptionsKey<object?>(prop.Key), prop.Value);
            }

            return clone;
        }
    }
}
