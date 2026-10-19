using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The seeded series of the VWAP_PULLBACK properties (spec P3-05, D12): deterministic from the seed (<see cref="SeededSeries"/>, no <c>System.Random</c>). A series is a banded prefix of 21 sessions (up or down, 50/50, ending at a random level between 90 and 150
/// in quarter-rupees, so that the hourly and daily trend items can be true) followed by random days built bar by bar from a two-phase band-walk around the running VWAP: a <i>run phase</i> (3 to 6 bars) drifts in the direction of the prefix trend by 0.3 to 1.0
/// times the recent true range per bar (and 0.15 times that from 13:15 on, when the day goes quiet) (one bar in 24 is a shock against the trend of 1 to 2 times it, so that some entries fail), a <i>pull phase</i> (1 to 3 bars) drifts back toward the running VWAP by 0.5 to 1.5 times it, the phases alternate at random. Each bar is four quarter-rupee prices, its 15 minutes are a noisy path between its open and its
/// close that touches its high and its low, volumes are random between 500 and 1,500 per minute, and now and then an interior minute (never the first or last of a bar, never the one that holds the extreme) is absent.
/// </summary>
internal static class VwapSeeded
{
    private const int MinutesPerBar = 15;

    private const int BarsPerSession = 25;

    /// <summary>The <c>pull_atr</c> a seed is run with (0.5, 1.0, 1.5 or 2.0: all inside the declared range, and a larger one makes the dip test easier, so that more bars sit near the threshold).</summary>
    public static double PullAtr(ulong seed) => 0.5 * (3 + (int)(seed * 2_654_435_761UL % 4));

    /// <summary>The series of the seed: the prefix, then <paramref name="days"/> random days (1 to 3 when not given, from the seed).</summary>
    public static Bar[] Of(ulong seed, int? days = null)
    {
        var rng = new SeededSeries(seed * 2_654_435_761UL + 17);
        var up = rng.Int(2) == 0;
        var end = 90 + rng.Int(241) * 0.25;
        var count = days ?? 1 + rng.Int(3);
        var fixtureDate = DateOnly.FromDayNumber(Day.Monday.DayNumber);
        var dates = BandedDays.SessionsBefore(fixtureDate, 21);
        var bars = new List<Bar>();
        var ranges = new List<double>();
        double previousClose = end;
        // the prefix climbs (falls) by 0.5, 1.0 or 1.5 rupees a session and its last sessions turn back, as many as it takes (and with a fractional last step) for the hourly trend item (EMA 20 against EMA 50, seven hourly bars a session) to start
        // the first day near its tipping point, a margin between -0.15 and +0.30 steps in the direction of the trend: a rule text read wrongly then changes candidates, which the mutant controls of the reference loop need
        var slope = 0.5 * (1 + rng.Int(3));
        var target = -0.15 + 0.45 * rng.Unit();

        // the last 15 minutes of every prefix session sit away from the rest of it (the partial hourly bar, D2), so that the hourly bucket rule is what the trend item depends on (in the direction of the trend)
        var stub = 5.0 * (rng.Int(5) - 2);
        var margin = target * slope / 0.5 - stub * StubMargin.Value;
        var pick = Turns.Value.MinBy(t => Math.Abs(t.Margin * slope / 0.5 - margin));
        var levels = Levels(dates.Length, pick.Turn, pick.Fraction, up, end, slope);
        stub = up ? stub : -stub;

        for (var i = 0; i < dates.Length; i++)
        {
            bars.AddRange(BandedDays.Session(dates[i], levels[i], stub).Bars());
            ranges.Add(1.0);
            previousClose = levels[i];
        }

        var date = fixtureDate;
        for (var d = 0; d < count; d++)
        {
            previousClose = AddDay(rng, date, up, previousClose, bars, ranges);
            date = date.DayOfWeek == DayOfWeek.Friday ? date.AddDays(3) : date.AddDays(1);
        }

        return [.. bars];
    }

    /// <summary>The turn-back shapes of the prefix with the hourly margin each gives at the start of the first random day, in rupees for a climb of 0.5 a session (the margin is linear in the slope and does not depend on the level).</summary>
    private static readonly Lazy<(int Turn, double Fraction, double Margin)[]> Turns = new(() =>
    [
        .. from t in Enumerable.Range(0, 7)
           from f in new[] { 0.0, 0.25, 0.5, 0.75 }
           let dates = BandedDays.SessionsBefore(Day.Monday, 21)
           let levels = Levels(dates.Length, t, f, true, 100, 0.5)
           let trend = ReferenceLoop.TrendAtEnd(Day.Join([.. dates.Select((d, i) => BandedDays.Session(d, levels[i]))]))
           select (t, f, (double)(trend.HourlyFast - trend.HourlySlow)),
    ]);

    /// <summary>The hourly margin one rupee of stub offset adds on a flat prefix.</summary>
    private static readonly Lazy<double> StubMargin = new(() =>
    {
        var dates = BandedDays.SessionsBefore(Day.Monday, 21);
        var trend = ReferenceLoop.TrendAtEnd(Day.Join([.. dates.Select(d => BandedDays.Session(d, 100, 1))]));
        return (double)(trend.HourlyFast - trend.HourlySlow);
    });

    /// <summary>The index in the list where the first random day starts (after the 21 banded sessions of 375 minutes).</summary>
    public const int PrefixMinutes = 21 * 375;

    private static double[] Levels(int sessions, int turn, double fraction, bool up, double end, double slope)
    {
        var levels = new double[sessions];
        var level = end;
        for (var i = sessions - 1; i >= 0; i--)
        {
            levels[i] = level;
            var back = sessions - 1 - i;
            var step = back < turn ? -slope : back == turn ? slope * (2 * fraction - 1) : slope;
            level += up ? -step : step;
        }

        return levels;
    }

    private static double AddDay(SeededSeries rng, DateOnly date, bool up, double open, List<Bar> bars, List<double> ranges)
    {
        var direction = up ? 1.0 : -1.0;
        var price = open;
        double turnover = 0;
        double volume = 0;
        var running = true;
        var left = 3 + rng.Int(4);
        for (var k = 0; k < BarsPerSession; k++)
        {
            if (left == 0)
            {
                running = !running;
                left = running ? 3 + rng.Int(4) : 1 + rng.Int(3);
            }

            left--;
            var scale = Math.Max(0.5, ranges.Skip(Math.Max(0, ranges.Count - 14)).Average());
            var vwap = volume > 0 ? turnover / volume : price;
            var drift = running
                ? direction * (rng.Int(24) == 0 ? -(1.0 + rng.Unit()) : 0.3 + 0.7 * rng.Unit()) * scale
                : Math.Sign(vwap - price) * (0.5 + rng.Unit()) * scale;
            drift *= k < 16 ? 1.0 : 0.15;
            var barOpen = price;
            var barClose = Quarter(Math.Max(5, barOpen + drift + (rng.Unit() - 0.5) * 0.6 * scale));
            var high = Quarter(Math.Max(barOpen, barClose) + rng.Unit() * 0.5 * scale);
            var low = Quarter(Math.Max(1, Math.Min(barOpen, barClose) - rng.Unit() * 0.5 * scale));
            var minutes = Minutes(rng, date, 9 * 60 + 15 + k * MinutesPerBar, barOpen, high, low, barClose);
            bars.AddRange(minutes);

            var barVolume = minutes.Sum(m => (double)m.Volume);
            turnover += (high + low + barClose) / 3 * barVolume;
            volume += barVolume;
            var trueRange = Math.Max(high - low, Math.Max(Math.Abs(high - open), Math.Abs(low - open)));
            ranges.Add(trueRange);
            open = barClose;
            price = barClose;
        }

        return price;
    }

    private static Bar[] Minutes(SeededSeries rng, DateOnly date, int start, double open, double high, double low, double close)
    {
        var highMinute = rng.Int(MinutesPerBar);
        var lowMinute = rng.Int(MinutesPerBar);
        if (lowMinute == highMinute)
        {
            lowMinute = (lowMinute + 1 + rng.Int(MinutesPerBar - 1)) % MinutesPerBar;
        }

        var list = new List<Bar>();
        var previous = open;
        for (var i = 0; i < MinutesPerBar; i++)
        {
            var target = i == MinutesPerBar - 1 ? close : Math.Clamp(Quarter(open + (close - open) * (i + 1) / MinutesPerBar + (rng.Unit() - 0.5) * (high - low) * 0.5), low, high);
            var minuteHigh = Math.Max(previous, target);
            var minuteLow = Math.Min(previous, target);
            if (rng.Int(3) == 0)
            {
                minuteHigh = Math.Min(high, minuteHigh + Quarter(rng.Unit() * 0.5 * (high - low)));
            }

            if (rng.Int(3) == 0)
            {
                minuteLow = Math.Max(low, minuteLow - Quarter(rng.Unit() * 0.5 * (high - low)));
            }

            if (i == highMinute)
            {
                minuteHigh = high;
            }

            if (i == lowMinute)
            {
                minuteLow = low;
            }

            var absent = i is > 0 and < MinutesPerBar - 1 && i != highMinute && i != lowMinute && rng.Int(300) == 0;
            if (!absent)
            {
                list.Add(new Bar(Day.Epoch(date, start + i), previous, minuteHigh, minuteLow, target, 500 + rng.Int(1001)));
            }

            previous = target;
        }

        return [.. list];
    }

    private static double Quarter(double value) => Math.Round(value * 4, MidpointRounding.AwayFromZero) / 4;
}
