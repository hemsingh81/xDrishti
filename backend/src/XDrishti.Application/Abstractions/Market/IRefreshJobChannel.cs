namespace XDrishti.Application.Abstractions.Market;

/// <summary>Hands registered refreshes to the background worker of the API process.</summary>
public interface IRefreshJobChannel
{
    void Enqueue(RefreshJob job);

    IAsyncEnumerable<RefreshJob> ReadAllAsync(CancellationToken cancellationToken);
}
