using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>Criterion 5: strategy parameters are numbers known at compile time; they may be catalog arguments; an unknown or non-number one is an error; the map is not changed; two maps give two independent plans.</summary>
public sealed class ParametersTests
{
    [Fact]
    public void A_parameter_as_a_catalog_argument_equals_the_same_number_written_out()
    {
        var withParameter = Condition("rsi(rsi_len) > 50", parameters: Params(("rsi_len", 21)));
        var written = Condition("rsi(21) > 50");

        withParameter.Features.ShouldBe(written.Features);
        withParameter.Features.ShouldHaveSingleItem().ShouldBe(Specs.Rsi(Timeframe.M15, 21));
    }

    [Fact]
    public void A_parameter_in_a_comparison_is_a_number_of_the_tree()
    {
        var plan = Condition("rsi(14) > rsi_min", parameters: Params(("rsi_min", 55)));

        var compare = plan.Tree.ShouldBeOfType<BoundCompare>();
        compare.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(55);
        compare.Right.Type.ShouldBe(BoundType.Number);
    }

    [Fact]
    public void A_parameter_can_be_used_in_arithmetic_for_a_catalog_argument_and_for_a_count()
    {
        var plan = Condition("rsi(base * 2 + 1) > 50 and rising(close, steps)", parameters: Params(("base", 7), ("steps", 3)));

        plan.Features.ShouldBe([Specs.Rsi(Timeframe.M15, 15), Specs.Close(Timeframe.M15)]);
        plan.HistoryDepth.ShouldBe(4);
    }

    [Fact]
    public void An_unknown_parameter_is_an_error_with_the_closest_parameter_as_the_suggestion()
    {
        var error = OnlyError("rsi(rsi_lne) > 50", parameters: Params(("rsi_len", 21), ("rsi_min", 55)));

        error.Code.ShouldBe(LintCodes.UnknownName);
        error.Suggestion.ShouldBe("rsi_len");
        error.Span.Start.ShouldBe(4);
        error.Span.End.ShouldBe(11);
    }

