using System.Globalization;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>One minute of the comparison: its India time (HH:mm), the words of spec P3-09c D3, whether it counts as a match (within the tolerance) and the largest difference in ticks (0 when a side is missing).</summary>
internal sealed record MinuteComparison(string Minute, string Words, bool Matches, int Ticks = 0)
{
    /// <summary>A match that is not exact: it differs, but within the tolerance. Counted so that the owner sees the size of the noise.</summary>
    public bool IsNoise => Matches && Ticks > 0;
}

/// <summary>The minutes of one row, and the price level offset note when the stored prices sit at a constant ratio to the second source's (adjusted history).</summary>
internal sealed record BarOutcome(IReadOnlyList<MinuteComparison> Minutes, string? OffsetNote);

/// <summary>
/// The bar comparison (spec P3-09c D3, amended in ADR 0046), pure. Volume is not compared. First the median ratio stored / second source over the cited minutes' open, high, low and close is found; when it differs from 1 by more than one tick of the median price,
/// a data note records the price level offset and the second source is scaled by the ratio before comparing. A minute whose own ratio strays from the day's by more than 0.1 % plus two ticks of price is <c>price level not constant</c> (not a match).
/// Otherwise a minute is a <c>match</c> when high and low differ by at most one tick and open and close by at most <see cref="OpenCloseTicks"/> ticks plus <see cref="OpenCloseFraction"/> of the price (the two feeds disagree on a minute's first and last trade;
/// RELIANCE showed 6 to 7 ticks, 0.02 %), <c>differs by N ticks</c> (N rounded up) otherwise, <c>missing in stored</c> or <c>missing in second source</c> when a side has no bar. The 09:15 bar names its known cause first (ISS-21). A minute neither side has is not reported.
/// </summary>
internal static class BarComparison
{
    public const string Iss21 = "ISS-21: the importer can widen the opening bar";
    public const int OpenCloseTicks = 2;
    public const decimal OpenCloseFraction = 0.0002m;
    public const decimal RatioSpread = 0.001m;
    public const decimal BandLow = 0.5m;
    public const decimal BandHigh = 1.5m;
    public const int MinPairs = 3;

    private const int IndiaOffsetSeconds = 19800;

    public static IReadOnlyList<MinuteComparison> Compare(IReadOnlyList<ReaderBar> stored, IReadOnlyList<ReaderBar> second, IReadOnlyCollection<long> epochs, decimal tick) =>
        Analyse(stored, second, epochs, tick).Minutes;

