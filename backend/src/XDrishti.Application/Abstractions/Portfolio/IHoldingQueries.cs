namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Read side of holdings (projected, not tracked).</summary>
public interface IHoldingQueries
{
    ValueTask<IReadOnlyList<HoldingData>> ListAsync(HoldingFilter filter, CancellationToken cancellationToken);

    /// <summary>One holding of an active account; null when it does not exist.</summary>
    ValueTask<HoldingData?> GetAsync(Guid holdingId, CancellationToken cancellationToken);
}
