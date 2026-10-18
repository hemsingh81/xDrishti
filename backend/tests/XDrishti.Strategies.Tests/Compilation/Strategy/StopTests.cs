using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Criteria 10 and 12: the stop kinds of the <i>Contract</i> table, a valid example and the failing rows of each, and the quantity that names a parameter. The tables are literals written from the spec's table, not driven
/// from the validator's data.
/// </summary>
public sealed class StopTests
{
    private static Quantity Q(double value) => Quantity.Of(value);

    private static StrategyDefinition With(StopSpec stop, Side side = Side.Buy, params StrategyParam[] parameters) =>
        (side == Side.Buy ? Defs.Basic().WithBuy(b => b with { Stop = stop }) : Defs.Basic().WithSell(s => s with { Stop = stop })) with { Params = parameters };

    private static string PathOf(Side side) => side == Side.Buy ? "buy" : "sell";

    // ---- the valid examples: plan, features ---------------------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, StopSpec, Side, Timeframe, string, string?, string?> Valid { get; } = new()
    {
        // name, stop, side, evaluation, level, atr, expectation of pct/mult
        { "fixed_pct", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(1.5)), Side.Buy, Timeframe.M15, string.Empty, null, "pct 1.5" },
        { "fixed_pct sell", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(50)), Side.Sell, Timeframe.M5, string.Empty, null, "pct 50" },
        { "atr_mult", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2)), Side.Buy, Timeframe.M15, string.Empty, "atr/M15/14", "mult 2" },
        { "atr_mult with a period", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(20), AtrPeriod: Quantity.Of(10)), Side.Sell, Timeframe.H1, string.Empty, "atr/H1/10", "mult 20" },
        { "swing_low", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5)), Side.Buy, Timeframe.M15, "lowest/M15/5/Low", null, null },
        { "swing_low with a buffer", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5), BufferAtr: Quantity.Of(0.1)), Side.Buy, Timeframe.M15, "lowest/M15/5/Low", "atr/M15/14", null },
        { "swing_low with a zero buffer reads no atr", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5), BufferAtr: Quantity.Of(0)), Side.Buy, Timeframe.M15, "lowest/M15/5/Low", null, null },
        { "swing_high", new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Of(8), BufferAtr: Quantity.Of(5), AtrPeriod: Quantity.Of(7)), Side.Sell, Timeframe.M30, "highest/M30/8/High", "atr/M30/7", null },
        { "bar_low", new StopSpec(StopKind.BarLow), Side.Buy, Timeframe.M15, "low/M15/0/Close", null, null },
        { "bar_low with a buffer", new StopSpec(StopKind.BarLow, BufferAtr: Quantity.Of(0.3)), Side.Buy, Timeframe.M15, "low/M15/0/Close", "atr/M15/14", null },
        { "bar_high", new StopSpec(StopKind.BarHigh), Side.Sell, Timeframe.M15, "high/M15/0/Close", null, null },
        { "or_opposite buy", new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15)), Side.Buy, Timeframe.M15, "or_low/M15/15/Close", null, null },
        { "or_opposite sell on 5m", new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(30)), Side.Sell, Timeframe.M5, "or_high/M5/30/Close", null, null },
        { "swing_low on 1h at 15m", new StopSpec(StopKind.SwingLow, Timeframe.H1, Lookback: Quantity.Of(3)), Side.Buy, Timeframe.M15, "lowest/H1/3/Low", null, null },
        { "swing_low on a finer timeframe than the evaluation", new StopSpec(StopKind.SwingLow, Timeframe.M5, Lookback: Quantity.Of(3)), Side.Buy, Timeframe.M15, "lowest/M5/3/Low", null, null },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void A_valid_stop_compiles_to_the_plan_and_the_features_of_the_table(string name, StopSpec stop, Side side, Timeframe evaluation, string level, string? atr, string? number)
    {
        var definition = With(stop, side) with { Timeframes = [Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1] };

        var compiled = Located.Ok(definition, evaluation, side);

        var plan = compiled.Stop;
        plan.Kind.ShouldBe(stop.Kind, name);
        plan.Timeframe.ShouldBe(stop.Timeframe ?? evaluation, name);
        Show(plan.Level).ShouldBe(level.Length == 0 ? "-" : level, name);
        Show(plan.Atr).ShouldBe(atr ?? "-", name);
        (plan.Pct is { } p ? "pct " + p : plan.Mult is { } m ? "mult " + m : null).ShouldBe(number, name);
        plan.BufferAtr.ShouldBe(stop.BufferAtr?.Number ?? 0, name);
        foreach (var spec in new[] { plan.Level, plan.Atr }.OfType<XDrishti.Indicators.FeatureSpec>())
        {
            compiled.Features.ShouldContain(spec, name);
        }
    }

    private static string Show(XDrishti.Indicators.FeatureSpec? spec) => spec is { } s ? s.Name + "/" + s.Timeframe + "/" + s.P1 + (s.Name is "lowest" or "highest" or "low" or "high" or "or_low" or "or_high" ? "/" + s.Source : string.Empty) : "-";

    // ---- the failing rows ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, StopSpec, Side, Timeframe, string, string> Failing { get; } = new()
    {
        { "swing_low without lookback", new StopSpec(StopKind.SwingLow), Side.Buy, Timeframe.M15, "model.field_missing", "stop.lookback" },
        { "swing_high without lookback", new StopSpec(StopKind.SwingHigh), Side.Sell, Timeframe.M15, "model.field_missing", "stop.lookback" },
        { "or_opposite without minutes", new StopSpec(StopKind.OrOpposite), Side.Buy, Timeframe.M15, "model.field_missing", "stop.minutes" },
        { "fixed_pct without pct", new StopSpec(StopKind.FixedPct), Side.Buy, Timeframe.M15, "model.field_missing", "stop.pct" },
        { "atr_mult without mult", new StopSpec(StopKind.AtrMult), Side.Buy, Timeframe.M15, "model.field_missing", "stop.mult" },
        { "pct on atr_mult", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), Pct: Quantity.Of(1)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.pct" },
        { "timeframe on fixed_pct", new StopSpec(StopKind.FixedPct, Timeframe.H1, Pct: Quantity.Of(1)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.timeframe" },
        { "mult on fixed_pct", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(1), Mult: Quantity.Of(2)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.mult" },
        { "lookback on bar_low", new StopSpec(StopKind.BarLow, Lookback: Quantity.Of(5)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.lookback" },
        { "minutes on swing_low", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5), Minutes: Quantity.Of(15)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.minutes" },
        { "atr_period on fixed_pct", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(1), AtrPeriod: Quantity.Of(14)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.atr_period" },
        { "buffer_atr on atr_mult", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), BufferAtr: Quantity.Of(1)), Side.Buy, Timeframe.M15, "model.field_not_used", "stop.buffer_atr" },
        { "swing_high on BUY", new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Of(5)), Side.Buy, Timeframe.M15, "model.stop_wrong_side", "stop" },
        { "bar_high on BUY", new StopSpec(StopKind.BarHigh), Side.Buy, Timeframe.M15, "model.stop_wrong_side", "stop" },
        { "swing_low on SELL", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5)), Side.Sell, Timeframe.M15, "model.stop_wrong_side", "stop" },
        { "bar_low on SELL", new StopSpec(StopKind.BarLow), Side.Sell, Timeframe.M15, "model.stop_wrong_side", "stop" },
        { "pct 0", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(0)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.pct" },
        { "pct 51", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(51)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.pct" },
        { "pct negative", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(-1)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.pct" },
        { "mult 0", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(0)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.mult" },
        { "mult 20.5", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(20.5)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.mult" },
        { "buffer_atr -0.1", new StopSpec(StopKind.BarLow, BufferAtr: Quantity.Of(-0.1)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.buffer_atr" },
        { "buffer_atr 5.1", new StopSpec(StopKind.BarLow, BufferAtr: Quantity.Of(5.1)), Side.Buy, Timeframe.M15, "model.value_out_of_range", "stop.buffer_atr" },
        { "lookback 0", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(0)), Side.Buy, Timeframe.M15, "model.feature_invalid", "stop.lookback" },
        { "lookback 2.5", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(2.5)), Side.Buy, Timeframe.M15, "model.feature_invalid", "stop.lookback" },
        { "lookback 10001", new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Of(10_001)), Side.Sell, Timeframe.M15, "model.feature_invalid", "stop.lookback" },
        { "atr_period 0", new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(0)), Side.Buy, Timeframe.M15, "model.feature_invalid", "stop.atr_period" },
        { "atr_period 14.5", new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5), BufferAtr: Quantity.Of(0.1), AtrPeriod: Quantity.Of(14.5)), Side.Buy, Timeframe.M15, "model.feature_invalid", "stop.atr_period" },
        { "or_opposite on a daily stop timeframe", new StopSpec(StopKind.OrOpposite, Timeframe.D1, Minutes: Quantity.Of(15)), Side.Buy, Timeframe.M15, "model.feature_invalid", "stop.timeframe" },
        { "or_opposite with minutes that are not whole bars", new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(20)), Side.Buy, Timeframe.M15, "model.feature_invalid", "stop.minutes" },
        { "or_opposite on a 1D evaluation timeframe", new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15)), Side.Buy, Timeframe.D1, "model.feature_invalid", "stop" },
        { "a pct that is not finite", new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(double.PositiveInfinity)), Side.Buy, Timeframe.M15, "model.bad_quantity", "stop.pct" },
    };

    [Theory]
    [MemberData(nameof(Failing))]
    public void A_stop_with_a_problem_gives_its_code_at_its_field_and_nothing_else(string name, StopSpec stop, Side side, Timeframe evaluation, string code, string field)
    {
        var definition = With(stop, side) with { Timeframes = [Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1] };
        if (evaluation == Timeframe.D1)
        {
            definition = definition with { Style = Style.Swing, Timeframes = [Timeframe.D1], Sides = [Side.Buy], TimeRules = new TimeRules(null, null, null, 3) };
        }

        var result = Located.Compile(definition, evaluation, side);

        var at = PathOf(side) + "." + field;
        result.ErrorList().ShouldBe([code + "@" + at], name + "\n" + Located.Show(result));
    }

    [Fact]
    public void A_stop_the_catalog_refuses_returns_the_catalogs_own_message()
    {
        var result = Located.Compile(With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(0))));
        var wholeNumber = Located.Compile(With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(2.5))));
        var bars = Located.Compile(With(new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(20))));

        result.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.lookback").Problem.Message.ShouldContain("must be between 1 and");
        wholeNumber.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.lookback").Problem.Message.ShouldContain("must be a whole number");
        bars.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.minutes").Problem.Message.ShouldContain("whole");
    }

    [Fact]
    public void A_stop_on_1D_at_15m_reads_the_last_closed_day_exactly_as_the_tf_1D_lowest_of_the_language_does()
    {
        var stop = new StopSpec(StopKind.SwingLow, Timeframe.D1, Lookback: Quantity.Of(5));

        var compiled = Located.Ok(With(stop));
        var language = Compiling.Condition("tf(\"1D\").lowest(5) > 0");

        compiled.Stop.Timeframe.ShouldBe(Timeframe.D1);
        compiled.Stop.Level.ShouldBe(language.Features[0]);
        compiled.Features.ShouldContain(language.Features[0]);
    }

    [Fact]
    public void A_stop_that_is_checked_twice_by_two_kinds_of_exit_features_does_not_report_one_catalog_problem_twice()
    {
        var stop = new StopSpec(StopKind.AtrMult, Timeframe.D1, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(0));

        var result = Located.Compile(With(stop));

        result.ErrorList().ShouldBe(["model.feature_invalid@buy.stop.atr_period"]);
    }

    // ---- criterion 12: quantities ----------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_misspelt_parameter_name_gives_unknown_param_at_the_field_with_the_closest_declared_name_as_the_suggestion()
    {
        var definition = With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("swing_lookbak")), Side.Buy, new StrategyParam("swing_lookback", 5));

        var result = Located.Compile(definition);

        var problem = result.ShouldHave(ModelCodes.UnknownParam, "buy.stop.lookback").Problem;
        problem.Suggestion.ShouldBe("swing_lookback");
        problem.Message.ShouldContain("Did you mean `swing_lookback`?");
        result.ErrorList().ShouldBe(["model.unknown_param@buy.stop.lookback"]);
    }

    [Fact]
    public void A_parameter_name_that_is_far_from_every_declared_one_gives_the_error_without_a_suggestion()
    {
        var definition = With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("zzz")), Side.Buy, new StrategyParam("swing_lookback", 5));

        var problem = Located.Compile(definition).ShouldHave(ModelCodes.UnknownParam, "buy.stop.lookback").Problem;

        problem.Suggestion.ShouldBeNull();
        problem.Message.ShouldNotContain("Did you mean");
    }

    [Fact]
    public void A_quantity_with_neither_or_both_set_or_a_literal_that_is_not_finite_gives_bad_quantity()
    {
        var neither = Located.Compile(With(new StopSpec(StopKind.FixedPct, Pct: new Quantity(null, null))));
        var both = Located.Compile(With(new StopSpec(StopKind.FixedPct, Pct: new Quantity(1, "aa")), Side.Buy, new StrategyParam("aa", 1)));
        var nan = Located.Compile(With(new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(double.NaN))));

        neither.ErrorList().ShouldBe(["model.bad_quantity@buy.stop.pct"]);
        both.ErrorList().ShouldBe(["model.bad_quantity@buy.stop.pct"]);
        nan.ErrorList().ShouldBe(["model.bad_quantity@buy.stop.pct"]);
    }

    [Fact]
    public void A_named_parameter_is_resolved_to_its_number_before_the_ranges_are_checked()
    {
        var literal = Located.Compile(With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(0))));
        var named = Located.Compile(With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("lb")), Side.Buy, new StrategyParam("lb", 0)));
        var fractional = Located.Compile(With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("lb")), Side.Buy, new StrategyParam("lb", 2.5)));
        var outOfRange = Located.Compile(With(new StopSpec(StopKind.FixedPct, Pct: Quantity.Named("lb")), Side.Buy, new StrategyParam("lb", 60)));
        var fine = Located.Ok(With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("lb")), Side.Buy, new StrategyParam("lb", 7)));

        named.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.lookback").Problem.Message.ShouldBe(literal.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.lookback").Problem.Message);
        fractional.ShouldHave(ModelCodes.FeatureInvalid, "buy.stop.lookback").Problem.Message.ShouldContain("must be a whole number");
        outOfRange.ShouldHave(ModelCodes.ValueOutOfRange, "buy.stop.pct");
        fine.Stop.Level!.Value.P1.ShouldBe(7);
    }

    [Fact]
    public void A_named_parameter_whose_value_is_not_finite_is_refused_where_it_is_used_as_well_as_at_its_declaration()
    {
        var definition = With(new StopSpec(StopKind.FixedPct, Pct: Quantity.Named("aa")), Side.Buy, new StrategyParam("aa", double.NaN));

        var result = Located.Compile(definition);

        result.ErrorList().ShouldBe(["model.bad_param_value@params[0]", "model.value_out_of_range@buy.stop.pct"]);
    }

    [Fact]
    public void A_param_used_only_in_a_stop_compiles_and_a_param_named_like_a_stop_field_is_just_a_param()
    {
        var definition = With(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("lookback")), Side.Buy, new StrategyParam("lookback", 4));

        var compiled = Located.Ok(definition);

        compiled.Stop.Level!.Value.P1.ShouldBe(4);
    }
}
