using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Infrastructure.Platform;

/// <summary>
/// <c>LISTEN active_set_changed</c> (ADR 0024 §6): one connection of its own waits for the notification (no payload) and calls back. When the connection is lost it waits a few seconds and listens again; a notification that comes in
/// the meantime is lost, which costs at most the one-minute recompute that stays as the safety net. Only the channel name is fixed here; nothing secret is logged.
/// </summary>
internal sealed class ActiveSetChanges(NpgsqlDataSource dataSource, TimeProvider clock, ILogger<ActiveSetChanges> logger) : IActiveSetChanges
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    public async Task WatchAsync(Action changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(changed, cancellationToken);
            }
#pragma warning disable CA1031 // a lost listener is listened for again; the one-minute recompute covers the gap
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return; // stopping: Npgsql reports the cancelled wait as its own exception too
            }
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ActiveSetChangesLog.Lost(logger, ex.GetType().Name);
            }

            try
            {
                await Task.Delay(RetryAfter, clock, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ListenAsync(Action changed, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        void OnNotification(object sender, NpgsqlNotificationEventArgs args) => changed();
        connection.Notification += OnNotification;
        try
        {
            await using (var listen = new NpgsqlCommand("LISTEN active_set_changed", connection))
            {
                await listen.ExecuteNonQueryAsync(cancellationToken);
            }

            while (true)
            {
                await connection.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            connection.Notification -= OnNotification;
        }
    }
}

/// <summary>Log events of the listener (event id 4142): the type of the failure only, never its text.</summary>
internal static partial class ActiveSetChangesLog
{
    [LoggerMessage(EventId = 4142, Level = LogLevel.Warning, Message = "feed.listener_lost type={Type}: listening again shortly; the one-minute recompute covers the gap")]
    public static partial void Lost(ILogger logger, string type);
}
