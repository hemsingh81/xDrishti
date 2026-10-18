using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Tests.Research;

/// <summary>Records every log event with its id, its template and its named arguments with their types, so that a test can assert what kinds of value reach the log and not only the rendered text.</summary>
internal sealed class StructuredLogger<T> : ILogger<T>
{
    public List<LoggedEvent> Events { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var pairs = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        var template = pairs.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value as string ?? string.Empty;
        Events.Add(new LoggedEvent(eventId.Id, logLevel, template, formatter(state, exception), [.. pairs.Where(p => p.Key != "{OriginalFormat}")]));
    }
}

internal sealed record LoggedEvent(int Id, LogLevel Level, string Template, string Message, IReadOnlyList<KeyValuePair<string, object?>> Arguments)
{
    public object? Argument(string name) => Arguments.Single(a => a.Key == name).Value;
}
