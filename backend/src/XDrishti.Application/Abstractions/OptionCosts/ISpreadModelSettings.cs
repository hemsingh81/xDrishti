using XDrishti.Domain.Options.Costs;

namespace XDrishti.Application.Abstractions.OptionCosts;

/// <summary>The settings <c>Options:Costs:*</c> and <c>Options:Liquidity:*</c> (P14-07). The assumed half-spread is the owner's and has no default.</summary>
public interface ISpreadModelSettings
{
    /// <summary>The usable chain sessions an underlying needs before a measured model is built.</summary>
    int MinUsableSessions { get; }

    /// <summary>The quotes a bucket needs before its own median is used.</summary>
    int MinQuotesPerBucket { get; }

    /// <summary>The owner's assumed half-spread in percent of the mid for the canonical symbol; null when he set none.</summary>
    decimal? AssumedHalfSpreadPct(string canonicalSymbol);

    LiquidityThresholds Liquidity { get; }
}
