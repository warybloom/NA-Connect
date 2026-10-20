using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.Logging
{
    /// <summary>
    /// Provider de logging que grava, em um único arquivo por execução em lote, tudo que passar
    /// pelo pipeline padrão do Microsoft.Extensions.Logging (ILogger.LogInformation/Warning/Error/
    /// Critical, com exceção completa quando houver), em qualquer classe/escopo de DI, sem exigir
    /// alterações nessas classes.
    ///
    /// Uso: registrado como singleton (Program.cs) e injetado onde o lote é orquestrado
    /// (GrdAutomationWorker). Uma execução é delimitada por BeginExecution(caminho)/DisposeAsync
    /// da sessão retornada — enquanto ativa, todo ILogger de qualquer serviço resolvido via DI
    /// (mesmo em escopos diferentes) grava nesse arquivo, porque todos compartilham o mesmo
    /// ILoggerFactory/provider.
    /// </summary>
    [ProviderAlias("File")]
    public sealed class ExecutionFileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly object _sessionLock = new();
        private ExecutionLogSession? _activeSession;
        private IExternalScopeProvider? _scopeProvider;

        /// <summary>
        /// Inicia a captura de uma nova execução em lote. Se já existir uma sessão ativa
        /// (não deveria, mas por segurança), ela é encerrada com flush antes de abrir a nova.
        /// </summary>
        public ExecutionLogSession BeginExecution(string filePath)
        {
            lock (_sessionLock)
            {
                _activeSession?.EndSynchronously();

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var session = new ExecutionLogSession(filePath, this);
                _activeSession = session;
                return session;
            }
        }

        internal void EnqueueLine(string line)
        {
            _activeSession?.Enqueue(line);
        }

        internal IExternalScopeProvider? ScopeProvider => _scopeProvider;

        internal void OnSessionEnded(ExecutionLogSession session)
        {
            lock (_sessionLock)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new ExecutionFileLogger(categoryName, this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
            // Rede de segurança: se a aplicação for encerrada com uma sessão ainda aberta
            // (ex.: shutdown abrupto do host), garante que o arquivo seja fechado com flush.
            _activeSession?.EndSynchronously();
        }
    }

    /// <summary>
    /// Representa o arquivo "Execucao_Completa_*.txt" de uma execução em lote específica.
    /// Todas as linhas (vindas de ILogger.* de qualquer serviço, ou de Console.WriteLine via o
    /// interceptor mantido em GrdAutomationWorker) passam por um Channel single-writer, escritas
    /// por uma única Task consumidora — thread-safe mesmo com múltiplos serviços/threads
    /// escrevendo simultaneamente, sem abrir/fechar o arquivo a cada linha.
    /// </summary>
    public sealed class ExecutionLogSession : IAsyncDisposable
    {
        private readonly ExecutionFileLoggerProvider _provider;
        private readonly Channel<string> _channel;
        private readonly Task _pumpTask;
        private readonly StreamWriter _writer;
        private int _ended;

        internal ExecutionLogSession(string filePath, ExecutionFileLoggerProvider provider)
        {
            _provider = provider;
            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            // UTF-8 sem BOM para preservar acentos/emojis sem sujar o início do arquivo.
            _writer = new StreamWriter(filePath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = false
            };

            _pumpTask = Task.Run(PumpAsync);
        }

        /// <summary>Enfileira uma linha já formatada (chamado pelo ExecutionFileLogger e pelo interceptor de console).</summary>
        public void Enqueue(string line)
        {
            if (Volatile.Read(ref _ended) != 0) return;
            _channel.Writer.TryWrite(line);
        }

        /// <summary>Usado pelo ConsoleOutputInterceptor para linhas cruas de Console.WriteLine.</summary>
        public void WriteRaw(string line) => Enqueue(line);

        private async Task PumpAsync()
        {
            try
            {
                await foreach (var line in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    try
                    {
                        await _writer.WriteLineAsync(line).ConfigureAwait(false);
                        await _writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Uma falha de escrita (disco cheio, permissão, etc.) nunca deve derrubar
                        // a execução do lote — a próxima linha simplesmente tenta de novo.
                    }
                }
            }
            catch
            {
                // Encerramento do canal (Complete) não deve propagar exceção.
            }
        }

        /// <summary>Finaliza a sessão: para de aceitar linhas novas, drena a fila e fecha o arquivo com flush.</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) return;

            _channel.Writer.TryComplete();
            await _pumpTask.ConfigureAwait(false);

            try
            {
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
                _provider.OnSessionEnded(this);
            }
        }

        /// <summary>Variante síncrona usada apenas como rede de segurança em Dispose() do provider.</summary>
        internal void EndSynchronously()
        {
            try
            {
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Melhor esforço: nunca lançar a partir de um caminho de limpeza defensivo.
            }
        }
    }

    internal sealed class ExecutionFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ExecutionFileLoggerProvider _provider;

        public ExecutionFileLogger(string categoryName, ExecutionFileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _provider.ScopeProvider?.Push(state) ?? NullScope.Instance;

        // A filtragem por nível já é resolvida pelo pipeline padrão (LoggerFilterOptions,
        // via appsettings "Logging:File:LogLevel"). Este provider não impõe um segundo filtro.
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception == null) return;

            var tags = CollectScopeTags();

            var line = new StringBuilder(256);
            line.Append('[').Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff")).Append("] ");
            line.Append('[').Append(logLevel).Append("] ");
            line.Append('[').Append(_categoryName).Append(']');

            if (!string.IsNullOrEmpty(tags.ExecutionId)) line.Append(" [ExecutionId:").Append(tags.ExecutionId).Append(']');
            if (!string.IsNullOrEmpty(tags.ProjectExecutionId)) line.Append(" [ProjectExecutionId:").Append(tags.ProjectExecutionId).Append(']');
            if (!string.IsNullOrEmpty(tags.Project)) line.Append(" [Project:").Append(tags.Project).Append(']');
            if (!string.IsNullOrEmpty(tags.StepNumber) || !string.IsNullOrEmpty(tags.StepName))
            {
                string step = !string.IsNullOrEmpty(tags.StepNumber) && !string.IsNullOrEmpty(tags.StepName)
                    ? $"{tags.StepNumber}-{tags.StepName}"
                    : (tags.StepNumber ?? tags.StepName);
                line.Append(" [Step:").Append(step).Append(']');
            }
            if (!string.IsNullOrEmpty(tags.OperationId)) line.Append(" [OperationId:").Append(tags.OperationId).Append(']');

            line.Append(' ').Append(message);

            if (exception != null)
            {
                line.Append(Environment.NewLine).Append(exception);
            }

            _provider.EnqueueLine(SecretMasker.Mask(line.ToString()));
        }

        private readonly struct ScopeTags
        {
            public string? ExecutionId { get; init; }
            public string? ProjectExecutionId { get; init; }
            public string? Project { get; init; }
            public string? StepNumber { get; init; }
            public string? StepName { get; init; }
            public string? OperationId { get; init; }
        }

        /// <summary>
        /// Coleta, nos escopos ativos (ILogger.BeginScope em GrdAutomationWorker e nas etapas), os
        /// identificadores estruturados de log (ver seção 6 da manutenção técnica). Nunca inventa um
        /// identificador ausente — cada bracket só aparece na linha quando a chave correspondente foi
        /// explicitamente colocada em escopo por quem chamou.
        /// </summary>
        private ScopeTags CollectScopeTags()
        {
            string? executionId = null, projectExecutionId = null, project = null, stepNumber = null, stepName = null, operationId = null;

            _provider.ScopeProvider?.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        string? valor = pair.Value?.ToString();
                        if (string.IsNullOrEmpty(valor)) continue;

                        switch (pair.Key)
                        {
                            case "ExecutionId": executionId ??= valor; break;
                            case "ProjectExecutionId": projectExecutionId ??= valor; break;
                            case "Project": project ??= valor; break;
                            case "StepNumber": stepNumber ??= valor; break;
                            case "StepName": stepName ??= valor; break;
                            case "OperationId": operationId ??= valor; break;
                        }
                    }
                }
            }, state: (object?)null);

            return new ScopeTags
            {
                ExecutionId = executionId,
                ProjectExecutionId = projectExecutionId,
                Project = project,
                StepNumber = stepNumber,
                StepName = stepName,
                OperationId = operationId
            };
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            private NullScope() { }
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Mascaramento defensivo de segredos antes de qualquer linha ser gravada no arquivo de
    /// execução (tokens, secrets, senhas, connection strings, cabeçalho Authorization).
    /// </summary>
    internal static class SecretMasker
    {
        private static readonly Regex[] Patterns =
        {
            new(@"(Bearer\s+)[A-Za-z0-9\-_\.~\+/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"((?:access|refresh)[_\s-]?token[""'\s:=]+)[^\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"(client[_\s-]?secret[""'\s:=]+)[^\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"(client[_\s-]?id[""'\s:=]+)[^\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"(password[""'\s:=]+)[^\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"(pwd[""'\s:=]+)[^\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"(Authorization\s*[:=]\s*)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"(Connection\s*String[""'\s:=]+)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        public static string Mask(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            foreach (var pattern in Patterns)
            {
                input = pattern.Replace(input, "$1***");
            }
            return input;
        }
    }
}
