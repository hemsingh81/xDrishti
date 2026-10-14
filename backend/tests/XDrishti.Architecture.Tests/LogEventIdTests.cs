using System.Reflection;
using Microsoft.Extensions.Logging;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Log events are found by their id in the log store, so one id must mean one message in the whole solution: an id reused in another module (two modules once
/// both numbered 3101) makes the events indistinguishable. Every <c>[LoggerMessage]</c> that sets an <c>EventId</c> is checked across all the shipped assemblies.
/// </summary>
public sealed class LogEventIdTests
{
    private static readonly string[] Shipped = ["XDrishti.Domain", "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed"];

    private static List<(int Id, string Method)> EventIds() =>
        [.. Shipped
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<LoggerMessageAttribute>().Where(a => a.EventId >= 0).Select(a => (a.EventId, $"{method.DeclaringType!.FullName}.{method.Name}")))];

    [Fact]
    public void Logger_messages_with_an_event_id_are_found_in_the_shipped_assemblies()
    {
        EventIds().Count.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void An_event_id_is_used_by_one_logger_message_in_the_whole_solution()
    {
        var reused = EventIds().GroupBy(e => e.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Method))}").ToList();

        reused.ShouldBeEmpty();
    }
}
