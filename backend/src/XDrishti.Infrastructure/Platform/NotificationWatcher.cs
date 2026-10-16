using Npgsql;

namespace XDrishti.Infrastructure.Platform;

/// <summary>
/// <c>LISTEN</c> on one PostgreSQL channel (ADR 0024 §6) for the readers of the feed's signals: a connection of its own waits for the notification (no payload) and calls back. When the connection is lost, or cannot be opened because the database
/// is down or restarting, it is opened again by itself after a growing wait (<see cref="FirstRetry"/> doubling up to <see cref="LongestRetry"/>, back to the first after a connection that listened), and the callback is called once when listening resumes,
/// because a notification sent in between is lost and the receiver reads the database anyway. Only the fixed channel name is used here; nothing secret is logged, only the type of the failure.
/// </summary>
internal sealed class NotificationWatcher(NpgsqlDataSource dataSource, TimeProvider clock, string channel, Action<string, int> lost)
{
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan LongestRetry = TimeSpan.FromSeconds(30);

    private readonly string _listen = "LISTEN " + ValidChannel(channel);

    /// <summary>The wait before the next attempt after <paramref name="failures"/> attempts in a row that did not get as far as listening.</summary>
    public static TimeSpan Backoff(int failures) =>
        failures <= 0 ? FirstRetry : TimeSpan.FromTicks(Math.Min(FirstRetry.Ticks << Math.Min(failures, 10), LongestRetry.Ticks));

    public async Task WatchAsync(Action changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var failures = 0;
        var resumed = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var listened = false;
            try
            {
                await ListenAsync(changed, resumed, () => listened = true, cancellationToken);
            }
#pragma warning disable CA1031 // a lost listener is listened for again; the receivers' safety nets cover the gap
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return; // stopping: Npgsql reports the cancelled wait as its own exception too
            }
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures = listened ? 0 : failures + 1;
                lost(ex.GetType().Name, (int)Backoff(failures - 1).TotalSeconds);
            }

            resumed = true;
            try
            {
                await Task.Delay(Backoff(failures - 1), clock, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ListenAsync(Action changed, bool resumed, Action onListening, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        void OnNotification(object sender, NpgsqlNotificationEventArgs args) => changed();
        connection.Notification += OnNotification;
        try
        {
            await using (var listen = new NpgsqlCommand(_listen, connection))
            {
                await listen.ExecuteNonQueryAsync(cancellationToken);
            }

            onListening();
            if (resumed)
            {
                changed(); // what was sent while nobody listened is read now
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

    private static string ValidChannel(string name) =>
        name.Length > 0 && name.All(c => c is '_' or (>= 'a' and <= 'z')) ? name : throw new ArgumentException("A channel name is lower-case letters and underscores.", nameof(name));
}
