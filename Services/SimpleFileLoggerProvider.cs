// Caminho: Services/SimpleFileLoggerProvider.cs
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services
{
    public class SimpleFileLoggerProvider : ILoggerProvider
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();
        private volatile bool _disposed = false;

        // 🎯 Só aceita logs de categorias que começam com um destes prefixos.
        // Ajuste aqui se tiver outros namespaces de serviço que quer ver no log.
        private static readonly string[] CategoriasPermitidas =
        {
            "APS.WebApp.Services",
            "NA_Connect"
        };

        // 🎯 Nível mínimo aceito. Information já corta Debug/Trace,
        // que normalmente são muito verbosos.
        private const LogLevel NivelMinimo = LogLevel.Information;

        public SimpleFileLoggerProvider(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(filePath, true, Encoding.UTF8) { AutoFlush = true };
        }

        public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, this);

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try { _writer.Dispose(); } catch { }
            }
        }

        private void WriteLine(string text)
        {
            lock (_lock)
            {
                if (_disposed) return;
                try { _writer.WriteLine(text); }
                catch (ObjectDisposedException) { }
            }
        }

        private class SimpleFileLogger : ILogger
        {
            private readonly string _category;
            private readonly string _categoriaCurta;
            private readonly bool _aceita;
            private readonly SimpleFileLoggerProvider _provider;

            public SimpleFileLogger(string category, SimpleFileLoggerProvider provider)
            {
                _category = category;
                _provider = provider;

                // Só aceita se a categoria começar com um dos prefixos permitidos
                _aceita = false;
                foreach (var prefixo in CategoriasPermitidas)
                {
                    if (category.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
                    {
                        _aceita = true;
                        break;
                    }
                }

                // Nome curto: pega só a última parte (ex: "GrdAutomationWorker" em vez do caminho completo)
                var partes = category.Split('.');
                _categoriaCurta = partes.Length > 0 ? partes[partes.Length - 1] : category;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => _aceita && logLevel >= NivelMinimo;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                if (formatter == null) return;

                string message = formatter(state, exception).Trim();
                message = Regex.Replace(message, @"\x1B\[[^@-~]*[@-~]", "");
                // colapsa quebras de linha extras que vêm de mensagens multi-linha
                message = Regex.Replace(message, @"\s*\r?\n\s*", " | ");

                string nivel = NivelParaTag(logLevel);
                string linha = $"[{DateTime.Now:HH:mm:ss}] {nivel}  {message}";

                if (exception != null)
                {
                    linha += Environment.NewLine + "    ↳ " + exception.GetType().Name + ": " + exception.Message;
                }

                _provider.WriteLine(linha);
            }

            private static string NivelParaTag(LogLevel level) => level switch
            {
                LogLevel.Information => "[INFO]  ",
                LogLevel.Warning => "[AVISO] ",
                LogLevel.Error => "[ERRO]  ",
                LogLevel.Critical => "[FALHA] ",
                _ => $"[{level}]"
            };
        }
    }
}