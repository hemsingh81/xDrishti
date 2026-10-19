using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// Puts the pack together (spec P3-09, V1 to V3): the random part of <see cref="VerificationSampler"/>, then the cover rule, then the spot checks. The cover rule goes through the categories in table order; a category that occurs and is not yet
/// covered by a trade of the pack gets the trade of it with the lowest <c>SeededRank(seed, "edge|code|key")</c>, which also covers every other category it belongs to. The rows are then put in the order of a seeded rank, so that the place on the
/// sheet does not say which trade was drawn at random and which was picked for its category. Pure; the same seed and records give the same plan.
/// </summary>
public static class PackPlanner
{
    /// <summary>The source of a trade drawn at random.</summary>
    public const string RandomSource = "random";

    /// <summary>The source of a trade picked by the cover rule.</summary>
    public const string EdgeSource = "edge";

    /// <summary>The source of a spot line, in <c>engine-values.csv</c>.</summary>
    public const string SpotSource = "spot";

    /// <summary>Plans the pack.</summary>
    /// <param name="runs">The runs, one per strategy, in the order given.</param>
    /// <param name="seed">The owner's seed.</param>
    /// <param name="count">The number of random trades.</param>
    public static PackPlan Plan(IReadOnlyList<SimulationRun> runs, string seed, int count)
    {
        ArgumentNullException.ThrowIfNull(runs);

        return Complete(runs, seed, VerificationSampler.Draw(runs, seed, count));
    }

    /// <summary>The cover rule and the spot checks over a given random part (the tests give one; <see cref="Plan"/> gives the sampler's).</summary>
    internal static PackPlan Complete(IReadOnlyList<SimulationRun> runs, string seed, IReadOnlyList<SampledTrade> random)
    {
        var classify = runs.Select(EdgeCategories.For).ToList();
        var occurs = EdgeCategories.Codes.ToDictionary(c => c, _ => false, StringComparer.Ordinal);
        var best = new Dictionary<string, (string Rank, SampledTrade Trade)>(StringComparer.Ordinal);
        for (var r = 0; r < runs.Count; r++)
        {
            foreach (var runTrade in runs[r].Trades)
            {
                var categories = classify[r](runTrade);
                if (categories.Count == 0)
                {
                    continue;
                }

                var key = runTrade.Trade.Key;
                foreach (var code in categories)
                {
                    occurs[code] = true;
                    var rank = SeededRank.Of(seed, "edge|" + code + "|" + key);
                    if (!best.TryGetValue(code, out var current) || string.CompareOrdinal(rank, current.Rank) < 0)
                    {
                        best[code] = (rank, new SampledTrade(r, runTrade));
                    }
                }
            }
        }

        var chosen = new List<(SampledTrade Trade, string Source)>(random.Select(t => (t, RandomSource)));
        var covered = new HashSet<string>(random.SelectMany(t => classify[t.RunIndex](t.Trade)), StringComparer.Ordinal);
        foreach (var code in EdgeCategories.Codes)
        {
            if (!occurs[code] || covered.Contains(code))
            {
                continue;
            }

            var pick = best[code].Trade;
            chosen.Add((pick, EdgeSource));
            covered.UnionWith(classify[pick.RunIndex](pick.Trade));
        }

        var ordered = chosen.OrderBy(c => SeededRank.Of(seed, "order|" + c.Trade.Trade.Trade.Key), StringComparer.Ordinal).ToList();
        var rows = new List<PackRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var (trade, source) = ordered[i];
            var instrument = runs[trade.RunIndex].Instruments.FirstOrDefault(x => x.InstrumentId == trade.Trade.Trade.InstrumentId);
            rows.Add(new PackRow(i + 1, trade.RunIndex, trade.Trade, source, classify[trade.RunIndex](trade.Trade), instrument?.TickSize));
        }

        return new PackPlan(rows, SpotChecks.Choose(runs, seed, rows.Count + 1), occurs);
    }
}
