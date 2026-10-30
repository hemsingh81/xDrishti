namespace XDrishti.Execution.Domain.Recon;

/// <summary>A fill of the day and a trade the portfolio stored, as the cross-check sees them (account, instrument, side, quantity, price at four decimals; the day is the session).</summary>
public sealed record GapFill(string ExchangeTradeId, string SecurityId, ExecSide Side, int Qty, decimal Price);

public sealed record StoredTrade(string SecurityId, ExecSide Side, int Qty, decimal Price);

/// <summary>After the 16:30 run (spec P12-06 11a): a fill with no stored trade of the same instrument, side, quantity and price is a journal gap. A warning, not a break; one stored trade explains one fill.</summary>
public static class JournalGap
{
    public static IReadOnlyList<GapFill> Find(IReadOnlyList<GapFill> fills, IReadOnlyList<StoredTrade> stored)
    {
        ArgumentNullException.ThrowIfNull(fills);
        ArgumentNullException.ThrowIfNull(stored);
        var pool = stored.Select(s => (s.SecurityId, s.Side, s.Qty, Price: Math.Round(s.Price, 4))).ToList();
        List<GapFill> gaps = [];
        foreach (var fill in fills.OrderBy(f => f.ExchangeTradeId, StringComparer.Ordinal))
        {
            var key = (fill.SecurityId, fill.Side, fill.Qty, Price: Math.Round(fill.Price, 4));
            var at = pool.IndexOf(key);
            if (at < 0)
            {
                gaps.Add(fill);
            }
            else
            {
                pool.RemoveAt(at);
            }
        }

        return gaps;
    }
}
