using System.Diagnostics;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Benchmarks;

/// <summary>
/// A small benchmark harness without BenchmarkDotNet, like the one of the Indicators tests. The tests are <b>explicit</b>: <c>dotnet test</c> and <c>scripts/check.sh</c> skip them. Run them in Release, one at a time so
/// that the cold figures are those of a fresh process:
/// <c>dotnet test --project tests/XDrishti.Strategies.Tests -c Release -- --explicit only --filter-method "*Compile_bind_and_build_ten_conditions*"</c>.
/// They report the nanoseconds to evaluate the ten conditions of <see cref="TenConditions"/> over a 20-feature frame (criterion 12: 0.3 µs per set) and the milliseconds to parse, bind, check and compile the ten
/// (≤ 20 ms cold, ≤ 3 ms warm). The cold figure is the first run in the process: it contains the JIT of the parser, the binder and the expression compiler, which is why it only means something when this is the first
/// thing the process does. The delegates are built when a condition is first bound to a frame, so "compile" below is <c>Compile</c> of the ten texts plus <c>Merge</c>, the frame, and the ten binds.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class EvaluationBenchmarks
{
    private const int Repeats = 5;

    private const int WarmRuns = 40;

    private static void Report(string title, IEnumerable<string> lines)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(title + "\n" + string.Join("\n", lines.Select(l => "  " + l)));
    }

    [Fact(Explicit = true)]
    public void Evaluate_ten_conditions_per_set()
    {
        var bars = Series.Sessions(5, 330);
        var (frame, bound, _) = TenConditions.Build(bars);
        var conditions = bound.ToArray();
        for (var i = 0; i < 20_000; i++)
        {
            frame.Step(bars[i]);
        }

        // the frame stays where it is: this is the cost of the ten evaluations alone
        const int Sets = 2_000_000;
        long sum = 0;
        var best = double.MaxValue;
        var bestTruth = double.MaxValue;
        for (var repeat = 0; repeat < Repeats; repeat++)
        {
            var watch = Stopwatch.StartNew();
            for (var s = 0; s < Sets; s++)
            {
                for (var c = 0; c < conditions.Length; c++)
                {
                    sum += conditions[c].Evaluate(frame) ? 1 : 0;
                }
            }

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds * 1e6 / Sets);
            watch.Restart();
            for (var s = 0; s < Sets; s++)
            {
                for (var c = 0; c < conditions.Length; c++)
                {
                    sum += (int)conditions[c].EvaluateTruth(frame);
                }
            }

            bestTruth = Math.Min(bestTruth, watch.Elapsed.TotalMilliseconds * 1e6 / Sets);
        }

        // and the cost of the whole step: one 1-minute bar into the frame, then the ten evaluations
        var stepOnly = double.MaxValue;
        var stepAndEvaluate = double.MaxValue;
        const int Steps = 100_000;
        for (var repeat = 0; repeat < Repeats; repeat++)
        {
            var (f1, b1, _) = TenConditions.Build(bars);
            for (var i = 0; i < 20_000; i++)
            {
                f1.Step(bars[i]);
            }

            var watch = Stopwatch.StartNew();
            for (var i = 20_000; i < 20_000 + Steps; i++)
            {
                f1.Step(bars[i]);
            }

            stepOnly = Math.Min(stepOnly, watch.Elapsed.TotalMilliseconds * 1e6 / Steps);
            var (f2, b2, _) = TenConditions.Build(bars);
            for (var i = 0; i < 20_000; i++)
            {
                f2.Step(bars[i]);
            }

            watch.Restart();
            for (var i = 20_000; i < 20_000 + Steps; i++)
            {
                f2.Step(bars[i]);
                for (var c = 0; c < b2.Count; c++)
                {
                    sum += b2[c].Evaluate(f2) ? 1 : 0;
                }
            }

            stepAndEvaluate = Math.Min(stepAndEvaluate, watch.Elapsed.TotalMilliseconds * 1e6 / Steps);
            GC.KeepAlive(b1);
        }

        Report(
            "Evaluating the ten conditions of a 20-feature frame (best of " + Repeats + ")",
            [
                $"Evaluate x10 (decision)       {best,8:F1} ns per set   {best / 10,6:F1} ns per condition",
                $"EvaluateTruth x10             {bestTruth,8:F1} ns per set   {bestTruth / 10,6:F1} ns per condition",
                $"frame.Step alone              {stepOnly,8:F1} ns per 1-minute bar",
                $"frame.Step + Evaluate x10     {stepAndEvaluate,8:F1} ns per 1-minute bar   (evaluation share {stepAndEvaluate - stepOnly,6:F1} ns)",
                "target of the spec: 300 ns per set of 10 (0.3 µs); asserted on the figure while the frame moves, the honest one (the first figure is a frame that stands still, which the branch predictor learns)",
                "(checksum " + sum + ")",
            ]);
        (stepAndEvaluate - stepOnly).ShouldBeLessThanOrEqualTo(300, "target: 0.3 µs per evaluation of a 10-condition set, measured while the frame moves");
    }

    [Fact(Explicit = true)]
    public void Compile_bind_and_build_ten_conditions_cold_and_warm()
    {
        var bars = Series.Sessions(5, 3);
        var context = Contexts.Covering(DateOnly.FromDateTime(bars[0].Start.UtcDateTime), DateOnly.FromDateTime(bars[^1].Start.UtcDateTime), 4, daysBefore: 120);
        var times = new List<(double Compile, double Merge, double Frame, double Bind, double Total)>();
        for (var run = 0; run < WarmRuns + 1; run++)
        {
            var watch = Stopwatch.StartNew();
            var conditions = new CompiledCondition[TenConditions.Sources.Length];
            for (var i = 0; i < conditions.Length; i++)
            {
                var result = ConditionCompiler.Compile(TenConditions.Sources[i], new CompileContext(TenConditions.Evaluation));
                conditions[i] = result.Condition ?? throw new InvalidOperationException(TenConditions.Sources[i]);
            }

            var compile = watch.Elapsed.TotalMilliseconds;
            var plan = ConditionSet.Merge(conditions);
            var merge = watch.Elapsed.TotalMilliseconds - compile;
            var frame = FeatureFrame.Create(plan.Features, new FrameOptions { HistoryDepth = plan.HistoryDepth, Context = context }).Value;
            var created = watch.Elapsed.TotalMilliseconds - compile - merge;
            var bound = plan.BindAll(frame);
            var total = watch.Elapsed.TotalMilliseconds;
            times.Add((compile, merge, created, total - compile - merge - created, total));
            bound.Count.ShouldBe(10);
        }

        var cold = times[0];
        var warm = times.Skip(1).OrderBy(t => t.Total).ToList();
        var median = warm[warm.Count / 2];
        var best = warm[0];
        Report(
            "Parse + bind + lint + compile of ten conditions (milliseconds)",
            [
                $"cold (first run of the process)  total {cold.Total,7:F2}   parse+bind+lint {cold.Compile,7:F2}   merge {cold.Merge,5:F2}   frame {cold.Frame,5:F2}   delegates (Bind) {cold.Bind,6:F2}",
                $"warm median of {WarmRuns}                total {median.Total,7:F2}   parse+bind+lint {median.Compile,7:F2}   merge {median.Merge,5:F2}   frame {median.Frame,5:F2}   delegates (Bind) {median.Bind,6:F2}",
                $"warm best                        total {best.Total,7:F2}",
                "targets of the spec: cold <= 20 ms, warm <= 3 ms (parse + bind + lint + compile; the frame is not part of it)",
                $"cold without the frame: {cold.Total - cold.Frame,6:F2} ms   warm median without the frame: {median.Total - median.Frame,6:F2} ms",
            ]);
        (median.Total - median.Frame).ShouldBeLessThanOrEqualTo(3, "target: parse + bind + lint + compile of a 10-condition strategy in 3 ms warm");
    }

    [Fact(Explicit = true)]
    public void Compile_one_condition_per_stage_warm()
    {
        const int Rounds = 2_000;
        var report = new List<string>();
        foreach (var source in new[] { "close > 5", "rsi(14) > 55 and close > ema(20)", "rising(close, 3) and adx(14) > 20", TenConditions.Sources[8], TenConditions.Sources[9] })
        {
            for (var warm = 0; warm < 200; warm++)
            {
                var r = ConditionCompiler.Compile(source, new CompileContext(TenConditions.Evaluation)).Condition!;
                r.Bind(Frames.Of(new FrameOptions { HistoryDepth = r.HistoryDepth, Context = Contexts.Covering(Clock.Mon, Clock.Fri, 1) }, [.. r.Features]));
            }

            var frames = new List<FeatureFrame>();
            var compileWatch = Stopwatch.StartNew();
            var made = new List<CompiledCondition>(Rounds);
            for (var i = 0; i < Rounds; i++)
            {
                made.Add(ConditionCompiler.Compile(source, new CompileContext(TenConditions.Evaluation)).Condition!);
            }

            var compileMs = compileWatch.Elapsed.TotalMilliseconds / Rounds;
            var context = Contexts.Covering(Clock.Mon, Clock.Fri, 1);
            foreach (var c in made.Take(1))
            {
                frames.Add(Frames.Of(new FrameOptions { HistoryDepth = c.HistoryDepth, Context = context }, [.. c.Features]));
            }

            var bindWatch = Stopwatch.StartNew();
            foreach (var c in made)
            {
                c.Bind(frames[0]);
            }

            var bindMs = bindWatch.Elapsed.TotalMilliseconds / Rounds;
            report.Add($"{source,-80} parse+bind+lint {compileMs * 1000,7:F1} us   first Bind (build delegate) {bindMs * 1000,7:F1} us");
        }

        Report("One condition, warm (microseconds)", report);
    }
}
