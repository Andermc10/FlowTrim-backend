using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FlowTrim.Tests.Api;

/// <summary>
/// Proveedor de logs en memoria para pruebas: guarda nivel, categoría, mensaje formateado, excepción y los
/// valores de los scopes activos (p. ej. CorrelationId) de cada línea.
/// </summary>
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    /// <summary>Texto completo capturado (mensajes, excepciones y scopes) para buscar fugas de datos.</summary>
    public string AllText() => string.Join('\n', Entries.Select(e =>
        $"{e.Category} {e.Message} {e.Exception} {string.Join(',', e.Scopes.Select(s => $"{s.Key}={s.Value}"))}"));

    private sealed class CaptureLogger(string category, LogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scopes = new Dictionary<string, object?>();
            owner._scopes.ForEachScope((scope, values) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        values[pair.Key] = pair.Value;
                    }
                }
            }, scopes);

            owner.Entries.Enqueue(new CapturedLog(logLevel, category, formatter(state, exception), exception, scopes));
        }
    }
}

public sealed record CapturedLog(
    LogLevel Level, string Category, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Scopes);
