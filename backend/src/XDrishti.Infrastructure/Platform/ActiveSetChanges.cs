using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Infrastructure.Platform;

/// <summary>
/// <c>LISTEN active_set_changed</c> (ADR 0024 §6): the feed waits for the notification (no payload) and recomputes its set. The listening, the growing wait after a lost connection and the log of a loss are <see cref="NotificationWatcher"/>'s;
/// a notification that comes in meanwhile is lost, which costs at most the one-minute recompute that stays as the safety net.
/// </summary>
internal sealed class ActiveSetChanges(NpgsqlDataSource dataSource, TimeProvider clock, ILogger<ActiveSetChanges> logger) : IActiveSetChanges
{
    private readonly NotificationWatcher _watcher = new(dataSource, clock, "active_set_changed", (type, seconds) => ActiveSetChangesLog.Lost(logger, type, seconds));

    public Task WatchAsync(Action changed, CancellationToken cancellationToken) => _watcher.WatchAsync(changed, cancellationToken);
}

/// <summary>Log events of the feed's listener (event id 4142): the type of the failure only, never its text.</summary>
internal static partial class ActiveSetChangesLog
{
    [LoggerMessage(EventId = 4142, Level = LogLevel.Warning, Message = "feed.listener_lost type={Type}: listening again in {Seconds} s; the one-minute recompute covers the gap")]
    public static partial void Lost(ILogger logger, string type, int seconds);
}
