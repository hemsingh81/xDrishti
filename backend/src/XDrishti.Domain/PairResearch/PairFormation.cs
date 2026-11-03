using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// One formation (P14-13) over daily closes before the quarter: for each candidate the Engle-Granger test (lags 1) on the pair as given (<c>y</c> first), Benjamini-Hochberg at q over <b>all</b> candidates (an unavailable test counts in <c>m</c> with p = 1), then, for the rejected pairs only, the half-life of the daily
/// spread, which must lie in the pre-registered range in sessions, and a hedge ratio above 0. A pair kept carries the window's <c>alpha</c> and <c>beta</c> and the mean of its daily spread for the quarter. Pure and deterministic: the same closes give the same selection.
/// </summary>
public static class PairFormation
{
    public static FormationResult Form(IReadOnlyList<PairMember> members, IReadOnlyList<PairCandidate> candidates, PairStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        var core = options.Core;
        var tests = new PairsResult<EngleGrangerResult>[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var ly = LogSpread.Ln([.. members[c.YIndex].Closes]);
            var lx = LogSpread.Ln([.. members[c.XIndex].Closes]);
            tests[i] = ly.TryGet(out var logY) && lx.TryGet(out var logX) ? EngleGranger.Test(logY, logX, core) : default;
        }

        var bh = BenjaminiHochberg.Reject(tests, core.Q);
        var rejected = bh.TryGet(out var outcome) ? outcome : new BhOutcome(new bool[candidates.Count], 0, candidates.Count);
        var m = candidates.Count;
        var order = Enumerable.Range(0, m).OrderBy(i => tests[i].TryGet(out var t) ? t.PValue : 1.0).ThenBy(i => i).ToArray();
        var rows = new PairTestRow[m];
        var selected = new List<SelectedPair>();
        int unavailable = 0, hlOut = 0, betaBad = 0;
        for (var rank = 0; rank < m; rank++)
        {
            var i = order[rank];
            var c = candidates[i];
            var has = tests[i].TryGet(out var test);
            if (!has)
            {
                unavailable++;
            }

            var threshold = (rank + 1) * core.Q / m;
            double? hl = null;
            string? hlReason = null;
            string? notSelected = null;
            var keep = false;
            if (rejected.Rejected[i] && has)
            {
                var residuals = test.Hedge.Residuals;
                var half = HalfLife.Of([.. residuals], core);
                if (!half.TryGet(out var bars))
                {
                    hlReason = half.Reason!.Value.Label();
                    notSelected = "half_life_" + hlReason;
                    hlOut++;
                }
                else
                {
                    hl = bars;
                    if (!HalfLife.InRange(bars, core))
                    {
                        notSelected = "half_life_out_of_range";
                        hlOut++;
                    }
                    else if (!(test.Hedge.Beta > 0))
                    {
                        notSelected = "beta_not_positive";
                        betaBad++;
                    }
                    else
                    {
                        keep = true;
                        selected.Add(new SelectedPair(c.Y, c.X, test.Hedge.Alpha, test.Hedge.Beta, residuals.Sum() / residuals.Count, bars, test.Tau, test.PValue));
                    }
                }
            }
            else if (!has)
            {
                notSelected = "test_unavailable";
            }
            else if (!rejected.Rejected[i])
            {
                notSelected = "not_rejected";
            }

            rows[rank] = new PairTestRow(c.Y, c.X, has ? test.Tau : null, has ? test.PValue : null, has ? null : tests[i].Reason?.Label() ?? "bad_input", rank + 1, threshold, rejected.Rejected[i], hl, hlReason, keep, notSelected);
        }

        return new FormationResult(rows, selected, rejected.K, m, unavailable, hlOut, betaBad);
    }
}
