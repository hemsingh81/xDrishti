using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The oracle (see <see cref="InterpreterOracleTests"/>) on the ways conditions are shared: conditions compiled for <b>different evaluation timeframes</b> merged into one plan and one frame (a strategy has a setup on
/// 1h and a trigger on 5m), and one bound condition used with <b>two frames of the same layout</b> run side by side over different series (one strategy over many instruments), the steps interleaved so that a frame cannot
/// borrow anything from the other.
/// </summary>
public sealed class OracleSharingTests
{
    private static Tri Of(Truth truth) => truth switch { Truth.True => Tri.True, Truth.False => Tri.False, _ => Tri.Unknown };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Conditions_compiled_for_different_evaluation_timeframes_agree_with_the_interpreter_on_one_frame(int block)
    {
        Timeframe[] evaluations = [Timeframe.M5, Timeframe.H1, Timeframe.M15, Timeframe.M1, Timeframe.M30];
        var models = new List<Node>();
        var sources = new List<string>();
        var compiled = new List<CompiledCondition>();
        for (var i = 0; i < evaluations.Length; i++)
        {
            var generator = new ModelGenerator(8_000 + block * 10 + i, evaluations[i], allowContext: true, allowExpiryDays: true);
            for (var j = 0; j < 4; j++)
            {
                var model = generator.Next();
                var source = Printer.Print(model, (i + j) % 2 == 0 ? Parens.Always : Parens.Minimal, (Spacing)((i + j) % 3));
                models.Add(model);
                sources.Add(source);
                compiled.Add(Compiling.Condition(source, evaluations[i], ModelGenerator.Parameters));
            }
        }

        var facts = models.Select(ModelFacts.Of).ToList();
        var plan = ConditionSet.Merge(compiled);
        var bars = Series.Sessions(600 + block, 2);
        var context = OracleContext.For(block, bars);
        var specs = facts.SelectMany(f => f.Features).Concat(evaluations.Select(Specs.Close)).Distinct().ToList();
        var created = FeatureFrame.Create(specs, new FrameOptions(null, facts.Max(f => f.HistoryDepth), context, OracleContext.ExpiryDays(block)));
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        var frame = created.Value;
        var bound = plan.BindAll(frame);
        var interpreter = new Interpreter(frame);
        var counts = new Dictionary<Tri, int> { [Tri.True] = 0, [Tri.False] = 0, [Tri.Unknown] = 0 };

        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var k = 0; k < models.Count; k++)
            {
                var expected = interpreter.Evaluate(models[k]);
                Of(bound[k].EvaluateTruth(frame)).ShouldBe(expected, $"`{sources[k]}` at step {frame.Steps}");
                counts[expected]++;
            }
        }

        plan.Features.Count.ShouldBeGreaterThan(evaluations.Length, "the conditions share hardly any feature, and read hardly any other timeframe");
        counts[Tri.True].ShouldBeGreaterThan(0);
        counts[Tri.False].ShouldBeGreaterThan(0);
        counts[Tri.Unknown].ShouldBeGreaterThan(0);
    }

    [Fact]
    public void One_bound_condition_serves_two_frames_of_the_same_layout_run_side_by_side_over_different_series()
    {
        var generator = new ModelGenerator(8_500, Timeframe.M15, allowContext: true, allowExpiryDays: true);
        var models = Enumerable.Range(0, 10).Select(_ => generator.Next()).ToList();
        var sources = models.Select((m, i) => Printer.Print(m, i % 2 == 0 ? Parens.Always : Parens.Minimal)).ToList();
        var facts = models.Select(ModelFacts.Of).ToList();
        var plan = ConditionSet.Merge([.. sources.Select(s => Compiling.Condition(s, Timeframe.M15, ModelGenerator.Parameters))]);
        var barsA = Series.Sessions(701, 2);
        var barsB = Series.Sessions(702, 2);
        var context = OracleContext.For(8_500, barsA);
        var specs = facts.SelectMany(f => f.Features).Append(Specs.Close(Timeframe.M15)).Distinct().ToList();
        var options = new FrameOptions(null, facts.Max(f => f.HistoryDepth), context, OracleContext.ExpiryDays(8_500));
        var frameA = FeatureFrame.Create(specs, options).Value;
        var frameB = FeatureFrame.Create(specs, options).Value;
        var bound = plan.BindAll(frameA);
        var interpreterA = new Interpreter(frameA);
        var interpreterB = new Interpreter(frameB);

        for (var i = 0; i < Math.Min(barsA.Length, barsB.Length); i++)
        {
            frameA.Step(barsA[i]);
            frameB.Step(barsB[i]);
            for (var k = 0; k < models.Count; k++)
            {
                Of(bound[k].EvaluateTruth(frameA)).ShouldBe(interpreterA.Evaluate(models[k]), $"frame A, `{sources[k]}` at step {i + 1}");
                Of(bound[k].EvaluateTruth(frameB)).ShouldBe(interpreterB.Evaluate(models[k]), $"frame B, `{sources[k]}` at step {i + 1}");
            }
        }
    }
}
