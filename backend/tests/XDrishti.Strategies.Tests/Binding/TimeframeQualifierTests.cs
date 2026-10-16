using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>Timeframes (spec P3-02, <i>Timeframes</i>): <c>tf("…")</c> takes one of nine codes, case as written; the catalog's refusals become errors that name the feature and the timeframe.</summary>
public sealed class TimeframeQualifierTests
{
    private static readonly (string Code, Timeframe Frame)[] Nine =
    [
        ("1m", Timeframe.M1), ("5m", Timeframe.M5), ("10m", Timeframe.M10), ("15m", Timeframe.M15), ("30m", Timeframe.M30), ("1h", Timeframe.H1), ("1D", Timeframe.D1), ("1W", Timeframe.W1), ("1M", Timeframe.Mo1),
    ];

    public static TheoryData<string, Timeframe> Codes { get; } = [.. Nine];

    public static TheoryData<string, Timeframe> NamesOnNineTimeframes { get; } =
        [.. IndicatorCatalog.All.SelectMany(i => Nine.Select(n => (i.Name, n.Frame)))];

    [Theory]
    [MemberData(nameof(Codes))]
    public void Each_of_the_nine_codes_qualifies_a_feature_with_its_timeframe(string code, Timeframe frame)
    {
        var plan = Condition($"tf(\"{code}\").close > 0", Timeframe.M5);

        plan.Features.ShouldBe([Specs.Close(frame)]);
    }

    [Fact]
    public void One_minute_and_one_month_are_two_timeframes()
    {
        Condition("tf(\"1m\").close > tf(\"1M\").close").Features.ShouldBe([Specs.Close(Timeframe.M1), Specs.Close(Timeframe.Mo1)]);
    }

    [Theory]
    [InlineData("1d", "\"1D\"")]
    [InlineData("1w", "\"1W\"")]
    [InlineData("1H", "\"1h\"")]
    [InlineData("15M", "\"15m\"")]
    [InlineData("5M", "\"5m\"")]
    [InlineData("30M", "\"30m\"")]
    [InlineData("2m", "\"1m\"")]
    [InlineData("2h", "\"1h\"")]
    [InlineData(" 1m", "\"1m\"")]
    [InlineData("1m ", "\"1m\"")]
    [InlineData("1Day", null)]
    [InlineData("daily", null)]
    [InlineData("15min", null)]
    [InlineData("", null)]
    [InlineData("tf", null)]
    [InlineData("60 minutes", null)]
    public void Any_other_text_is_an_error_at_the_string_with_the_nearest_code_as_the_suggestion(string text, string? suggestion)
    {
        var source = $"tf(\"{text}\").close > 0";

        var error = OnlyError(source);

        error.Code.ShouldBe(LintCodes.UnsupportedTimeframe);
        error.Suggestion.ShouldBe(suggestion);
        (error.Span.Start, error.Span.End).ShouldBe((3, 3 + text.Length + 2));
        error.Message.ShouldContain("1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M", Case.Sensitive, "lists the nine");
    }

    [Theory]
    [MemberData(nameof(NamesOnNineTimeframes))]
    public void A_feature_qualified_with_a_timeframe_is_refused_exactly_where_the_catalog_refuses_it_with_the_feature_and_timeframe_named(string name, Timeframe frame)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();
        var code = frame.ToCode();
        var form = info.Parameters.Count > 0 ? name + "()" : name;
        var source = $"tf(\"{code}\").{form}" + (name == "regime" ? " == 1" : " > 0");
        var spec = info.DefaultSpec(frame);

        var result = Of(source, Timeframe.M15);

