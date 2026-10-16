using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Benchmarks;

/// <summary>
/// A small benchmark harness without BenchmarkDotNet, like the one of the Indicators tests. The tests are <b>explicit</b>: <c>dotnet test</c> and <c>scripts/check.sh</c> skip them. Run them in Release, one at a time and each in
/// a fresh process, because the cold figures are the first thing a process does (<c>scripts/bench-coldstart.sh</c> does so 15 times and reports the minimum, median and maximum):
/// <c>dotnet test --project tests/XDrishti.Strategies.Tests -c Release -- --explicit only --filter-method "*Compile_bind_and_build_ten_conditions*"</c>.
/// They report the nanoseconds to evaluate the ten conditions of <see cref="TenConditions"/> over a 20-feature frame (criterion 12: 0.3 µs per set) and the milliseconds to parse, bind, check and compile the ten.
/// The compile has three figures (criterion 12 as amended for ISS-12, ADR 0034 amendment 2). <b>Warm</b>, with the compiler already compiled to machine code by use: ≤ 3 ms, asserted. <b>First compile after the warm-up</b>:
/// ≤ 20 ms, asserted; the process has run <see cref="CompilerWarmup.Run"/> first, as a host does right after it starts, and this is what a person waits for. <b>Process-cold</b>, the very first compile of a process
/// with no warm-up: reported, not asserted; it is dominated by the JIT compiling about a thousand methods of the parser, the binder and the expression compiler (about 40 ms on an Apple M4 Pro, JIT only), and no setting
/// of the runtime brings it under 20 ms. The delegates are built when a condition is first bound to a frame, so "compile" below is <c>Compile</c> of the ten texts plus <c>Merge</c>, the frame, and the ten binds; the frame is not part of the figures.
/// Each report ends with <c>metric name value</c> lines that the script reads.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class EvaluationBenchmarks
{
    private const int Repeats = 5;

    private const int WarmRuns = 40;

    /// <summary>The time asserted for the first compile after the warm-up (milliseconds), and for the warm-up itself.</summary>
    private const double FirstCompileAfterWarmUpMilliseconds = 20;

    private const double WarmUpMilliseconds = 150;

    private static void Report(string title, IEnumerable<string> lines)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(title + "\n" + string.Join("\n", lines.Select(l => "  " + l)));
    }

    /// <summary>The line the script reads: <c>metric</c>, the name and the value with a point as the decimal separator.</summary>
    private static string Metric(string name, double value) => string.Create(CultureInfo.InvariantCulture, $"metric {name} {value:F2}");

    /// <summary>What one pass over the ten conditions cost, in milliseconds, and how many methods the JIT compiled on this thread while it ran.</summary>
    private readonly record struct Timing(double Compile, double Merge, double Frame, double Bind, double Total, long JitMethods, double JitMilliseconds)
    {
        /// <summary>The figure of the criterion: everything but the creation of the frame.</summary>
        public double WithoutFrame => Total - Frame;
    }

    /// <summary>Parse, bind, lint and merge the ten conditions, make a frame for them and bind them to it (which builds their delegates).</summary>
    private static Timing CompileTen(MarketContext context)
    {
        var jitMethods = JitInfo.GetCompiledMethodCount(currentThread: true);
        var jitTime = JitInfo.GetCompilationTime(currentThread: true);
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
        var timing = new Timing(compile, merge, created, total - compile - merge - created, total, JitInfo.GetCompiledMethodCount(currentThread: true) - jitMethods, (JitInfo.GetCompilationTime(currentThread: true) - jitTime).TotalMilliseconds);

        // after the figures are taken: the first call of an assertion is itself JIT-compiled
        bound.Count.ShouldBe(10);
        return timing;
    }

    private static MarketContext ContextOfTheBenchmark()
    {
        var bars = Series.Sessions(5, 3);
        return Contexts.Covering(DateOnly.FromDateTime(bars[0].Start.UtcDateTime), DateOnly.FromDateTime(bars[^1].Start.UtcDateTime), 4, daysBefore: 120);
    }

    private static Timing Median(IEnumerable<Timing> timings)
    {
        var ordered = timings.OrderBy(t => t.WithoutFrame).ToList();
        return ordered[ordered.Count / 2];
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
        var context = ContextOfTheBenchmark();
        var cold = CompileTen(context);
        var warm = Enumerable.Range(0, WarmRuns).Select(_ => CompileTen(context)).ToList();
        var median = Median(warm);
        var best = warm.OrderBy(t => t.WithoutFrame).First();
        Report(
            "Parse + bind + lint + compile of ten conditions, no warm-up (milliseconds)",
            [
                $"process-cold (first run, no warm-up)  total {cold.Total,7:F2}   parse+bind+lint {cold.Compile,7:F2}   merge {cold.Merge,5:F2}   frame {cold.Frame,5:F2}   delegates (Bind) {cold.Bind,6:F2}   JIT {cold.JitMethods} methods, {cold.JitMilliseconds:F1} ms",
                $"warm median of {WarmRuns}                    total {median.Total,7:F2}   parse+bind+lint {median.Compile,7:F2}   merge {median.Merge,5:F2}   frame {median.Frame,5:F2}   delegates (Bind) {median.Bind,6:F2}   JIT {median.JitMethods} methods",
                $"warm best                             total {best.Total,7:F2}",
                "process-cold is reported, not asserted (ISS-12): it is the JIT compiling the compiler. See Compile_ten_conditions_first_run_after_the_warmup for the figure that a person waits for, and CompilerWarmup.",
                "target of the spec: warm <= 3 ms (parse + bind + lint + compile; the frame is not part of it), asserted",
                $"process-cold without the frame: {cold.WithoutFrame,6:F2} ms   warm median without the frame: {median.WithoutFrame,6:F2} ms",
                Metric("process_cold_ms", cold.WithoutFrame),
                Metric("process_cold_jit_methods", cold.JitMethods),
                Metric("warm_median_ms", median.WithoutFrame),
            ]);
        median.WithoutFrame.ShouldBeLessThanOrEqualTo(3, "target: parse + bind + lint + compile of a 10-condition strategy in 3 ms warm");
    }

    [Fact(Explicit = true)]
    public void Compile_ten_conditions_first_run_after_the_warmup()
    {
        // The assertion assumes what a host does: the warm-up has run to its end before the first request (it runs on a thread-pool thread right after the host starts, and a host needs far longer than the warm-up to be
        // ready). Here both run on this thread, one after the other. A request that arrives while the warm-up is still running waits for the part of the JIT that it has not done yet.
        var context = ContextOfTheBenchmark();
        var watch = Stopwatch.StartNew();
        CompilerWarmup.Run();
        var warmUp = watch.Elapsed.TotalMilliseconds;
        var first = CompileTen(context);
        var second = CompileTen(context);
        Report(
            "Parse + bind + lint + compile of ten conditions, first run after CompilerWarmup.Run (milliseconds)",
            [
                $"warm-up                      {warmUp,7:F2}   (once per process, on a background thread of the host; asserted <= {WarmUpMilliseconds:F0})",
                $"first compile after warm-up  total {first.Total,7:F2}   parse+bind+lint {first.Compile,7:F2}   merge {first.Merge,5:F2}   frame {first.Frame,5:F2}   delegates (Bind) {first.Bind,6:F2}   JIT {first.JitMethods} methods, {first.JitMilliseconds:F1} ms",
                $"second compile               total {second.Total,7:F2}",
                $"first without the frame: {first.WithoutFrame,6:F2} ms (asserted <= {FirstCompileAfterWarmUpMilliseconds:F0}); the JIT methods are what the warm-up did not cover",
                Metric("warmup_ms", warmUp),
                Metric("after_warmup_first_ms", first.WithoutFrame),
                Metric("after_warmup_jit_methods", first.JitMethods),
            ]);
        first.WithoutFrame.ShouldBeLessThanOrEqualTo(FirstCompileAfterWarmUpMilliseconds, "target: the first compile of a 10-condition strategy in a process that has run the warm-up");
        warmUp.ShouldBeLessThanOrEqualTo(WarmUpMilliseconds, "target: the warm-up costs a core for no more than 150 ms, once");
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
