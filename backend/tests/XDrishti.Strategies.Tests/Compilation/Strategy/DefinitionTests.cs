using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Criteria 3 to 7: the checks of the definition level, whichever side is compiled (params, identity, timeframes, style, sides, time rules, filters).</summary>
public sealed class DefinitionTests
{
    private static StrategyDefinition Basic() => Defs.Basic();

    private static StrategyParam P(string name, double value = 1) => new(name, value);

    // ---- criterion 3: params ----------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Four_params_compile_and_a_fifth_gives_exactly_one_too_many_params_at_params()
    {
        Located.Compile(Basic() with { Params = [P("a"), P("b"), P("c"), P("d")] }).IsSuccess.ShouldBeTrue();

        var five = Located.Compile(Basic() with { Params = [P("a"), P("b"), P("c"), P("d"), P("e")] });
        var six = Located.Compile(Basic() with { Params = [P("a"), P("b"), P("c"), P("d"), P("e"), P("f")] });

        five.ShouldHave(ModelCodes.TooManyParams, "params");
        five.Problems.Count.ShouldBe(1, Located.Show(five));
        six.ErrorList().ShouldBe(["model.too_many_params@params"]);
    }

    [Fact]
    public void A_repeated_name_gives_duplicate_param_at_the_second_and_the_first_one_is_offered_to_the_conditions()
    {
        var definition = Basic().WithBuy(b => b with { Trigger = Defs.All("close > level") }) with { Params = [P("level", 5), P("level", 6)] };

        var result = Located.Compile(definition);

        result.ErrorList().ShouldBe(["model.duplicate_param@params[1]"], "the declaration is reported once and does not cascade into the item that uses it");
    }

    [Theory]
    [InlineData("RSI_LEN")]
    [InlineData("1x")]
    [InlineData("")]
    [InlineData("_x")]
    [InlineData("a-b")]
    [InlineData("MON")]
    public void A_name_that_is_not_lower_case_snake_gives_bad_param_name(string name)
    {
        var result = Located.Compile(Basic() with { Params = [P(name)] });

        result.ShouldHave(ModelCodes.BadParamName, "params[0]");
    }

