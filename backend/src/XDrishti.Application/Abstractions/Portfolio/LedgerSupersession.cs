namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// Which imported charge entries Dhan's own ledger now covers (ADR 0029): a charge of the broker's ledger with the same day and amount (to the paisa) covers one imported line, so for each day and amount as many entries as there are broker charges are covered - the oldest ones - and no more.
/// Only the charges that count are those of <see cref="OpenCharges"/>: on a day the ledger has depository charges, only they; a day with charges of other kinds only (brokerage, GST, AMC, penalties) is compared with all of them, because the label of Dhan's depository charge is not verified.
/// Shared by the store, the sheet's plan and the test doubles so that all decide the same. Pure.
/// </summary>
public static class LedgerSupersession
{
    /// <summary>
    /// How many broker charges are there to match a line of the same day and amount (to the paisa): for each day, the charges that name a depository charge; if the day has none, all of the day's charges.
    /// </summary>
    public static Dictionary<(DateOnly Date, decimal Paid), int> OpenCharges(IEnumerable<LedgerChargeOnDay> brokerCharges)
    {
        ArgumentNullException.ThrowIfNull(brokerCharges);
        var open = new Dictionary<(DateOnly, decimal), int>();
        foreach (var day in brokerCharges.GroupBy(c => c.Date))
        {
            var pool = day.Any(c => c.Depository) ? day.Where(c => c.Depository) : day;
            foreach (var charge in pool)
            {
                var key = (charge.Date, Math.Round(charge.Paid, 2));
                open[key] = open.GetValueOrDefault(key) + 1;
            }
        }

        return open;
    }

    /// <summary>The ids of the <paramref name="imported"/> entries (id, day, rupees paid; oldest first) that the <paramref name="brokerCharges"/> cover.</summary>
    public static IReadOnlyList<Guid> Covered(IEnumerable<(Guid Id, DateOnly Date, decimal Paid)> imported, IEnumerable<LedgerChargeOnDay> brokerCharges)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(brokerCharges);
        var open = OpenCharges(brokerCharges);
        var covered = new List<Guid>();
        foreach (var (id, date, paid) in imported)
        {
            var key = (date, Math.Round(paid, 2));
            if (open.TryGetValue(key, out var left) && left > 0)
            {
                open[key] = left - 1;
                covered.Add(id);
            }
        }

        return covered;
    }
}
