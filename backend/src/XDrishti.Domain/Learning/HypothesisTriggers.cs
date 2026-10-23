namespace XDrishti.Domain.Learning;

/// <summary>The trigger rules of doc 16 <c>hypothesis_loop.trigger</c> that can run on replayed data (spec P6-10, ADR 0076). The weekly schedule is the pipeline's (P7); these are the pure parts it will call. Pure.</summary>
public static class HypothesisTriggers
{
    /// <summary>Doc 16 <c>min_new_trades_per_cell</c>.</summary>
    public const int DefaultMinNewTrades = 30;

    /// <summary>Cells with at least <paramref name="minNewTrades"/> closed trades whose exit date is after <paramref name="since"/>, ordered by cell key.</summary>
    public static IReadOnlyList<HypothesisTrigger> NewTrades(IEnumerable<(string CellKey, DateOnly ExitDate)> trades, DateOnly since, int minNewTrades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return [.. trades.Where(t => t.ExitDate > since)
            .GroupBy(t => t.CellKey, StringComparer.Ordinal)
            .Where(g => g.Count() >= minNewTrades)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new HypothesisTrigger("new_trades", g.Key, $"{g.Count()} closed trades since {since:yyyy-MM-dd} (threshold {minNewTrades})"))];
    }

    /// <summary>One trigger per cell with a drift alarm that went on (a pause proposal of the drift replay), ordered by cell key.</summary>
    public static IReadOnlyList<HypothesisTrigger> Drift(IEnumerable<DriftProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        return [.. proposals.Where(p => p.Pause)
            .GroupBy(p => p.CellKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new HypothesisTrigger("drift", g.Key, g.First().Reason))];
    }
}
