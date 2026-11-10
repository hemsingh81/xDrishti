using XDrishti.Domain.Common;

namespace XDrishti.Domain.Baskets.Derived;

/// <summary>The one engine behind the fourteen baskets: named exclusions, the basket's formula and gate, ranking with ties by symbol, then buffer and dwell.</summary>
public sealed class DerivedEvaluator(DerivedDefinition definition) : IDerivedEvaluator
{
    public DerivedDefinition Definition => definition;

    public Result<DerivedResult> Evaluate(IReadOnlyList<InstrumentBars> universe, DateOnly asOf, DerivedParams parameters, DerivedResult? previous = null, DateTimeOffset? formationTime = null)
    {
        if (definition.NeedsIntraday && formationTime is null)
        {
            return Result.Failure<DerivedResult>(Error.Validation("derived.formation_time_required", $"Basket {definition.Id} ({definition.Name}) is formed intraday and needs a formation time."));
        }

        var ordered = universe.OrderBy(u => u.Symbol, StringComparer.Ordinal).ToList();
        if (definition.Form == SelectionForm.Clusters)
        {
            return Result.Success(Clusters(ordered, asOf, parameters, previous));
        }

        var context = new ScoreContext(parameters, asOf, formationTime);
        var excluded = new List<DerivedExclusion>();
        var candidates = new List<(string Symbol, Outcome Outcome)>();
        foreach (var instrument in ordered)
        {
            var reason = Eligibility(instrument, asOf, parameters, out var n);
            var outcome = reason is null ? DerivedScorers.Score(definition.Id, instrument, n, context) : new Outcome(reason, 0, false);
            if (outcome.Exclusion is null && !double.IsFinite(outcome.Score))
            {
                outcome = new Outcome(ExclusionReason.BadData, 0, false);
            }

            if (outcome.Exclusion is { } named)
            {
                excluded.Add(new(instrument.Symbol, named));
            }
            else
            {
                candidates.Add((instrument.Symbol, outcome));
            }
        }

        if (definition.Id == 10)
        {
            var allowed = candidates.OrderByDescending(c => c.Outcome.Secondary).ThenBy(c => c.Symbol, StringComparer.Ordinal).Take(parameters.Whole("Basket10.AdtvTop")).Select(c => c.Symbol).ToHashSet();
            candidates = [.. candidates.Select(c => (c.Symbol, c.Outcome with { Passes = allowed.Contains(c.Symbol) }))];
        }

        var ranked = candidates.Where(c => c.Outcome.Passes)
            .OrderBy(c => definition.Ascending ? c.Outcome.Score : -c.Outcome.Score).ThenBy(c => c.Symbol, StringComparer.Ordinal).Select(c => c.Symbol).ToList();
        var rank = ranked.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i + 1);
        var held = Held(previous);
        var k = parameters.Values.ContainsKey(definition.Key("TopK")) ? parameters.Whole(definition.Key("TopK"))
            : parameters.Values.ContainsKey(definition.Key("Cap")) ? parameters.Whole(definition.Key("Cap")) : Math.Max(ranked.Count, 1);
        var chosen = RankSelector.Select(ranked, k, parameters["BufferFactor"], held, definition.IsSlow ? parameters.Whole("MinDwellSessions") : 0);
        var members = chosen.Select(s => new DerivedMember(s, held.TryGetValue(s, out var h) ? h : 0)).ToList();
        var chosenSet = chosen.ToHashSet();
        return Result.Success(new DerivedResult(
            members,
            [.. candidates.Select(c => new DerivedScore(c.Symbol, c.Outcome.Score, rank.GetValueOrDefault(c.Symbol)))],
            excluded,
            [.. candidates.Where(c => !chosenSet.Contains(c.Symbol)).Select(c => c.Symbol)],
            [],
            null,
            parameters.Hash));
    }

    private static Dictionary<string, int> Held(DerivedResult? previous) =>
        previous?.Members.ToDictionary(m => m.Symbol, m => m.SessionsHeld + 1) ?? [];

    private string? Eligibility(InstrumentBars instrument, DateOnly asOf, DerivedParams p, out int n)
    {
        n = CountUpTo(instrument.Daily, asOf);
        if (instrument.CorporateActionUnresolved)
        {
            return ExclusionReason.CorporateActionUnresolved;
        }

        var need = Required(p);
        if (n < need)
        {
            return ExclusionReason.InsufficientHistory;
        }

        for (var k = n - need; k < n; k++)
        {
            var b = instrument.Daily[k];
            if (!double.IsFinite(b.Open) || !double.IsFinite(b.High) || !double.IsFinite(b.Low) || !double.IsFinite(b.Close) || !double.IsFinite(b.Volume) || b.Close <= 0)
            {
                return ExclusionReason.BadData;
            }
        }

        return definition.Id == 13 && !PassesFlags(instrument, asOf) ? ExclusionReason.NotInUniverseFilter : null;
    }

    /// <summary>The bars needed: the basket's MinBars (the 6-1 variant of basket 6 its own), never fewer than the formula reads.</summary>
    private int Required(DerivedParams p)
    {
        var min = definition.Id == 6 && p.Whole("Basket6.Variant") == 1 ? p.Whole("Basket6.MinBars6m") : p.Whole(definition.Key("MinBars"));
        var floor = definition.Id switch
        {
            1 => p.Whole("Basket1.AtrWindow") + 1,
            2 => p.Whole("Basket2.Window") + 1,
            3 => p.Whole("Basket3.Window"),
            4 => p.Whole("Basket4.AtrWindow") + 1,
            6 => p.Whole("Basket6.Variant") == 1 ? p.Whole("Basket6.ShortWindow") : p.Whole("Basket6.LongWindow"),
            7 => Math.Max(p.Whole("Basket7.ErWindow") + 1, p.Whole("Basket7.Lag") + 3),
            8 => Math.Max(p.Whole("Basket8.ErWindow") + 1, (2 * p.Whole("Basket8.AdxWindow")) + 1),
            9 => Math.Max(p.Whole("Basket9.Window"), Math.Max(p.Whole("Basket9.SmaWindow"), 20)),
            10 => Math.Max(p.Whole("Basket10.AdtvWindow"), 3),
            11 => p.Whole("Basket11.Window") + 1,
            13 => 20,
            _ => 2,
        };
        return Math.Max(min, floor);
    }

    private static bool PassesFlags(InstrumentBars instrument, DateOnly asOf)
    {
        DayFlag? flag = null;
        foreach (var f in instrument.Flags ?? [])
        {
            if (f.Date <= asOf && (flag is null || f.Date > flag.Value.Date))
            {
                flag = f;
            }
        }

        return flag is { AsmGsm: false, InBan: false } f2 && f2.Series == "EQ";
    }

    /// <summary>The number of bars dated at or before <paramref name="asOf"/> (daily arrays are sorted by date).</summary>
    internal static int CountUpTo(Bar[] bars, DateOnly asOf)
    {
        int lo = 0, hi = bars.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (bars[mid].Date <= asOf)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private DerivedResult Clusters(List<InstrumentBars> ordered, DateOnly asOf, DerivedParams p, DerivedResult? previous)
    {
        var window = p.Whole("Basket14.Window");
        var excluded = new List<DerivedExclusion>();
        var symbols = new List<string>();
        var industries = new List<string>();
        var residuals = new List<double[]>();
        foreach (var instrument in ordered)
        {
            var reason = Eligibility(instrument, asOf, p, out var n);
            double[]? residual = null;
            if (reason is null)
            {
                var (stock, market) = DerivedScorers.Aligned(instrument, n, asOf, window);
                if (stock.Length == window)
                {
                    residual = IndicatorKit.Winsorise([.. stock.Zip(market, (s, m) => s - m)], 0.01);
                    reason = residual.All(double.IsFinite) ? null : ExclusionReason.BadData;
                }
                else
                {
                    reason = ExclusionReason.InsufficientHistory;
                }
            }

            if (reason is not null)
            {
                excluded.Add(new(instrument.Symbol, reason));
            }
            else
            {
                symbols.Add(instrument.Symbol);
                industries.Add(instrument.Industry ?? "unclassified");
                residuals.Add(residual!);
            }
        }

        var m2 = symbols.Count;
        var labels = m2 == 0 ? [] : LabelClusters(residuals, Math.Min(p.Whole("Basket14.Clusters"), m2));
        var sizes = labels.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
        var names = labels.Select((l, i) => (l, ind: industries[i])).GroupBy(x => x.l)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.ind).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key);
        var held = Held(previous);
        var members = new List<DerivedMember>();
        var clusters = new List<ClusterAssignment>();
        var scores = new List<DerivedScore>();
        for (var i = 0; i < m2; i++)
        {
            scores.Add(new(symbols[i], labels[i], 0));
            if (sizes[labels[i]] < p.Whole("Basket14.MinSize"))
            {
                excluded.Add(new(symbols[i], ExclusionReason.SmallCluster));
                continue;
            }

            members.Add(new(symbols[i], held.GetValueOrDefault(symbols[i])));
            clusters.Add(new(symbols[i], labels[i], names[labels[i]]));
        }

        double? stability = null;
        if (previous is { Clusters.Count: > 0 })
        {
            var before = previous.Clusters.ToDictionary(c => c.Symbol, c => c.Cluster);
            var common = clusters.Where(c => before.ContainsKey(c.Symbol)).ToList();
            var ari = common.Count < 2 ? double.NaN : IndicatorKit.AdjustedRandIndex([.. common.Select(c => before[c.Symbol])], [.. common.Select(c => c.Cluster)]);
            stability = double.IsFinite(ari) ? ari : null;
        }

        return new DerivedResult(members, scores, excluded, [], clusters, stability, p.Hash);
    }

    private static int[] LabelClusters(List<double[]> residuals, int k)
    {
        var n = residuals.Count;
        var ranks = residuals.Select(r => IndicatorKit.Ranks(r)).ToList();
        var distance = new double[n * n];
        for (var a = 0; a < n; a++)
        {
            for (var b = a + 1; b < n; b++)
            {
                var rho = IndicatorKit.Pearson(ranks[a], ranks[b]);
                distance[(a * n) + b] = distance[(b * n) + a] = IndicatorKit.CorrelationDistance(double.IsFinite(rho) ? rho : 0);
            }
        }

        return IndicatorKit.AverageLinkage(distance, n, k);
    }
}
