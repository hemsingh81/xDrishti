using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>Criterion 3, second half: a table pins the spec produced for more than thirty forms, and two spellings of one feature give equal specs so that the frame shares one instance.</summary>
public sealed class FeatureSpecFormsTests
{
    [Fact]
    public void The_table_has_at_least_thirty_forms()
    {
        FeatureSpecForms.Cases.Count.ShouldBeGreaterThanOrEqualTo(30);
    }

    [Theory]
    [MemberData(nameof(FeatureSpecForms.Sources), MemberType = typeof(FeatureSpecForms))]
    public void A_form_binds_to_the_hand_written_specs_in_the_order_they_are_read(string source)
    {
        var expected = FeatureSpecForms.Cases[source];

        var plan = Compiling.Condition(source, expected.Evaluation, expected.Parameters);

        plan.Features.ShouldBe(expected.Features);
    }

    [Theory]
    [InlineData("highest(20) > 0", "highest(20, source=high) > 0")]
    [InlineData("highest() > 0", "highest(20, source=high) > 0")]
    [InlineData("rsi() > 0", "rsi(14) > 0")]
    [InlineData("rsi(14) > 0", "rsi(14, source=close) > 0")]
    [InlineData("prev_day.high > 0", "prev_day_high > 0")]
    [InlineData("today.open > 0", "today_open > 0")]
    [InlineData("index.trend == 1", "index_trend == 1")]
    [InlineData("index.trend == 1", "tf(\"1m\").index_trend == 1")]
    [InlineData("close > 0", "tf(\"15m\").close > 0")]
    [InlineData("close > 0", "close() > 0")]
    [InlineData("atr() > 0", "atr(14) > 0")]
    [InlineData("regime() == 1", "regime() == \"UP_LOW_VOL\"")]
    [InlineData("bb_upper() > 0", "bb_upper(20, 2) > 0")]
    [InlineData("macd_line() > 0", "macd_line(12, 26, 9, source=close) > 0")]
    public void Two_spellings_of_one_feature_give_equal_specs(string first, string second)
    {
        var a = Compiling.Condition(first).Features.ShouldHaveSingleItem();
        var b = Compiling.Condition(second).Features.ShouldHaveSingleItem();

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Two_spellings_of_one_feature_in_one_condition_are_one_feature_of_the_plan()
    {
        var plan = Compiling.Condition("highest() > 0 and highest(20, source=high) > 0 and prev_day.high > prev_day_high");

        plan.Features.Count.ShouldBe(2);
    }

    [Fact]
    public void The_same_text_on_two_timeframes_gives_specs_of_those_timeframes()
    {
        Compiling.Condition("rsi(14) > 55", Timeframe.M5).Features.ShouldBe([Specs.Rsi(Timeframe.M5)]);
        Compiling.Condition("rsi(14) > 55", Timeframe.H1).Features.ShouldBe([Specs.Rsi(Timeframe.H1)]);
    }

    [Fact]
    public void Features_that_differ_in_a_parameter_or_a_source_are_different_features()
    {
        var plan = Compiling.Condition("rsi(14) > rsi(15) and rsi(14, source=open) > 0 and ema(14) > 0");

        plan.Features.Distinct().Count().ShouldBe(4);
    }
}
