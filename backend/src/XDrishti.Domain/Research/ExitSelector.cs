using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// The per-cell exit choice by walk-forward (P4-06, ADR 0054): expanding train windows with a session embargo, one test window of calendar months after each, the score <c>mean_R - lambda * downside_dev_R</c>, the plateau rule of doc 08 §3 and a
/// stability verdict over the folds. A pure function of <see cref="SelectionInput"/>: no clock, no random, no I/O. The only thing fitted is the choice of one candidate among those named; the entries are fixed.
/// </summary>
public static class ExitSelector
{
    private const long MaxCompositions = 2_000_000;

    /// <summary>Chooses.</summary>
    /// <param name="input">The comparison's trades and the options.</param>
    /// <exception cref="ArgumentException">Fewer than two candidates, duplicate ids, a trade of an unknown candidate, unsorted sessions or unusable options.</exception>
    public static ExitSelection Select(SelectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var o = input.Options;
        if (o.Problems().Count > 0)
        {
            throw new ArgumentException(string.Join("; ", o.Problems()), nameof(input));
        }

        if (input.Candidates.Count < 2)
        {
            throw new ArgumentException("An exit choice needs two candidates or more.", nameof(input));
        }

        var ids = input.Candidates.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException("Two candidates have the same id.", nameof(input));
        }

        for (var i = 1; i < input.Sessions.Count; i++)
        {
            if (input.Sessions[i] <= input.Sessions[i - 1])
            {
                throw new ArgumentException("The sessions must ascend and be distinct.", nameof(input));
            }
        }

        var slot = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var trades = new List<SelectionTrade>[ids.Length];
        for (var i = 0; i < trades.Length; i++)
        {
            trades[i] = [];
        }

        foreach (var trade in input.Trades)
        {
            if (!slot.TryGetValue(trade.CandidateId, out var s))
            {
                throw new ArgumentException("A trade names a candidate that is not in the comparison: " + trade.CandidateId, nameof(input));
            }

            trades[s].Add(trade);
        }

        var exits = input.Candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var neighbours = Neighbours(exits);
        var all = input.Trades;
        DateOnly? first = all.Count == 0 ? null : all.Min(t => t.Entry);
        DateOnly? last = all.Count == 0 ? null : all.Max(t => t.Entry);
        var (windows, dropped) = Windows(first, last, o);

        var folds = new List<SelectionFold>(windows.Count);
        foreach (var (start, end) in windows)
        {
            folds.Add(Fold(folds.Count + 1, start, end, input, trades, neighbours, ids));
        }

        var full = new Acc[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            foreach (var t in trades[i])
            {
                full[i].Add(t.R);
            }
        }

        var finalPick = Choose(full, neighbours, o);
        var usable = folds.Count(f => f.Winner is not null && !f.Thin);
        string? noChoice = null;
        if (finalPick is null)
        {
            noChoice = "fewer than 2 candidates have " + o.MinTrades.ToString(CultureInfo.InvariantCulture) + " trades in the whole sample";
        }
        else if (usable < 2)
        {
            noChoice = "fewer than 2 folds are usable (" + usable.ToString(CultureInfo.InvariantCulture) + " of " + folds.Count.ToString(CultureInfo.InvariantCulture) + " have a winner with " + o.MinTrades.ToString(CultureInfo.InvariantCulture) + " test trades)";
        }

