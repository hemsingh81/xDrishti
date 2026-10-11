using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>A Serilog sink (picked up through the host's <c>ReadFrom.Services</c>) that keeps every event as text: message, properties and exception.</summary>
public sealed class CapturedLogs : ILogEventSink
{
    private readonly ConcurrentQueue<string> _events = new();

    public IReadOnlyCollection<string> All => _events;

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var properties = string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
        _events.Enqueue(string.Create(CultureInfo.InvariantCulture, $"{logEvent.Level} {logEvent.RenderMessage(CultureInfo.InvariantCulture)} {properties} {logEvent.Exception}"));
    }
}
