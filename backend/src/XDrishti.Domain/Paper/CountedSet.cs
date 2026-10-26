namespace XDrishti.Domain.Paper;

/// <summary>The counted trades in exit order and the counts of everything set apart (spec P10-04, criterion 1). Pure.</summary>
/// <param name="Trades">The counted trades: exit instant, then entry instant, then ticket id, then entry id.</param>
/// <param name="Counts">The counts.</param>
public sealed record CountedSet(IReadOnlyList<CountedTrade> Trades, CompareCounts Counts)
{
    /// <summary>Selects the counted set from the entries of one epoch: one ticket counts once (by trade key), reserve, straddle and open tickets are counted apart.</summary>
    public static CountedSet Select(IReadOnlyList<PaperCompareRow> rows, int otherEpochs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var counted = new List<(PaperCompareRow Row, long Ticket)>();
        int reserve = 0, straddle = 0, open = 0;
        foreach (var group in rows.GroupBy(r => r.TradeKey, StringComparer.Ordinal))
        {
            var rep = group.OrderBy(r => r.ExitAt is null).ThenBy(r => r.EntryId).First();
            if (!string.Equals(rep.TicketGroup, "primary", StringComparison.Ordinal))
            {
                reserve++;
            }
            else if (string.Equals(rep.MeasureStatus, "straddle", StringComparison.Ordinal))
            {
                straddle++;
            }
            else if (!string.Equals(rep.MeasureStatus, "measured", StringComparison.Ordinal))
            {
                continue;
            }
            else if (rep.ExitAt is null || rep.RSim is null || rep.RBasis is null)
            {
                open++;
            }
            else
            {
                counted.Add((rep, rep.PlanItemId));
            }
        }

        var trades = counted
            .OrderBy(c => c.Row.ExitAt).ThenBy(c => c.Row.EntryAt).ThenBy(c => c.Ticket).ThenBy(c => c.Row.EntryId)
            .Select(c => new CountedTrade(c.Row.CellKey, c.Row.RSim!.Value, c.Row.RBasis!, c.Row.ChargeSheetId, c.Row.EntryAt, c.Row.ExitAt!.Value, c.Ticket, c.Row.ApprovalKind))
            .ToList();
        return new CountedSet(trades, new CompareCounts(trades.Count, reserve, straddle, open, otherEpochs));
    }
}
