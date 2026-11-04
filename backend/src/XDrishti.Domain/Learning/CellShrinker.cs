using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>One cell window to shrink: who it is, its trades in the window and CF-01's effective n (null when unavailable).</summary>
internal sealed record ShrinkSubject(CellId Cell, IReadOnlyList<CellTrade> Trades, double? IndependentN);

/// <summary>
/// The second pass of the cell statistics (CF-02, ADR 0204): for one window, the shrunk mean R, its lower bound and the estimated-strength win rate of every cell. Pure; the result of a cell depends only on the set of cells, never on their order.
/// <para>
/// The parent chain: a regime cell is shrunk toward its own <c>*</c> cell without it (<c>star</c>), then toward the other <c>*</c> cells of its strategy (<c>strategy</c>), then toward every other <c>*</c> cell (<c>system</c>); a <c>*</c> cell starts at the strategy.
/// The first parent with at least <see cref="ShrinkageOptions.MinParentTrades"/> trades, the cell excluded, is used. The tau squared and the Beta strength are estimated once per parent group over all its siblings (a stated, small inconsistency with the
/// leave-one-out parent mean); a sibling is a cell with at least <see cref="ShrinkageOptions.MinTradesForSibling"/> trades.
/// </para>
/// </summary>
internal static class CellShrinker
{
    private const string Star = "*";

    // from this many trades a cell's own variance of R is used (floored, and blended with the group's up to SigmaPoolStrength trades); below it the group's alone
    private const int OwnVarianceTrades = 5;

    // shortcut: O(cells^2) per window, group estimates recomputed per cell; cache per parent group if cells exceed a few thousand
    public static IReadOnlyList<ShrinkageAssessment> Assess(IReadOnlyList<ShrinkSubject> subjects, DateOnly asOf, CellStatsOptions cell, ShrinkageOptions options, double sigmaFloorR)
    {
        var units = subjects.Select(s => Unit.Make(s.Cell, s.Trades, asOf, cell, s.IndependentN)).ToList();
        var stars = units.Where(u => u.Cell.Regime == Star).OrderBy(u => u.Cell.Key, StringComparer.Ordinal).ToList();
        var starByKey = stars.GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.First());
        var result = new ShrinkageAssessment[units.Count];
        for (var i = 0; i < units.Count; i++)
        {
            result[i] = One(units[i], units, stars, starByKey, asOf, cell, options, sigmaFloorR);
        }

