using Microsoft.Extensions.Logging;

namespace UIEngine.Framework.Tests;

internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly object _Gate = new();
    private readonly List<RecordedLogEntry> _Entries = [];

    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get
        {
            lock (_Gate)
            {
                return _Entries.ToArray();
            }
        }
    }

    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
    }

    public ILogger CreateLogger(string categoryName) => new _RecordingLogger(
        categoryName,
        _Gate,
        _Entries);

    public void Dispose()
    {
    }

    private sealed class _RecordingLogger(
        string categoryName,
        object gate,
        List<RecordedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            lock (gate)
            {
                entries.Add(new RecordedLogEntry(
                    categoryName,
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    properties,
                    exception));
            }
        }
    }
}

internal sealed record RecordedLogEntry(
    string CategoryName,
    LogLevel LogLevel,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);
