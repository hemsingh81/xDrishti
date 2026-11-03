using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Domain.Options.Costs;

namespace XDrishti.Infrastructure.OptionCosts;

internal sealed class SpreadModelSettings(IOptions<OptionCostsOptions> costs, IOptions<OptionLiquidityOptions> liquidity) : ISpreadModelSettings
{
    private readonly Dictionary<string, decimal> _assumed = new(costs.Value.AssumedHalfSpreadPct, StringComparer.OrdinalIgnoreCase);

    public int MinUsableSessions => costs.Value.MinUsableSessions!.Value;

    public int MinQuotesPerBucket => costs.Value.MinQuotesPerBucket!.Value;

    public LiquidityThresholds Liquidity { get; } = new(liquidity.Value.MinOiNotionalCr, liquidity.Value.MinBarTurnoverLakh);

    /// <summary>The key is the part of the canonical symbol after the exchange prefix (<c>NSE:NIFTY</c> reads <c>NIFTY</c>).</summary>
    public decimal? AssumedHalfSpreadPct(string canonicalSymbol)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        var colon = canonicalSymbol.IndexOf(':', StringComparison.Ordinal);
        return _assumed.TryGetValue(colon >= 0 ? canonicalSymbol[(colon + 1)..] : canonicalSymbol, out var pct) ? pct : null;
    }
}
