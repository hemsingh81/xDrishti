using XDrishti.Domain.Options.Costs;

namespace XDrishti.Application.Abstractions.OptionCosts;

/// <summary>
/// The bounded read of the half-spread model (P14-07, ADR 0194): the quotes of one underlying on one India day, as the bid, the ask and the bucket keys only (instant, expiry, strike, the underlying's price). It reads one <c>(underlying_id, ts)</c> range
/// per call and the caller asks only for sessions the quality checks call usable (ADR 0178); it returns no IV, no Greek, no open interest and no volume, and writes nothing. The second reader of <c>options.chain_snapshots</c> after the quality reader.
/// </summary>
public interface IChainSpreadReader
{
    /// <summary>The snapshot rows of the underlying on the India day, ordered by instant, expiry and strike.</summary>
    ValueTask<IReadOnlyList<SpreadQuote>> QuotesAsync(Guid underlyingId, DateOnly session, CancellationToken cancellationToken);
}
