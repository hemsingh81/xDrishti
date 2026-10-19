using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Criteria 13 to 18: the compiled side of the example of doc 11 and of a bare ORB, the sentinel feature, the single feature list, what the frame needs and the layout check of <c>Bind</c>.</summary>
public sealed class CompiledSideTests
{
    private static FeatureSpec Spec(string name, Timeframe timeframe, double p1 = 0, PriceSource source = PriceSource.Close) => Specs.Of(name, timeframe, p1, source: source);

    [Fact]
    public void The_buy_side_of_the_example_of_doc_11_compiles_to_seven_features_in_the_order_of_the_model()
    {
        var result = Located.Compile(Defs.VwapRsiTrend());

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        var side = result.Compiled!;
        side.Features.ShouldBe(
        [
            Spec("ema", Timeframe.H1, 20),
            Spec("ema", Timeframe.H1, 50),
            Spec("rsi", Timeframe.M15, 14),
            Spec("close", Timeframe.M15),
            Spec("vwap", Timeframe.M15),
            Spec("lowest", Timeframe.M15, 5, PriceSource.Low),
            Spec("atr", Timeframe.M15, 14),
        ]);
        side.HistoryDepth.ShouldBe(2);
        side.NeedsContext.ShouldBeFalse();
        side.NeedsExpiryDays.ShouldBeFalse();
        (side.StrategyId, side.Side, side.Style, side.EvaluationTimeframe).ShouldBe(("VWAP_RSI_TREND", Side.Buy, Style.Intraday, Timeframe.M15));
    }

    [Fact]
    public void The_example_has_the_item_paths_the_stop_plan_the_exit_plans_the_session_rules_and_three_warnings()
    {
        var side = Located.Ok(Defs.VwapRsiTrend());

        side.Items.Select(i => i.Path).ShouldBe(["buy.setup[0]", "buy.setup[1]", "buy.setup[2]", "buy.trigger[0]"]);
        side.Items.Select(i => i.Condition.Source).ShouldBe(["time >= \"09:45\"", "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)", "rsi(rsi_len) > rsi_min", "crosses_above(close, vwap)"]);
        side.Setup!.Path.ShouldBe("buy.setup");
        side.Setup.Mode.ShouldBe(ListMode.All);
        side.Setup.Items.Count.ShouldBe(3);
        side.Trigger.Items.Count.ShouldBe(1);
        side.Invalidate.ShouldBeNull();
        side.Entry.ShouldBe(EntryKind.AtClose);
        side.Stop.ShouldBe(new StopPlan(StopKind.SwingLow, Timeframe.M15, Spec("lowest", Timeframe.M15, 5, PriceSource.Low), Spec("atr", Timeframe.M15, 14), null, null, 0.1));
        side.Exits.Count.ShouldBe(2);
        side.Exits[0].Policy.ShouldBe(ExitPolicy.FixedRr);
        side.Exits[0].Values["rr"].ShouldBe([2.0]);
        side.Exits[0].Reads.ShouldBeEmpty();
        side.Exits[1].Policy.ShouldBe(ExitPolicy.PartialTrail);
        side.Exits[1].Reads.ShouldBe([Spec("atr", Timeframe.M15, 14)]);
        side.Exits[1].Values["atr_period"].ShouldBe([14.0]);
        side.Overlays.ShouldBeEmpty();
        side.TimeRules.ShouldBe(new SessionRules(null, 870, 915, null));
        side.Filters.ShouldBe(["not_event_day", "min_turnover"]);
        side.Warnings.Select(w => (w.Path, w.Problem.Code)).ShouldBe([("buy.exits[1]", ModelCodes.ExitNotEvaluated), ("filters[0]", ModelCodes.FilterNotEvaluated), ("filters[1]", ModelCodes.FilterNotEvaluated)]);
    }

    [Fact]
    public void The_example_compiles_with_no_error_and_only_those_three_problems_in_all()
    {
        var result = Located.Compile(Defs.VwapRsiTrend());

        result.Errors.ShouldBeEmpty();
        result.Problems.Count.ShouldBe(3);
    }

