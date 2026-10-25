using System.Collections.Frozen;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// Runs the compiler once over a fixed, representative set of conditions, so that the .NET JIT has compiled the parser, the binder, the lint and the expression compiler before a person asks for the first compile
/// (ISS-12, ADR 0034 amendment 2). The first compile of a process takes about 40 ms because the JIT turns about a thousand small methods into machine code on the way (no setting of the runtime changes that); in a
/// process that has run the warm-up, the first compile of ten conditions takes 3 to 5 ms. A host calls <see cref="Run"/> once, on a thread-pool thread, right after it starts, and does not wait for it: nothing depends on
/// it, and it costs about 60 ms of one core, once per process (measured 60 to 63 ms on a quiet Apple M4 Pro, 76 to 90 ms on a loaded one). A compile that starts while it is still running waits for the part of the JIT it has not done yet, which is
/// no worse than not having it.
/// <para>
/// What it does: compiles every source of <see cref="Valid"/>, merges them into a plan, makes a frame for the plan and binds every condition to it (which builds the delegates), then compiles every source of
/// <see cref="Invalid"/> for the paths that build the problems. It uses only the public pipeline and returns nothing; the results are dropped. Pure and thread-safe, like the compiler: no clock, no I/O, no state kept between calls (a
/// second call does the work again). It does not catch an exception: a source that stopped compiling is a defect that the tests of this library find, and the host that calls it logs and ignores any failure.
/// </para>
/// <para>
/// Since P3-02b it also compiles the strategy definitions of <see cref="WarmupStrategies"/> (valid ones on both sides, then invalid ones) through <see cref="StrategyCompiler.Compile"/>, and binds the sides that compiled to
/// a frame and evaluates them once, so that the model checks, the stop and exit compile, the side compile and the list-level evaluation are compiled to machine code too (about 30 ms of one core more: 60 to 63 ms became 92 to 95 ms, ADR 0036).
/// </para>
/// <para>
/// The sets are only as good as their coverage: a language element the sets never use is compiled by the first real request, and that costs (a set without the time of day, the timeframes, the market context, most of the catalog and most error paths made the first request take 16 ms instead of 4). So the unit
/// tests of the library prove that the sets use every builtin, operator, bound node, catalog feature, timeframe and kind of problem, and a new element of the language fails them until it is added here.
/// </para>
/// </summary>
public static class CompilerWarmup
{
    /// <summary>The strategy parameters the sources use (every case is compiled with them).</summary>
    internal static readonly FrozenDictionary<string, double> Parameters = new Dictionary<string, double>
    {
        ["rsi_len"] = 14,
        ["level"] = 55,
        ["fast"] = 9,
        ["slow"] = 21,
        ["steps"] = 3,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The conditions that compile: every builtin, operator, kind of node, catalog feature, timeframe, subscript, parameter and warning of the language, in conditions of the shapes a strategy has. Written out by hand
    /// and kept apart from the sources of the benchmarks, so that the benchmark measures a compile of conditions the warm-up has never seen.
    /// </summary>
    internal static readonly IReadOnlyList<Case> Valid =
    [
        // comparisons, logical and arithmetic operators, a call with arguments and a price source
        new("rsi(14) > 55 and close >= ema(20, source=high) and volume > 0"),
        new("close < open or high <= low or not (close == open) or close != vwap"),
        new("(close - open) * 2 / atr(14) + 1 > -atr(14)"),

        // the builtins
        new("crosses_above(close, vwap) or crosses_below(close, ema(20))"),
        new("rising(close, 3) and falling(rsi(14), 2) and adx(14) > 20"),
        new("between(rsi(14), 30, 70) and abs(close - open) < min(atr(14), 5) and max(high, close) > 0"),

        // the time of day and the days of the week
        new("time > \"09:30\" and time >= \"09:45\" and \"15:00\" > time and time <= \"15:15\" and time != \"12:00\" and time == \"10:00\""),
        new("day_of_week == MON or day_of_week == TUE or day_of_week == WED or day_of_week == THU or day_of_week == FRI or day_of_week == SAT or day_of_week == SUN"),

        // lagged reads and the nine timeframes
        new("close[2] > open[3] and tf(\"1D\").close[1] < close"),
        new("close > highest(20)[1] and rsi(14)[1] < 70 and crosses_above(close, lowest(5)[2]) and tf(\"1h\").ema(20)[1] > atr(14)[3]"),
        new("tf(\"1m\").close > tf(\"10m\").close and tf(\"15m\").ema(9) > tf(\"30m\").ema(21) and tf(\"1h\").rsi(14) < 60"),
        new("tf(\"1D\").sma(20) > tf(\"1W\").close and tf(\"1W\").ema(10) > tf(\"1M\").close"),

        // the regime, the market context, the calendar and the previous day
        new("regime() == \"UP_LOW_VOL\" or regime() != 3 or vix < 15 and vix_pct > 50 and vix_chg < 1"),
        new("index.trend == 1 and index.adx > 20 and index_vol_pct < 50 and breadth > 50 and regime_trend > 0 and regime_vol > 0"),
        new("is_expiry_day == 1 and gap_pct > 0.5 and today.open < today.high and today.low > 0"),
        new("prev_day.open > 0 and prev_day.high > prev_day.low and prev_day.close > 0 and prev_day.volume > 0"),

        // the rest of the catalog
        new("sma(20) > ema(50) and supertrend_dir(10, 3) > 0 and close > supertrend(10, 3) and plus_di(14) > minus_di(14)"),
        new("macd_line() > macd_signal() and macd_hist() > 0 and roc(5) > 0"),
        new("bb_lower(20, 2) < bb_middle(20, 2) and bb_middle(20, 2) < bb_upper(20, 2) and bb_width(20, 2) < 0.05"),
        new("highest(10) > lowest(10) and close > or_high(15) and close < or_low(15) and rel_volume(20) > 1.5"),
        new("inside_bar > 0 and nr() > 0 and engulfing > 0"),
        new("pivot_high() > 0 and pivot_low() > 0 and pivot_high_age() < 5 and pivot_low_age() < 5"),
        new("open > 0 and high > 0 and low > 0 and close > 0 and volume > 0"),

        // strategy parameters
        new("rsi(rsi_len) > level and ema(fast) > ema(slow) and rising(close, steps)"),

        // the warnings: a condition that is the same on every bar, a timeframe that is the strategy's own, and a read of a per-session feature from further back than a session holds (75 bars of 5m are 375 minutes)
        new("1 < 2"),
        new("close > open and true"),
        new("tf(\"5m\").close > 0"),
        new("rising(vwap, 75)"),
        new("close > highest(20) and rsi(14) > 100"),

        // a daily strategy
        new("close > sma(200) and rsi(14) < 70", Timeframe.D1),
    ];

    /// <summary>The conditions that do not compile: one of each kind of mistake the parser, the binder and the lint report, so that the paths that build their problems are compiled too.</summary>
    internal static readonly IReadOnlyList<Case> Invalid =
    [
        // the lexer
        new(string.Empty),
        new("$ > 3"),
        new("a ＞ b"),
        new("55abc"),
        new("1e400"),
        new("close > \"abc"),

        // the grammar and its recovery
        new("rsi(14 >"),
        new("rsi(14))"),
        new("a + * b"),
        new("a b"),
        new("a < b < c"),
        new("rsi(14,)"),
        new("rsi(,14)"),
        new("a = b"),
        new("(close)[1]"),
        new("x[a]"),
        new("tf(\"1h\")"),

        // names, arguments, timeframes and lags
        new("rsii(14) > 55"),
        new("rsi(14, 3, 4) > 55"),
        new("rsi > 55"),
        new("tf(\"2m\").close > 1"),
        new("tf(\"1D\").vwap > 0"),
        new("close[0] > 1"),
        new("close[300] > 1"),
        new("MON[1] > 1"),
        new("crosses_above(close, tf(\"1h\").close)"),

        // time, regime and types
        new("time >= \"9:45\""),
        new("time >= \"09:45\"", Timeframe.D1),
        new("regime() == \"UP_LOW_VOLL\""),
        new("close and rsi(14)"),
        new("close"),
    ];

    /// <summary>Compiles the sets once, so that the next compile in the process finds the compiler already compiled to machine code. See the remarks of the class.</summary>
    public static void Run() => _ = Warm();

    /// <summary>The work of <see cref="Run"/>, with a count of what it did, so that a test can prove that every valid source was compiled and bound and every invalid one refused.</summary>
    internal static Outcome Warm()
    {
        var compiled = new List<CompiledCondition>(Valid.Count);
        foreach (var item in Valid)
        {
            if (ConditionCompiler.Compile(item.Source, item.Context).Condition is { } condition)
            {
                compiled.Add(condition);
            }
        }

        // what a host does with a strategy: merge the conditions, make the frame for the plan and bind them to it, which builds their delegates; the frame has no data, the compiler does not need any
        var plan = ConditionSet.Merge(compiled);
        var bound = 0;
        var context = MarketContext.Create([]);
        if (context.IsSuccess)
        {
            var options = new FrameOptions { HistoryDepth = plan.HistoryDepth, Context = context.Value, ExpiryDays = new HashSet<DateOnly>() };
            var frame = FeatureFrame.Create(plan.Features, options);
            if (frame.IsSuccess)
            {
                bound = plan.BindAll(frame.Value).Count;
            }
        }

        var refused = 0;
        foreach (var item in Invalid)
        {
            if (!ConditionCompiler.Compile(item.Source, item.Context).IsSuccess)
            {
                refused++;
            }
        }

        var (strategiesCompiled, strategiesBound, strategiesRefused) = WarmStrategies();
        return new Outcome(compiled.Count, bound, refused, strategiesCompiled, strategiesBound, strategiesRefused);
    }

    /// <summary>What a host does with a strategy: compile the side, make the frame from its features, bind it and evaluate it once; then the definitions that are refused.</summary>
    private static (int Compiled, int Bound, int Refused) WarmStrategies()
    {
        var compiled = 0;
        var bound = 0;
        var context = MarketContext.Create([]);
        foreach (var item in WarmupStrategies.Valid)
        {
            if (StrategyCompiler.Compile(item.Definition, item.Timeframe, item.Side).Compiled is not { } side)
            {
                continue;
            }

            compiled++;
            if (!context.IsSuccess)
            {
                continue;
            }

            var options = new FrameOptions { HistoryDepth = side.HistoryDepth, Context = context.Value, ExpiryDays = new HashSet<DateOnly>() };
            var frame = FeatureFrame.Create(side.Features, options);
            if (frame.IsSuccess)
            {
                var sideBound = side.Bind(frame.Value);
                var truths = new Truth[side.Items.Count];
                _ = sideBound.Evaluate(frame.Value, truths);
                _ = sideBound.InEntryWindow(frame.Value);
                bound++;
            }
        }

        var refused = 0;
        foreach (var item in WarmupStrategies.Invalid)
        {
            if (!StrategyCompiler.Compile(item.Definition, item.Timeframe, item.Side).IsSuccess)
            {
                refused++;
            }
        }

        return (compiled, bound, refused);
    }

    /// <summary>One condition of a set: the text, and the timeframe of the strategy it is compiled for (5m, the one most strategies run on, unless the case is about another).</summary>
    internal readonly record struct Case(string Source, Timeframe Timeframe = Timeframe.M5)
    {
        /// <summary>The context the source is compiled for.</summary>
        public CompileContext Context => new(Timeframe, Parameters);
    }

    /// <summary>
    /// What a pass did: the conditions of <see cref="Valid"/> that compiled, how many of them were bound to a frame (which builds the delegate), and the conditions of <see cref="Invalid"/> that were refused, as they should be;
    /// and the same three counts for the strategy definitions of <see cref="WarmupStrategies"/>: the valid ones that compiled, the sides bound to a frame and evaluated, and the invalid ones that were refused.
    /// </summary>
    internal readonly record struct Outcome(int Compiled, int Bound, int Refused, int StrategiesCompiled, int StrategiesBound, int StrategiesRefused);
}
