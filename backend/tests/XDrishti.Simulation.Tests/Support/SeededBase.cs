using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>One invented session of a <see cref="BaseSeries"/>: its open, high, low, close (all on the 0.05 grid) and its volume (a multiple of 4).</summary>
internal sealed record DailyRow(decimal O, decimal H, decimal L, decimal C, long V);

/// <summary>
/// One invented daily series of P3-06 (D10): 80 to 160 consecutive sessions from a Monday, its parameters, the calendar the generator used (a holiday in 30 % of the series, never in the first week), the cases that were planted, and the minutes it is
/// made of. The sessions are <b>sparse</b>: four minutes each, 09:15 <c>O/O/O/O</c>, 10:00 <c>O/H/O/H</c>, 12:00 <c>H/H/L/L</c>, 15:29 <c>L/C/L/C</c>, the day's volume split evenly, so that its daily bar is exactly (O, H, L, C, V) and the minute of
/// the high comes before the minute of the low.
/// </summary>
internal sealed record BaseSeries(
    int Seed,
    int BaseLen,
    decimal BasePct,
    decimal VolX,
    decimal RewardRisk,
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<DailyRow> Rows,
    IReadOnlySet<DateOnly> Holidays,
    TradingCalendar Calendar,
    IReadOnlySet<string> Tags,
    IReadOnlySet<int> LateCloses)
{
    /// <summary>The minutes of the series. With <paramref name="withMissingClose"/> the 15:29 minute is missing on the sessions the generator chose (15 %): the daily bar of such a session closes late.</summary>
    public Bar[] Bars(bool withMissingClose = false) => SeededBase.Minutes(this, withMissingClose);

    /// <summary>The minutes of the sessions up to and including the session numbered <paramref name="sessions"/> (1 to the number of sessions).</summary>
    public Bar[] Prefix(int sessions, bool withMissingClose = false) => [.. Bars(withMissingClose).Where(b => SessionClock.DateOf(b.EpochSeconds) <= Dates[sessions - 1])];
}

/// <summary>
/// The generator of the base-breakout properties (spec P3-06, D10). Seed = the series index, SplitMix64 (<see cref="SeededSeries"/>), no <c>System.Random</c>. A rising random walk (0.3 % a day, ±1 % noise) in which, in 60 % of the series, a base of 20 sessions
/// (a tight one, or in 15 % of the plants a wide one) and a breakout session are planted (one plant, two in a quarter of them); the breakout close is a tie of the base's top in 15 % of the plants and its volume is 1.2 to 3 times the mean of the 20 sessions
/// before (20 % of them under 1.5). The session after the breakout opens above the entry limit in 20 % of the plants and through the stop in 10 %; the breakout falls on a Friday in 25 %; a strong continuation (+6 % a day) follows in 30 %.
/// </summary>
internal static class SeededBase
{
    public const decimal Tick = 0.05m;

    private const int BaseSessions = 20;

    private static readonly int[] BaseLens = [10, 15, 20];

    private static readonly decimal[] BasePcts = [5m, 8m, 12m];

    private static readonly decimal[] VolXs = [1.5m, 1.5m, 2.0m];

    private static readonly decimal[] Multiples = [1.5m, 2.0m, 3.0m];

    private static decimal Snap(double price) => Math.Max(Tick, Math.Round((decimal)price / Tick, MidpointRounding.AwayFromZero) * Tick);

    private static decimal Snap(decimal price) => Math.Max(Tick, Math.Round(price / Tick, MidpointRounding.AwayFromZero) * Tick);

    private sealed record Plant(int Breakout, bool Wide, bool Tie, bool Strong, bool Weak, bool GapAbove, bool EntryGap, bool GapThroughStop, bool Second, double VolumeFactor);

