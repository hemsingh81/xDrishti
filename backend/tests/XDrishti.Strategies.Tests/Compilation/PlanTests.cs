using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>
/// Criterion 11: the plan lists exactly the features the expression reads (no more, no fewer, each once, in the order they first appear), the smallest history depth that serves it (the largest lag read plus 1, at
/// least 1) and says whether the frame needs a context or expiry dates; the frame accepts a plan.
/// </summary>
public sealed class PlanTests
{
    private const Timeframe M15 = Timeframe.M15;

    private static FeatureSpec Close => Specs.Close(M15);

    private static FeatureSpec Of(string name, double p1 = 0, double p2 = 0, Timeframe frame = M15) => Specs.Of(name, frame, p1, p2);

    /// <summary>Conditions with the features and the depth worked out by hand: the depth is the largest lag read plus 1 (a crossing also reads one bar further back, <c>rising(x, n)</c> n bars further).</summary>
    public static readonly IReadOnlyDictionary<string, (FeatureSpec[] Features, int Depth)> Hand = new Dictionary<string, (FeatureSpec[], int)>
    {
        ["close > 5"] = ([Close], 1),
        ["rsi(14) > 55"] = ([Of("rsi", 14)], 1),
        ["close[1] > close"] = ([Close], 2),
        ["close[3] > open[7]"] = ([Close, Of("open")], 8),
        ["close > open and open > close"] = ([Close, Of("open")], 1),
        ["open > close and close > open"] = ([Of("open"), Close], 1),
        ["ema(20) > ema(50) and ema(20) > close"] = ([Of("ema", 20), Of("ema", 50), Close], 1),
        ["ema(20) > ema()"] = ([Of("ema", 20)], 1),
        ["crosses_above(close, vwap)"] = ([Close, Of("vwap")], 2),
        ["crosses_below(close, close[1])"] = ([Close], 3),
        ["crosses_above(close[2], vwap)"] = ([Close, Of("vwap")], 4),
        ["rising(close, 3)"] = ([Close], 4),
        ["falling(close, 1)"] = ([Close], 2),
        ["rising(close[2], 3)"] = ([Close], 6),
        ["falling(rsi(14), 5) and close[1] > 0"] = ([Of("rsi", 14), Close], 6),
        ["rising(close, 3) and crosses_above(close, vwap)"] = ([Close, Of("vwap")], 4),
        ["between(rsi(14), 30, 70)"] = ([Of("rsi", 14)], 1),
        ["abs(close - open) > atr(14)"] = ([Close, Of("open"), Of("atr", 14)], 1),
        ["time >= \"09:45\""] = ([], 1),
        ["time >= \"09:45\" and close > vwap"] = ([Close, Of("vwap")], 1),
        ["1 < 2"] = ([], 1),
        ["crosses_above(1, 2)"] = ([], 1),
        ["rising(1, 200)"] = ([], 1),
        ["close[255] > 0"] = ([Close], 256),
        ["rising(close, 255)"] = ([Close], 256),
        ["crosses_above(close[254], vwap)"] = ([Close, Of("vwap")], 256),
        ["tf(\"1h\").ema(20) > tf(\"1h\").ema(50) and close > 0"] = ([Of("ema", 20, 0, Timeframe.H1), Of("ema", 50, 0, Timeframe.H1), Close], 1),
        ["tf(\"1D\").close[1] > tf(\"1D\").close"] = ([Specs.Close(Timeframe.D1)], 2),
        ["vix[3] > 20"] = ([Of("vix", 0, 0, Timeframe.M1)], 4),
        ["day_of_week == TUE and day_of_week[2] != 5"] = ([Of("day_of_week", 0, 0, Timeframe.M1)], 3),
    };

    public static TheoryData<string> HandSources { get; } = [.. Hand.Keys];

