namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>One run of the derived baskets at a time across processes (the scheduled run, refresh-now and a backfill): a database advisory lock held until the returned handle is disposed.</summary>
public interface IDerivedRunLock
{
    /// <summary>The held lock, or null when another run holds it (nothing waits).</summary>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}
