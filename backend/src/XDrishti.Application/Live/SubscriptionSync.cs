using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Live;

/// <summary>Splits subscriptions into the messages Dhan accepts: one mode per message, at most <see cref="ILiveFeedConnection.MaxBatch"/> securities each (P2-10 criterion 4).</summary>
internal static class SubscriptionBatcher
{
    public static IReadOnlyList<(FeedMode Mode, IReadOnlyList<FeedSecurity> Securities)> Split(IReadOnlyList<ActiveSetMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var batches = new List<(FeedMode, IReadOnlyList<FeedSecurity>)>();
        foreach (var group in members.GroupBy(m => m.Mode).OrderBy(g => g.Key))
        {
            batches.AddRange(group.Select(m => m.Key).Chunk(ILiveFeedConnection.MaxBatch).Select(chunk => (group.Key, (IReadOnlyList<FeedSecurity>)chunk)));
        }

        return batches;
    }
}

/// <summary>
/// Keeps one connection's subscriptions equal to the active set: the first call subscribes everything, later calls send only the difference (unsubscribe first, then subscribe), in messages of at most 100 with a pause of
/// <see cref="LiveFeedSettings.SubscribeSpacing"/> between two messages so that Dhan is not flooded (P2-10 criteria 4 and 8). A new connection starts with a new instance: after a reconnect the whole set is subscribed again.
/// </summary>
internal sealed class SubscriptionSync(ILiveFeedConnection connection, LiveFeedSettings settings, TimeProvider clock)
{
    private long _lastSent = -1;

    /// <summary>The set the connection is subscribed to now; null before the first <see cref="ApplyAsync"/>.</summary>
    public ActiveSet? Current { get; private set; }

    /// <summary>Messages sent so far (subscribe and unsubscribe).</summary>
    public int Messages { get; private set; }

    /// <summary>Applies the difference between the current set and <paramref name="next"/> and returns it (empty when nothing changed).</summary>
    public async ValueTask<ActiveSetDiff> ApplyAsync(ActiveSet next, CancellationToken cancellationToken)
    {
        var diff = ActiveSetBuilder.Diff(Current, next);
        foreach (var (mode, securities) in SubscriptionBatcher.Split(diff.Unsubscribe))
        {
            await PaceAsync(cancellationToken);
            await connection.UnsubscribeAsync(mode, securities, cancellationToken);
            Sent();
        }

        foreach (var (mode, securities) in SubscriptionBatcher.Split(diff.Subscribe))
        {
            await PaceAsync(cancellationToken);
            await connection.SubscribeAsync(mode, securities, cancellationToken);
            Sent();
        }

        Current = next;
        return diff;
    }

    private void Sent()
    {
        Messages++;
        _lastSent = clock.GetTimestamp();
    }

    private async ValueTask PaceAsync(CancellationToken cancellationToken)
    {
        if (_lastSent < 0)
        {
            return;
        }

        var wait = settings.SubscribeSpacing - clock.GetElapsedTime(_lastSent);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, clock, cancellationToken);
        }
    }
}
