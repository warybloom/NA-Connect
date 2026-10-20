using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NA_Connect.LdpTipo2.Tests.TestHelpers
{
    /// <summary>
    /// ILogger de teste que só acumula as mensagens em memória — usado para provar que uma célula com
    /// valor não reconhecido gera warning controlado (nunca FormatException) sem precisar de um
    /// provedor de log real.
    /// </summary>
    internal sealed class CapturingLogger : ILogger
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
