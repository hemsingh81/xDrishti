using XDrishti.Domain.Filings;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The features of <c>fu-1</c> at a decision instant (P15-05, ADR 0185, pure). The facts and the shareholding handed over are the answers visible at the instant, so nothing later can enter. One basis per instrument: consolidated when every quarter a feature needs has it, else standalone when
/// every one has it, else NaN (<c>basis_mixed</c> when the two bases together would cover the quarters, else <c>short_history</c>). EPS of every quarter is put on the share basis of the decision date for the splits and bonuses that came after the quarter was published (<see cref="EpsAdjustment"/>). Values built on facts of a synthetic parser are labelled
/// (<see cref="FeatureCell.Unverified"/>). Nothing is filled or imputed.
/// </summary>
public static class FundamentalFeatures
{
    public const int PeWindowSessions = 1250;

    public const int PeMinimumValues = 1000;

    private const string QuarterSpan = ResultCodes.Quarter;

    public static FeatureCell[] Compute(FundamentalFeatureInput input, int peWindow = PeWindowSessions, int peMinimum = PeMinimumValues)
    {
        ArgumentNullException.ThrowIfNull(input);
        var cells = new FeatureCell[FundamentalFeatureCatalog.Names.Count];
        FillFacts(input, cells, peWindow, peMinimum);
        FillShareholding(input.Shareholding, cells);
        return cells;
    }

    private static void FillFacts(FundamentalFeatureInput input, FeatureCell[] cells, int peWindow, int peMinimum)
    {
        var newest = input.Facts.Facts.Where(f => f.Span == QuarterSpan && f.State == "value").Select(f => (DateOnly?)f.PeriodEnd).Max();
        if (newest is not { } q)
        {
            foreach (var i in new[] { 0, 1, 2, 3, 6 })
            {
                cells[i] = FeatureCell.Unknown(FeatureReasons.NoFiling);
            }

            return;
        }

        var today = IndiaTime.Today(input.At);
        var unverified = !ResultsParser.IsVerified(input.Facts.ParserVersion);
        cells[0] = Standardised(Gather(Offsets(SurpriseMath.SueYoyQuarters), q, (period, basis) => Eps(input, today, period, basis)), SurpriseMath.SueYoy, unverified);
        cells[1] = Standardised(Gather(Offsets(SurpriseMath.SueTtmQuarters), q, (period, basis) => Eps(input, today, period, basis)), SurpriseMath.SueTtm, unverified);

        var revenue = Gather<double>([0, 4], q, (period, basis) => Value(input.Facts, period, ResultCodes.Revenue, basis));
        cells[2] = revenue.Values is { } r ? Finite(SurpriseMath.Growth(r[0], r[1]), unverified) : FeatureCell.Unknown(revenue.Reason!);

        var margin = Gather<Pair>([0, 4], q, (period, basis) => Value(input.Facts, period, ResultCodes.Revenue, basis) is { } rev && Value(input.Facts, period, ResultCodes.Pat, basis) is { } pat ? new Pair(pat, rev) : null);
        cells[3] = margin.Values is { } m ? Finite(SurpriseMath.MarginChange(m[0].Pat, m[0].Revenue, m[1].Pat, m[1].Revenue), unverified) : FeatureCell.Unknown(margin.Reason!);

        cells[6] = PePercentile(input, q, today, unverified, peWindow, peMinimum);
    }

    private static FeatureCell PePercentile(FundamentalFeatureInput input, DateOnly q, DateOnly today, bool unverified, int window, int minimum)
    {
        var ttmQuarters = Gather(Offsets(4), q, (period, basis) => Eps(input, today, period, basis));
        if (ttmQuarters.Values is not { } quarters)
        {
            return FeatureCell.Unknown(ttmQuarters.Reason!);
        }

        var ttm = quarters.Sum();
        if (ttm <= 0)
        {
            return FeatureCell.Unknown(FeatureReasons.Degenerate);
        }

        var earlier = input.Closes.Where(c => c.Date < today).ToList();
        if (earlier.Count == 0 || PreviousSession(today, input) is not { } previous || earlier[^1].Date != previous)
        {
            return FeatureCell.Unknown(FeatureReasons.NoBars);
        }

        var current = earlier[^1].Close * EpsAdjustment.Factor(input.Actions, previous, today) / ttm;
        var history = History(input, ttmQuarters.Basis!, earlier, window);
        var rank = SurpriseMath.Percentile(current, history, minimum);
        return double.IsNaN(rank) ? FeatureCell.Unknown(FeatureReasons.ShortHistory) : FeatureCell.Known(rank, unverified);
    }

