using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace APS.WebApp.Services.Http
{
    /// <summary>
    /// DelegatingHandler modular responsável por aplicar retry com backoff exponencial
    /// em todas as chamadas HTTP feitas contra a Autodesk Platform Services (APS).
    ///
    /// Comportamento alinhado à documentação oficial da Autodesk sobre rate limiting
    /// (https://aps.autodesk.com/blog/rate-limiting e
    /// https://aps.autodesk.com/blog/autodesk-platform-services-aps-api-rate-limits-best-practices-developers):
    ///   - Em respostas HTTP 429 (Too Many Requests), a APS retorna o cabeçalho "Retry-After"
    ///     com o número de segundos que o cliente deve aguardar antes de tentar novamente.
    ///     Esse valor é sempre respeitado integralmente quando presente.
    ///   - Quando não há "Retry-After" (ex.: erros transitórios 5xx ou falhas de rede),
    ///     é aplicado backoff exponencial com jitter.
    ///
    /// Requisições com corpo baseado em Stream (uploads de arquivo) não são reenviadas
    /// automaticamente: o stream já estaria parcialmente consumido após a 1ª tentativa,
    /// então essas chamadas são executadas uma única vez, sem retry.
    /// </summary>
    public class AutodeskRetryHandler : DelegatingHandler
    {
        private readonly ILogger<AutodeskRetryHandler> _logger;
        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
        private readonly int _maxRetryAttempts;

        public AutodeskRetryHandler(ILogger<AutodeskRetryHandler> logger, IOptions<AutodeskRetryOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var settings = options?.Value ?? new AutodeskRetryOptions();
            _maxRetryAttempts = settings.MaxRetryAttempts;

            _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = settings.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(settings.BaseDelaySeconds),
                    MaxDelay = TimeSpan.FromSeconds(settings.MaxDelaySeconds),
                    ShouldHandle = ShouldRetryAsync,
                    DelayGenerator = GetRetryDelayAsync,
                    OnRetry = OnRetryAsync
                })
                .Build();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!IsSafeToRetry(request))
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            var template = await RequestSnapshot.CaptureAsync(request).ConfigureAwait(false);

            return await _pipeline.ExecuteAsync(async token =>
            {
                using var attemptRequest = template.ToRequestMessage();
                return await base.SendAsync(attemptRequest, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Só é seguro clonar e reenviar a requisição quando o corpo (se existir) já está
        /// totalmente em memória (StringContent, ByteArrayContent, FormUrlEncodedContent).
        /// StreamContent (upload de arquivos) fica de fora: o stream não pode ser "rebobinado"
        /// com segurança após uma tentativa falha.
        /// </summary>
        private static bool IsSafeToRetry(HttpRequestMessage request)
        {
            return request.Content is null || request.Content is ByteArrayContent;
        }

        private static ValueTask<bool> ShouldRetryAsync(RetryPredicateArguments<HttpResponseMessage> args)
        {
            if (args.Outcome.Exception is HttpRequestException)
            {
                return ValueTask.FromResult(true);
            }

            if (args.Outcome.Exception is TaskCanceledException && !args.Context.CancellationToken.IsCancellationRequested)
            {
                // Timeout interno do HttpClient (não um cancelamento explícito do chamador).
                return ValueTask.FromResult(true);
            }

            var response = args.Outcome.Result;
            if (response is null)
            {
                return ValueTask.FromResult(false);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return ValueTask.FromResult(true);
            }

            var statusCode = (int)response.StatusCode;
            var isTransientServerError = statusCode is 500 or 502 or 503 or 504;
            return ValueTask.FromResult(isTransientServerError);
        }

        private static ValueTask<TimeSpan?> GetRetryDelayAsync(RetryDelayGeneratorArguments<HttpResponseMessage> args)
        {
            var response = args.Outcome.Result;

            if (response is not null && response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is not null)
            {
                var retryAfter = response.Headers.RetryAfter;

                if (retryAfter.Delta.HasValue)
                {
                    return ValueTask.FromResult<TimeSpan?>(retryAfter.Delta.Value);
                }

                if (retryAfter.Date.HasValue)
                {
                    var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                    return ValueTask.FromResult<TimeSpan?>(delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
                }
            }

            // Sem "Retry-After": devolve null para a Polly aplicar o backoff exponencial padrão do pipeline.
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        private ValueTask OnRetryAsync(OnRetryArguments<HttpResponseMessage> args)
        {
            var response = args.Outcome.Result;
            var exception = args.Outcome.Exception;
            var requestUri = response?.RequestMessage?.RequestUri?.ToString() ?? "URI desconhecida";
            var attempt = args.AttemptNumber + 1;

            if (response is not null && response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "[APS 429 - Rate Limit] A Autodesk pausou o robô ao chamar '{RequestUri}'. Tentativa {Attempt}/{MaxAttempts}. Aguardando {DelaySeconds:0.0}s (respeitando o cabeçalho Retry-After) antes de tentar novamente.",
                    requestUri, attempt, _maxRetryAttempts, args.RetryDelay.TotalSeconds);
            }
            else if (response is not null)
            {
                _logger.LogWarning(
                    "[APS Retry] Resposta {StatusCode} ao chamar '{RequestUri}'. Tentativa {Attempt}/{MaxAttempts}. Nova tentativa em {DelaySeconds:0.0}s.",
                    (int)response.StatusCode, requestUri, attempt, _maxRetryAttempts, args.RetryDelay.TotalSeconds);
            }
            else
            {
                _logger.LogWarning(
                    exception,
                    "[APS Retry] Falha de rede ao chamar '{RequestUri}'. Tentativa {Attempt}/{MaxAttempts}. Nova tentativa em {DelaySeconds:0.0}s.",
                    requestUri, attempt, _maxRetryAttempts, args.RetryDelay.TotalSeconds);
            }

            // A resposta descartada não será devolvida ao chamador: liberamos os recursos aqui
            // para evitar vazamento de conexões/sockets num robô de longa duração.
            response?.Dispose();

            return default;
        }

        /// <summary>
        /// Captura um snapshot imutável da requisição original (método, URI, headers e corpo já
        /// materializado em bytes) para permitir a criação de uma nova <see cref="HttpRequestMessage"/>
        /// a cada tentativa — necessário porque uma instância de HttpRequestMessage não pode ser
        /// reenviada pelo HttpClient.
        /// </summary>
        private sealed class RequestSnapshot
        {
            private readonly HttpMethod _method;
            private readonly Uri? _requestUri;
            private readonly Version _version;
            private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers;
            private readonly byte[]? _contentBytes;
            private readonly List<KeyValuePair<string, IEnumerable<string>>>? _contentHeaders;

            private RequestSnapshot(
                HttpMethod method,
                Uri? requestUri,
                Version version,
                List<KeyValuePair<string, IEnumerable<string>>> headers,
                byte[]? contentBytes,
                List<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
            {
                _method = method;
                _requestUri = requestUri;
                _version = version;
                _headers = headers;
                _contentBytes = contentBytes;
                _contentHeaders = contentHeaders;
            }

            public static async Task<RequestSnapshot> CaptureAsync(HttpRequestMessage request)
            {
                byte[]? contentBytes = null;
                List<KeyValuePair<string, IEnumerable<string>>>? contentHeaders = null;

                if (request.Content is not null)
                {
                    contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    contentHeaders = request.Content.Headers
                        .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value))
                        .ToList();
                }

                var headers = request.Headers
                    .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value))
                    .ToList();

                return new RequestSnapshot(request.Method, request.RequestUri, request.Version, headers, contentBytes, contentHeaders);
            }

            public HttpRequestMessage ToRequestMessage()
            {
                var message = new HttpRequestMessage(_method, _requestUri)
                {
                    Version = _version
                };

                foreach (var header in _headers)
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                if (_contentBytes is not null)
                {
                    var content = new ByteArrayContent(_contentBytes);
                    if (_contentHeaders is not null)
                    {
                        foreach (var header in _contentHeaders)
                        {
                            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }
                    message.Content = content;
                }

                return message;
            }
        }
    }
}
