using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>
/// A second implementation of the exit choice, written from the text of D2 to D8 and ADR 0054 only, in the plainest way (P4-06, D11): lists and loops, two-pass statistics, neighbours by pairwise comparison, the chance of passing by luck by walking every
/// sequence of winners. It shares no code with <see cref="ExitSelector"/>. Slow on purpose.
/// </summary>
internal static class SelectionOracle
{
    internal sealed record Fold(DateOnly Start, DateOnly End, int Train, int Test, int Embargoed, int Purged, string? Winner, bool Plateau, bool Isolated, bool Lone, bool Thin, double? WinnerTestScore, List<(string Id, bool Qualified, int TrainN, int TestN, double? TrainScore, double? TestScore)> Rows);

    internal sealed record Result(List<Fold> Folds, int Dropped, string? Final, string Chosen, bool NoChoice, bool? Stable, string? Modal, int Agreeing, int Positive, int Used, int LonePeaks, double? Chance, List<(string Name, int N, double? Score)> Pooled);

    private static double? Score(List<double> r, double lambda)
    {
        if (r.Count == 0)
        {
            return null;
        }

        var mean = r.Sum() / r.Count;
        var loss = r.Where(x => x < 0).Sum(x => x * x) / r.Count;
        return mean - (lambda * Math.Sqrt(loss));
    }

    private static double StdErr(List<double> r)
    {
        if (r.Count < 2)
        {
            return 0;
        }

        var mean = r.Sum() / r.Count;
        var variance = r.Sum(x => (x - mean) * (x - mean)) / (r.Count - 1);
        return Math.Sqrt(variance) / Math.Sqrt(r.Count);
    }

    /// <summary>Pairs of ids that are neighbours: same policy, differing in exactly one parameter, and adjacent among the candidates that agree on all the others.</summary>
    private static HashSet<(string, string)> NeighbourPairs(List<ExitCandidate> cs)
    {
        var pairs = new HashSet<(string, string)>();
        foreach (var a in cs)
        {
            foreach (var b in cs)
            {
                if (a.Id == b.Id || a.Policy != b.Policy || a.Parameters.Count != b.Parameters.Count)
                {
                    continue;
                }

                var differing = a.Parameters.Where(p => !p.Values.SequenceEqual(b.Parameters.First(q => q.Name == p.Name).Values)).ToList();
                if (differing.Count != 1)
                {
                    continue;
                }

                var name = differing[0].Name;
                var va = a.Parameters.First(p => p.Name == name).Values[0];
                var vb = b.Parameters.First(p => p.Name == name).Values[0];
                var between = cs.Any(c => c.Policy == a.Policy && c.Id != a.Id && c.Id != b.Id && c.Parameters.Count == a.Parameters.Count
                    && a.Parameters.Where(p => p.Name != name).All(p => p.Values.SequenceEqual(c.Parameters.First(q => q.Name == p.Name).Values))
                    && c.Parameters.First(p => p.Name == name).Values[0] > Math.Min(va, vb) && c.Parameters.First(p => p.Name == name).Values[0] < Math.Max(va, vb));
                if (!between)
                {
                    pairs.Add((a.Id, b.Id));
                }
            }
        }

        return pairs;
    }

    private static (string Id, bool Plateau, bool Isolated, bool Lone)? Pick(List<ExitCandidate> cs, Dictionary<string, List<double>> sample, HashSet<(string, string)> nb, SelectionOptions o)
    {
        var ids = cs.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).Where(i => sample[i].Count >= o.MinTrades).ToList();
        if (ids.Count < 2)
        {
            return null;
        }

        double S(string id) => Score(sample[id], o.Lambda)!.Value;
        bool IsPlateau(string id)
        {
            var near = ids.Where(n => nb.Contains((id, n))).ToList();
            return near.Count > 0 && near.All(n => S(n) >= S(id) - StdErr(sample[id]));
        }

        var best = ids[0];
        foreach (var id in ids)
        {
            if (S(id) > S(best))
            {
                best = id;
            }
        }

        bool Iso(string id) => !ids.Any(n => nb.Contains((id, n)));
        if (!o.PreferPlateau)
        {
            return (best, IsPlateau(best), Iso(best), false);
        }

        var floor = S(best) - StdErr(sample[best]);
        string? chosen = null;
        foreach (var id in ids)
        {
            if (IsPlateau(id) && S(id) >= floor && (chosen is null || S(id) > S(chosen)))
            {
                chosen = id;
            }
        }