    // The P/E of each earlier session on the share basis of that session: raw close over the TTM EPS of the four newest consecutive quarters visible at that session's close (broadcast + lag), each EPS on that session's basis. Sessions with fewer
    // than four visible consecutive quarters or a TTM of 0 or less have no value.
    private static List<double> History(FundamentalFeatureInput input, string basis, List<DailyClose> all, int window)
    {
        var quarters = input.Facts.Facts
            .Where(f => f.Span == QuarterSpan && f.Field == ResultCodes.EpsBasic && f.Basis == basis && f.State == "value" && f.Value is not null && f.BroadcastTs is not null)
            .OrderBy(f => f.BroadcastTs!.Value.UtcTicks).ThenBy(f => f.PeriodEnd)
            .ToList();
        var values = new List<double>(Math.Min(window, all.Count));
        if (quarters.Count == 0 || window <= 0)
        {
            return values;
        }

        var seen = new Dictionary<DateOnly, (double Eps, DateOnly Since)>();
        var next = 0;
        var start = all.Count - window;
        for (var i = 0; i < all.Count; i++)
        {
            var session = all[i];
            var closeAt = ReactionSession.CloseOf(session.Date, input.Calendar) ?? new DateTimeOffset(session.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
            while (next < quarters.Count && quarters[next].BroadcastTs!.Value + input.Lag <= closeAt)
            {
                seen[quarters[next].PeriodEnd] = ((double)quarters[next].Value!.Value, EpsAdjustment.SinceOf(quarters[next].BroadcastTs, quarters[next].PeriodEnd));
                next++;
            }

            if (i < start || seen.Count < 4)
            {
                continue;
            }

            var newest = seen.Keys.Max();
            double ttm = 0;
            var complete = true;
            for (var k = 0; k < 4 && complete; k++)
            {
                var period = QuarterEnds.Back(newest, k);
                if (seen.TryGetValue(period, out var quarter))
                {
                    ttm += quarter.Eps * EpsAdjustment.Factor(input.Actions, quarter.Since, session.Date);
                }
                else
                {
                    complete = false;
                }
            }

            if (complete && ttm > 0)
            {
                values.Add(session.Close / ttm);
            }
        }

        return values;
    }

    private static void FillShareholding(ShareholdingAnswer answer, FeatureCell[] cells)
    {
        var promoter = answer.State == ShareholdingAnswer.Value ? answer.Current?.Lines.FirstOrDefault(l => l.Category == "promoter") : null;
        cells[4] = promoter?.PledgedPct is { } pledged ? FeatureCell.Known((double)pledged) : FeatureCell.Unknown(FeatureReasons.NoFiling);
        if (promoter?.Pct is not { } now)
        {
            cells[5] = FeatureCell.Unknown(FeatureReasons.NoFiling);
            return;
        }

        var before = answer.Previous?.Lines.FirstOrDefault(l => l.Category == "promoter")?.Pct;
        cells[5] = before is { } earlier ? FeatureCell.Known((double)(now - earlier)) : FeatureCell.Unknown(FeatureReasons.ShortHistory);
    }

    private static FeatureCell Standardised((double[]? Values, string? Basis, string? Reason) gathered, Func<IReadOnlyList<double>, double> sue, bool unverified) =>
        gathered.Values is { } values ? Finite(sue(values), unverified) : FeatureCell.Unknown(gathered.Reason!);

    private static FeatureCell Finite(double value, bool unverified) => double.IsNaN(value) || double.IsInfinity(value) ? FeatureCell.Unknown(FeatureReasons.Degenerate) : FeatureCell.Known(value, unverified);

    private static int[] Offsets(int count) => [.. Enumerable.Range(0, count)];

    // The EPS of a quarter on the share basis of the decision date; null when the fact is not there.
    private static double? Eps(FundamentalFeatureInput input, DateOnly today, DateOnly period, string basis) =>
        input.Facts.Get(period, ResultCodes.EpsBasic, QuarterSpan, basis) is { State: "value", Value: { } v } fact
            ? (double)v * EpsAdjustment.Factor(input.Actions, EpsAdjustment.SinceOf(fact.BroadcastTs, period), today)
            : null;

    // The value of one fact; null when the fact is not there or unavailable.
    private static double? Value(ResultFactsAnswer facts, DateOnly period, string field, string basis) =>
        facts.Get(period, field, QuarterSpan, basis) is { State: "value", Value: { } v } ? (double)v : null;

    private static DateOnly? PreviousSession(DateOnly date, FundamentalFeatureInput input)
    {
        for (var i = 1; i <= 30; i++)
        {
            var candidate = date.AddDays(-i);
            if (input.Calendar.IsTradingDay(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // One basis for all the offsets: consolidated if complete, else standalone if complete, else the reason.
    private static (T[]? Values, string? Basis, string? Reason) Gather<T>(int[] offsets, DateOnly q, Func<DateOnly, string, T?> get)
        where T : struct
    {
        foreach (var basis in ResultCodes.Bases)
        {
            var values = new T[offsets.Length];
            var complete = true;
            for (var i = 0; i < offsets.Length && complete; i++)
            {
                if (get(QuarterEnds.Back(q, offsets[i]), basis) is { } value)
                {
                    values[i] = value;
                }
                else
                {
                    complete = false;
                }
            }

            if (complete)
            {
                return (values, basis, null);
            }
        }

        var coveredTogether = offsets.All(o => ResultCodes.Bases.Any(b => get(QuarterEnds.Back(q, o), b) is not null));
        var usesBoth = ResultCodes.Bases.All(b => offsets.Any(o => get(QuarterEnds.Back(q, o), b) is not null));
        return (null, null, coveredTogether && usesBoth ? FeatureReasons.BasisMixed : FeatureReasons.ShortHistory);
    }

    private readonly record struct Pair(double Pat, double Revenue);
}