    [Fact]
    public void A_parameter_that_is_not_given_is_unknown_even_when_the_text_would_be_fine_with_it()
    {
        var error = OnlyError("rsi(rsi_len) > 50");

        error.Code.ShouldBe(LintCodes.UnknownName);
        error.Suggestion.ShouldBeNull();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_parameter_that_is_not_a_finite_number_is_an_error_where_it_is_used(double value)
    {
        var error = OnlyError("rsi(14) > limit", parameters: Params(("limit", value)));

        error.Code.ShouldBe(LintCodes.TypeMismatch);
        error.Span.Start.ShouldBe("rsi(14) > limit".IndexOf("limit", StringComparison.Ordinal));
        error.Message.ShouldContain("limit", Case.Sensitive);
    }

    [Fact]
    public void A_parameter_that_is_not_used_is_not_checked()
    {
        Condition("rsi(14) > 55", parameters: Params(("unused", double.NaN))).Features.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_parameter_that_is_out_of_range_for_a_catalog_argument_is_the_catalogs_error_at_the_parameter()
    {
        var error = OnlyError("rsi(rsi_len) > 50", parameters: Params(("rsi_len", 0)));

        error.Code.ShouldBe(LintCodes.BadArguments);
        error.Span.Start.ShouldBe(4);
        error.Message.ShouldContain("'n' must be between 1 and 10000", Case.Sensitive);
    }

    [Fact]
    public void The_map_is_not_changed()
    {
        var map = new Dictionary<string, double> { ["rsi_len"] = 21, ["close"] = 5, ["unused"] = 1 };
        var before = map.ToDictionary(p => p.Key, p => p.Value);

        _ = Of("rsi(rsi_len) > close and closee > 1", Timeframe.M15, map);
        _ = Of("rsi(rsi_len) > 50", Timeframe.M15, map);

        map.ShouldBe(before);
    }

    [Fact]
    public void The_plan_does_not_depend_on_the_map_after_the_compile()
    {
        var map = new Dictionary<string, double> { ["rsi_len"] = 21 };
        var plan = Condition("rsi(rsi_len) > 50", parameters: map);

        map["rsi_len"] = 5;
        map["new"] = 1;
        map.Clear();

        plan.Features.ShouldBe([Specs.Rsi(Timeframe.M15, 21)]);
    }

    [Fact]
    public void The_same_text_with_two_maps_gives_two_independent_plans()
    {
        var first = Condition("rsi(rsi_len) > level", parameters: Params(("rsi_len", 21), ("level", 55)));
        var second = Condition("rsi(rsi_len) > level", parameters: Params(("rsi_len", 7), ("level", 40)));

        first.ShouldNotBeSameAs(second);
        first.Features.ShouldBe([Specs.Rsi(Timeframe.M15, 21)]);
        second.Features.ShouldBe([Specs.Rsi(Timeframe.M15, 7)]);
        first.Tree.ShouldBeOfType<BoundCompare>().Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(55);
        second.Tree.ShouldBeOfType<BoundCompare>().Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(40);
    }

    [Fact]
    public void A_parameter_that_hides_a_feature_wins_and_a_warning_says_so()
    {
        var result = Of("rsi(14) > close", Timeframe.M15, Params(("close", 5)));

        result.IsSuccess.ShouldBeTrue();
        result.Condition!.Features.ShouldBe([Specs.Rsi(Timeframe.M15)], "close is the parameter, so the plan does not read the feature");
        result.Condition.Tree.ShouldBeOfType<BoundCompare>().Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(5);
        var warning = result.Condition.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LintCodes.ShadowedName);
        warning.Severity.ShouldBe(ProblemSeverity.Warning);
        warning.Span.Start.ShouldBe("rsi(14) > close".IndexOf("close", StringComparison.Ordinal));
    }

    [Fact]
    public void A_parameter_that_hides_a_name_is_warned_about_once_however_often_it_is_used()
    {
        var result = Of("close > 1 and close < 7 and close != 3", Timeframe.M15, Params(("close", 5)));

        result.Problems.Count(p => p.Code == LintCodes.ShadowedName).ShouldBe(1);
    }

    [Fact]
    public void A_parameter_that_hides_a_feature_cannot_be_called_and_cannot_be_subscripted()
    {
        var called = Failures("rsi(14) > 3", parameters: Params(("rsi", 7)));
        var subscripted = Failures("close[1] > 3", parameters: Params(("close", 7)));

        called.Select(p => p.Code).ShouldBe([LintCodes.ShadowedName, LintCodes.BadArguments]);
        subscripted.Select(p => p.Code).ShouldBe([LintCodes.ShadowedName, LintCodes.LagOnNonFeature]);
    }

    [Fact]
    public void A_qualified_feature_is_the_feature_even_when_a_parameter_has_its_name()
    {
        var plan = Condition("tf(\"1h\").close > close", parameters: Params(("close", 5)));

        plan.Features.ShouldBe([Specs.Close(Timeframe.H1)]);
    }

    [Fact]
    public void A_parameter_does_not_change_the_meaning_of_a_source_name_or_a_named_argument()
    {
        var plan = Condition("ema(20, source=high) > 0", parameters: Params(("high", 99), ("source", 1)));

        plan.Features.ShouldBe([Specs.Of("ema", Timeframe.M15, 20, source: XDrishti.Indicators.PriceSource.High)]);
    }

    [Fact]
    public void Parameters_are_case_sensitive()
    {
        var error = OnlyError("rsi(RSI_LEN) > 50", parameters: Params(("rsi_len", 21)));

        error.Code.ShouldBe(LintCodes.UnknownName);
        error.Suggestion.ShouldBe("rsi_len", "the wrong case is suggested first");
    }

    [Fact]
    public void Parameters_are_case_sensitive_even_when_the_map_is_not()
    {
        // the usual shape of a map read from a file: names compared without case
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["rsi_len"] = 14, ["close"] = 3 };

        var upperParameter = Failures("rsi(RSI_LEN) > 55", parameters: map);
        var upperFeature = Failures("CLOSE > 1", parameters: map);
        var exact = Of("rsi(rsi_len) > 55", Timeframe.M15, map);
        var shadowing = Of("close > 1", Timeframe.M15, map);

        var parameterError = upperParameter.ShouldHaveSingleItem();
        parameterError.Code.ShouldBe(LintCodes.UnknownName);
        parameterError.Suggestion.ShouldBe("rsi_len");
        upperFeature.Select(p => p.Code).ShouldBe([LintCodes.UnknownName], "CLOSE is neither the parameter close nor the feature: no shadowing warning, no constant condition");
        exact.IsSuccess.ShouldBeTrue();
        exact.Condition!.Features.ShouldBe([Specs.Rsi(Timeframe.M15)]);
        shadowing.Condition!.Warnings.Select(w => w.Code).ShouldBe([LintCodes.ShadowedName, LintCodes.ConstantCondition], "the exact spelling is the parameter: it hides the feature and the comparison is constant");
    }

    [Fact]
    public void A_read_only_dictionary_of_any_kind_is_accepted()
    {
        var plan = Condition("rsi(n) > 50", parameters: new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(new Dictionary<string, double> { ["n"] = 10 }));

        plan.Features.ShouldBe([Specs.Rsi(Timeframe.M15, 10)]);
    }
}
