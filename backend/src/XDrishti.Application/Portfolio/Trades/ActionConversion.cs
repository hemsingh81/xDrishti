using System.Numerics;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Brings a trade to today's share basis with the instrument's <b>applied</b> corporate actions (ADR 0017 §5, ADR 0023), in the direction of <see cref="AdjustmentFactors"/>: a bar dated before an action's ex-date is multiplied by the action's
/// <c>Factor</c>, so a trade dated before the ex-date has its <b>price multiplied by the factor</b> and its <b>quantity divided by the factor</b> (a 1:5 split has factor 1/5, so 10 shares at 1,000 become 50 shares at 200; a 1:1 bonus has
/// factor 1/2: 10 shares become 20). A trade on the ex-date or later is already on the new basis. Several actions multiply, in ex-date order. The quantity is computed exactly (as a fraction of the action's ratio, not through the rounded 12-decimal
/// factor) and must come out whole; the price is multiplied by the stored factors. Pure.
/// </summary>
internal static class ActionConversion
{
    /// <summary>The trade's quantity and price on today's basis; <c>Qty</c> is null when the converted quantity is not a whole number.</summary>
    public static (long? Qty, decimal Price) Convert(long qty, decimal price, IReadOnlyList<AppliedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
        {
            return (qty, price);
        }

        var numerator = BigInteger.One;
        var denominator = BigInteger.One;
        var factor = 1m;
        foreach (var action in actions)
        {
            // Quantity moves opposite to the price factor: a split old:new turns one share into new/old; a bonus a:b into (a+b)/b.
            var (up, down) = action.Kind == CorporateActionKind.Split
                ? (action.Denominator, action.Numerator)
                : (action.Numerator + action.Denominator, action.Denominator);
            numerator *= up;
            denominator *= down;
            factor *= action.Factor;
        }

        var scaled = qty * numerator;
        var whole = BigInteger.DivRem(scaled, denominator, out var remainder);
        var converted = remainder.IsZero && whole <= long.MaxValue ? (long?)(long)whole : null;
        return (converted, price * factor);
    }
}
