namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>Reads the <c>1D</c> rows of <c>market.bars_agg</c> of the regime index and the volatility index (P3-01d).</summary>
public interface IContextBarSource
{
    /// <summary>Resolves each index by its symbols in order, then by display name (<c>Nifty 50</c>, <c>India VIX</c>), among the tracked, not retired instruments of type index with a Dhan id in IDX_I; reads their daily rows.</summary>
    ValueTask<ContextLegs> ReadAsync(IReadOnlyList<string> indexSymbols, IReadOnlyList<string> vixSymbols, CancellationToken cancellationToken);
}