    // ---- criterion 14: the bare ORB -----------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.M5)]
    public void A_bare_ORB_has_the_close_and_the_two_ends_of_the_opening_range_on_the_evaluation_timeframe(Timeframe evaluation)
    {
        var buy = Located.Ok(Defs.Orb(), evaluation, Side.Buy);
        var sell = Located.Ok(Defs.Orb(), evaluation, Side.Sell);

        buy.Features.ShouldBe([Spec("close", evaluation), Spec("or_high", evaluation, 15), Spec("or_low", evaluation, 15)]);
        sell.Features.ShouldBe([Spec("close", evaluation), Spec("or_low", evaluation, 15), Spec("or_high", evaluation, 15)]);
        buy.HistoryDepth.ShouldBe(2);
        sell.HistoryDepth.ShouldBe(2);
        buy.Warnings.ShouldBeEmpty();
        sell.Warnings.ShouldBeEmpty();
        buy.Stop.Level.ShouldBe(Spec("or_low", evaluation, 15));
        sell.Stop.Level.ShouldBe(Spec("or_high", evaluation, 15));
        buy.TimeRules.ShouldBe(new SessionRules(570, 690, 915, null));
    }

    [Theory]
    [InlineData(Timeframe.M10)]
    [InlineData(Timeframe.M30)]
    public void A_bare_ORB_on_a_timeframe_that_does_not_divide_15_minutes_reports_the_cell_as_not_applicable_and_does_not_throw(Timeframe evaluation)
    {
        var definition = Defs.Orb(Timeframe.M15, Timeframe.M5, Timeframe.M10, Timeframe.M30);
        var standalone = ConditionCompiler.Compile(definition.Buy!.Trigger!.Items[0], new CompileContext(evaluation, new Dictionary<string, double> { ["or_min"] = 15 }));

        var result = Located.Compile(definition, evaluation, Side.Buy);

        result.IsSuccess.ShouldBeFalse();
        var inTrigger = result.Problems.Where(p => p.Path == "buy.trigger[0]").Select(p => p.Problem).ToList();
        inTrigger.ShouldBe(standalone.Problems);
        inTrigger.ShouldAllBe(p => p.Code == LintCodes.BadArguments);
        result.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.minutes");
    }

    [Fact]
    public void The_sell_block_compiles_without_the_buy_block_and_a_fault_in_the_rules_of_one_side_never_changes_the_result_for_the_other()
    {
        var clean = Defs.Orb();
        var brokenBuy = clean.WithBuy(b => b with { Trigger = Defs.All("closee > 1", "rsi >"), Entry = null, Stop = null, Exits = [] });

        var sellOfClean = Located.Compile(clean, Timeframe.M15, Side.Sell);
        var sellOfBroken = Located.Compile(brokenBuy, Timeframe.M15, Side.Sell);
        var sellAlone = Located.Compile(clean with { Buy = null, Sides = [Side.Sell] }, Timeframe.M15, Side.Sell);
        var buyOfBrokenSell = Located.Compile(clean.WithSell(s => s with { Stop = null }), Timeframe.M15, Side.Buy);

        sellOfBroken.IsSuccess.ShouldBeTrue();
        sellOfBroken.Problems.ShouldBe(sellOfClean.Problems);
        sellOfBroken.Compiled!.Features.ShouldBe(sellOfClean.Compiled!.Features);
        sellAlone.IsSuccess.ShouldBeTrue();
        sellAlone.Compiled!.Features.ShouldBe(sellOfClean.Compiled.Features);
        buyOfBrokenSell.IsSuccess.ShouldBeTrue();
    }

