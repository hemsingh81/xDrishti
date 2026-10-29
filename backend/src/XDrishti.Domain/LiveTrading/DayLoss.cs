using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>The words of a loss measure (spec P11-04 criterion 10): <c>value</c> (realised plus every open lot marked), <c>realised_only</c> (an open lot has no usable price, so it is not in the figure) and <c>unavailable</c> (the day's fills could not be read).</summary>
public static class MeasureStates
{
    public const string Value = "value";
    public const string RealisedOnly = "realised_only";
    public const string Unavailable = "unavailable";
}

/// <summary>The measured result of one day, in rupees. Positive is a gain; the loss is the negated sum when negative.</summary>
/// <param name="State">value, realised_only or unavailable.</param>
/// <param name="Realised">Gain of lots opened and closed within the day (gross of charges); null when unavailable.</param>
/// <param name="Open">Gain of the lots still open at the end of the day marked at the last price; null when some lot has no price or when unavailable.</param>
/// <param name="CarriedQty">Shares sold or bought back today against a position opened before today: not measured here.</param>
/// <param name="OpenUnmarked">Open lots with no usable price.</param>
public sealed record DayLoss(string State, decimal? Realised, decimal? Open, long CarriedQty, int OpenUnmarked)
{
    public static DayLoss Unavailable { get; } = new(MeasureStates.Unavailable, null, null, 0, 0);

    /// <summary>The gain or loss counted, or null when unavailable: realised plus the marked open lots (realised only when a lot is unmarked).</summary>
    public decimal? Net => State == MeasureStates.Unavailable ? null : Realised + (Open ?? 0m);

    /// <summary>The loss as a non-negative amount (zero when the day is flat or up); null when unavailable.</summary>
    public decimal? Loss => Net is { } n ? Math.Max(0m, -n) : null;

    /// <summary>
    /// Pairs each fill of one day first in, first out per symbol, pure. A fill that closes a lot opened today realises the difference; a fill that closes more than today's lots is a carried position and is left out (counted, not measured: the history
    /// before today is not part of the day's loss); lots still open are marked with <paramref name="mark"/> (null = no usable price). Charges are not in the figure.
    /// </summary>
    public static DayLoss Measure(IReadOnlyList<LiveFill> fills, Func<string, decimal?> mark)
    {
        ArgumentNullException.ThrowIfNull(fills);
        ArgumentNullException.ThrowIfNull(mark);
        decimal realised = 0m, open = 0m;
        long carried = 0;
        var unmarked = 0;
        foreach (var group in fills.GroupBy(f => f.Symbol, StringComparer.Ordinal))
        {
            var lots = new List<(Side Side, long Qty, decimal Price)>();
            foreach (var fill in group.OrderBy(f => f.Date).ThenBy(f => f.Time ?? TimeOnly.MaxValue).ThenBy(f => f.Id))
            {
                var remaining = fill.Qty;
                while (remaining > 0 && lots.Count > 0 && lots[0].Side != fill.Side)
                {
                    var lot = lots[0];
                    var q = Math.Min(remaining, lot.Qty);
                    realised += (lot.Side == Side.Buy ? 1m : -1m) * (fill.Price - lot.Price) * q;
                    remaining -= q;
                    if (lot.Qty > q)
                    {
                        lots[0] = (lot.Side, lot.Qty - q, lot.Price);
                    }
                    else
                    {
                        lots.RemoveAt(0);
                    }
                }

                if (remaining <= 0)
                {
                    continue;
                }

                // A delivery sale with nothing bought today to close can only be the sale of a position carried in: not measured here. Every other remainder opens a lot (a buy, or an intraday short sale).
                if (fill.Side == Side.Sell && lots.Count == 0 && IsDelivery(fill.Product))
                {
                    carried += remaining;
                }
                else
                {
                    lots.Add((fill.Side, remaining, fill.Price));
                }
            }

            if (lots.Count > 0)
            {
                if (mark(group.Key) is { } price && price > 0)
                {
                    open += lots.Sum(l => (l.Side == Side.Buy ? 1m : -1m) * (price - l.Price) * l.Qty);
                }
                else
                {
                    unmarked += lots.Count;
                }
            }
        }

        return new(unmarked > 0 ? MeasureStates.RealisedOnly : MeasureStates.Value, realised, unmarked > 0 ? null : open, carried, unmarked);
    }

    /// <summary>The broker products that are held overnight (delivery); every other product is squared off in the session. Upper case.</summary>
    public static readonly IReadOnlyList<string> DeliveryProducts = ["CNC", "MTF", "MARGIN"];

    /// <summary>True for a delivery product (any case).</summary>
    public static bool IsDelivery(string product) => DeliveryProducts.Contains(product.ToUpperInvariant());
}
