using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// The strategy of the allocation test and of the benchmarks (criterion 12): ten conditions on a 5-minute strategy that read twenty features together, on 1m, 5m, 15m, 1h and 1D, among them the calendar and the market
/// context, and use every kind of builtin (a crossing, a run, <c>between</c>, <c>time</c>, a regime name, a lagged read of a daily bar). One frame serves all of them.
/// </summary>
internal static class TenConditions
{
    public const Timeframe Evaluation = Timeframe.M5;

    public static readonly string[] Sources =
    [
        "rsi(14) > 55 and close > ema(20)",
        "crosses_above(close, vwap)",
        "tf(\"15m\").ema(9) > tf(\"15m\").ema(21) and tf(\"1h\").rsi(14) < 60",
        "rising(close, 3) and adx(14) > 20",
        "between(rsi(14), 30, 70) and not (macd_hist() < 0)",
        "time >= \"09:45\" and time < \"15:00\" and day_of_week != FRI",
        "tf(\"1D\").close[1] < close and close > prev_day.high",
        "regime() == \"UP_LOW_VOL\" or vix < 15",
        "bb_width(20, 2) < 0.05 and atr(14) > 0 and falling(roc(5), 2)",
        "(supertrend_dir(10, 3) > 0 and close > supertrend(10, 3)) or index.trend == 1",
    ];

    /// <summary>Compiles the ten conditions and merges them.</summary>
    public static CompiledPlan Plan() =>
        ConditionSet.Merge([.. Sources.Select(s => Compiling.Condition(s, Evaluation))]);

    /// <summary>A frame for the plan over a context that covers the dates of <paramref name="bars"/>, and the ten conditions bound to it.</summary>
    public static (FeatureFrame Frame, IReadOnlyList<BoundCondition> Bound, CompiledPlan Plan) Build(IReadOnlyList<Bar> bars)
    {
        var plan = Plan();
        var context = Contexts.Covering(DateOnly.FromDateTime(bars[0].Start.UtcDateTime), DateOnly.FromDateTime(bars[^1].Start.UtcDateTime), 4, daysBefore: 120);
        var created = FeatureFrame.Create(plan.Features, new FrameOptions { HistoryDepth = plan.HistoryDepth, Context = context });
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return (created.Value, plan.BindAll(created.Value), plan);
    }
}
