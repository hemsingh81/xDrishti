using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>What an identifier means (spec P3-02, <i>Names</i>): a strategy parameter, then a builtin or constant or <c>time</c>, then a catalog feature; the dotted spellings; the qualifier; nothing else.</summary>
public sealed class NameResolutionTests
{
    /// <summary>The dotted spellings and the flat catalog names they stand for, written out from the spec and doc 11.</summary>
    public static readonly IReadOnlyDictionary<string, string> Dotted = new Dictionary<string, string>
    {
        ["prev_day.open"] = "prev_day_open",
        ["prev_day.high"] = "prev_day_high",
        ["prev_day.low"] = "prev_day_low",
        ["prev_day.close"] = "prev_day_close",
        ["prev_day.volume"] = "prev_day_volume",
        ["today.open"] = "today_open",
        ["today.high"] = "today_high",
        ["today.low"] = "today_low",
        ["index.trend"] = "index_trend",
        ["index.adx"] = "index_adx",
    };

    public static TheoryData<string> DottedNames { get; } = [.. Dotted.Keys];

    public static TheoryData<string> OtherDottedPairs { get; } =
    [
        "prev_day.gap_pct", "prev_day.vwap", "prev_day.close_", "prev_day.rsi", "prev_day.day_of_week", "today.volume", "today.close", "today.gap_pct", "today.vwap", "today.prev_day",
        "index.vol_pct", "index.regime", "index.high", "index.close", "index.trend_", "regime.trend", "vix.pct", "close.high", "tf.close", "time.hour", "MON.x", "foo.bar", "rsi.n",
    ];

    [Theory]
    [MemberData(nameof(DottedNames))]
    public void A_dotted_spelling_binds_to_the_flat_catalog_name(string dotted)
    {
        var flat = Dotted[dotted];
        IndicatorCatalog.TryGet(flat, out var info).ShouldBeTrue("the flat name must exist in the catalog");

        var plan = Condition(dotted + " > 0", Timeframe.M1);

        plan.Features.ShouldBe([info.DefaultSpec(Timeframe.M1)]);
        Condition(flat + " > 0", Timeframe.M1).Features.ShouldBe(plan.Features, "the flat spelling is the same feature");
    }

    [Theory]
    [MemberData(nameof(OtherDottedPairs))]
    public void Every_other_dotted_pair_is_an_error(string pair)
    {
        var error = OnlyError(pair + " > 0");

        error.Code.ShouldBe(LintCodes.UnknownName);
        error.Span.Start.ShouldBe(0);
    }

    [Theory]
    [InlineData("MON", 1)]
    [InlineData("TUE", 2)]
    [InlineData("WED", 3)]
    [InlineData("THU", 4)]
    [InlineData("FRI", 5)]
    [InlineData("SAT", 6)]
    [InlineData("SUN", 7)]
    public void The_weekday_constants_are_the_numbers_1_to_7(string name, int number)
    {
        var plan = Condition("day_of_week == " + name);

        var compare = plan.Tree.ShouldBeOfType<BoundCompare>();
        compare.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(number);
    }

    [Fact]
    public void A_function_of_the_language_is_called_a_feature_is_called_or_named_and_neither_can_be_confused()
    {
        Condition("abs(close - open) > 1").Features.Count.ShouldBe(2);
        Condition("close() > 1").Features.ShouldHaveSingleItem();
        OnlyError("abs > 1").Code.ShouldBe(LintCodes.BadArguments);
        OnlyError("rsi > 1").Code.ShouldBe(LintCodes.BadArguments);
        OnlyError("regime == 1").Code.ShouldBe(LintCodes.BadArguments);
        OnlyError("time() > 1").Code.ShouldBe(LintCodes.BadArguments);
        OnlyError("MON() == 1").Code.ShouldBe(LintCodes.BadArguments);
    }

    [Fact]
    public void The_regime_is_written_with_parentheses_and_is_the_catalog_feature_of_that_name()
    {
        var plan = Condition("regime() == 4");

        plan.Features.ShouldBe([Specs.Of("regime", Timeframe.M1)]);
        var compare = plan.Tree.ShouldBeOfType<BoundCompare>();
        compare.Left.ShouldBeOfType<BoundFeature>().Type.ShouldBe(BoundType.Regime);
        plan.NeedsContext.ShouldBeTrue();
    }

    [Fact]
    public void The_resolution_order_is_parameter_then_function_then_feature()
    {
        // `max` is a function and `close` a feature: a parameter of either name wins, and nothing else of that name can be reached
        var parameters = Params(("max", 3), ("close", 4), ("open", 5));

        var plan = Condition("max > high and open < low and close != volume", parameters: parameters);

        plan.Features.ShouldBe([Specs.Of("high", Timeframe.M15), Specs.Of("low", Timeframe.M15), Specs.Of("volume", Timeframe.M15)], "max, open and close are the parameters");
        plan.Warnings.Select(w => w.Code).ShouldBe([LintCodes.ShadowedName, LintCodes.ShadowedName, LintCodes.ShadowedName]);
        plan.Warnings.Select(w => w.Message).ShouldAllBe(m => m.Contains("wins over it", StringComparison.Ordinal));
    }

    [Fact]
    public void A_timeframe_qualifier_always_means_the_feature_whatever_the_parameters_are()
    {
        var plan = Condition("tf(\"1D\").close > tf(\"1h\").open", parameters: Params(("close", 4), ("open", 5)));

        plan.Features.ShouldBe([Specs.Close(Timeframe.D1), Specs.Of("open", Timeframe.H1)]);
        plan.Warnings.ShouldBeEmpty("the parameters are not used, so they hide nothing");
    }

    [Theory]
    [InlineData("tf(\"1h\").crosses_above(close, vwap)")]
    [InlineData("tf(\"1h\").abs(close) > 1")]
    [InlineData("tf(\"1h\").time > 1")]
    [InlineData("tf(\"1h\").MON > 1")]
    [InlineData("tf(\"1h\").rsi_len > 1")]
    [InlineData("tf(\"1h\").nonsense > 1")]
    [InlineData("tf(\"1h\").nonsense() > 1")]
    public void Only_a_feature_can_be_qualified_with_a_timeframe(string source)
    {
        var error = OnlyError(source, parameters: Params(("rsi_len", 14)));

        error.Code.ShouldBe(LintCodes.UnknownName);
    }

    [Fact]
    public void Names_are_case_sensitive_everywhere()
    {
        foreach (var source in new[] { "Close > 1", "CLOSE > 1", "RSI(14) > 1", "Rsi() > 1", "day_of_week == tue", "day_of_week == Tue", "TIME >= \"09:45\"", "True", "regime() == \"Up_Low_Vol\"", "tf(\"1H\").close > 1" })
        {
            var errors = Of(source, Timeframe.M15).Errors.ToList();

            errors.ShouldNotBeEmpty(source);
        }
    }

    [Fact]
    public void A_condition_may_use_every_kind_of_name_together()
    {
        var plan = Condition(
            "day_of_week != SAT and time >= \"09:45\" and crosses_above(close, vwap) and rsi(rsi_len) > level and tf(\"1h\").ema(20) > tf(\"1h\").ema(50) and prev_day.high < today.open and regime() == \"UP_LOW_VOL\"",
            parameters: Params(("rsi_len", 14), ("level", 55)));

        plan.Features.Count.ShouldBe(9);
        plan.NeedsContext.ShouldBeTrue();
        plan.HistoryDepth.ShouldBe(2);
    }
}
