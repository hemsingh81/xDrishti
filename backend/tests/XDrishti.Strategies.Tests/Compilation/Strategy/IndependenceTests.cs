using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Criterion 28: the compile does not change the definition and keeps no reference to the caller's lists, two compiles share nothing mutable, and one compiled side serves many frames on many threads.</summary>
public sealed class IndependenceTests
{
    [Fact]
    public void Compile_does_not_change_the_definition_compared_element_by_element_before_and_after()
    {
        var definition = Defs.VwapRsiTrend();
        var untouched = Defs.VwapRsiTrend();

        _ = Located.Compile(definition);
        _ = Located.Compile(definition, Timeframe.M15, Side.Sell);
        _ = Located.Compile(Defs.Orb(), Timeframe.M10);

        definition.ShouldBe(untouched);
        definition.GetHashCode().ShouldBe(untouched.GetHashCode());
    }

    [Fact]
    public void The_compiled_side_keeps_no_reference_to_the_callers_lists()
    {
        var items = new List<string> { "close > open", "rsi(14) > 55" };
        var filters = new List<string> { FilterNames.NotEventDay };
        var rr = new List<Quantity> { Quantity.Of(2) };
        var parameters = new List<ExitParameter> { new("rr", rr) };
        var exits = new List<ExitSpec> { new(ExitPolicy.FixedRr, parameters) };
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = new ConditionList(ListMode.All, items), Exits = exits }) with { Filters = filters };
        var side = Located.Ok(definition);
        var sources = side.Items.Select(i => i.Condition.Source).ToList();

        items.Clear();
        items.Add("close < 0");
        filters.Clear();
        rr.Add(Quantity.Of(9));
        exits.Clear();
        parameters.Clear();

        side.Items.Select(i => i.Condition.Source).ShouldBe(sources);
        side.Filters.ShouldBe([FilterNames.NotEventDay]);
        side.Exits.Single().Values["rr"].ShouldBe([2.0]);
        side.Trigger.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void The_lists_of_a_compiled_side_cannot_be_changed_through_the_interfaces_they_are_exposed_as()
    {
        var side = Located.Ok(Defs.VwapRsiTrend());

        IsReadOnly(side.Features).ShouldBeTrue();
        IsReadOnly(side.Items).ShouldBeTrue();
        IsReadOnly(side.Exits).ShouldBeTrue();
        IsReadOnly(side.Overlays).ShouldBeTrue();
        IsReadOnly(side.Filters).ShouldBeTrue();
        IsReadOnly(side.Warnings).ShouldBeTrue();
        IsReadOnly(side.Trigger.Items).ShouldBeTrue();
        IsReadOnly(side.Setup!.Items).ShouldBeTrue();
        IsReadOnly(side.Exits[0].Reads).ShouldBeTrue();
        IsReadOnly(side.Exits[0].Values["rr"]).ShouldBeTrue();
        (side.Exits[0].Values is IDictionary<string, IReadOnlyList<double>> { IsReadOnly: false }).ShouldBeFalse();
    }

    private static bool IsReadOnly<T>(IReadOnlyList<T> list) => list is not ICollection<T> { IsReadOnly: false } && list is not T[];

    [Fact]
    public void Two_compiles_for_two_timeframes_share_nothing_mutable()
    {
        var definition = Defs.Orb();

        var fifteen = Located.Ok(definition, Timeframe.M15);
        var five = Located.Ok(definition, Timeframe.M5);
        var again = Located.Ok(definition, Timeframe.M15);

        fifteen.Features.ShouldNotBeSameAs(five.Features);
        fifteen.Features.ShouldNotBeSameAs(again.Features);
        fifteen.Items.ShouldNotBeSameAs(again.Items);
        fifteen.Exits[0].Values.ShouldNotBeSameAs(again.Exits[0].Values);
        fifteen.Features.ShouldBe(again.Features);
        fifteen.Features.ShouldNotBe(five.Features);
        fifteen.Items[0].Condition.ShouldNotBeSameAs(again.Items[0].Condition);
    }

    [Fact]
    public async Task Sixteen_threads_that_bind_one_compiled_side_to_sixteen_frames_at_once_give_the_decisions_of_one_thread()
    {
        var side = Located.Ok(Defs.Basic().WithBuy(b => b with { Setup = Defs.All("rsi(14) > 45", "tf(\"1h\").close > 1"), Trigger = Defs.Any("crosses_above(close, vwap)", "close > open and close > ema(20)"), Invalidate = Defs.All("rsi(14) > 90") }));
        var bars = Series.Sessions(21, 5);
        var expected = Run(side, bars);

        var results = new List<SideEvaluation>[16];
        using var barrier = new Barrier(16);
        var tasks = Enumerable.Range(0, 16).Select(i => Task.Factory.StartNew(
            () =>
            {
                barrier.SignalAndWait();
                results[i] = Run(side, bars);
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)).ToArray();
        await Task.WhenAll(tasks);

        expected.Count.ShouldBeGreaterThan(100);
        expected.Any(e => e.Fires).ShouldBeTrue("the run contains decisions that fire");
        expected.Any(e => !e.Fires).ShouldBeTrue("and decisions that do not");
        expected.Any(e => e.Setup == Truth.Unknown).ShouldBeTrue("and unknown ones");
        foreach (var result in results)
        {
            result.ShouldBe(expected);
        }
    }

    private static List<SideEvaluation> Run(CompiledSide side, Bar[] bars)
    {
        var rig = SideRig.Of(side);
        var log = new List<SideEvaluation>();
        foreach (var bar in bars)
        {
            rig.Frame.Step(bar);
            if (rig.Frame.BarClosed(side.EvaluationTimeframe))
            {
                log.Add(rig.Evaluate());
            }
        }

        return log;
    }

    [Fact]
    public async Task Sixteen_threads_that_compile_the_same_definition_get_equal_results()
    {
        var definition = Defs.VwapRsiTrend();
        var expected = Located.Compile(definition);

        var results = new StrategyCompileResult[16];
        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() => results[i] = Located.Compile(definition), TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            result.Problems.ShouldBe(expected.Problems);
            result.Compiled!.Features.ShouldBe(expected.Compiled!.Features);
            result.Compiled.HistoryDepth.ShouldBe(expected.Compiled.HistoryDepth);
        }
    }

    [Fact]
    public void A_frame_has_the_same_layout_for_every_instrument_so_one_side_runs_over_many_frames_in_turn()
    {
        var side = Located.Ok(Defs.VwapRsiTrend());
        var bars = Series.Sessions(5, 2);

        Run(side, bars).ShouldBe(Run(side, bars));
        FeatureFrame.Create(side.Features, new FrameOptions { HistoryDepth = side.HistoryDepth }).IsSuccess.ShouldBeTrue();
    }
}
