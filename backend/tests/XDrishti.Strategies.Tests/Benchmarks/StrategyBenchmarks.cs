using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Benchmarks;

/// <summary>
/// The budgets of the strategy compile (spec P3-02b, <i>Non-functional</i>), measured by <b>explicit</b> tests that <c>dotnet test</c> and <c>scripts/check.sh</c> skip, like the ones of <see cref="EvaluationBenchmarks"/>
/// (ISS-12). Run them in Release, one at a time and each in a fresh process:
/// <c>dotnet test --project tests/XDrishti.Strategies.Tests -c Release -- --explicit only --filter-method "*Compile_the_example_side_first_run_after_the_warmup*"</c>.
/// <list type="bullet">
/// <item>A warm <c>Compile</c> of the example side of doc 11 (4 items, 7 features) takes at most 3 ms, and the model checks and the merge add under 0.5 ms to the compile of the items (asserted).</item>
/// <item>The first compile after <see cref="CompilerWarmup.Run"/> takes at most 20 ms, and the warm-up itself at most 150 ms (asserted).</item>
/// <item>Evaluating a side of ten simple items takes at most 0.4 µs per call (asserted); zero allocation is asserted in <c>check.sh</c> (<c>SideAllocationTests</c>).</item>
/// </list>
/// These three are unguarded budgets: <c>check.sh</c> does not run them, so a regression is found by running them again (ADR 0036, the text of ISS-12).
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class StrategyBenchmarks
{
    private const double WarmCompileMilliseconds = 3;

    private const double ModelOverheadMilliseconds = 0.5;

    private const double FirstCompileAfterWarmUpMilliseconds = 20;

    private const double WarmUpMilliseconds = 150;

    private const double EvaluateNanoseconds = 400;

    private static void Report(string title, IEnumerable<string> lines) =>
        TestContext.Current.TestOutputHelper?.WriteLine(title + "\n" + string.Join("\n", lines.Select(l => "  " + l)));

    private static string Metric(string name, double value) => string.Create(CultureInfo.InvariantCulture, $"metric {name} {value:F3}");

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();
        return ordered[ordered.Count / 2];
    }

    [Fact(Explicit = true)]
    public void Compile_the_example_side_warm_and_what_the_model_adds_to_the_items()
    {
        var definition = Defs.VwapRsiTrend();
        var items = definition.Buy!.Setup!.Items.Concat(definition.Buy.Trigger!.Items).ToList();
        var context = new CompileContext(Timeframe.M15, definition.Params.ToDictionary(p => p.Name, p => p.Value));
        for (var i = 0; i < 300; i++)
        {
            _ = StrategyCompiler.Compile(definition, Timeframe.M15, Side.Buy);
            foreach (var item in items)
            {
                _ = ConditionCompiler.Compile(item, context);
            }
        }

        var whole = new List<double>();
        var itemsOnly = new List<double>();
        for (var run = 0; run < 200; run++)
        {
            var watch = Stopwatch.StartNew();
            var result = StrategyCompiler.Compile(definition, Timeframe.M15, Side.Buy);
            whole.Add(watch.Elapsed.TotalMilliseconds);
            result.IsSuccess.ShouldBeTrue();
            watch.Restart();
            foreach (var item in items)
            {
                _ = ConditionCompiler.Compile(item, context);
            }

            itemsOnly.Add(watch.Elapsed.TotalMilliseconds);
        }

        var compile = Median(whole);
        var itemCompile = Median(itemsOnly);
        Report(
            "Compile of the example side of doc 11, warm (milliseconds, median of 200)",
            [
                $"StrategyCompiler.Compile          {compile,7:F3}   (asserted <= {WarmCompileMilliseconds:F0})",
                $"the four items alone              {itemCompile,7:F3}",
                $"the model checks and the merge add {compile - itemCompile,6:F3}   (asserted <= {ModelOverheadMilliseconds:F1})",
                Metric("strategy_compile_warm_ms", compile),
                Metric("strategy_items_only_ms", itemCompile),
                Metric("strategy_model_overhead_ms", compile - itemCompile),
            ]);
        compile.ShouldBeLessThanOrEqualTo(WarmCompileMilliseconds, "target: a warm compile of the example side in 3 ms");
        (compile - itemCompile).ShouldBeLessThanOrEqualTo(ModelOverheadMilliseconds, "target: the model checks and the merge add under 0.5 ms to the compile of the items");
    }

    [Fact(Explicit = true)]
    public void Compile_the_example_side_first_run_after_the_warmup()
    {
        // As in EvaluationBenchmarks: both run on this thread, one after the other, which is what a host that has finished its warm-up looks like. The sets of the warm-up do not contain this definition.
        var watch = Stopwatch.StartNew();
        CompilerWarmup.Run();
        var warmUp = watch.Elapsed.TotalMilliseconds;
        var definition = Defs.VwapRsiTrend();
        var jitMethods = JitInfo.GetCompiledMethodCount(currentThread: true);
        var jitTime = JitInfo.GetCompilationTime(currentThread: true);

        watch.Restart();
        var result = StrategyCompiler.Compile(definition, Timeframe.M15, Side.Buy);
        var first = watch.Elapsed.TotalMilliseconds;
        var side = result.Compiled ?? throw new InvalidOperationException(Located.Show(result));
        var frame = FeatureFrame.Create(side.Features, new FrameOptions { HistoryDepth = side.HistoryDepth }).Value;
        watch.Restart();
        var bound = side.Bind(frame);
        var bind = watch.Elapsed.TotalMilliseconds;
        var jit = JitInfo.GetCompiledMethodCount(currentThread: true) - jitMethods;
        var jitMilliseconds = (JitInfo.GetCompilationTime(currentThread: true) - jitTime).TotalMilliseconds;
        watch.Restart();
        _ = StrategyCompiler.Compile(definition, Timeframe.M15, Side.Buy);
        var second = watch.Elapsed.TotalMilliseconds;

        Report(
            "Compile of the example side, first run after CompilerWarmup.Run (milliseconds)",
            [
                $"warm-up (conditions and strategies)  {warmUp,7:F2}   (asserted <= {WarmUpMilliseconds:F0})",
                $"first compile after the warm-up      {first,7:F2}   (asserted <= {FirstCompileAfterWarmUpMilliseconds:F0}); JIT {jit} methods, {jitMilliseconds:F1} ms",
                $"first Bind (builds the delegates)    {bind,7:F2}",
                $"second compile                       {second,7:F3}",
                Metric("strategy_warmup_ms", warmUp),
                Metric("strategy_after_warmup_first_ms", first),
                Metric("strategy_after_warmup_bind_ms", bind),
                Metric("strategy_after_warmup_jit_methods", jit),
            ]);
        bound.Side.ShouldBeSameAs(side);
        first.ShouldBeLessThanOrEqualTo(FirstCompileAfterWarmUpMilliseconds, "target: the first strategy compile in a process that has run the warm-up");
        warmUp.ShouldBeLessThanOrEqualTo(WarmUpMilliseconds, "target: the warm-up of the conditions and the strategies costs a core for no more than 150 ms, once");
    }

    [Fact(Explicit = true)]
    public void Compile_the_example_side_process_cold_without_a_warmup()
    {
        var definition = Defs.VwapRsiTrend();
        var jitMethods = JitInfo.GetCompiledMethodCount(currentThread: true);
        var watch = Stopwatch.StartNew();

        var result = StrategyCompiler.Compile(definition, Timeframe.M15, Side.Buy);

        var cold = watch.Elapsed.TotalMilliseconds;
        var jit = JitInfo.GetCompiledMethodCount(currentThread: true) - jitMethods;
        Report("Compile of the example side, process-cold, no warm-up (reported, not asserted)", [$"first compile {cold,7:F2} ms, JIT {jit} methods", Metric("strategy_process_cold_ms", cold)]);
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact(Explicit = true)]
    public void Evaluate_a_side_of_ten_simple_items()
    {
        var items = new[] { "close > open", "close > ema(20)", "rsi(14) > 50", "close < high", "volume > 0", "close > low", "ema(9) > ema(21)", "rsi(14) < 90", "close >= open", "adx(14) > 10" };
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All(items[0], items[1], items[2]), Trigger = Defs.All(items[3], items[4], items[5], items[6]), Invalidate = Defs.Any(items[7], items[8], items[9]) });
        var side = Located.Ok(definition);
        var rig = SideRig.Of(side);
        var bars = Series.Sessions(5, 30);
        foreach (var bar in bars.Take(4_000))
        {
            rig.Frame.Step(bar);
        }

        const int Calls = 2_000_000;
        var truths = new Truth[side.Items.Count];
        long sum = 0;
        for (var i = 0; i < 200_000; i++)
        {
            sum += rig.Bound.Evaluate(rig.Frame, truths).Fires ? 1 : 0;
        }

        var best = double.MaxValue;
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < Calls; i++)
            {
                sum += rig.Bound.Evaluate(rig.Frame, truths).Fires ? 1 : 0;
            }

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds * 1e6 / Calls);
        }

        var window = Stopwatch.StartNew();
        for (var i = 0; i < Calls; i++)
        {
            sum += rig.Bound.InEntryWindow(rig.Frame) ? 1 : 0;
        }

        var windowNs = window.Elapsed.TotalMilliseconds * 1e6 / Calls;
        Report(
            "Evaluate of a side of ten simple items, on a frame that stands still (nanoseconds per call, best of 5)",
            [
                $"Evaluate      {best,7:F1} ns   (asserted <= {EvaluateNanoseconds:F0})",
                $"InEntryWindow {windowNs,7:F1} ns",
                $"(checksum {sum})",
                Metric("side_evaluate_ns", best),
                Metric("side_window_ns", windowNs),
            ]);
        best.ShouldBeLessThanOrEqualTo(EvaluateNanoseconds, "target: 0.4 µs per call for ten simple items");
    }
}
