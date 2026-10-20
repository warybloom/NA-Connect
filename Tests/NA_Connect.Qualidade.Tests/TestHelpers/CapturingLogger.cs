using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NA_Connect.Qualidade.Tests.TestHelpers
{
    /// <summary>
    /// ILogger de teste que só acumula as mensagens em memória, para verificar nível (Warning x Error)
    /// e conteúdo sem precisar de um provedor de log real.
    /// </summary>
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entradas { get; } = new List<(LogLevel, string)>();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Entradas.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }
}
