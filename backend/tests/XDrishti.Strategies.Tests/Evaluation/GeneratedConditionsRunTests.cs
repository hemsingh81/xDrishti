using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Random valid conditions (the generator of the plan tests, which knows nothing of the compiler) run over seeded frames on six evaluation timeframes: nothing throws at any step — not the lags of a deep subscript, not a
/// division by zero, not a context with a gap —, the decision is true exactly where the three-valued result is, a bound condition gives the same answer twice, and over the whole run the conditions take all three values
/// (so the run is not vacuous). This is a robustness test of the compiled code; what the answers must be is the business of the hand-calculated tables and of the independent interpreter.
/// </summary>
public sealed class GeneratedConditionsRunTests
{
    public static TheoryData<Timeframe, int> Blocks { get; } =
        [.. new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1, Timeframe.W1 }.SelectMany(t => Enumerable.Range(0, 6).Select(b => (t, b)))];

    private static Bar[] BarsFor(Timeframe timeframe, int seed) => timeframe switch
    {
        Timeframe.D1 => Series.Sparse(seed, 60),
        Timeframe.W1 => Series.Sparse(seed, 160),
        _ => Series.Sessions(seed, 3),
    };

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Generated_conditions_never_throw_and_the_decision_is_the_true_result(Timeframe timeframe, int block)
    {
        var seed = 31_000 + (int)timeframe * 100 + block;
        var generator = new ValidConditionGenerator(seed, timeframe);
        var conditions = Enumerable.Range(0, 25).Select(_ => Compiling.Condition(generator.Next().Source, timeframe, ValidConditionGenerator.Parameters)).ToList();
        var plan = ConditionSet.Merge(conditions);
        var bars = BarsFor(timeframe, seed);
        var context = Contexts.Covering(DateOnly.FromDateTime(bars[0].Start.UtcDateTime), DateOnly.FromDateTime(bars[^1].Start.UtcDateTime), seed, daysBefore: 20);
        var expiry = new HashSet<DateOnly>(Enumerable.Range(0, 800).Select(i => Clock.Mon.AddDays(i)).Where(d => d.DayOfWeek == DayOfWeek.Thursday));
        var created = FeatureFrame.Create(
            plan.Features.Count == 0 ? [Specs.Close(timeframe)] : plan.Features,
            new FrameOptions { HistoryDepth = plan.HistoryDepth, Context = plan.NeedsContext ? context : null, ExpiryDays = plan.NeedsExpiryDays ? expiry : null });
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        var frame = created.Value;
        var bound = plan.BindAll(frame);
        var counts = new Dictionary<Truth, int> { [Truth.True] = 0, [Truth.False] = 0, [Truth.Unknown] = 0 };

        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var c = 0; c < bound.Count; c++)
            {
                var truth = bound[c].EvaluateTruth(frame);
                if (bound[c].Evaluate(frame) != (truth == Truth.True) || bound[c].EvaluateTruth(frame) != truth)
                {
                    throw new ShouldAssertException($"step {frame.Steps}, {conditions[c].Source}: the decision and the result differ, or the answer changed between two reads");
                }

                counts[truth]++;
            }
        }

        counts[Truth.True].ShouldBeGreaterThan(0, "no condition was ever true: " + string.Join(" | ", conditions.Select(c => c.Source)));
        counts[Truth.False].ShouldBeGreaterThan(0);
        (counts[Truth.True] + counts[Truth.False] + counts[Truth.Unknown]).ShouldBe(bars.Length * conditions.Count);
    }
}