    [Fact]
    public void A_name_of_33_characters_is_too_long_and_one_of_32_is_fine()
    {
        Located.Compile(Basic() with { Params = [P(new string('a', 33))] }).ShouldHave(ModelCodes.BadParamName, "params[0]");
        Located.Compile(Basic() with { Params = [P(new string('a', 32))] }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_null_param_or_a_null_name_is_a_bad_name_and_never_an_exception()
    {
        var result = Located.Compile(Basic() with { Params = [null!, new StrategyParam(null!, 1)] });

        result.ErrorList().ShouldBe(["model.bad_param_name@params[0]", "model.bad_param_name@params[1]"]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_finite_gives_bad_param_value(double value)
    {
        Located.Compile(Basic() with { Params = [P("aa", value)] }).ShouldHave(ModelCodes.BadParamValue, "params[0]");
    }

    [Theory]
    [InlineData("rsi")]
    [InlineData("time")]
    [InlineData("close")]
    [InlineData("rising")]
    [InlineData("abs")]
    public void A_param_named_like_a_feature_a_function_or_time_gives_the_warning_param_shadows_name(string name)
    {
        var result = Located.Compile(Basic() with { Params = [P(name)] });

        result.IsSuccess.ShouldBeTrue();
        result.ShouldHave(ModelCodes.ParamShadowsName, "params[0]", ProblemSeverity.Warning);
        result.Compiled!.Warnings.ShouldContain(w => w.Problem.Code == ModelCodes.ParamShadowsName);
    }

    [Fact]
    public void The_language_warning_shadowed_name_stays_on_each_item_that_uses_such_a_param_next_to_the_warning_at_the_declaration()
    {
        var definition = Basic().WithBuy(b => b with { Trigger = Defs.All("close > 1", "close < 100", "open > 1") }) with { Params = [P("close", 50)] };

        var result = Located.Compile(definition);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        result.ShouldHave(ModelCodes.ParamShadowsName, "params[0]", ProblemSeverity.Warning);
        result.ShouldHave(LintCodes.ShadowedName, "buy.trigger[0]", ProblemSeverity.Warning);
        result.ShouldHave(LintCodes.ShadowedName, "buy.trigger[1]", ProblemSeverity.Warning);
        result.At(LintCodes.ShadowedName, "buy.trigger[2]").ShouldBeEmpty();
    }

    [Fact]
    public void A_condition_that_calls_a_feature_with_the_name_of_a_param_gets_the_languages_bad_arguments_at_the_item_next_to_the_declaration_warning()
    {
        var definition = Basic().WithBuy(b => b with { Trigger = Defs.All("rsi(14) > 55") }) with { Params = [P("rsi", 14)] };

        var result = Located.Compile(definition);

        result.ShouldHave(LintCodes.BadArguments, "buy.trigger[0]");
        result.ShouldHave(ModelCodes.ParamShadowsName, "params[0]", ProblemSeverity.Warning);
        result.ErrorList().ShouldBe(["lint.bad_arguments@buy.trigger[0]"]);
    }

    [Fact]
    public void A_declaration_with_a_bad_name_is_reported_once_and_is_still_offered_to_the_items()
    {
        var definition = Basic().WithBuy(b => b with { Trigger = Defs.All("rsi(RSI_LEN) > 50") }) with { Params = [P("RSI_LEN", 14)] };

        var result = Located.Compile(definition);

        result.ErrorList().ShouldBe(["model.bad_param_name@params[0]"]);
    }

    // ---- criterion 4: identity, timeframes, style ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Schema_2_gives_schema_unsupported_at_schema()
    {
        Located.Compile(Basic() with { Schema = 2 }).ShouldHave(ModelCodes.SchemaUnsupported, "schema");
        Located.Compile(Basic() with { Schema = 0 }).ShouldHave(ModelCodes.SchemaUnsupported, "schema");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1x")]
    [InlineData("_x")]
    [InlineData("a b")]
    [InlineData("a-b")]
    public void An_id_that_is_empty_starts_with_a_digit_or_has_other_characters_gives_bad_id(string id)
    {
        Located.Compile(Basic() with { Id = id }).ShouldHave(ModelCodes.BadId, "id");
    }

    [Fact]
    public void An_id_of_65_characters_gives_bad_id_and_one_of_64_does_not_and_a_null_id_never_throws()
    {
        Located.Compile(Basic() with { Id = "A" + new string('b', 64) }).ShouldHave(ModelCodes.BadId, "id");
        Located.Compile(Basic() with { Id = "A" + new string('b', 63) }).IsSuccess.ShouldBeTrue();
        Located.Compile(Basic() with { Id = null! }).ShouldHave(ModelCodes.BadId, "id");
        Located.Compile(Basic() with { Id = "VWAP_RSI_TREND" }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void No_timeframes_gives_timeframes_empty_and_a_null_list_does_too()
    {
        Located.Compile(Basic() with { Timeframes = [] }).ShouldHave(ModelCodes.TimeframesEmpty, "timeframes");
        Located.Compile(Basic() with { Timeframes = null! }).ShouldHave(ModelCodes.TimeframesEmpty, "timeframes");
    }

    [Theory]
    [InlineData(Timeframe.M1)]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    [InlineData((Timeframe)42)]
    public void A_timeframe_that_is_not_an_entry_timeframe_gives_bad_timeframe_at_its_index(Timeframe timeframe)
    {
        var result = Located.Compile(Basic() with { Timeframes = [Timeframe.M15, timeframe] });

        result.ShouldHave(ModelCodes.BadTimeframe, "timeframes[1]");
        result.ErrorList().ShouldBe(["model.bad_timeframe@timeframes[1]"]);
    }

    [Fact]
    public void The_six_entry_timeframes_are_accepted()
    {
        foreach (var timeframe in new[] { Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1 })
        {
            Located.Compile(Basic() with { Timeframes = [timeframe] }, timeframe).IsSuccess.ShouldBeTrue(timeframe.ToCode());
        }

        Located.Compile(Defs.Swing() with { Timeframes = [Timeframe.D1] }, Timeframe.D1).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_repeated_timeframe_gives_duplicate_timeframe_at_the_second()
    {
        Located.Compile(Basic() with { Timeframes = [Timeframe.M5, Timeframe.M15, Timeframe.M5] }).ShouldHave(ModelCodes.DuplicateTimeframe, "timeframes[2]");
    }

    [Fact]
    public void Compiling_for_a_timeframe_the_strategy_does_not_list_gives_timeframe_not_declared_at_timeframes()
    {
        var result = Located.Compile(Basic() with { Timeframes = [Timeframe.M5] }, Timeframe.M15);

        result.ErrorList().ShouldBe(["model.timeframe_not_declared@timeframes"]);
    }

    [Theory]
    [InlineData(Style.Intraday, Timeframe.D1, true)]
    [InlineData(Style.Intraday, Timeframe.M5, false)]
    [InlineData(Style.Intraday, Timeframe.H1, false)]
    [InlineData(Style.Swing, Timeframe.M5, true)]
    [InlineData(Style.Swing, Timeframe.M10, true)]
    [InlineData(Style.Swing, Timeframe.M15, true)]
    [InlineData(Style.Swing, Timeframe.M30, false)]
    [InlineData(Style.Swing, Timeframe.H1, false)]
    [InlineData(Style.Swing, Timeframe.D1, false)]
    public void An_intraday_strategy_does_not_list_1D_and_a_swing_strategy_lists_30m_1h_and_1D_only(Style style, Timeframe timeframe, bool mismatch)
    {
        var rules = style == Style.Intraday ? Defs.IntradayRules : new TimeRules(null, null, null, 3);
        var definition = Basic() with { Style = style, Timeframes = [timeframe], Sides = [Side.Buy], TimeRules = rules };

        var result = Located.Compile(definition, timeframe);

        result.At(ModelCodes.StyleTimeframeMismatch, "timeframes[0]").Count.ShouldBe(mismatch ? 1 : 0, Located.Show(result));
    }

    [Fact]
    public void An_enum_field_with_a_value_outside_its_set_gives_undefined_value_at_that_fields_path()
    {
        Located.Compile(Basic() with { Style = (Style)7 }).ShouldHave(ModelCodes.UndefinedValue, "style");
        Located.Compile(Basic() with { Sides = [Side.Buy, (Side)9] }).ShouldHave(ModelCodes.UndefinedValue, "sides[1]");
        Located.Compile(Basic().WithBuy(b => b with { Entry = new EntrySpec((EntryKind)9) })).ShouldHave(ModelCodes.UndefinedValue, "buy.entry.kind");
        Located.Compile(Basic().WithBuy(b => b with { Stop = new StopSpec((StopKind)9) })).ShouldHave(ModelCodes.UndefinedValue, "buy.stop.kind");
        Located.Compile(Basic().WithBuy(b => b with { Stop = b.Stop! with { Timeframe = (Timeframe)99, Kind = StopKind.AtrMult, Pct = null, Mult = Quantity.Of(2) } })).ShouldHave(ModelCodes.UndefinedValue, "buy.stop.timeframe");
        Located.Compile(Basic().WithBuy(b => b with { Trigger = new ConditionList((ListMode)5, ["close > 1"]) })).ShouldHave(ModelCodes.UndefinedValue, "buy.trigger.mode");
        Located.Compile(Basic().WithBuy(b => b with { Exits = [new ExitSpec((ExitPolicy)9, [])] })).ShouldHave(ModelCodes.UndefinedValue, "buy.exits[0].policy");
        Located.Compile(Basic().WithBuy(b => b with { Overlays = [new OverlaySpec((OverlayKind)9, [])] })).ShouldHave(ModelCodes.UndefinedValue, "buy.overlays[0].kind");
    }

    // ---- criterion 5: sides -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_empty_sides_list_gives_sides_empty_and_a_repeated_side_duplicate_side_at_the_second()
    {
        Located.Compile(Basic() with { Sides = [] }).ShouldHave(ModelCodes.SidesEmpty, "sides");
        Located.Compile(Basic() with { Sides = [Side.Sell, Side.Buy, Side.Sell] }).ShouldHave(ModelCodes.DuplicateSide, "sides[2]");
    }

    [Fact]
    public void Compiling_a_side_that_sides_does_not_list_gives_side_not_declared_at_sides()
    {
        Located.Compile(Basic() with { Sides = [Side.Buy] }, Timeframe.M15, Side.Sell).ShouldHave(ModelCodes.SideNotDeclared, "sides");
    }

    [Fact]
    public void Rules_of_a_side_that_sides_does_not_list_give_the_warning_side_rules_unused_at_that_block()
    {
        var buyOnly = Located.Compile(Basic() with { Sides = [Side.Buy] });
        var sellOnly = Located.Compile(Basic() with { Sides = [Side.Sell] }, Timeframe.M15, Side.Sell);

        buyOnly.IsSuccess.ShouldBeTrue();
        buyOnly.ShouldHave(ModelCodes.SideRulesUnused, "sell", ProblemSeverity.Warning);
        sellOnly.ShouldHave(ModelCodes.SideRulesUnused, "buy", ProblemSeverity.Warning);
    }

    [Fact]
    public void A_swing_strategy_that_lists_sell_gives_swing_sell_unsupported_at_that_side_and_the_message_cites_doc_07()
    {
        var result = Located.Compile(Defs.Swing() with { Sides = [Side.Buy, Side.Sell], Sell = Defs.Swing().Buy }, Timeframe.H1);

        var problem = result.ShouldHave(ModelCodes.SwingSellUnsupported, "sides[1]");
        problem.Problem.Message.ShouldContain("doc 07 §7");
        problem.Problem.Message.ShouldContain("futures");
    }

    [Fact]
    public void The_buy_side_of_a_strategy_with_a_null_buy_block_gives_side_rules_missing_at_buy()
    {
        var result = Located.Compile(Basic() with { Sides = [Side.Buy], Sell = null, Buy = null });

        result.ErrorList().ShouldBe(["model.side_rules_missing@buy"]);
    }

    [Fact]
    public void The_sell_side_without_a_sell_block_is_mirrored_from_the_buy_side_and_only_a_definition_with_no_buy_block_gives_sell_block_missing()
    {
        var doc11 = Defs.VwapRsiTrend();

        var sell = Located.Compile(doc11, Timeframe.M15, Side.Sell);
        var buy = Located.Compile(doc11, Timeframe.M15, Side.Buy);
        var noBuy = Located.Compile(doc11 with { Buy = null }, Timeframe.M15, Side.Sell);

        sell.IsSuccess.ShouldBeTrue(Located.Show(sell));
        sell.At(ModelCodes.SellBlockMissing, "sell").ShouldBeEmpty();
        buy.IsSuccess.ShouldBeTrue(Located.Show(buy));
        buy.At(ModelCodes.SellBlockMissing, "sell").ShouldBeEmpty();
        noBuy.ShouldHave(ModelCodes.SellBlockMissing, "sell").Problem.Message.ShouldContain("write it");
    }

    [Fact]
    public void A_fault_in_the_rules_of_one_side_never_changes_the_result_for_the_other()
    {
        var broken = Basic().WithBuy(b => b with { Trigger = Defs.All("closee > 1", "rsi >"), Stop = null, Exits = [], Entry = null });

        var sell = Located.Compile(broken, Timeframe.M15, Side.Sell);
        var cleanSell = Located.Compile(Basic(), Timeframe.M15, Side.Sell);

        sell.IsSuccess.ShouldBeTrue(Located.Show(sell));
        sell.Compiled!.Features.ShouldBe(cleanSell.Compiled!.Features);
        sell.Problems.ShouldBe(cleanSell.Problems);
    }

    // ---- criterion 6: time rules --------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_time_rules_become_minutes_of_the_india_day()
    {
        var windowed = Located.Ok(Basic() with { TimeRules = new TimeRules(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null) });
        var late = Located.Ok(Basic() with { TimeRules = new TimeRules(null, new TimeOnly(14, 30), new TimeOnly(15, 15), null) });
        var bare = Located.Ok(Basic() with { TimeRules = new TimeRules(null, null, new TimeOnly(15, 15), null) });
        var swing = Located.Ok(Defs.Swing(), Timeframe.H1);

        windowed.TimeRules.ShouldBe(new SessionRules(570, 690, 915, null));
        late.TimeRules.ShouldBe(new SessionRules(null, 870, 915, null));
        bare.TimeRules.ShouldBe(new SessionRules(null, null, 915, null));
        swing.TimeRules.ShouldBe(new SessionRules(null, null, null, 5));
    }

    [Fact]
    public void A_time_at_the_edges_of_the_session_is_accepted_and_a_time_outside_or_with_seconds_gives_bad_time()
    {
        Located.Compile(Basic() with { TimeRules = new TimeRules(new TimeOnly(9, 15), new TimeOnly(15, 29), new TimeOnly(15, 30), null) }).IsSuccess.ShouldBeTrue();
        Located.Compile(Basic() with { TimeRules = Defs.IntradayRules with { FirstEntry = new TimeOnly(9, 14) } }).ShouldHave(ModelCodes.BadTime, "time_rules.first_entry");
        Located.Compile(Basic() with { TimeRules = Defs.IntradayRules with { SquareOff = new TimeOnly(15, 31) } }).ShouldHave(ModelCodes.BadTime, "time_rules.square_off");
        Located.Compile(Basic() with { TimeRules = Defs.IntradayRules with { LastEntry = new TimeOnly(11, 30, 15) } }).ShouldHave(ModelCodes.BadTime, "time_rules.last_entry");
        Located.Compile(Basic() with { TimeRules = Defs.IntradayRules with { LastEntry = new TimeOnly(11, 30, 0, 500) } }).ShouldHave(ModelCodes.BadTime, "time_rules.last_entry");
    }

    [Fact]
    public void The_order_is_first_entry_at_most_last_entry_before_square_off()
    {
        var reversed = Located.Compile(Basic() with { TimeRules = new TimeRules(new TimeOnly(11, 30), new TimeOnly(9, 30), new TimeOnly(15, 15), null) });
        var equalToSquareOff = Located.Compile(Basic() with { TimeRules = new TimeRules(null, new TimeOnly(15, 15), new TimeOnly(15, 15), null) });
        var afterSquareOff = Located.Compile(Basic() with { TimeRules = new TimeRules(new TimeOnly(15, 20), null, new TimeOnly(15, 15), null) });

        reversed.ErrorList().ShouldBe(["model.time_rules_out_of_order@time_rules.last_entry"]);
        equalToSquareOff.ErrorList().ShouldBe(["model.time_rules_out_of_order@time_rules.square_off"]);
        afterSquareOff.ErrorList().ShouldBe(["model.time_rules_out_of_order@time_rules.square_off"]);
        Located.Compile(Basic() with { TimeRules = new TimeRules(new TimeOnly(9, 30), new TimeOnly(9, 30), new TimeOnly(15, 15), null) }).IsSuccess.ShouldBeTrue("first_entry equal to last_entry is a window of one bar");
    }

    [Fact]
    public void An_intraday_strategy_without_square_off_or_without_time_rules_gives_time_rule_missing()
    {
        Located.Compile(Basic() with { TimeRules = null }).ErrorList().ShouldBe(["model.time_rule_missing@time_rules.square_off"]);
        Located.Compile(Basic() with { TimeRules = new TimeRules(new TimeOnly(9, 30), null, null, null) }).ErrorList().ShouldBe(["model.time_rule_missing@time_rules.square_off"]);
    }

    [Fact]
    public void A_swing_strategy_needs_max_hold_sessions_from_1_to_5_and_no_square_off()
    {
        Located.Compile(Defs.Swing() with { TimeRules = null }, Timeframe.H1).ErrorList().ShouldBe(["model.time_rule_missing@time_rules.max_hold_sessions"]);
        foreach (var hold in new[] { 1, 3, 5 })
        {
            Located.Compile(Defs.Swing() with { TimeRules = new TimeRules(null, null, null, hold) }, Timeframe.H1).IsSuccess.ShouldBeTrue("max hold " + hold);
        }

        foreach (var hold in new[] { 0, 6, -1 })
        {
            Located.Compile(Defs.Swing() with { TimeRules = new TimeRules(null, null, null, hold) }, Timeframe.H1).ShouldHave(ModelCodes.MaxHoldOutOfRange, "time_rules.max_hold_sessions");
        }

        Located.Compile(Defs.Swing() with { TimeRules = new TimeRules(null, null, new TimeOnly(15, 15), 3) }, Timeframe.H1).ErrorList().ShouldBe(["model.square_off_on_swing@time_rules.square_off"]);
    }

    [Fact]
    public void An_intraday_strategy_with_max_hold_sessions_gives_max_hold_on_intraday()
    {
        Located.Compile(Basic() with { TimeRules = Defs.IntradayRules with { MaxHoldSessions = 2 } }).ErrorList().ShouldBe(["model.max_hold_on_intraday@time_rules.max_hold_sessions"]);
    }

    [Fact]
    public void An_entry_window_on_a_1D_evaluation_timeframe_gives_time_rule_on_daily_at_that_field()
    {
        var both = Defs.Swing() with { TimeRules = new TimeRules(new TimeOnly(9, 30), new TimeOnly(11, 30), null, 5) };

        var result = Located.Compile(both, Timeframe.D1);

        result.ErrorList().ShouldBe(["model.time_rule_on_daily@time_rules.first_entry", "model.time_rule_on_daily@time_rules.last_entry"]);
        Located.Compile(both, Timeframe.H1).IsSuccess.ShouldBeTrue("the same rules are fine on an intraday timeframe of a swing strategy");
    }

    // ---- criterion 7: filters -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void No_filters_compile_without_a_warning()
    {
        var result = Located.Compile(Basic() with { Filters = [] });

        result.IsSuccess.ShouldBeTrue();
        result.Problems.ShouldBeEmpty();
        result.Compiled!.Filters.ShouldBeEmpty();
    }

    [Fact]
    public void The_two_known_filters_compile_with_a_warning_each_that_says_what_data_is_missing()
    {
        var result = Located.Compile(Basic() with { Filters = [FilterNames.NotEventDay, FilterNames.MinTurnover] });

        result.IsSuccess.ShouldBeTrue();
        result.ShouldHave(ModelCodes.FilterNotEvaluated, "filters[0]", ProblemSeverity.Warning).Problem.Message.ShouldContain("results calendar");
        result.ShouldHave(ModelCodes.FilterNotEvaluated, "filters[1]", ProblemSeverity.Warning).Problem.Message.ShouldContain("liquidity");
        result.Compiled!.Filters.ShouldBe([FilterNames.NotEventDay, FilterNames.MinTurnover]);
        result.Compiled.Warnings.Count.ShouldBe(2);
    }

    [Fact]
    public void An_unknown_filter_gives_unknown_filter_with_the_closest_name_as_the_suggestion_or_none()
    {
        var typo = Located.Compile(Basic() with { Filters = ["not_event_dy"] });
        var far = Located.Compile(Basic() with { Filters = ["zzz"] });

        typo.ShouldHave(ModelCodes.UnknownFilter, "filters[0]").Problem.Suggestion.ShouldBe("not_event_day");
        far.ShouldHave(ModelCodes.UnknownFilter, "filters[0]").Problem.Suggestion.ShouldBeNull();
    }

    [Fact]
    public void A_repeated_filter_gives_duplicate_filter_at_the_second_and_a_null_name_never_throws()
    {
        var twice = Located.Compile(Basic() with { Filters = [FilterNames.MinTurnover, FilterNames.MinTurnover] });
        var missing = Located.Compile(Basic() with { Filters = [null!] });

        twice.ShouldHave(ModelCodes.DuplicateFilter, "filters[1]");
        twice.At(ModelCodes.FilterNotEvaluated, "filters[1]").ShouldBeEmpty("the repeated filter is reported once");
        missing.ShouldHave(ModelCodes.UnknownFilter, "filters[0]");
    }
}
