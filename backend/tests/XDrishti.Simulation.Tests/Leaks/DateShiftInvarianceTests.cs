using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// Date-shift invariance through <c>Simulator.Run</c> (spec P3-08b, Annex B (b)). The same series moved forward by 1, 5 and 52 whole weeks (the same weekdays; 52 weeks crosses a year end and the calendar year) and the calendar of exceptions moved with it
/// gives the same candidates, trades, ratios and counters, with the dates and epochs of the shifted run equal to the base run's plus the shift. <c>SwingShiftInvarianceTests</c> inserts a night into a walk and does not move a date; this does. Not claimed: a shift
/// by days that changes the weekday (the default week and the weekly bar of <c>BASE_BREAKOUT</c> depend on it by design). The negative control is a calendar anchored to the old dates: the shifted bars are run with the calendar left where it was, so
/// a holiday no longer lies on its night.
/// </summary>
public sealed class DateShiftInvarianceTests(ITestOutputHelper output)
{
    private static readonly int[] Weeks = [1, 5, 52];

    private sealed record Subject(string Name, int Seed, Bar[] Bars, Simulator Simulator, decimal Tick, TradingCalendar? Calendar);

    private static readonly Lazy<IReadOnlyList<Subject>> All = new(Build);

    private static List<Subject> Build()
    {
        var subjects = new List<Subject>();
        var orb = Sim.Create(OrbV1.Create(), Timeframe.M15, FillRules.Default);
        var swing = Sim.Create(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default);
        var daily = Sim.Create(TestStrategies.SwingDaily(DailyFixture.Trigger), Timeframe.D1, FillRules.Default);
        foreach (var seed in Enumerable.Range(1, 200))
        {
            subjects.Add(new Subject("ORB", seed, SeededSeries.Of((ulong)seed), orb, 0.05m, Rep.DefaultCalendar));
        }

        foreach (var seed in Enumerable.Range(1, 60))
        {
            subjects.Add(new Subject("SWING_1H", seed, SeededSeries.Of((ulong)seed), swing, 0.05m, Rep.DefaultCalendar));
        }

        foreach (var series in Enumerable.Range(1, 200).Select(SeededDaily.Make))
        {
            subjects.Add(new Subject("SWING_DAILY", series.Seed, [.. series.Bars], daily, series.Tick, series.Truth));
        }

        foreach (var series in Enumerable.Range(1, 30).Select(SeededBase.Make))
        {
            var simulator = Sim.Create(BaseBreakoutV1.Create(series.BaseLen, (double)series.BasePct, (double)series.VolX, (double)series.RewardRisk), Timeframe.D1, Cand.NoSlippage);
            subjects.Add(new Subject("BASE_BREAKOUT", series.Seed, series.Bars(), simulator, SeededBase.Tick, series.Calendar));
        }

        return subjects;
    }

    private static string Base(Subject s) => Leak.Digest(Sim.Run(s.Simulator, s.Bars, tick: Leak.Tick(s.Tick), calendar: s.Calendar));

    private static string Shifted(Subject s, int weeks, bool moveCalendar)
    {
        var bars = Leak.Shift(s.Bars, weeks);
        var calendar = moveCalendar && s.Calendar is { } c ? Leak.ShiftCalendar(c, s.Bars, weeks) : s.Calendar;
        return Leak.Digest(Sim.Run(s.Simulator, bars, tick: Leak.Tick(s.Tick), calendar: calendar), shiftWeeks: weeks);
    }

    [Fact]
    public void Moving_the_series_and_its_calendar_by_whole_weeks_changes_nothing_but_the_dates()
    {
        var mismatches = new List<string>();
        var trades = new SortedDictionary<string, int>();
        var overHoliday = 0;
        foreach (var s in All.Value)
        {
            var run = Sim.Run(s.Simulator, s.Bars, tick: Leak.Tick(s.Tick), calendar: s.Calendar);
            var expected = Leak.Digest(run);
            trades[s.Name] = trades.GetValueOrDefault(s.Name) + run.Outcomes.Count(o => o.Walk.Trade is not null);
            overHoliday += s.Name == "SWING_DAILY" && run.Outcomes.Any(o => o.Walk.Trade is { SessionsUnseen: > 0 } || (o.Walk.Trade is not null && s.Calendar is { } c && HasHolidayBetween(c, o.Walk.Trade.TradeDate, SessionClock.DateOf(o.Walk.Trade.ExitEpochSeconds)))) ? 1 : 0;
            foreach (var weeks in Weeks)
            {
                if (Shifted(s, weeks, moveCalendar: true) != expected)
                {
                    mismatches.Add($"{s.Name} seed {s.Seed} +{weeks} weeks");
                }
            }
        }

        output.WriteLine($"{All.Value.Count} series x {Weeks.Length} shifts = {All.Value.Count * Weeks.Length} shifted runs, mismatches {mismatches.Count}; trades " + string.Join(", ", trades.Select(t => t.Key + " " + t.Value)) + $"; daily series with a position over a holiday: {overHoliday}");
        mismatches.ShouldBeEmpty();
        trades["ORB"].ShouldBeGreaterThanOrEqualTo(150);
        trades["SWING_DAILY"].ShouldBeGreaterThanOrEqualTo(100);
        overHoliday.ShouldBeGreaterThanOrEqualTo(10);
    }

    private static bool HasHolidayBetween(TradingCalendar calendar, DateOnly from, DateOnly to)
    {
        for (var d = from.AddDays(1); d < to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && calendar.SessionOf(d) is null)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Control_a_calendar_left_at_the_old_dates_is_reported_on_at_least_ten_seeds_while_the_honest_shift_reports_none()
    {
        var daily = All.Value.Where(s => s.Name == "SWING_DAILY").ToList();
        var expected = daily.Select(Base).ToList();

        var reported = new Dictionary<int, int>();
        foreach (var weeks in Weeks)
        {
            reported[weeks] = daily.Where((s, i) => Shifted(s, weeks, moveCalendar: false) != expected[i]).Count();
        }

        var honest = daily.Where((s, i) => Shifted(s, 5, moveCalendar: true) != expected[i]).Count();
        output.WriteLine("calendar-anchored control on SWING_DAILY (" + daily.Count + " seeds): " + string.Join(", ", reported.Select(r => $"+{r.Key} weeks {r.Value}")) + $"; honest +5 weeks {honest}");
        honest.ShouldBe(0);
        reported.Values.ShouldAllBe(v => v >= 10);
    }
}
