using Microsoft.Extensions.Logging;

namespace XDrishti.Mcp.Tests.Support;

/// <summary>Collects every formatted log line (with its event id) so that a test can prove what was and was not logged.</summary>
internal sealed class CapturedLogger<T> : ILogger<T>
{
    public List<(int EventId, LogLevel Level, string Message)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
        {
            Lines.Add((eventId.Id, logLevel, formatter(state, exception)));
        }
    }

    public string All() => string.Join('\n', Lines.Select(l => l.Message));
}
