namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The API's side of <c>NOTIFY live_prices</c> (ADR 0024 §6): tells it that live prices were written. The notification has no payload; the receiver reads the prices.
/// </summary>
public interface ILivePriceChanges
{
    /// <summary>
    /// Calls <paramref name="changed"/> whenever a notification arrives, until cancelled. When the database connection is lost it is opened again by itself with a growing wait (1 s doubling to 30 s), and <paramref name="changed"/> is called once
    /// when listening resumes, because notifications sent in between are lost. The callback must be quick and must not throw.
    /// </summary>
    Task WatchAsync(Action changed, CancellationToken cancellationToken);
}