    public static BarOutcome Analyse(IReadOnlyList<ReaderBar> stored, IReadOnlyList<ReaderBar> second, IReadOnlyCollection<long> epochs, decimal tick)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(epochs);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tick, 0m);

        var storedBy = stored.ToDictionary(b => b.EpochSeconds);
        var secondBy = second.ToDictionary(b => b.EpochSeconds);
        var pairs = new List<(long Epoch, ReaderBar? A, ReaderBar? B)>();
        foreach (var epoch in epochs.Distinct().Order())
        {
            storedBy.TryGetValue(epoch, out var a);
            secondBy.TryGetValue(epoch, out var b);
            if (a is not null || b is not null)
            {
                pairs.Add((epoch, a, b));
            }
        }

        var (ratio, note) = Level(pairs, tick);
        var result = new List<MinuteComparison>();
        foreach (var (epoch, a, b) in pairs)
        {
            var label = Label(epoch);
            if (a is null)
            {
                result.Add(new MinuteComparison(label, "missing in stored", false));
            }
            else if (b is null)
            {
                result.Add(new MinuteComparison(label, "missing in second source", false));
            }
            else
            {
                result.Add(Minute(label, a, b, ratio, tick));
            }
        }

        return new BarOutcome(result, note);
    }

    private static MinuteComparison Minute(string label, ReaderBar a, ReaderBar b, decimal? ratio, decimal tick)
    {
        var scale = ratio ?? 1m;
        if (ratio is { } r && Math.Abs(MinuteRatio(a, b) - r) > RatioSpread + (2m * tick / Math.Max(b.Close, tick)))
        {
            return new MinuteComparison(label, "price level not constant (this minute is " + MinuteRatio(a, b).ToString("0.00000", CultureInfo.InvariantCulture) + " x the second source, the day " + r.ToString("0.00000", CultureInfo.InvariantCulture) + ")", false);
        }

        var open = Math.Abs(a.Open - (b.Open * scale));
        var close = Math.Abs(a.Close - (b.Close * scale));
        var high = Math.Abs(a.High - (b.High * scale));
        var low = Math.Abs(a.Low - (b.Low * scale));
        var allowed = (OpenCloseTicks * tick) + (OpenCloseFraction * a.Open);
        var ticks = (int)Math.Ceiling(new[] { open, close, high, low }.Max() / tick);
        var within = Math.Max(high, low) <= tick && Math.Max(open, close) <= allowed;
        if (within)
        {
            return new MinuteComparison(label, ticks == 0 ? "match" : "match (" + Plural(ticks) + ")", true, ticks);
        }

        var words = "differs by " + Plural(ticks);
        return new MinuteComparison(label, label == "09:15" ? Iss21 + "; " + words : words, false, ticks);
    }

    public static string Plural(int ticks) => ticks.ToString(CultureInfo.InvariantCulture) + (ticks == 1 ? " tick" : " ticks");

    private static decimal MinuteRatio(ReaderBar a, ReaderBar b) => Median(Ratios(a, b));

    private static List<decimal> Ratios(ReaderBar a, ReaderBar b)
    {
        var list = new List<decimal>();
        foreach (var (x, y) in new[] { (a.Open, b.Open), (a.High, b.High), (a.Low, b.Low), (a.Close, b.Close) })
        {
            if (y > 0m)
            {
                list.Add(x / y);
            }
        }

        return list;
    }

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return 1m;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
    }

    private static (decimal? Ratio, string? Note) Level(List<(long Epoch, ReaderBar? A, ReaderBar? B)> pairs, decimal tick)
    {
        var both = pairs.Where(p => p.A is not null && p.B is not null).ToList();
        if (both.Count == 0)
        {
            return (null, null);
        }

        if (both.Count < MinPairs)
        {
            return (null, "price level not checked: only " + both.Count.ToString(CultureInfo.InvariantCulture) + " paired minutes, at least " + MinPairs.ToString(CultureInfo.InvariantCulture) + " needed");
        }

        var ratio = Median([.. both.SelectMany(p => Ratios(p.A!, p.B!))]);
        var price = Median([.. both.Select(p => p.B!.Close)]);
        if (ratio < BandLow || ratio > BandHigh)
        {
            return (null, "price level outside the adjustment band: stored is " + ratio.ToString("0.00000", CultureInfo.InvariantCulture) + " x second source, not scaled (a wrong scale, or empty prices?)");
        }

        if (price <= 0m || Math.Abs(ratio - 1m) <= tick / price)
        {
            return (null, null);
        }

        return (ratio, "price level offset: stored is " + ratio.ToString("0.00000", CultureInfo.InvariantCulture) + " x second source (adjusted history?)");
    }

    /// <summary>The minutes that matter for a reading: the opening range, the trigger bar, the entry and the exit minute, each with the minute before and after.</summary>
    public static IReadOnlyCollection<long> Minutes(long dayOpenEpoch, ReaderStrategy strategy, ReaderReading? reading)
    {
        var epochs = new SortedSet<long>();
        for (var i = 0; i < strategy.OrMinutes; i++)
        {
            epochs.Add(dayOpenEpoch + (i * 60L));
        }

        if (reading is not null)
        {
            for (var i = -1; i <= strategy.EvaluationMinutes; i++)
            {
                epochs.Add(reading.TriggerBarStartEpoch + (i * 60L));
            }

            foreach (var centre in new[] { reading.EntryEpoch, reading.ExitEpoch })
            {
                for (var i = -1; i <= 1; i++)
                {
                    epochs.Add(centre + (i * 60L));
                }
            }
        }

        return epochs;
    }

    public static string Label(long epoch) => TimeSpan.FromSeconds(((epoch + IndiaOffsetSeconds) % 86400 + 86400) % 86400).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