    public static TheoryData<Timeframe, int> Blocks { get; } =
        [.. new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1 }.SelectMany(t => Enumerable.Range(0, 10).Select(b => (t, b)))];

    private static FrameOptions OptionsFor(CompiledPlanFacts plan) =>
        new(null, plan.Depth, plan.NeedsContext ? MarketContext.Create([]).Value : null, plan.NeedsExpiryDays ? new HashSet<DateOnly>() : null);

    private sealed record CompiledPlanFacts(int Depth, bool NeedsContext, bool NeedsExpiryDays);

    [Theory]
    [MemberData(nameof(HandSources))]
    public void The_plan_lists_exactly_the_features_read_and_the_smallest_sufficient_depth(string source)
    {
        var (features, depth) = Hand[source];

        var plan = Compiling.Condition(source);

        plan.Features.ShouldBe(features);
        plan.HistoryDepth.ShouldBe(depth);
        plan.Source.ShouldBe(source);
        plan.EvaluationTimeframe.ShouldBe(M15);
    }

    [Theory]
    [InlineData("regime() == 1", true, false)]
    [InlineData("vix > 20", true, false)]
    [InlineData("index.trend == 1 and day_of_week == TUE", true, false)]
    [InlineData("is_expiry_day == 1", false, true)]
    [InlineData("is_expiry_day == 1 and breadth > 50", true, true)]
    [InlineData("day_of_week == TUE", false, false)]
    [InlineData("close > vwap", false, false)]
    public void The_plan_says_whether_the_frame_needs_a_context_or_expiry_dates(string source, bool context, bool expiryDays)
    {
        var plan = Compiling.Condition(source);

        plan.NeedsContext.ShouldBe(context);
        plan.NeedsExpiryDays.ShouldBe(expiryDays);
        FeatureFrame.Create(plan.Features, new FrameOptions { HistoryDepth = plan.HistoryDepth }).IsSuccess.ShouldBe(!context && !expiryDays, "a frame without them is refused exactly when the plan says they are needed");
        FeatureFrame.Create(plan.Features, OptionsFor(new CompiledPlanFacts(plan.HistoryDepth, true, true))).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void The_features_the_depth_and_the_needs_of_generated_conditions_are_the_ones_they_were_built_from_and_the_frame_accepts_the_plan(Timeframe timeframe, int block)
    {
        var generator = new ValidConditionGenerator(timeframe.GetHashCode() * 1000 + block, timeframe);

        for (var i = 0; i < 100; i++)
        {
            var generated = generator.Next();
            var result = Compiling.Of(generated.Source, timeframe, ValidConditionGenerator.Parameters);

            result.IsSuccess.ShouldBeTrue(generated.Source + " -> " + Compiling.Describe(result));
            var plan = result.Condition!;
            plan.Features.ShouldBe(generated.Features, generated.Source);
            plan.HistoryDepth.ShouldBe(generated.HistoryDepth, generated.Source);
            plan.NeedsContext.ShouldBe(generated.NeedsContext, generated.Source);
            plan.NeedsExpiryDays.ShouldBe(generated.NeedsExpiryDays, generated.Source);

            var created = FeatureFrame.Create(plan.Features, OptionsFor(new CompiledPlanFacts(plan.HistoryDepth, plan.NeedsContext, plan.NeedsExpiryDays)));
            created.IsSuccess.ShouldBeTrue(generated.Source + " -> " + (created.IsFailure ? created.Error.Message + " " + string.Join("; ", created.Error.Fields?.Select(f => f.Field + ": " + f.Message) ?? []) : string.Empty));
            created.Value.HistoryDepth.ShouldBe(plan.HistoryDepth);
            created.Value.Count.ShouldBe(plan.Features.Count);
            for (var h = 0; h < plan.Features.Count; h++)
            {
                created.Value.SpecOf(h).ShouldBe(plan.Features[h], "the handle of a feature is its position in the plan");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_frame_refuses_the_plan_of_a_generated_condition_exactly_when_the_plan_says_it_needs_a_context_or_expiry_dates(Timeframe timeframe, int block)
    {
        var generator = new ValidConditionGenerator(7_000_000 + timeframe.GetHashCode() * 1000 + block, timeframe);

        for (var i = 0; i < 50; i++)
        {
            var generated = generator.Next();
            var plan = Compiling.Condition(generated.Source, timeframe, ValidConditionGenerator.Parameters);

            var bare = FeatureFrame.Create(plan.Features, new FrameOptions { HistoryDepth = plan.HistoryDepth });

            bare.IsSuccess.ShouldBe(!plan.NeedsContext && !plan.NeedsExpiryDays, generated.Source);
        }
    }

    [Fact]
    public void A_condition_that_needs_no_context_is_accepted_without_the_options_the_generated_conditions_would_get()
    {
        var generator = new ValidConditionGenerator(99, Timeframe.M5, allowContext: false, allowExpiryDays: false);

        for (var i = 0; i < 200; i++)
        {
            var generated = generator.Next();
            generated.NeedsContext.ShouldBeFalse();
            generated.NeedsExpiryDays.ShouldBeFalse();
            var plan = Compiling.Condition(generated.Source, Timeframe.M5, ValidConditionGenerator.Parameters);

            FeatureFrame.Create(plan.Features, new FrameOptions { HistoryDepth = plan.HistoryDepth }).IsSuccess.ShouldBeTrue(generated.Source);
        }
    }

    [Fact]
    public void The_deepest_generated_histories_stay_inside_the_frame_and_equal_the_largest_lag_plus_one()
    {
        var generator = new ValidConditionGenerator(5, Timeframe.M15, maxLag: 200);

        var depths = Enumerable.Range(0, 300).Select(_ => generator.Next()).ToList();

        depths.Max(d => d.HistoryDepth).ShouldBeGreaterThan(100, "the generator must reach deep histories for this test to mean anything");
        foreach (var generated in depths)
        {
            Compiling.Condition(generated.Source, Timeframe.M15, ValidConditionGenerator.Parameters).HistoryDepth.ShouldBe(generated.HistoryDepth, generated.Source);
        }
    }

    [Fact]
    public void The_generator_is_deterministic_and_makes_many_different_conditions()
    {
        var first = new ValidConditionGenerator(1, Timeframe.M15);
        var second = new ValidConditionGenerator(1, Timeframe.M15);

        var a = Enumerable.Range(0, 200).Select(_ => first.Next().Source).ToList();
        var b = Enumerable.Range(0, 200).Select(_ => second.Next().Source).ToList();

        a.ShouldBe(b);
        a.Distinct().Count().ShouldBeGreaterThan(190);
        a.ShouldContain(s => s.Contains("crosses_", StringComparison.Ordinal));
        a.ShouldContain(s => s.Contains("rising(", StringComparison.Ordinal) || s.Contains("falling(", StringComparison.Ordinal));
        a.ShouldContain(s => s.Contains("between(", StringComparison.Ordinal));
        a.ShouldContain(s => s.Contains("tf(\"", StringComparison.Ordinal));
        a.ShouldContain(s => s.Contains("time ", StringComparison.Ordinal));
        a.ShouldContain(s => s.Contains("regime()", StringComparison.Ordinal));
    }
}
