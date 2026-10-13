using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Why a holding's trades cannot be applied (<c>quantity_mismatch</c>).</summary>
internal static class MismatchReasons
{
    /// <summary>The trades explain more shares than the broker holds (opening quantity below 0).</summary>
    public const string NegativeOpening = "negative_opening";

    /// <summary>A sale is larger than all the shares open at that point.</summary>
    public const string SellExceedsOpen = "sell_exceeds_open";

    /// <summary>A quantity is not a whole number after the splits and bonuses are applied.</summary>
    public const string FractionalAfterAdjustment = "fractional_after_adjustment";
}

/// <summary>Notes on how the cost of a rebuild was worked out (<c>costNote</c> of the rebuild block).</summary>
internal static class CostNotes
{
    /// <summary>The trades cost more than the broker's average cost allows, so the opening lot costs the average cost.</summary>
    public const string OpeningCostFallback = "opening_cost_fallback";
}

/// <summary>
/// The outcome of rebuilding one holding's lots from its trades: <see cref="Applicable"/> with the planned lots, or a mismatch with its reason. <see cref="Net"/> is bought minus sold (today's shares) and <see cref="OpeningQty"/> what the
/// broker holds beyond that; both are null when a quantity is not whole after the conversion. <see cref="RealisedPnl"/> is the sum over the closed pieces. <see cref="AdjustedFor"/> counts the corporate actions that changed a trade. <see cref="CostNote"/> is set when the opening lot's cost fell back to the average cost.
/// </summary>
internal sealed record Reconstruction(
    bool Applicable,
    string? Reason,
    long Qty,
    long? Net,
    long? OpeningQty,
    IReadOnlyList<LotPlan> Lots,
    decimal RealisedPnl,
    int AdjustedFor,
    int Buys,
    int Sells,
    string? CostNote);
