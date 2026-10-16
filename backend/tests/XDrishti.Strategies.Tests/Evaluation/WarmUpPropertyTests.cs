using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 8, second half: <b>no decision is ever true while an operand of its deciding comparison is not ready</b>, over the warm-up of every catalog feature on every timeframe it can be asked for. For each of the 56
/// names and each of the nine evaluation timeframes a frame is run over seeded sessions long enough for the feature to become ready (a short form is used: <c>rsi(2)</c>, <c>macd_line(2, 3, 2)</c>), and after every 1-minute step
/// about thirty conditions built from the feature are evaluated, among them <c>not (F &gt; x)</c> and <c>F &lt; x or close &gt; 0</c>. What each must be is worked out from the frame's own readiness (<c>IsReady</c>,
/// <c>IsReadyAt</c>, <c>At</c>), and by Kleene's tables: where the deciding operand is not ready the result is unknown (so the decision is false, and so is its negation), except where the other side decides (<c>true or
/// unknown</c> is true, <c>false and unknown</c> is false). The series are checked to contain both states, so the property is not met by never being ready (or always being ready).
/// </summary>
public sealed class WarmUpPropertyTests
{
    /// <summary>The 56 names with small arguments so that they are ready within a few bars of their own timeframe (the others take none).</summary>
    private static readonly Dictionary<string, string> ShortForms = new Dictionary<string, string>(AllFeatureForms.Forms)
    {
        ["sma"] = "sma(2)",
        ["ema"] = "ema(2)",
        ["rsi"] = "rsi(2)",
        ["atr"] = "atr(2)",
        ["highest"] = "highest(2)",
        ["lowest"] = "lowest(2)",
        ["rel_volume"] = "rel_volume(2)",
        ["or_high"] = "or_high(60)",
        ["or_low"] = "or_low(60)",
        ["adx"] = "adx(2)",
        ["plus_di"] = "plus_di(2)",
        ["minus_di"] = "minus_di(2)",
        ["macd_line"] = "macd_line(2, 3, 2)",
        ["macd_signal"] = "macd_signal(2, 3, 2)",
        ["macd_hist"] = "macd_hist(2, 3, 2)",
        ["bb_upper"] = "bb_upper(2, 2)",
        ["bb_middle"] = "bb_middle(2, 2)",
        ["bb_lower"] = "bb_lower(2, 2)",
        ["bb_width"] = "bb_width(2, 2)",
        ["supertrend"] = "supertrend(2, 2)",
        ["supertrend_dir"] = "supertrend_dir(2, 2)",
        ["roc"] = "roc(2)",
        ["nr"] = "nr(2)",
        ["pivot_high"] = "pivot_high(1)",
        ["pivot_low"] = "pivot_low(1)",
        ["pivot_high_age"] = "pivot_high_age(1)",
        ["pivot_low_age"] = "pivot_low_age(1)",
    };

    /// <summary>
    /// The bars of a case. The intraday timeframes run over four full sessions (every minute: the buckets are whole). The features of the daily, weekly and monthly timeframes and the date-driven ones look at the day, not the
    /// minute, so they run over sparse sessions of four bars each (cheap to run for months): 30 sessions for a day, 80 for a week (16 weeks), 400 for a month (19 months), which is enough for the short forms to be ready and
    /// to have their two previous values; and 40 sessions for a date-driven feature (the market context of the test is ready after about two weeks).
    /// </summary>
    private static Bar[] BarsFor(Timeframe timeframe, bool dateDriven, int seed) => dateDriven
        ? Series.Sparse(seed, 40)
        : timeframe switch
        {
            Timeframe.D1 => Series.Sparse(seed, 30),
            Timeframe.W1 => Series.Sparse(seed, 80),
            Timeframe.Mo1 => Series.Sparse(seed, 400),
            _ => Series.Sessions(seed, 4),
        };

    /// <summary>A condition and what it must be, from the readiness and the values of the feature at the lags 0, 1 and 2.</summary>
    private sealed record Check(string Source, Func<Probe, Truth> Expected);