        if (!info.Supports(frame))
        {
            var error = result.Errors.ShouldHaveSingleItem(source);
            error.Code.ShouldBe(LintCodes.UnsupportedTimeframe);
            (error.Span.Start, error.Span.End).ShouldBe((3, 3 + code.Length + 2));
            error.Message.ShouldContain("'" + name + "'", Case.Sensitive);
            error.Message.ShouldContain(code, Case.Sensitive);
            foreach (var supported in info.SupportedTimeframes)
            {
                error.Message.ShouldContain(supported.ToCode(), Case.Sensitive, "lists the timeframes it runs on");
            }
        }
        else if (FeatureFrame.Create([spec], new FrameOptions(Context: MarketContext.Create([]).Value, ExpiryDays: new HashSet<DateOnly>())).IsFailure)
        {
            result.Errors.ShouldHaveSingleItem(source).Code.ShouldBe(LintCodes.BadArguments);
        }
        else
        {
            result.IsSuccess.ShouldBeTrue(source + " -> " + Describe(result));
            result.Condition!.Features.ShouldBe([spec]);
        }
    }

    [Fact]
    public void The_refusals_the_spec_names_are_errors_with_the_feature_and_the_timeframe()
    {
        var vwapOnDaily = OnlyError("tf(\"1D\").vwap > 0");
        var previousDayOnWeekly = OnlyError("tf(\"1W\").prev_day_high > 0");
        var previousDayOnWeeklyStrategy = OnlyError("prev_day.high > 0", Timeframe.W1);

        vwapOnDaily.Message.ShouldContain("'vwap'", Case.Sensitive);
        vwapOnDaily.Message.ShouldContain("1D", Case.Sensitive);
        previousDayOnWeekly.Message.ShouldContain("'prev_day_high'", Case.Sensitive);
        previousDayOnWeekly.Message.ShouldContain("1W", Case.Sensitive);
        previousDayOnWeeklyStrategy.Code.ShouldBe(LintCodes.UnsupportedTimeframe);
        previousDayOnWeeklyStrategy.Message.ShouldContain("the strategy runs on 1W", Case.Sensitive);
    }

    [Theory]
    [InlineData(Timeframe.D1)]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void The_strategy_timeframe_is_where_an_unqualified_feature_is_read_and_it_may_be_a_calendar_timeframe(Timeframe frame)
    {
        Condition("rsi() > 50", frame).Features.ShouldBe([Specs.Rsi(frame)]);
        Condition("close > ema()", frame).Features.ShouldBe([Specs.Close(frame), Specs.Ema(frame)]);
    }

    [Fact]
    public void A_feature_of_the_day_is_read_on_1m_whatever_the_strategy_timeframe_and_qualifying_it_with_1m_is_not_an_error()
    {
        foreach (var frame in new[] { Timeframe.M1, Timeframe.M15, Timeframe.H1, Timeframe.D1, Timeframe.W1 })
        {
            Condition("day_of_week == TUE", frame).Features.ShouldBe([Specs.Of("day_of_week", Timeframe.M1)]);
            Condition("tf(\"1m\").day_of_week == TUE", frame).Features.ShouldBe([Specs.Of("day_of_week", Timeframe.M1)]);
        }
    }

    [Theory]
    [InlineData("(tf(\"15m\").ema(20)) > 0", "ema(20)")]
    [InlineData("tf( \"15m\" ) . ema( 20 , source = high ) > 0", "ema(20, source=high)")]
    [InlineData("(tf(\"15m\").close) > 0", "close")]
    [InlineData("tf(\"15m\").close[2] > 0", "close")]
    [InlineData("tf(\"15m\").prev_day_high > 0", "prev_day_high")]
    public void The_replacement_of_a_redundant_qualifier_is_the_feature_written_plainly(string source, string plain)
    {
        var result = Of(source);

        var warning = result.Problems.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LintCodes.RedundantTimeframe);
        warning.Suggestion.ShouldBe(plain);
        warning.Message.ShouldEndWith("Write `" + plain + "`.", Case.Sensitive);
    }

    [Fact]
    public void The_qualifier_is_redundant_on_the_strategy_timeframe_and_a_warning_says_so_without_making_the_plan_different()
    {
        var qualified = Of("tf(\"15m\").ema(20) > 0");
        var plain = Condition("ema(20) > 0");

        qualified.IsSuccess.ShouldBeTrue();
        qualified.Condition!.Features.ShouldBe(plain.Features);
        qualified.Condition.Warnings.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.RedundantTimeframe);
        plain.Warnings.ShouldBeEmpty();
    }
}
