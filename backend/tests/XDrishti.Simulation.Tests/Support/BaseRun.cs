using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The runs of the worked rows of P3-06 (D11): <c>BASE_BREAKOUT</c> with <c>base_len = 10</c> (the lowest allowed) through the simulator and through the replay, in the exact or the modelled fill mode.</summary>
internal static class BaseRun
{
    public const double BaseLen = 10;

    /// <summary>T<paramref name="n"/> of the rows: the day after S, n sessions on (T1 is session 71).</summary>
    public static Day T(int n, double o, double h, double l, double c, long perMinute = Day.MinuteVolume) => BaseDays.Ohlc(70 + n, o, h, l, c, perMinute);

    /// <summary>The continuation of P1: T1 to T3, the third reaching the target.</summary>
    public static Day[] P1Tail() => [T(1, 102.05, 103.20, 101.80, 103.00), T(2, 103.00, 105.00, 102.90, 104.80), T(3, 104.80, 107.50, 104.60, 107.00)];

    /// <summary>One run of <see cref="Simulator"/> over the days (the test calendar without a holiday unless <paramref name="calendar"/> is given; <paramref name="noCalendar"/> passes none).</summary>
    public static SimulationResult Go(IEnumerable<Day> days, bool exact = true, double basePct = 8, double volX = 1.5, double baseLen = BaseLen, TradingCalendar? calendar = null, bool noCalendar = false) =>
        Sim.Run(BaseBreakoutV1.Create(baseLen, basePct, volX), BaseDays.Bars(days), Timeframe.D1, calendar: noCalendar ? null : calendar ?? Rep.DefaultCalendar, fills: exact ? Cand.NoSlippage : null);

    /// <summary>One run through <see cref="InstrumentReplayer"/> (exact fills) of the days, the window from the first session to the session numbered <paramref name="to"/> unless dates are given.</summary>
    public static InstrumentReplay Replayed(IEnumerable<Day> days, DateOnly from, DateOnly to, TradingCalendar? calendar = null)
    {
        var strategy = Rep.Prepare(BaseBreakoutV1.Create(BaseLen), Timeframe.D1, Cand.NoSlippage);
        return Rep.Replay(strategy, BaseDays.Bars(days), from, to, calendar: calendar ?? Rep.DefaultCalendar);
    }

    /// <summary>The engine's run over a generated series (its parameters, its calendar, the exact fills unless <paramref name="exact"/> is false; <paramref name="missingClose"/> drops the 15:29 minute of the sessions the generator chose).</summary>
    public static SimulationResult Of(BaseSeries series, bool exact = true, bool missingClose = false) =>
        Sim.Run(BaseBreakoutV1.Create(series.BaseLen, (double)series.BasePct, (double)series.VolX, (double)series.RewardRisk), series.Bars(missingClose), Timeframe.D1, calendar: series.Calendar, fills: exact ? Cand.NoSlippage : null);

    /// <summary>The India dates of the decisions (the date of the minute that closed the daily bar), in order.</summary>
    public static List<DateOnly> Decided(SimulationResult result) => [.. result.Outcomes.Select(o => SessionClock.DateOf(o.Candidate.DecisionEpochSeconds - 60))];
}