    public static BaseSeries Make(int seed)
    {
        var rng = new SeededSeries(((ulong)seed * 104_729UL) + 7UL);
        var baseLen = BaseLens[rng.Int(BaseLens.Length)];
        var basePct = BasePcts[rng.Int(BasePcts.Length)];
        var volX = VolXs[rng.Int(VolXs.Length)];
        var rewardRisk = Multiples[rng.Int(Multiples.Length)];
        var count = 80 + rng.Int(81);
        var start = new DateOnly(2026, 1, 5);
        var tags = new HashSet<string>();

        var holidays = new HashSet<DateOnly>();
        if (rng.Unit() < 0.30)
        {
            var week = 1 + rng.Int((count / 5) - 3);
            holidays.Add(start.AddDays((7 * week) + rng.Int(5)));
            tags.Add("holiday");
        }

        var dates = new List<DateOnly>();
        for (var date = start; dates.Count < count; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date))
            {
                dates.Add(date);
            }
        }

        var plants = new List<Plant>();
        if (rng.Unit() < 0.97)
        {
            var friday = rng.Unit() < 0.30;
            plants.Add(NewPlant(rng, dates, friday, 55 + BaseSessions - 20, count - 9, tags));
            if (rng.Unit() < 0.55 && plants[0].Breakout + 30 < count - 4)
            {
                plants.Add(NewPlant(rng, dates, rng.Unit() < 0.25, plants[0].Breakout + 28, count - 4, tags));
                tags.Add("two-plants");
            }
        }

        var rows = Rows(rng, baseLen, dates, plants);
        var late = new HashSet<int>();
        for (var i = 1; i < count; i++)
        {
            if (rng.Unit() < 0.15)
            {
                late.Add(i);
            }
        }

        var calendar = TestCalendars.Of(holidays, []);
        return new BaseSeries(seed, baseLen, basePct, volX, rewardRisk, dates, rows, holidays, calendar, tags, late);
    }

    private static Plant NewPlant(SeededSeries rng, List<DateOnly> dates, bool friday, int lowest, int highest, HashSet<string> tags)
    {
        var at = lowest + rng.Int(Math.Max(1, highest - lowest));
        for (var tries = 0; tries < 10 && (dates[at].DayOfWeek == DayOfWeek.Friday) != friday; tries++)
        {
            at = at + 1 < highest ? at + 1 : lowest;
        }

        var wide = rng.Unit() < 0.08;
        var tie = rng.Unit() < 0.20;
        var factor = rng.Unit() < 0.12 ? 1.2 + (rng.Unit() * 0.29) : rng.Unit() < 0.55 ? 2.05 + (rng.Unit() * 0.95) : 1.5 + (rng.Unit() * 0.5);
        var strong = rng.Unit() < 0.40;
        var weak = !strong && rng.Unit() < 0.30;
        var gapAbove = rng.Unit() < 0.25;
        var entryGap = !gapAbove && rng.Unit() < 0.20;
        var gapThrough = !gapAbove && !entryGap && rng.Unit() < 0.30;
        var plant = new Plant(at, wide, tie, strong, weak, gapAbove, entryGap, gapThrough, rng.Unit() < 0.30, factor);
        AddTags(tags, plant, dates[at].DayOfWeek == DayOfWeek.Friday);
        return plant;
    }

    private static void AddTags(HashSet<string> tags, Plant plant, bool friday)
    {
        tags.Add("plant");
        if (plant.Wide)
        {
            tags.Add("wide-base");
        }

        if (plant.Tie)
        {
            tags.Add("tie");
        }

        if (plant.VolumeFactor < 1.5)
        {
            tags.Add("weak-volume");
        }

        if (plant.GapAbove)
        {
            tags.Add("gap-above-limit");
        }

        if (plant.Second)
        {
            tags.Add("second-signal");
        }

        if (plant.EntryGap)
        {
            tags.Add("entry-through-stop");
        }

        if (plant.GapThroughStop)
        {
            tags.Add("gap-through-stop");
        }

        if (friday)
        {
            tags.Add("friday-signal");
        }
    }

    private static List<DailyRow> Rows(SeededSeries rng, int baseLen, List<DateOnly> dates, List<Plant> plants)
    {
        var rows = new List<DailyRow>();
        var price = (decimal)(20 + (rng.Unit() * 280));
        var previousClose = Snap(price);
        var volume0 = 200_000 + rng.Int(800_000);
        Plant? PlantAt(int t) => plants.FirstOrDefault(p => t >= p.Breakout - BaseSessions && t <= p.Breakout);
        Plant? AfterPlant(int t) => plants.FirstOrDefault(p => t > p.Breakout && t <= p.Breakout + 6);

        for (var t = 0; t < dates.Count; t++)
        {
            decimal close;
            var wiggle = 0.003 + ((rng.Unit() - 0.5) * 0.02);
            var reach = 0.012;
            var plant = PlantAt(t);
            var after = AfterPlant(t);
            if (plant is not null)
            {
                var anchor = rows[plant.Breakout - BaseSessions - 1].C;
                if (t < plant.Breakout)
                {
                    var amplitude = plant.Wide ? 0.07 : 0.01;
                    close = Snap((double)anchor * (1 + ((rng.Unit() - 0.5) * 2 * amplitude)));
                    reach = 0.006;
                }
                else
                {
                    var top = rows.Skip(plant.Breakout - BaseSessions).Take(BaseSessions).Max(r => r.C);
                    close = plant.Tie ? top : Snap((double)top * (1 + 0.001 + (rng.Unit() * 0.014)));
                    reach = 0.006;
                }
            }
            else
            {
                if (after is { Strong: true })
                {
                    wiggle = 0.06;
                }
                else if (after is { Weak: true })
                {
                    wiggle = -0.025;
                }
                else if (after is not null)
                {
                    wiggle = (rng.Unit() - 0.5) * 0.024;
                }

                close = Snap((double)previousClose * (1 + wiggle));
                if (after is { Second: true } && t == after.Breakout + 1)
                {
                    close = Snap((double)previousClose * 1.01);
                }
            }

            var open = Snap((double)previousClose * (1 + ((rng.Unit() - 0.5) * 0.0016)));
            if (after is not null && t == after.Breakout + 1 && after.GapAbove)
            {
                open = Snap((double)previousClose * (1.003 + (rng.Unit() * 0.047)));
            }
            else if (after is not null && ((t == after.Breakout + 1 && after.EntryGap) || (t == after.Breakout + 2 && after.GapThroughStop)))
            {
                var stop = rows.Skip(after.Breakout + 1 - baseLen).Take(baseLen).Min(r => r.L);
                open = Snap((double)stop * (0.97 + (rng.Unit() * 0.025)));
                close = Snap((double)open * (1 + ((rng.Unit() - 0.5) * 0.01)));
            }

            var high = Math.Max(Snap((double)Math.Max(open, close) * (1 + (rng.Unit() * reach))), Math.Max(open, close));
            var low = Math.Min(Snap((double)Math.Min(open, close) * (1 - (rng.Unit() * reach))), Math.Min(open, close));
            var volume = Four(volume0 * (0.8 + (0.4 * rng.Unit())));
            if (plant is not null && t == plant.Breakout)
            {
                var mean = rows.Skip(t - 20).Take(20).Average(r => (double)r.V);
                volume = Four(mean * plant.VolumeFactor);
            }

            if (after is { Second: true } && t == after.Breakout + 1)
            {
                volume = Four(rows.Skip(t - 20).Take(20).Average(r => (double)r.V) * 1.8);
            }

            rows.Add(new DailyRow(open, high, low, close, volume));
            previousClose = close;
        }

        return rows;
    }

    private static long Four(double volume) => Math.Max(4, (long)(volume / 4)) * 4;

    internal static Bar[] Minutes(BaseSeries series, bool withMissingClose)
    {
        var bars = new List<Bar>();
        for (var t = 0; t < series.Dates.Count; t++)
        {
            var row = series.Rows[t];
            var (o, h, l, c) = ((double)row.O, (double)row.H, (double)row.L, (double)row.C);
            var q = row.V / 4;
            var date = series.Dates[t];
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("09:15")), o, o, o, o, q));
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("10:00")), o, h, o, h, q));
            bars.Add(new Bar(Day.Epoch(date, Day.Minute("12:00")), h, h, l, l, q));
            if (!(withMissingClose && series.LateCloses.Contains(t)))
            {
                bars.Add(new Bar(Day.Epoch(date, Day.Minute("15:29")), l, c, l, c, q));
            }
        }

        return [.. bars];
    }
}
