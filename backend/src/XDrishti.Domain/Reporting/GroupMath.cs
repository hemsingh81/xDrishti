using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Reporting;

/// <summary>The figures of one group, from its trades in exit order (ADR 0099). The shrinkage is <see cref="BetaShrinkage"/>, the same code the cell statistics use; every trade has weight 1.</summary>
internal static class GroupMath
{
    public static ReportGroup Build(GroupKey key, IReadOnlyList<ReportRow> rows, GroupPriors priors, ReportOptions options, int minTrades)
    {
        var n = rows.Count;
        var wins = rows.Count(r => r.R > 0);
        var evidence = EvidenceOf(rows, options.Basis);

        var missing = key.Parts.Where(p => p.Value is null).Select(p => p.Dimension).ToList();
        if (missing.Count > 0)
        {
            var runs = string.Join(", ", rows.Select(r => r.Trade.RunKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            var reason = $"{string.Join(" and ", missing.Select(d => d.ToString().ToLowerInvariant()))} not recorded for run {runs}";
            return Wrap(key, n, wins, _ => Metric.Unavailable(reason), null, evidence);
        }

        if (n < minTrades)
        {
            var reason = $"{n} {(n == 1 ? "trade" : "trades")}, at least {minTrades} needed";
            return Wrap(key, n, wins, _ => Metric.Insufficient(reason), null, evidence);
        }

        double sumWin = 0;
        double sumNonWin = 0;
        double sumLoss = 0;
        double total = 0;
        double running = 0;
        double peak = 0;
        double drawdown = 0;
        var losses = 0;
        var nonWins = 0;
        var streak = 0;
        var longest = 0;
        long sessions = 0;
        foreach (var row in rows)
        {
            var r = row.R;
            total += r;
            sessions += row.Trade.SessionsHeld;
            running += r;
            peak = Math.Max(peak, running);
            drawdown = Math.Max(drawdown, peak - running);
            if (r > 0)
            {
                sumWin += r;
            }
            else
            {
                sumNonWin += r;
                nonWins++;
            }

            if (r < 0)
            {
                sumLoss += r;
                losses++;
                streak++;
                longest = Math.Max(longest, streak);
            }
            else
            {
                streak = 0;
            }
        }

        var avgWin = wins > 0 ? Metric.Of(sumWin / wins) : Metric.NotEvaluable("no winning trade");
        var avgLoss = losses > 0 ? Metric.Of(sumLoss / losses) : Metric.NotEvaluable("no losing trade");
        var strength = options.Stats.PriorStrength;
        var (posterior, lower, _) = BetaShrinkage.Posterior(wins, n, priors.WinRate, strength, options.Stats.CredibleLevel);

        // the mean win and the mean non-win are shrunk toward the parent group's with the strength of the win-rate prior: (sum + s x parent mean) / (count + s)
        var shrunkWin = wins > 0 && priors.AvgWin is { } pw ? (sumWin + (strength * pw)) / (wins + strength) : (double?)null;
        var shrunkNonWin = nonWins > 0 && priors.AvgNonWin is { } pn ? (sumNonWin + (strength * pn)) / (nonWins + strength) : (double?)null;
        Metric FromRate(double rate, bool ranking)
        {
            if (wins == 0 || shrunkWin is null)
            {
                return Metric.NotEvaluable("no winning trade");
            }

            if (nonWins == 0 || shrunkNonWin is null)
            {
                return Metric.NotEvaluable("no losing trade");
            }

            if (ranking && wins < options.MinWins)
            {
                return Metric.NotEvaluable($"only {wins} {(wins == 1 ? "win" : "wins")}, at least {options.MinWins} needed for a bound on the payoff");
            }

            if (ranking && nonWins < options.MinNonWins)
            {
                return Metric.NotEvaluable($"only {nonWins} non-winning {(nonWins == 1 ? "trade" : "trades")}, at least {options.MinNonWins} needed for a bound on the payoff");
            }

            return Metric.Of((rate * shrunkWin.Value) + ((1 - rate) * shrunkNonWin.Value));
        }

        var values = new Dictionary<ReportMetric, Metric>
        {
            [ReportMetric.WinRate] = Metric.Of((double)wins / n),
            [ReportMetric.AvgWinR] = avgWin,
            [ReportMetric.AvgLossR] = avgLoss,
            [ReportMetric.ExpectancyR] = Metric.Of(total / n),
            [ReportMetric.ProfitFactor] = losses > 0 ? Metric.Of(sumWin / -sumLoss) : Metric.NotEvaluable("no losing trade"),
            [ReportMetric.TotalR] = Metric.Of(total),
            [ReportMetric.MaxDrawdownR] = Metric.Of(drawdown),
            [ReportMetric.LongestLossStreak] = Metric.Of(longest),
            [ReportMetric.AvgHoldingSessions] = Metric.Of((double)sessions / n),
            [ReportMetric.ShrunkWinRate] = Metric.Of(posterior),
            [ReportMetric.WinRateLower] = Metric.Of(lower),
            [ReportMetric.ShrunkExpectancyR] = FromRate(posterior, ranking: false),
            [ReportMetric.ExpectancyLowerR] = FromRate(lower, ranking: true),
        };

        if (evidence.Provisional)
        {
            var provisional = string.Join(", ", rows.Where(r => r.Provisional).Select(r => r.Trade.RunKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            var reason = $"rests on a provisional run: {provisional}";
            foreach (var metric in values.Keys.ToList())
            {
                values[metric] = values[metric].AsProvisional(reason);
            }
        }

        return Wrap(key, n, wins, m => values[m], priors, evidence);
    }

    /// <summary>The evidence block of some rows in one pass (a 500,000-trade population is walked once, not once per figure).</summary>
    public static EvidenceBlock EvidenceOf(IReadOnlyList<ReportRow> rows, ReportBasis basis)
    {
        DateOnly first = default;
        DateOnly last = default;
        var provisional = false;
        var runs = new HashSet<string>(StringComparer.Ordinal);
        string? lastRun = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var trade = row.Trade;
            if (i == 0 || trade.EntryDate < first)
            {
                first = trade.EntryDate;
            }

            if (i == 0 || trade.ExitDate > last)
            {
                last = trade.ExitDate;
            }

            provisional |= row.Provisional;
            if (!ReferenceEquals(lastRun, trade.RunKey))
            {
                lastRun = trade.RunKey;
                runs.Add(trade.RunKey);
            }
        }

        return new EvidenceBlock(
            rows.Count,
            rows.Count == 0 ? null : first,
            rows.Count == 0 ? null : last,
            basis,
            provisional,
            new ValueList<string>(runs.Order(StringComparer.Ordinal)));
    }

    private static ReportGroup Wrap(GroupKey key, int n, int wins, Func<ReportMetric, Metric> metric, GroupPriors? priors, EvidenceBlock evidence) => new(
        key,
        n,
        wins,
        metric(ReportMetric.WinRate),
        metric(ReportMetric.AvgWinR),
        metric(ReportMetric.AvgLossR),
        metric(ReportMetric.ExpectancyR),
        metric(ReportMetric.ProfitFactor),
        metric(ReportMetric.TotalR),
        metric(ReportMetric.MaxDrawdownR),
        metric(ReportMetric.LongestLossStreak),
        metric(ReportMetric.AvgHoldingSessions),
        metric(ReportMetric.ShrunkWinRate),
        metric(ReportMetric.WinRateLower),
        metric(ReportMetric.ShrunkExpectancyR),
        metric(ReportMetric.ExpectancyLowerR),
        priors?.WinRate,
        priors?.AvgWin,
        priors?.AvgNonWin,
        evidence);

    /// <summary>Compares two values of one dimension: a value that was not recorded last, weekdays in calendar order, everything else by ordinal text.</summary>
    public static int CompareValues(ReportDimension dimension, string? a, string? b)
    {
        if (a is null || b is null)
        {
            return (a is null ? 1 : 0) - (b is null ? 1 : 0);
        }

        return dimension == ReportDimension.Weekday
            ? Array.IndexOf(Weekdays, a).CompareTo(Array.IndexOf(Weekdays, b))
            : string.CompareOrdinal(a, b);
    }

    public static int CompareKeys(GroupKey a, GroupKey b)
    {
        for (var i = 0; i < Math.Min(a.Parts.Count, b.Parts.Count); i++)
        {
            var c = CompareValues(a.Parts[i].Dimension, a.Parts[i].Value, b.Parts[i].Value);
            if (c != 0)
            {
                return c;
            }
        }

        return a.Parts.Count.CompareTo(b.Parts.Count);
    }

    public static readonly string[] Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public static string WeekdayOf(DateOnly date) => Weekdays[((int)date.DayOfWeek + 6) % 7];

    public static string MonthOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
