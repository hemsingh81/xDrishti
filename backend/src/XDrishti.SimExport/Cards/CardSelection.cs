using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// Which trades of a run get a card (spec P3-04c, C1). Pure, on <c>run.Trades</c> only (the <c>EndOfData</c> trades are apart and get none). A stratum is an exit reason times a side, <b>derived from the enums</b> (every
/// <see cref="ExitReason"/> but <c>EndOfData</c> in declaration order, times <see cref="Side"/> in declaration order); a trade with a value outside that set goes into one last stratum and is never dropped. Inside a stratum the trades are
/// ranked by <see cref="SeededRank"/> with the run key as the seed. If every trade fits the cap, every trade gets a card; otherwise round r gives the r-th ranked trade of each stratum in the fixed order of the strata, skipping a stratum
/// with no r-th trade, until the cap is reached (the last round may be cut), so the cap is spread equally over the strata that exist. The result is in the run's trade order. The sizes of the strata are never printed.
/// </summary>
public static class CardSelection
{
    /// <summary>The name of the last stratum, for a trade whose exit reason or side is not in the known set.</summary>
    public const string OtherStratum = "other";

    /// <summary>The most cards a verb may ask for.</summary>
    public const int MaxCap = 500;

    /// <summary>The strata that exist today, in order: the exit reasons but <c>EndOfData</c> in declaration order, each with the sides in declaration order, then <see cref="OtherStratum"/>.</summary>
    public static IReadOnlyList<string> StratumNames { get; } = [.. Known().Select(s => Name(s.Exit, s.Side)), OtherStratum];

    /// <summary>Chooses at most <paramref name="cap"/> trades.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The cap is negative.</exception>
    public static IReadOnlyList<CardChoice> Choose(SimulationRun run, int cap)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentOutOfRangeException.ThrowIfNegative(cap);
        if (cap == 0 || run.Trades.Count == 0)
        {
            return [];
        }

        var numbered = new List<(int Number, RunTrade Trade, string Stratum)>(run.Trades.Count);
        for (var i = 0; i < run.Trades.Count; i++)
        {
            var trade = run.Trades[i];
            if (trade.Trade.ExitReason != ExitReason.EndOfData)
            {
                numbered.Add((i + 1, trade, StratumOf(trade.Trade)));
            }
        }

        if (numbered.Count <= cap)
        {
            return [.. numbered.Select(n => new CardChoice(n.Number, n.Trade, n.Stratum))];
        }

        var picked = new HashSet<string>(StringComparer.Ordinal);
        var ranked = new List<IReadOnlyList<string>>();
        foreach (var name in StratumNames)
        {
            ranked.Add(SeededRank.Order(run.RunKey, numbered.Where(n => n.Stratum == name).Select(n => n.Trade.Trade.Key)));
        }

        for (var round = 0; picked.Count < cap && ranked.Any(r => r.Count > round); round++)
        {
            foreach (var stratum in ranked)
            {
                if (picked.Count == cap)
                {
                    break;
                }

                if (stratum.Count > round)
                {
                    picked.Add(stratum[round]);
                }
            }
        }

        return [.. numbered.Where(n => picked.Contains(n.Trade.Trade.Key)).Select(n => new CardChoice(n.Number, n.Trade, n.Stratum))];
    }

    private static IEnumerable<(ExitReason Exit, Side Side)> Known() =>
        Enum.GetValues<ExitReason>().Where(e => e != ExitReason.EndOfData).SelectMany(e => Enum.GetValues<Side>().Select(s => (e, s)));

    private static string StratumOf(SimTrade trade) =>
        Enum.IsDefined(trade.ExitReason) && Enum.IsDefined(trade.Side) ? Name(trade.ExitReason, trade.Side) : OtherStratum;

    private static string Name(ExitReason exit, Side side) => ExportSpellings.Of(exit) + "-" + ExportSpellings.Of(side).ToLowerInvariant();
}