        return chosen is not null ? (chosen, true, false, false) : (best, false, Iso(best), true);
    }

    public static Result Run(SelectionInput input)
    {
        var o = input.Options;
        var cs = input.Candidates.ToList();
        var ids = cs.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        var nb = NeighbourPairs(cs);
        var all = input.Trades.ToList();
        var folds = new List<Fold>();
        var dropped = 0;
        if (all.Count > 0)
        {
            var a = all.Min(t => t.Entry);
            var z = all.Max(t => t.Entry);
            var start = a;
            for (var m = 0; m < o.MinTrainMonths; m++)
            {
                start = start.AddMonths(1);
            }

            start = a.AddMonths(o.MinTrainMonths);
            if (start.Day != 1)
            {
                start = new DateOnly(start.Year, start.Month, 1).AddMonths(1);
            }

            while (start <= z)
            {
                var after = start.AddMonths(o.TestMonths);
                var lastDay = after.AddDays(-1);
                var shortWindow = lastDay > z && (z.DayNumber - start.DayNumber + 1) < (start.AddMonths(1).DayNumber - start.DayNumber);
                if (shortWindow)
                {
                    dropped++;
                    break;
                }

                folds.Add(OneFold(input, ids, cs, nb, start, lastDay));
                start = after;
            }
        }

        var full = ids.ToDictionary(i => i, i => all.Where(t => t.CandidateId == i).Select(t => t.R).ToList());
        var finalPick = Pick(cs, full, nb, o);
        var usable = folds.Where(f => f.Winner is not null && !f.Thin).ToList();
        var noChoice = finalPick is null || usable.Count < 2;
        var final = noChoice ? null : finalPick!.Value.Id;

        var pooled = new List<(string, int, double?)>();
        var withWinner = folds.Where(f => f.Winner is not null).ToList();
        List<double> Pool(Func<Fold, string> who) => [.. withWinner.SelectMany(f => all.Where(t => t.CandidateId == who(f) && t.Entry >= f.Start && t.Entry <= f.End).Select(t => t.R))];
        var walk = Pool(f => f.Winner!);
        pooled.Add(("walk-forward", walk.Count, Score(walk, o.Lambda)));
        var def = Pool(_ => cs[0].Id);
        pooled.Add(("default " + cs[0].Id, def.Count, Score(def, o.Lambda)));
        foreach (var id in ids)
        {
            var p = Pool(_ => id);
            pooled.Add((id, p.Count, Score(p, o.Lambda)));
        }

        bool? stable = null;
        string? modal = null;
        int agreeing = 0, positive = 0;
        double? chance = null;
        if (usable.Count >= 2)
        {
            var k = usable.Count;
            modal = ids.OrderByDescending(i => usable.Count(f => f.Winner == i)).ThenBy(i => i, StringComparer.Ordinal).First();
            var agreeingFolds = usable.Where(f => f.Winner == modal || nb.Contains((modal, f.Winner!))).ToList();
            agreeing = agreeingFolds.Count;
            positive = agreeingFolds.Count(f => f.WinnerTestScore > 0);
            stable = agreeing >= o.StableFoldsPct / 100.0 * k - 1e-9 && agreeing > 0 && positive >= o.StableFoldsPct / 100.0 * agreeing - 1e-9;
            chance = Chance(k, ids, nb, o.StableFoldsPct);
        }

        return new Result(folds, dropped, final, final ?? cs[0].Id, noChoice, stable, modal, agreeing, positive, usable.Count, usable.Count(f => f.Lone), chance, pooled);
    }

    private static Fold OneFold(SelectionInput input, List<string> ids, List<ExitCandidate> cs, HashSet<(string, string)> nb, DateOnly start, DateOnly end)
    {
        var o = input.Options;
        var before = input.Sessions.Where(s => s < start).ToList();
        DateOnly? cutoff = o.EmbargoSessions == 0 ? start.AddDays(-1) : before.Count >= o.EmbargoSessions ? before[before.Count - o.EmbargoSessions] : null;
        var train = ids.ToDictionary(i => i, _ => new List<double>());
        var test = ids.ToDictionary(i => i, _ => new List<double>());
        int embargoed = 0, purged = 0;
        foreach (var t in input.Trades)
        {
            if (t.Entry >= start && t.Entry <= end)
            {
                test[t.CandidateId].Add(t.R);
            }
            else if (t.Entry < start)
            {
                if (cutoff is { } c && t.Entry <= c)
                {
                    if (t.Exit < start)
                    {
                        train[t.CandidateId].Add(t.R);
                    }
                    else
                    {
                        purged++;
                    }
                }
                else
                {
                    embargoed++;
                }
            }
        }

        var pick = Pick(cs, train, nb, o);
        var rows = ids.Select(i => (i, train[i].Count >= o.MinTrades, train[i].Count, test[i].Count, Score(train[i], o.Lambda), Score(test[i], o.Lambda))).ToList();
        var trainN = train.Values.Sum(l => l.Count);
        var testN = test.Values.Sum(l => l.Count);
        if (pick is not { } p)
        {
            return new Fold(start, end, trainN, testN, embargoed, purged, null, false, false, false, true, null, rows);
        }

        return new Fold(start, end, trainN, testN, embargoed, purged, p.Id, p.Plateau, p.Isolated, p.Lone, test[p.Id].Count < o.MinTrades, Score(test[p.Id], o.Lambda), rows);
    }

    /// <summary>The share of all sequences of <paramref name="k"/> winners among the candidates that pass the agreement rule; null above 6,000 sequences.</summary>
    private static double? Chance(int k, List<string> ids, HashSet<(string, string)> nb, double pct)
    {
        var total = Math.Pow(ids.Count, k);
        if (total > 6000)
        {
            return null;
        }

        var pass = 0;
        var sequence = new int[k];
        for (long n = 0; n < (long)total; n++)
        {
            var rest = n;
            for (var i = 0; i < k; i++)
            {
                sequence[i] = (int)(rest % ids.Count);
                rest /= ids.Count;
            }

            var modal = Enumerable.Range(0, ids.Count).OrderByDescending(c => sequence.Count(s => s == c)).ThenBy(c => c).First();
            var agree = sequence.Count(s => s == modal || nb.Contains((ids[modal], ids[s])));
            if (agree >= pct / 100.0 * k - 1e-9)
            {
                pass++;
            }
        }

        return pass / total;
    }
}
