namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The feed's side of <c>NOTIFY active_set_changed</c> (ADR 0024 §6): tells the feed at once that a holding or a price alert changed, so it recomputes its set without waiting for the one-minute recompute (which stays the safety
/// net: a lost listener loses nothing).
/// </summary>
public interface IActiveSetChanges
{
    /// <summary>Calls <paramref name="changed"/> whenever a notification arrives, until cancelled. Reconnects by itself after a failure. The callback must be quick and must not throw.</summary>
    Task WatchAsync(Action changed, CancellationToken cancellationToken);
}