    /// <summary>The state of the feature after a step, read from the frame by its own readiness and values (nothing of the compiled code).</summary>
    private readonly record struct Probe(bool R0, bool R1, bool R2, double V0, double V1, double V2);

    public static TheoryData<string, Timeframe> Cases { get; } = Build();

    private static TheoryData<string, Timeframe> Build()
    {
        var data = new TheoryData<string, Timeframe>();
        foreach (var info in IndicatorCatalog.All)
        {
            foreach (var timeframe in Timeframes.All)
            {
                if (info.Supports(info.DateDriven ? Timeframe.M1 : timeframe))
                {
                    data.Add(info.Name, timeframe);
                }
            }
        }

        return data;
    }

    private static Truth Of(bool holds) => holds ? Truth.True : Truth.False;

    private static List<Check> ChecksFor(string name, string form)
    {
        const string KnownTrue = "tf(\"1m\").close > 0";
        const string KnownFalse = "tf(\"1m\").close < 0";

        // P is true when the feature is ready and unknown when it is not; N is false when it is ready and unknown when it is not
        var (p, n) = name == "regime" ? ("(regime() == 1 or regime() != 1)", "(regime() == 1 and regime() != 1)") : ($"({form} > -1e18)", $"({form} < -1e18)");
        List<Check> checks =
        [
            new(p, s => s.R0 ? Truth.True : Truth.Unknown),
            new($"not {p}", s => s.R0 ? Truth.False : Truth.Unknown),
            new(n, s => s.R0 ? Truth.False : Truth.Unknown),
            new($"not {n}", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"{p} and {KnownTrue}", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"{p} and {KnownFalse}", _ => Truth.False),
            new($"{p} or {KnownTrue}", _ => Truth.True),
            new($"{p} or {KnownFalse}", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"{n} or {KnownTrue}", _ => Truth.True),
            new($"{n} or {KnownFalse}", s => s.R0 ? Truth.False : Truth.Unknown),
            new($"not ({p} and {KnownFalse})", _ => Truth.True),
            new($"not ({n} or {KnownTrue})", _ => Truth.False),
            new($"not {p} or {KnownFalse}", s => s.R0 ? Truth.False : Truth.Unknown),
            new($"not {p} and {KnownTrue}", s => s.R0 ? Truth.False : Truth.Unknown),
            new($"{p} == ({KnownTrue})", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"{p} != ({KnownTrue})", s => s.R0 ? Truth.False : Truth.Unknown),
        ];

        if (name == "regime")
        {
            checks.Add(new("regime() == 1", s => s.R0 ? Of(s.V0 == 1) : Truth.Unknown));
            checks.Add(new("not (regime() == 1)", s => s.R0 ? Of(s.V0 != 1) : Truth.Unknown));
            checks.Add(new("regime() != 1", s => s.R0 ? Of(s.V0 != 1) : Truth.Unknown));
            return checks;
        }

        checks.AddRange(
        [
            new($"crosses_above({form}, -1e18)", s => s.R0 && s.R1 ? Truth.False : Truth.Unknown),
            new($"crosses_below({form}, 1e18)", s => s.R0 && s.R1 ? Truth.False : Truth.Unknown),
            new($"rising({form}, 2)", s => s.R0 && s.R1 && s.R2 ? Of(s.V0 > s.V1 && s.V1 > s.V2) : Truth.Unknown),
            new($"falling({form}, 2)", s => s.R0 && s.R1 && s.R2 ? Of(s.V0 < s.V1 && s.V1 < s.V2) : Truth.Unknown),
            new($"not rising({form}, 2)", s => s.R0 && s.R1 && s.R2 ? Of(!(s.V0 > s.V1 && s.V1 > s.V2)) : Truth.Unknown),
            new($"between({form}, -1e18, 1e18)", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"not between({form}, -1e18, 1e18)", s => s.R0 ? Truth.False : Truth.Unknown),
            new($"{form} / 0 > 1", _ => Truth.Unknown),
            new($"not ({form} / 0 > 1)", _ => Truth.Unknown),
            new($"{form} / 0 > 1 or {KnownTrue}", _ => Truth.True),
            new($"{form} + 1 > -1e18", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"abs({form}) >= 0", s => s.R0 ? Truth.True : Truth.Unknown),
            new($"-{form} <= max({form}, 1e18)", s => s.R0 ? Truth.True : Truth.Unknown),
        ]);
        return checks;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void No_decision_is_true_while_an_operand_of_its_deciding_comparison_is_not_ready(string name, Timeframe timeframe)
    {
        var info = IndicatorCatalog.TryGet(name, out var found) ? found : throw new InvalidOperationException(name);
        var form = ShortForms[name];
        var checks = ChecksFor(name, form);
        var bars = BarsFor(timeframe, info.DateDriven, HashCode(name, timeframe));
        var context = Contexts.Covering(Clock.Mon, DateOnly.FromDateTime(bars[^1].Start.UtcDateTime), 3, daysBefore: 12);
        var expiry = new HashSet<DateOnly>(Enumerable.Range(0, 400).Select(i => Clock.Mon.AddDays(i)).Where(d => d.DayOfWeek == DayOfWeek.Tuesday));
        var rig = Rig.Of(timeframe, [.. checks.Select(c => c.Source)], context, expiry, minimumDepth: 3);
        var feature = Compiling.Condition(checks[0].Source, timeframe).Features.Single(f => f.Name == name);
        var handle = rig.Frame.HandleOf(feature);
        handle.ShouldBeGreaterThanOrEqualTo(0);
        var sawNotReady = false;
        var sawAllReady = false;
        var sawTrueDecision = false;
        var sawFalse = false;

        foreach (var bar in bars)
        {
            rig.Frame.Step(bar);
            var probe = new Probe(
                rig.Frame.IsReadyAt(handle, 0),
                rig.Frame.IsReadyAt(handle, 1),
                rig.Frame.IsReadyAt(handle, 2),
                rig.Frame.At(handle, 0),
                rig.Frame.At(handle, 1),
                rig.Frame.At(handle, 2));
            var truths = rig.Read();

            // the assumption under every check: a feature that is ready has a number and one that is not has NaN (no number that is not ready, no NaN that is)
            if (probe.R0 == double.IsNaN(probe.V0) || probe.R1 == double.IsNaN(probe.V1) || probe.R2 == double.IsNaN(probe.V2))
            {
                throw new ShouldAssertException($"{name} on {timeframe}, step {rig.Frame.Steps}: IsReadyAt and NaN disagree ({probe})");
            }
            for (var i = 0; i < checks.Count; i++)
            {
                var expected = checks[i].Expected(probe);
                if (truths[i] != expected)
                {
                    truths[i].ShouldBe(expected, $"{name} on {timeframe}, step {rig.Frame.Steps}, {checks[i].Source}");
                }

                if (rig.Bound[i].Evaluate(rig.Frame) != (expected == Truth.True))
                {
                    rig.Bound[i].Evaluate(rig.Frame).ShouldBe(expected == Truth.True, $"{name} on {timeframe}, step {rig.Frame.Steps}, decision of {checks[i].Source}");
                }
            }

            sawNotReady |= !probe.R0 || !probe.R1 || !probe.R2;
            sawAllReady |= probe.R0 && probe.R1 && probe.R2;
            sawTrueDecision |= truths[0] == Truth.True;
            sawFalse |= truths[1] == Truth.False;
        }

        sawNotReady.ShouldBeTrue($"{name} on {timeframe}: the series never has the feature not ready, so the property is not exercised");
        sawAllReady.ShouldBeTrue($"{name} on {timeframe}: the series is too short for the feature to become ready (and its two previous values)");
        sawTrueDecision.ShouldBeTrue();
        sawFalse.ShouldBeTrue();
        info.Name.ShouldBe(name);
    }

    private static int HashCode(string name, Timeframe timeframe) => name.Aggregate(17, (h, c) => h * 31 + c) + (int)timeframe * 7_919;
}