    // ---- criterion 15: the sentinel -------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_feature_read_by_the_trigger_the_stop_and_an_exit_is_listed_once()
    {
        var definition = Defs.Basic().WithBuy(b => b with
        {
            Trigger = Defs.All("close > or_low(15) and atr(14) > 1"),
            Stop = new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15), BufferAtr: Quantity.Of(0.5), AtrPeriod: Quantity.Of(14)),
            Exits = [Defs.PartialTrail()],
        });

        var side = Located.Ok(definition);

        side.Features.GroupBy(f => f).Where(g => g.Count() > 1).ShouldBeEmpty("a feature that several parts read is in the list once");
        side.Features.ShouldContain(Spec("atr", Timeframe.M15, 14));
        side.Features.ShouldContain(Spec("or_low", Timeframe.M15, 15));
        side.Features.Count(f => f.Equals(Spec("atr", Timeframe.M15, 14))).ShouldBe(1);
        side.Features.Count(f => f.Equals(Spec("close", Timeframe.M15))).ShouldBe(1);
    }

    [Fact]
    public void A_side_that_reads_only_1h_features_gets_a_close_on_the_evaluation_timeframe_as_the_last_feature()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All("tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"), Trigger = Defs.All("tf(\"1h\").close > tf(\"1h\").ema(20)"), Stop = Defs.FixedPct(1.0) });

        var side = Located.Ok(definition);

        side.Features.ShouldBe([Spec("ema", Timeframe.H1, 20), Spec("ema", Timeframe.H1, 50), Spec("close", Timeframe.H1), Spec("close", Timeframe.M15)]);
    }

    [Fact]
    public void A_frame_made_from_the_sentinel_list_has_a_lane_for_the_evaluation_timeframe_and_BarClosed_is_false_at_09_28_and_true_at_09_29()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All("tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"), Trigger = Defs.All("tf(\"1h\").close > tf(\"1h\").ema(20)") });
        var rig = SideRig.Of(Located.Ok(definition));

        rig.Frame.HasTimeframe(Timeframe.M15).ShouldBeTrue();
        var closed = new List<bool>();
        for (var minute = Clock.Open; minute <= Clock.At(9, 29); minute++)
        {
            rig.Step(Clock.Close(Clock.Mon, minute, 100));
            closed.Add(rig.Frame.BarClosed(Timeframe.M15));
        }

        closed.Take(closed.Count - 1).ShouldAllBe(c => !c, "false up to and including the 09:28 minute");
        closed[^1].ShouldBeTrue("the 09:29 minute closes the 09:15 bar of 15m");
    }

    [Fact]
    public void A_side_whose_only_condition_is_time_has_just_the_sentinel_and_a_history_depth_of_1()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = Defs.All("time >= \"09:45\"") });

        var side = Located.Ok(definition);

        side.Features.ShouldBe([Spec("close", Timeframe.M15)]);
        side.HistoryDepth.ShouldBe(1);
        FeatureFrame.Create(side.Features, new FrameOptions { HistoryDepth = 1 }).IsSuccess.ShouldBeTrue("a frame cannot be created from an empty list; this is the case the sentinel exists for");
    }

    [Fact]
    public void A_side_that_reads_a_feature_on_the_evaluation_timeframe_has_no_sentinel_and_no_duplicate_close()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All("tf(\"1h\").close > 1"), Trigger = Defs.All("close > open") });

        var side = Located.Ok(definition);

        side.Features.ShouldBe([Spec("close", Timeframe.H1), Spec("close", Timeframe.M15), Spec("open", Timeframe.M15)]);
    }

    [Fact]
    public void A_stop_that_reads_a_feature_on_the_evaluation_timeframe_is_the_feature_on_it_so_there_is_no_sentinel()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = Defs.All("tf(\"1h\").close > 1"), Stop = new StopSpec(StopKind.BarLow) });

        var side = Located.Ok(definition);

        side.Features.ShouldBe([Spec("close", Timeframe.H1), Spec("low", Timeframe.M15)]);
    }

    // ---- criterion 16: one feature list ---------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_stop_and_two_spellings_of_the_same_lowest_in_the_triggers_give_exactly_one_feature()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = Defs.All("close > lowest(5)", "close > lowest(5, source=low)"), Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5)) });

        var side = Located.Ok(definition);

        side.Features.Count(f => f.Name == "lowest").ShouldBe(1);
        side.Features.ShouldBe([Spec("close", Timeframe.M15), Spec("lowest", Timeframe.M15, 5, PriceSource.Low)]);
    }

    private static IEnumerable<StopSpec> StopsFor(Side side)
    {
        yield return new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(1));
        yield return new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2));
        yield return new StopSpec(StopKind.AtrMult, Timeframe.H1, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(7));
        yield return new StopSpec(side == Side.Buy ? StopKind.SwingLow : StopKind.SwingHigh, Lookback: Quantity.Of(5));
        yield return new StopSpec(side == Side.Buy ? StopKind.SwingLow : StopKind.SwingHigh, Lookback: Quantity.Of(3), BufferAtr: Quantity.Of(0.2));
        yield return new StopSpec(side == Side.Buy ? StopKind.SwingLow : StopKind.SwingHigh, Timeframe.D1, Lookback: Quantity.Of(3), BufferAtr: Quantity.Of(0.2), AtrPeriod: Quantity.Of(10));
        yield return new StopSpec(side == Side.Buy ? StopKind.BarLow : StopKind.BarHigh);
        yield return new StopSpec(side == Side.Buy ? StopKind.BarLow : StopKind.BarHigh, BufferAtr: Quantity.Of(0.5));
        yield return new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(30));
        yield return new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(60), BufferAtr: Quantity.Of(1));
    }

    public static TheoryData<Timeframe> Evaluations { get; } = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1];

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void For_every_stop_kind_and_exit_on_each_entry_timeframe_and_both_sides_a_side_that_compiles_makes_a_frame_whose_handles_are_the_positions(Timeframe evaluation)
    {
        var compiled = 0;
        var refused = 0;
        ExitSpec[][] exitSets = [[Defs.FixedRr(2)], [Defs.PartialTrail()], [Defs.FixedRr(1.5, 3), Defs.PartialTrail(40, 1, 3)]];
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            foreach (var stop in StopsFor(side))
            {
                foreach (var exits in exitSets)
                {
                    var rules = new SideRules(null, Defs.All(side == Side.Buy ? "close > open" : "close < open"), null, new EntrySpec(EntryKind.AtClose), stop, exits, null);
                    var definition = Defs.Basic() with
                    {
                        Style = evaluation == Timeframe.D1 || evaluation == Timeframe.M30 || evaluation == Timeframe.H1 ? Style.Swing : Style.Intraday,
                        Timeframes = [evaluation],
                        Sides = [Side.Buy, Side.Sell],
                        Buy = rules,
                        Sell = rules,
                        TimeRules = evaluation is Timeframe.D1 or Timeframe.M30 or Timeframe.H1 ? new TimeRules(null, null, null, 3) : Defs.IntradayRules,
                    };
                    if (definition.Style == Style.Swing)
                    {
                        definition = definition with { Sides = [Side.Buy], Sell = null };
                        if (side == Side.Sell)
                        {
                            continue;
                        }
                    }

                    var result = Located.Compile(definition, evaluation, side);
                    if (!result.IsSuccess)
                    {
                        refused++;
                        continue;
                    }

                    compiled++;
                    var features = result.Compiled!.Features;
                    var frame = FeatureFrame.Create(features, new FrameOptions { HistoryDepth = result.Compiled.HistoryDepth });
                    frame.IsSuccess.ShouldBeTrue(evaluation + " " + side + " " + stop.Kind + ": " + (frame.IsFailure ? frame.Error.Message : string.Empty));
                    for (var i = 0; i < features.Count; i++)
                    {
                        frame.Value.HandleOf(features[i]).ShouldBe(i, evaluation + " " + side + " " + stop.Kind + " feature " + i);
                    }

                    frame.Value.Count.ShouldBe(features.Count, "no two specs that the frame would treat as one");
                    features.Count(f => f.Timeframe == evaluation).ShouldBeGreaterThan(0, "the sentinel guarantees a lane for the evaluation timeframe");
                }
            }
        }

        compiled.ShouldBeGreaterThan(10, "the property is not vacuous for " + evaluation);
        (compiled + refused).ShouldBeGreaterThan(0);
    }

    // ---- criterion 17: needs --------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_condition_on_the_regime_makes_the_side_need_a_context_and_the_note_is_in_the_problems_and_not_in_the_warnings()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = Defs.All("regime() == \"UP_LOW_VOL\"") });

        var result = Located.Compile(definition);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        result.Compiled!.NeedsContext.ShouldBeTrue();
        result.Compiled.NeedsExpiryDays.ShouldBeFalse();
        result.ShouldHave(LintCodes.NeedsContext, "buy.trigger[0]", ProblemSeverity.Information);
        result.Compiled.Warnings.ShouldBeEmpty();
        FeatureFrame.Create(result.Compiled.Features, new FrameOptions { HistoryDepth = result.Compiled.HistoryDepth }).IsFailure.ShouldBeTrue("a frame made without the context is refused as before");
    }

    [Fact]
    public void A_condition_on_is_expiry_day_makes_the_side_need_the_expiry_dates()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = Defs.All("is_expiry_day == 1") });

        var result = Located.Compile(definition);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        result.Compiled!.NeedsExpiryDays.ShouldBeTrue();
        result.Compiled.NeedsContext.ShouldBeFalse();
        result.ShouldHave(LintCodes.NeedsExpiryDays, "buy.trigger[0]", ProblemSeverity.Information);
        FeatureFrame.Create(result.Compiled.Features, new FrameOptions { HistoryDepth = result.Compiled.HistoryDepth }).IsFailure.ShouldBeTrue();
        FeatureFrame.Create(result.Compiled.Features, new FrameOptions { HistoryDepth = result.Compiled.HistoryDepth, ExpiryDays = new HashSet<DateOnly>() }).IsSuccess.ShouldBeTrue();
    }

    // ---- criterion 18: layout --------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static CompiledSide Example() => Located.Ok(Defs.VwapRsiTrend());

    [Fact]
    public void Bind_succeeds_for_a_frame_made_from_the_features_with_the_history_depth_and_for_a_frame_with_more_features_after_them()
    {
        var side = Example();

        var exact = FeatureFrame.Create(side.Features, new FrameOptions { HistoryDepth = side.HistoryDepth }).Value;
        var more = FeatureFrame.Create([.. side.Features, Specs.Of("sma", Timeframe.M15, 20)], new FrameOptions { HistoryDepth = side.HistoryDepth + 3 }).Value;

        side.Bind(exact).Side.ShouldBeSameAs(side);
        side.Bind(more).Side.ShouldBeSameAs(side);
    }

    [Fact]
    public void Bind_throws_naming_the_feature_when_the_frame_has_the_features_in_another_order_or_one_is_missing()
    {
        var side = Example();
        var reversed = FeatureFrame.Create([.. side.Features.Reverse()], new FrameOptions { HistoryDepth = side.HistoryDepth }).Value;
        var missing = FeatureFrame.Create([.. side.Features.Where(f => f.Name != "vwap")], new FrameOptions { HistoryDepth = side.HistoryDepth }).Value;
        var swapped = FeatureFrame.Create([side.Features[1], side.Features[0], .. side.Features.Skip(2)], new FrameOptions { HistoryDepth = side.HistoryDepth }).Value;

        Should.Throw<ArgumentException>(() => side.Bind(reversed)).Message.ShouldContain("another order");
        Should.Throw<ArgumentException>(() => side.Bind(missing)).Message.ShouldContain("vwap");
        Should.Throw<ArgumentException>(() => side.Bind(swapped)).Message.ShouldContain("ema(20)");
    }

    [Fact]
    public void Bind_throws_naming_the_depth_when_the_frame_keeps_less_history_than_the_side_needs()
    {
        var side = Example();
        var shallow = FeatureFrame.Create(side.Features, new FrameOptions { HistoryDepth = 1 }).Value;

        var error = Should.Throw<ArgumentException>(() => side.Bind(shallow));

        error.Message.ShouldContain("history depth of 2");
        error.Message.ShouldContain("keeps 1");
    }

    [Fact]
    public void Bind_of_a_null_frame_is_an_argument_null_exception()
    {
        Should.Throw<ArgumentNullException>(() => Example().Bind(null!));
    }
}
