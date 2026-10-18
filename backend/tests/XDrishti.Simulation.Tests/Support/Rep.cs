using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>Prepares a strategy and replays an instrument in one line: the helpers every replay test uses. The fills are the exact ones of P3-03's tables (the 0.1 % limit, no slippage) and the tick is 0.05 unless a test says otherwise.</summary>
internal static class Rep
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-0000000000a1");

    public static readonly TradingCalendar DefaultCalendar = new();

    /// <summary>The test-only strategy of criterion 1: the ORB draft whose BUY side has the extra setup <c>close &gt; ema(10)</c>.</summary>
    public static StrategyDefinition OrbWithEma10() => TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("close > ema(10)") });

    /// <summary>The prepared strategy; the test fails with the located problems when there is none.</summary>
    public static PreparedStrategy Prepare(StrategyDefinition definition, Timeframe timeframe = Timeframe.M15, FillRules? fills = null, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var build = StrategyPreparation.Prepare(definition, parameters ?? new Dictionary<string, double>(), timeframe, fills ?? Cand.NoSlippage);
        build.Strategy.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        return build.Strategy;
    }

    public static InstrumentReplayInput Input(
        IReadOnlyList<Bar> bars,
        DateOnly from,
        DateOnly to,
        Guid? id = null,
        string symbol = "TEST",
        AdjustmentFactors? factors = null,
        DateOnly? csvAdjustedThrough = null,
        TradingCalendar? calendar = null,
        ReplayOptions? options = null,
        TickSpec? tick = null,
        SkipReason? preset = null,
        string? detail = null) =>
        new(id ?? Id, symbol, bars, factors ?? AdjustmentFactors.None, csvAdjustedThrough, calendar ?? DefaultCalendar, options ?? ReplayOptions.Default, from, to, preset == SkipReason.TickSizeInvalid ? null : tick ?? Sim.Tick, preset, detail);

    public static InstrumentReplay Replay(
        PreparedStrategy strategy,
        IReadOnlyList<Bar> bars,
        DateOnly from,
        DateOnly to,
        AdjustmentFactors? factors = null,
        DateOnly? csvAdjustedThrough = null,
        TradingCalendar? calendar = null,
        ReplayOptions? options = null,
        TickSpec? tick = null,
        SkipReason? preset = null,
        Guid? id = null,
        string symbol = "TEST")
    {
        var replay = InstrumentReplayer.Replay(strategy, Input(bars, from, to, id, symbol, factors, csvAdjustedThrough, calendar, options, tick, preset));
        ReplayIdentities.ShouldHold(replay);   // the identities of criterion 12 hold on every replay of every fixture
        return replay;
    }

    /// <summary>An applied split <c>numerator:denominator</c> with the ex-date.</summary>
    public static AdjustmentFactors Split(DateOnly exDate, int numerator, int denominator) =>
        AdjustmentFactors.From([new AppliedAction(exDate, CorporateActionKind.Split, numerator, denominator, CorporateActionRatio.Factor(CorporateActionKind.Split, numerator, denominator))]);

    /// <summary>A calendar with one confirmed special session.</summary>
    public static TradingCalendar WithSpecial(DateOnly date, int fromMinute, int toMinute) =>
        new([TradingDay.Special(date, fromMinute, toMinute, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

    /// <summary>A calendar with one confirmed holiday.</summary>
    public static TradingCalendar WithHoliday(DateOnly date) =>
        new([TradingDay.Holiday(date, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
}
