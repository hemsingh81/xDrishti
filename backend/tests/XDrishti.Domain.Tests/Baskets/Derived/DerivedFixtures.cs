using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>Builders for hand-made and seeded synthetic bars. Dates are consecutive calendar days (the engine has no calendar).</summary>
internal static class DerivedFixtures
{
    public static readonly DateOnly Start = new(2025, 1, 1);
    public static readonly TimeSpan Ist = new(5, 30, 0);

    public static DerivedParams Params(params (string Key, double Value)[] overrides) =>
        DerivedParams.Create(1, overrides.ToDictionary(o => o.Key, o => o.Value)).Value;

    public static DateOnly Day(int index) => Start.AddDays(index);

    /// <summary>Daily bars from closes: high = close + half, low = close - half, constant volume.</summary>
    public static Bar[] FromCloses(IReadOnlyList<double> closes, double half = 0, double volume = 1_000_000) =>
        [.. closes.Select((c, i) => new Bar(Day(i), c, c + half, c - half, c, volume))];

    public static Bar[] Flat(int count, double close, double half = 0, double volume = 1_000_000) => FromCloses(Enumerable.Repeat(close, count).ToList(), half, volume);

    /// <summary>Closes that multiply by (1 + r) for each return in the cycle, repeated.</summary>
    public static Bar[] FromReturns(int count, double start, params double[] cycle)
    {
        var closes = new List<double> { start };
        for (var i = 1; i < count; i++)
        {
            closes.Add(closes[^1] * (1 + cycle[(i - 1) % cycle.Length]));
        }

        return FromCloses(closes, 0.001);
    }

    public static InstrumentBars Inst(string symbol, Bar[] daily, Bar[]? benchmark = null, IntradayBar[]? intraday = null, DayFlag[]? flags = null, string? industry = null) =>
        new(symbol, daily, intraday, flags, benchmark, false, industry);

    public static DateTimeOffset At(int dayIndex, int hour, int minute) => new(Day(dayIndex).ToDateTime(new TimeOnly(hour, minute)), Ist);

    /// <summary>One bar per minute from 09:15 for <paramref name="minutes"/> minutes on the day.</summary>
    public static IntradayBar[] Session(int dayIndex, int minutes, double open = 100, double high = 100.5, double low = 99.5, double volume = 100) =>
        [.. Enumerable.Range(0, minutes).Select(m => new IntradayBar(At(dayIndex, 9, 15).AddMinutes(m), open, high, low, open, volume))];

    public static T Score<T>(DerivedResult r, string symbol, Func<DerivedScore, T> pick) => pick(r.Scores.Single(s => s.Symbol == symbol));

    public static DerivedResult Eval(int id, IReadOnlyList<InstrumentBars> universe, DateOnly asOf, DerivedParams? p = null, DerivedResult? previous = null, DateTimeOffset? formation = null) =>
        DerivedCatalogue.Evaluator(id).Evaluate(universe, asOf, p ?? DerivedParams.Default(), previous, formation).Value;

    /// <summary>A seeded universe: random-walk daily bars, a benchmark, 1-minute bars for the last 22 days, flags, industries.</summary>
    public static (List<InstrumentBars> Universe, DateOnly AsOf) Synthetic(int count, int bars, int seed, bool withIntraday = true)
    {
        var rng = new Random(seed);
        var benchmark = Walk(rng, bars, 1000, 0.008);
        var universe = new List<InstrumentBars>();
        for (var i = 0; i < count; i++)
        {
            var daily = Walk(rng, bars, 50 + rng.Next(500), 0.005 + (rng.NextDouble() * 0.02), 200_000 + rng.Next(5_000_000));
            IntradayBar[]? intraday = null;
            if (withIntraday)
            {
                intraday = [.. Enumerable.Range(bars - 22, 22).SelectMany(d => Session(d, 20, daily[d].Close, daily[d].Close * 1.003, daily[d].Close * 0.997, 50 + rng.Next(200)))];
            }

            universe.Add(new InstrumentBars($"S{i:D3}", daily, intraday, [new DayFlag(Start, false, false, "EQ")], benchmark, false, $"Ind{i % 3}", 0.05));
        }

        return (universe, Day(bars - 1));
    }

    public static Bar[] Walk(Random rng, int bars, double start, double vol, double volume = 1_000_000)
    {
        var close = start;
        var result = new Bar[bars];
        for (var i = 0; i < bars; i++)
        {
            var open = close;
            close *= 1 + ((rng.NextDouble() - 0.5) * 2 * vol);
            result[i] = new Bar(Day(i), open, Math.Max(open, close) * (1 + (rng.NextDouble() * vol / 2)), Math.Min(open, close) * (1 - (rng.NextDouble() * vol / 2)), close, volume * (0.5 + rng.NextDouble()));
        }

        return result;
    }

    /// <summary>A stable text form of a result, to compare two results byte for byte.</summary>
    public static string Text(DerivedResult r) => string.Join(
        "|",
        string.Join(",", r.Members.Select(m => $"{m.Symbol}:{m.SessionsHeld}")),
        string.Join(",", r.Scores.Select(s => FormattableString.Invariant($"{s.Symbol}:{s.Score:R}:{s.Rank}"))),
        string.Join(",", r.Excluded.Select(e => $"{e.Symbol}:{e.Reason}")),
        string.Join(",", r.BelowThreshold),
        string.Join(",", r.Clusters.Select(c => $"{c.Symbol}:{c.Cluster}:{c.Name}")),
        r.Stability?.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        r.ParamsHash);
}