        return result;
    }

    private static ShrinkageAssessment One(Unit u, List<Unit> all, List<Unit> stars, Dictionary<CellId, Unit> starByKey, DateOnly asOf, CellStatsOptions cell, ShrinkageOptions options, double sigmaFloorR)
    {
        var mode = options.Mode;
        if (u.Trades > 0 && !double.IsFinite(u.Mean))
        {
            return ShrinkageAssessment.Unavailable(mode, "a result is not a finite number");
        }

        Unit? ownStar = null;
        var levels = new List<(string Name, List<Unit> Tau, List<Unit> Parent)>();
        if (u.Cell.Regime != Star && starByKey.TryGetValue(u.Cell with { Regime = Star }, out ownStar))
        {
            var siblings = all.Where(x => x.Cell.Regime != Star && x.Cell with { Regime = Star } == ownStar.Cell).OrderBy(x => x.Cell.Key, StringComparer.Ordinal).ToList();
            var parent = siblings.Where(x => !ReferenceEquals(x, u)).ToList();
            var left = Remainder(ownStar, siblings);
            if (left.Count > 0)
            {
                parent.Add(Unit.Make(ownStar.Cell, left, asOf, cell, null));
            }

            levels.Add(("star", siblings, parent));
        }

        var (strategyTau, strategyParent) = Group(u, ownStar, stars.Where(x => x.Cell.Strategy == u.Cell.Strategy));
        levels.Add(("strategy", strategyTau, strategyParent));
        var (systemTau, systemParent) = Group(u, ownStar, stars);
        levels.Add(("system", systemTau, systemParent));

        foreach (var (name, tau, parent) in levels)
        {
            if (parent.Sum(x => x.Trades) >= options.MinParentTrades)
            {
                return Shrink(u, name, tau, parent, cell, options, sigmaFloorR);
            }
        }

        return ShrinkageAssessment.Unavailable(mode, $"no parent with {options.MinParentTrades.ToString(CultureInfo.InvariantCulture)} trades", u.N);
    }

    // A star cell is a sibling of its group and left out of its own parent; a regime cell is shrunk toward the other star cells, so its own star cell (which holds its trades) is in neither list.
    private static (List<Unit> Tau, List<Unit> Parent) Group(Unit u, Unit? ownStar, IEnumerable<Unit> members)
    {
        var all = members.ToList();
        return u.Cell.Regime == Star
            ? (all, [.. all.Where(x => !ReferenceEquals(x, u))])
            : ([.. all.Where(x => !ReferenceEquals(x, ownStar))], [.. all.Where(x => !ReferenceEquals(x, ownStar))]);
    }

    private static ShrinkageAssessment Shrink(Unit u, string level, List<Unit> tauGroup, List<Unit> parent, CellStatsOptions cell, ShrinkageOptions options, double sigmaFloorR)
    {
        var notes = new List<string>();
        var mode = options.Mode;
        var pooled = PooledVariance(tauGroup.Concat(parent).DistinctBy(x => x.Cell.Key, StringComparer.Ordinal), sigmaFloorR);
        var siblings = tauGroup.Where(x => x.Trades >= options.MinTradesForSibling && double.IsFinite(x.Mean) && x.N > 0).ToList();
        var tauInput = new List<(double Mean, double N, double Sigma2)>();
        foreach (var s in siblings)
        {
            if (Sigma2(s, pooled, options, sigmaFloorR) is { } variance)
            {
                tauInput.Add((s.Mean, s.N, variance));
            }
        }

        var tau = ShrinkageEstimators.TauSquared(tauInput, options.TauSquaredFloor);
        var strength = ShrinkageEstimators.BetaStrength([.. siblings.Select(s => ((double)s.Wins, (double)s.Trades))], options.StrengthMin, options.StrengthMax, cell.PriorStrength);
        var parentMean = ShrinkageEstimators.LeaveOneOutParent([.. parent.Select(x => (x.Mean, x.N))], -1);
        if (parentMean is null)
        {
            return ShrinkageAssessment.Unavailable(mode, "the parent mean is not a finite number", u.N);
        }

        var sigma2 = Sigma2(u, pooled, options, sigmaFloorR);
        if (sigma2 is null)
        {
            return ShrinkageAssessment.Unavailable(mode, "no variance of R (fewer than 2 trades in the cell and its group)", u.N);
        }

        if (NormalShrinkage.Posterior(u.Mean, sigma2.Value, u.N, parentMean.Value, tau.Value, options.QuantileLevel) is not { } posterior)
        {
            return ShrinkageAssessment.Unavailable(mode, "the posterior could not be computed from finite inputs", u.N);
        }

        var weight = parent.Sum(x => x.WeightTotal);
        double? shrunkRate = null;
        if (weight > 0 && double.IsFinite(weight))
        {
            var parentRate = parent.Sum(x => x.WeightedWins) / weight;
            shrunkRate = BetaShrinkage.Posterior(u.WeightedWins, u.WeightTotal, parentRate, strength.Strength, cell.CredibleLevel).Posterior;
        }

        if (u.NNote.Length > 0)
        {
            notes.Add(u.NNote);
        }

        if (u.Trades < OwnVarianceTrades)
        {
            notes.Add($"fewer than {OwnVarianceTrades.ToString(CultureInfo.InvariantCulture)} trades: variance of R taken from the group");
        }
        else if (u.Trades <= options.SigmaPoolStrength)
        {
            notes.Add("variance of R blended with the group");
        }

        if (tau.Note.Length > 0)
        {
            notes.Add(tau.Note);
        }

        if (strength.Note.Length > 0)
        {
            notes.Add(strength.Note);
        }

        return new ShrinkageAssessment(mode, level, parentMean, tau.Value, tau.Source, posterior.Blend, posterior.Mean, posterior.Lower, strength.Strength, strength.Source, shrunkRate, u.N, string.Join("; ", notes));
    }

    // The cell's variance of R: its own above SigmaPoolStrength trades, its own pulled toward the group's with weight n / (n + strength) from 5 trades on, the group's alone below 5. A cell's own sd is floored (CF-01 SigmaFloorR) from 5 trades on.
    private static double? Sigma2(Unit u, double? pooled, ShrinkageOptions options, double sigmaFloorR)
    {
        double? own = u.Trades >= OwnVarianceTrades && u.Variance is { } v && double.IsFinite(v) ? Math.Pow(PowerAnalysis.Sigma(Math.Sqrt(v), sigmaFloorR), 2) : null;
        if (own is null)
        {
            return u.Trades >= OwnVarianceTrades ? null : pooled;
        }

        if (u.Trades > options.SigmaPoolStrength || pooled is null)
        {
            return own;
        }

        var w = u.Trades / (u.Trades + options.SigmaPoolStrength);
        return (w * own.Value) + ((1 - w) * pooled.Value);
    }

    // sum((n - 1) s^2) / sum(n - 1) over the cells with 2 or more trades, floored like a cell's own sd
    private static double? PooledVariance(IEnumerable<Unit> members, double sigmaFloorR)
    {
        double sum = 0;
        double df = 0;
        foreach (var m in members)
        {
            if (m.Trades >= 2 && m.Variance is { } v && double.IsFinite(v))
            {
                sum += (m.Trades - 1) * v;
                df += m.Trades - 1;
            }
        }

        return df > 0 ? Math.Max(sum / df, sigmaFloorR * sigmaFloorR) : null;
    }

    // the trades of the star cell that are in none of its regime cells (unlabelled); a multiset difference, so equal trades are interchangeable
    private static List<CellTrade> Remainder(Unit star, List<Unit> regimeCells)
    {
        var counts = new Dictionary<CellTrade, int>();
        foreach (var t in regimeCells.SelectMany(r => r.TradeList))
        {
            counts[t] = counts.GetValueOrDefault(t) + 1;
        }

        var left = new List<CellTrade>();
        foreach (var t in star.TradeList)
        {
            if (counts.TryGetValue(t, out var c) && c > 0)
            {
                counts[t] = c - 1;
            }
            else
            {
                left.Add(t);
            }
        }

        return left;
    }

    /// <summary>The numbers of one cell window the shrinkage reads.</summary>
    private sealed record Unit(CellId Cell, IReadOnlyList<CellTrade> TradeList, int Trades, int Wins, double WeightedWins, double WeightTotal, double Mean, double? Variance, double N, string NNote)
    {
        public static Unit Make(CellId cell, IReadOnlyList<CellTrade> trades, DateOnly asOf, CellStatsOptions options, double? independentN)
        {
            double wTotal = 0;
            double wSquares = 0;
            double wWins = 0;
            double rSum = 0;
            var wins = 0;
            foreach (var t in trades)
            {
                var w = CellStatsCalculator.Weight(t, asOf, options);
                wTotal += w;
                wSquares += w * w;
                rSum += w * t.R;
                if (t.R > 0)
                {
                    wWins += w;
                    wins++;
                }
            }

            var mean = wTotal > 0 ? rSum / wTotal : double.NaN;
            double? variance = null;
            if (trades.Count >= 2)
            {
                var plain = trades.Average(t => t.R);
                variance = trades.Sum(t => (t.R - plain) * (t.R - plain)) / (trades.Count - 1);
            }

            var kish = wSquares > 0 ? wTotal * wTotal / wSquares : 0;
            var n = trades.Count == 0 ? 0 : independentN is { } eff ? Math.Min(kish, eff) : kish;
            var note = trades.Count == 0 || independentN is not null ? string.Empty : "n is the recency-weight n alone (effective n unavailable)";
            return new Unit(cell, trades, trades.Count, wins, wWins, wTotal, mean, variance, n, note);
        }
    }
}
