using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MarketData.Tests.Fakes;

/// <summary>
/// An <see cref="ILogger{T}"/> that records the rendered message of every entry, so a test can assert
/// on what was logged (e.g. the stats line). Thread-safe: the reporter logs from its own loop task.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _messages = new();

    /// <summary>Rendered messages in the order they were logged.</summary>
    public IReadOnlyList<string> Messages => _messages.ToArray();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _messages.Enqueue(formatter(state, exception));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