        var finalId = noChoice is null ? ids[finalPick!.Value.Index] : null;
        var defaultId = input.Candidates[0].Id;
        var pooled = Pooled(folds, trades, ids, defaultId, o);
        var stability = Stability(folds, exits, neighbours, ids, o);
        var banner = BannerOf(input, folds.Count, noChoice, stability);
        return new ExitSelection(
            input, defaultId, first, last, dropped, folds.AsReadOnly(), finalId, finalId is not null && finalPick!.Value.Plateau, finalId is not null && finalPick!.Value.LonePeak,
            finalId ?? defaultId, noChoice, pooled, stability, banner);
    }

    /// <summary>The score of a sample: <c>mean_R - lambda * sqrt(mean(min(R, 0)^2))</c>; null for an empty sample.</summary>
    public static double? Score(IReadOnlyList<double> r, double lambda)
    {
        ArgumentNullException.ThrowIfNull(r);
        var acc = new Acc();
        foreach (var v in r)
        {
            acc.Add(v);
        }

        return acc.Score(lambda);
    }

    /// <summary>The test windows (start, last day) of the expanding walk-forward and the number of short final windows dropped (D2).</summary>
    public static (List<(DateOnly Start, DateOnly End)> Windows, int Dropped) Windows(DateOnly? firstEntry, DateOnly? lastEntry, SelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var windows = new List<(DateOnly, DateOnly)>();
        if (firstEntry is not { } a || lastEntry is not { } z)
        {
            return (windows, 0);
        }

        var start = a.AddMonths(options.MinTrainMonths);
        if (start.Day != 1)
        {
            start = new DateOnly(start.Year, start.Month, 1).AddMonths(1);
        }

        var dropped = 0;
        while (start <= z)
        {
            var next = start.AddMonths(options.TestMonths);
            if (next > z.AddDays(1) && z.AddDays(1) < start.AddMonths(1))
            {
                dropped++;
                break;
            }

            windows.Add((start, next.AddDays(-1)));
            start = next;
        }

        return (windows, dropped);
    }

    private static SelectionFold Fold(int index, DateOnly start, DateOnly end, SelectionInput input, List<SelectionTrade>[] trades, int[][] neighbours, string[] ids)
    {
        var o = input.Options;
        var before = LowerBound(input.Sessions, start);
        DateOnly? cutoff = o.EmbargoSessions == 0 ? start.AddDays(-1) : before >= o.EmbargoSessions ? input.Sessions[before - o.EmbargoSessions] : null;
        var train = new Acc[ids.Length];
        var test = new Acc[ids.Length];
        int embargoed = 0, purged = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            foreach (var t in trades[i])
            {
                if (t.Entry >= start)
                {
                    if (t.Entry <= end)
                    {
                        test[i].Add(t.R);
                    }
                }
                else if (cutoff is { } c && t.Entry <= c)
                {
                    if (t.Exit < start)
                    {
                        train[i].Add(t.R);
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

        var pick = Choose(train, neighbours, o);
        var rows = new List<FoldCandidateRow>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            rows.Add(new FoldCandidateRow(ids[i], train[i].N >= o.MinTrades, train[i].N, train[i].N == 0 ? null : train[i].Mean, train[i].Score(o.Lambda), test[i].N, test[i].Score(o.Lambda)));
        }

        var trainTotal = train.Sum(a => a.N);
        var testTotal = test.Sum(a => a.N);
        if (pick is not { } p)
        {
            return new SelectionFold(index, start, end, cutoff, trainTotal, testTotal, embargoed, purged, null, false, false, false, null, 0, null, true, rows.AsReadOnly());
        }

        var thin = test[p.Index].N < o.MinTrades;
        return new SelectionFold(index, start, end, cutoff, trainTotal, testTotal, embargoed, purged, ids[p.Index], p.Plateau, p.Isolated, p.LonePeak, train[p.Index].Score(o.Lambda), test[p.Index].N, test[p.Index].Score(o.Lambda), thin, rows.AsReadOnly());
    }

    private static int LowerBound(IReadOnlyList<DateOnly> sessions, DateOnly date)
    {
        int lo = 0, hi = sessions.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sessions[mid] < date)
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

    /// <summary>The plateau rule of D7 on the accumulated train figures, or null when fewer than two candidates qualify (D5).</summary>
    private static Pick? Choose(Acc[] acc, int[][] neighbours, SelectionOptions o)
    {
        var qualified = new bool[acc.Length];
        var count = 0;
        for (var i = 0; i < acc.Length; i++)
        {
            qualified[i] = acc[i].N >= o.MinTrades;
            count += qualified[i] ? 1 : 0;
        }

        if (count < 2)
        {
            return null;
        }

        var score = acc.Select(a => a.N == 0 ? double.NegativeInfinity : a.Score(o.Lambda)!.Value).ToArray();
        var best = -1;
        var plateau = new bool[acc.Length];
        var isolated = new bool[acc.Length];
        for (var i = 0; i < acc.Length; i++)
        {
            if (!qualified[i])
            {
                continue;
            }

            var near = neighbours[i].Where(n => qualified[n]).ToArray();
            isolated[i] = near.Length == 0;
            plateau[i] = near.Length > 0 && near.All(n => score[n] >= score[i] - acc[i].StandardError);
            if (best < 0 || score[i] > score[best])
            {
                best = i;
            }
        }

        if (!o.PreferPlateau)
        {
            return new Pick(best, plateau[best], isolated[best], false);
        }

        // A plateau candidate is eligible only when it scores within one standard error of the best: the rule of D7 read literally would pick the worst candidate of a line (whose neighbours are all better), see ADR 0054.
        var floor = score[best] - acc[best].StandardError;
        var chosen = -1;
        for (var i = 0; i < acc.Length; i++)
        {
            if (plateau[i] && score[i] >= floor && (chosen < 0 || score[i] > score[chosen]))
            {
                chosen = i;
            }
        }

        return chosen >= 0 ? new Pick(chosen, true, false, false) : new Pick(best, false, isolated[best], true);
    }

    /// <summary>The plateau rule on bare samples, for the hand tables: the winner among <paramref name="candidates"/> given each one's R values, or null when fewer than two have the minimum of trades.</summary>
    /// <param name="candidates">The candidates; their order does not matter.</param>
    /// <param name="samples">The R values of each candidate, in the order of <paramref name="candidates"/>.</param>
    /// <param name="options">The options.</param>
    public static PlateauChoice? Choose(IReadOnlyList<ExitCandidate> candidates, IReadOnlyList<IReadOnlyList<double>> samples, SelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        var order = Enumerable.Range(0, candidates.Count).OrderBy(i => candidates[i].Id, StringComparer.Ordinal).ToArray();
        var sorted = order.Select(i => candidates[i]).ToArray();
        var acc = new Acc[sorted.Length];
        for (var i = 0; i < order.Length; i++)
        {
            foreach (var v in samples[order[i]])
            {
                acc[i].Add(v);
            }
        }

        return Choose(acc, Neighbours(sorted), options) is { } p ? new PlateauChoice(sorted[p.Index].Id, p.Plateau, p.Isolated, p.LonePeak) : null;
    }

    private static List<PooledFigure> Pooled(List<SelectionFold> folds, List<SelectionTrade>[] trades, string[] ids, string defaultId, SelectionOptions o)
    {
        var withWinner = folds.Where(f => f.Winner is not null).ToList();
        var figures = new List<PooledFigure>();
        var walk = new Acc();
        var perCandidate = new Acc[ids.Length];
        foreach (var f in withWinner)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                foreach (var t in trades[i])
                {
                    if (t.Entry >= f.TestStart && t.Entry <= f.TestEnd)
                    {
                        perCandidate[i].Add(t.R);
                        if (string.Equals(ids[i], f.Winner, StringComparison.Ordinal))
                        {
                            walk.Add(t.R);
                        }
                    }
                }
            }
        }

        figures.Add(Figure("walk-forward", walk, o));
        var defaultIndex = Array.IndexOf(ids, defaultId);
        figures.Add(Figure("default " + defaultId, perCandidate[defaultIndex], o));
        for (var i = 0; i < ids.Length; i++)
        {
            figures.Add(Figure(ids[i], perCandidate[i], o));
        }

        return figures;
    }

    private static PooledFigure Figure(string name, Acc a, SelectionOptions o) => new(name, a.N, a.N == 0 ? null : a.Mean, a.Score(o.Lambda));

    private static SelectionStability Stability(List<SelectionFold> folds, ExitCandidate[] exits, int[][] neighbours, string[] ids, SelectionOptions o)
    {
        var used = folds.Where(f => f.Winner is not null && !f.Thin).ToList();
        var lonePeaks = used.Count(f => f.LonePeak);
        if (used.Count < 2)
        {
            return new SelectionStability(null, "no stability statement: " + used.Count.ToString(CultureInfo.InvariantCulture) + " usable fold" + (used.Count == 1 ? string.Empty : "s") + " of " + folds.Count.ToString(CultureInfo.InvariantCulture) + " (at least 2 are needed)", used.Count, null, 0, 0, null, lonePeaks);
        }

        var wins = new int[ids.Length];
        foreach (var f in used)
        {
            wins[Array.IndexOf(ids, f.Winner)]++;
        }

        var modal = 0;
        for (var i = 1; i < wins.Length; i++)
        {
            if (wins[i] > wins[modal])
            {
                modal = i;
            }
        }

        var agree = used.Where(f => Agrees(Array.IndexOf(ids, f.Winner), modal, neighbours)).ToList();
        var positive = agree.Count(f => f.WinnerTestScore > 0);
        var k = used.Count;
        var agreeOk = agree.Count * 100.0 >= o.StableFoldsPct * k;
        var positiveOk = agree.Count > 0 && positive * 100.0 >= o.StableFoldsPct * agree.Count;
        var pct = o.StableFoldsPct.ToString("0.##", CultureInfo.InvariantCulture);
        var reason = !agreeOk
            ? "unstable: " + agree.Count.ToString(CultureInfo.InvariantCulture) + " of " + k.ToString(CultureInfo.InvariantCulture) + " folds agree with " + ids[modal] + ", below " + pct + " %"
            : !positiveOk
                ? "unstable: the winners agree but only " + positive.ToString(CultureInfo.InvariantCulture) + " of " + agree.Count.ToString(CultureInfo.InvariantCulture) + " agreeing folds scored above 0 out of sample (a stable loss is not an edge)"
                : "stable: " + agree.Count.ToString(CultureInfo.InvariantCulture) + " of " + k.ToString(CultureInfo.InvariantCulture) + " folds agree with " + ids[modal] + " and " + positive.ToString(CultureInfo.InvariantCulture) + " of them scored above 0 out of sample";
        return new SelectionStability(agreeOk && positiveOk, reason, k, ids[modal], agree.Count, positive, ChancePass(k, ids.Length, neighbours, o.StableFoldsPct), lonePeaks);
    }

    private static bool Agrees(int winner, int modal, int[][] neighbours) => winner == modal || Array.IndexOf(neighbours[modal], winner) >= 0;

    /// <summary>
    /// The exact probability that <paramref name="folds"/> winners drawn uniformly and independently among the candidates satisfy the agreement rule alone (the count of the most frequent winner, ties to the lowest id, plus those of its neighbours
    /// reaches the share); null when the enumeration of the possible tallies would exceed two million. The second rule (positive out of sample) depends on the returns and is not part of it.
    /// </summary>
    public static double? ChancePass(int folds, int candidates, int[][] neighbours, double stablePct)
    {
        ArgumentNullException.ThrowIfNull(neighbours);
        if (folds < 1 || candidates < 1)
        {
            return null;
        }

        var tallies = 1.0;
        for (var i = 1; i < candidates; i++)
        {
            tallies = tallies * (folds + i) / i;
            if (tallies > MaxCompositions)
            {
                return null;
            }
        }

        var logFactorial = new double[folds + 1];
        for (var i = 2; i <= folds; i++)
        {
            logFactorial[i] = logFactorial[i - 1] + Math.Log(i);
        }

        var counts = new int[candidates];
        var passing = 0.0;
        var logTotal = folds * Math.Log(candidates);
        Walk(0, folds);
        return Math.Min(1.0, passing);

        void Walk(int slot, int left)
        {
            if (slot == candidates - 1)
            {
                counts[slot] = left;
                var modal = 0;
                for (var i = 1; i < candidates; i++)
                {
                    if (counts[i] > counts[modal])
                    {
                        modal = i;
                    }
                }

                var agree = counts[modal] + neighbours[modal].Sum(n => counts[n]);
                if (agree * 100.0 >= stablePct * folds)
                {
                    var log = logFactorial[folds] - counts.Sum(c => logFactorial[c]) - logTotal;
                    passing += Math.Exp(log);
                }

                return;
            }

            for (var c = 0; c <= left; c++)
            {
                counts[slot] = c;
                Walk(slot + 1, left - c);
            }
        }
    }

    /// <summary>
    /// The neighbours of every candidate, in the order of <paramref name="sorted"/> (D7): along each parameter, among the candidates of the same policy that are equal in every other parameter, the one just below and the one just above by the first
    /// value (then by id). Candidates of different policies, or that differ in two parameters, are not neighbours.
    /// </summary>
    public static int[][] Neighbours(IReadOnlyList<ExitCandidate> sorted)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var result = new List<int>[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            result[i] = [];
            foreach (var parameter in sorted[i].Parameters)
            {
                var line = Enumerable.Range(0, sorted.Count)
                    .Where(k => sorted[k].Policy == sorted[i].Policy && SameElsewhere(sorted[i], sorted[k], parameter.Name))
                    .OrderBy(k => FirstValue(sorted[k], parameter.Name))
                    .ThenBy(k => k)
                    .ToList();
                var at = line.IndexOf(i);
                if (at > 0 && !result[i].Contains(line[at - 1]))
                {
                    result[i].Add(line[at - 1]);
                }

                if (at >= 0 && at < line.Count - 1 && !result[i].Contains(line[at + 1]))
                {
                    result[i].Add(line[at + 1]);
                }
            }
        }

        return [.. result.Select(r => r.Order().ToArray())];
    }

    private static double FirstValue(ExitCandidate c, string name) => c.Parameters.FirstOrDefault(p => p.Name == name)?.Values[0] ?? 0;

    private static bool SameElsewhere(ExitCandidate a, ExitCandidate b, string name)
    {
        if (a.Parameters.Count != b.Parameters.Count)
        {
            return false;
        }

        foreach (var p in a.Parameters)
        {
            if (p.Name == name)
            {
                continue;
            }

            var other = b.Parameters.FirstOrDefault(q => q.Name == p.Name);
            if (other is null || !p.Values.SequenceEqual(other.Values))
            {
                return false;
            }
        }

        return b.Parameters.Any(q => q.Name == name);
    }

    private static List<string> BannerOf(SelectionInput input, int folds, string? noChoice, SelectionStability stability)
    {
        var lines = new List<string>
        {
            ExitSelection.InSampleLine,
            folds.ToString(CultureInfo.InvariantCulture) + " folds only: the " + input.Options.StableFoldsPct.ToString("0.##", CultureInfo.InvariantCulture) + " % rule on few folds is weak evidence",
            "unit: " + input.Unit + (input.Unit == "RNet" ? " (after charges)" : " (gross, no charges)"),
        };
        if (stability.ChancePass is { } p)
        {
            lines.Add(stability.Folds.ToString(CultureInfo.InvariantCulture) + " folds; the " + input.Options.StableFoldsPct.ToString("0.##", CultureInfo.InvariantCulture) + " % rule on " + stability.Folds.ToString(CultureInfo.InvariantCulture) + " folds can pass by chance about " + p.ToString("0.0000", CultureInfo.InvariantCulture) + " of the time");
        }

        if (input.OverlapNotGated)
        {
            lines.Add("overlap not gated: every candidate walks every entry, overlaps allowed");
        }

        if (noChoice is not null)
        {
            lines.Add("no choice: " + noChoice + "; the default exit is used");
        }

        return lines;
    }

    private readonly record struct Pick(int Index, bool Plateau, bool Isolated, bool LonePeak);

    /// <summary>Mean, sum of squared deviations (Welford) and sum of squared losses of a sample, without keeping it.</summary>
    private struct Acc
    {
        private double _mean;
        private double _m2;
        private double _negSq;

        public int N { get; private set; }

        public readonly double Mean => _mean;

        public readonly double StandardError => N < 2 ? 0 : Math.Sqrt(_m2 / (N - 1) / N);

        public void Add(double r)
        {
            N++;
            var delta = r - _mean;
            _mean += delta / N;
            _m2 += delta * (r - _mean);
            if (r < 0)
            {
                _negSq += r * r;
            }
        }

        public readonly double? Score(double lambda) => N == 0 ? null : _mean - (lambda * Math.Sqrt(_negSq / N));
    }
}
