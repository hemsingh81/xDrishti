using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Infrastructure.Platform;

/// <summary>
/// <c>LISTEN live_prices</c> (ADR 0024 §6) in <c>xd-api</c>: the feed notifies after it wrote live prices (at most once a second, no payload) and the hub reads the prices. The listening, the reconnect with a growing wait
/// after a database restart and the one call when listening resumes are <see cref="NotificationWatcher"/>'s. With no listener, or no feed, nothing is harmed: the hub's own sweep reads the prices every 30 seconds.
/// </summary>
internal sealed class LivePriceChanges(NpgsqlDataSource dataSource, TimeProvider clock, ILogger<LivePriceChanges> logger) : ILivePriceChanges
{
    private readonly NotificationWatcher _watcher = new(dataSource, clock, "live_prices", (type, seconds) => LivePriceChangesLog.Lost(logger, type, seconds));

    public Task WatchAsync(Action changed, CancellationToken cancellationToken) => _watcher.WatchAsync(changed, cancellationToken);
}

/// <summary>Log events of the API's listener (event id 4143): the type of the failure only, never its text.</summary>
internal static partial class LivePriceChangesLog
{
    [LoggerMessage(EventId = 4143, Level = LogLevel.Warning, Message = "hub.listener_lost type={Type}: listening again in {Seconds} s; prices are read every 30 s meanwhile")]
    public static partial void Lost(ILogger logger, string type, int seconds);
}
