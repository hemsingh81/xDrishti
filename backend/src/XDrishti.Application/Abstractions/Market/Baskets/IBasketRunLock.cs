namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>One fetch run at a time across processes (fetch-now and the scheduled run): a database advisory lock held until the returned handle is disposed.</summary>
public interface IBasketRunLock
{
    /// <summary>The held lock, or null when another run holds it (nothing waits).</summary>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}
