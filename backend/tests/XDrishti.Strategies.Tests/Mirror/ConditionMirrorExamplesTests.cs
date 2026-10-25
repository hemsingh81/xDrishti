using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Syntax;
using static XDrishti.Strategies.Tests.Mirror.MirrorTestSupport;

namespace XDrishti.Strategies.Tests.Mirror;

/// <summary>Spec P3-02e: the hand examples from the stored strategies, the builtins, arithmetic and exact literals.</summary>
public sealed class ConditionMirrorExamplesTests
{
    public static readonly TheoryData<string, string> Examples = new()
    {
        { "close > high[1]", "close < low[1]" },
        { "gap_pct <= -gap_min and gap_pct >= -gap_max", "gap_pct >= gap_min and gap_pct <= gap_max" },
        { "crosses_above(close, bb_lower(20, 2)) and close > open", "crosses_below(close, bb_upper(20, 2)) and close < open" },
        { "low < prev_day.low and close > prev_day.low and close > open", "high > prev_day.high and close < prev_day.high and close < open" },
        { "inside_bar[1] == 1 and close > high[2]", "inside_bar[1] == 1 and close < low[2]" },
        { "low[1] < low[2] and low[1] <= vwap[1] + pull_atr * atr(14)", "high[1] > high[2] and high[1] >= vwap[1] - pull_atr * atr(14)" },
        { "tf(\"1D\").close > tf(\"1D\").ema(20)", "tf(\"1D\").close < tf(\"1D\").ema(20)" },
        { "rsi(14) > 55 and macd_hist > 0", "rsi(14) < 45 and macd_hist < 0" },
        { "supertrend_dir == 1", "supertrend_dir == -1" },
        { "adx(14) < adx_max", "adx(14) < adx_max" },
        { "rsi(14) > rsi_hi", "rsi(14) < 100 - rsi_hi" },
        { "rsi(14) > 55.1", "rsi(14) < 44.9" },
        { "rsi(14) > 0.1", "rsi(14) < 99.9" },
        { "close > highest(20, source=close)", "close < lowest(20, source=close)" },
        { "close > ema(20, source=high)", "close < ema(20, source=low)" },
        { "close - open > atr(14)", "open - close > atr(14)" },
        { "high - low > 2 * atr(14)", "high - low > 2 * atr(14)" },
        { "plus_di(14) > minus_di(14)", "minus_di(14) > plus_di(14)" },
        { "crosses_above(rsi(14), 30)", "crosses_below(rsi(14), 70)" },
        { "crosses_above(plus_di(14), minus_di(14))", "crosses_above(minus_di(14), plus_di(14))" },
        { "rising(close, 3)", "falling(close, 3)" },
        { "rising(atr(14), 3)", "rising(atr(14), 3)" },
        { "between(rsi(14), 30, 70)", "between(rsi(14), 30, 70)" },
        { "between(rsi(14), 40, 60)", "between(rsi(14), 40, 60)" },
        { "between(close, low, high)", "between(close, low, high)" },
        { "between(macd_hist, -1, 3)", "between(macd_hist, -3, 1)" },
        { "min(close, open) > ema(20)", "max(close, open) < ema(20)" },
        { "abs(macd_hist) > 1", "abs(macd_hist) > 1" },
        { "abs(close - open) > atr(14)", "abs(open - close) > atr(14)" },
        { "time >= \"09:45\" and not (close > open)", "time >= \"09:45\" and not (close < open)" },
        { "close > ema(20) + atr(14)", "close < ema(20) - atr(14)" },
        { "macd_hist > macd_signal", "macd_hist < macd_signal" },
        { "gap_pct > 0", "gap_pct < 0" },
        { "volume > prev_day_volume", "volume > prev_day_volume" },
        { "pivot_high_age(5) < 3", "pivot_low_age(5) < 3" },
        { "true", "true" },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void A_buy_condition_mirrors_to_the_hand_written_sell_condition(string buy, string sell)
    {
        Ok(buy).ShouldBe(ExpressionPrinter.Print(Tree(sell)));
    }

    [Fact]
    public void A_literal_with_a_decimal_part_is_complemented_exactly_not_in_floating_point()
    {
        Ok("rsi(14) > 55.1").ShouldBe("rsi(14) < 44.9");
    }

    [Fact]
    public void A_literal_with_more_than_fifteen_significant_digits_is_refused()
    {
        var result = Try("rsi(14) > 55.1234567890123456");

        result.Problem.ShouldNotBeNull().Code.ShouldBe(MirrorCodes.LiteralPrecision);
    }

    [Fact]
    public void The_lagged_call_row_is_inert_until_the_parser_accepts_a_subscript_over_a_call()
    {
        var result = Try("close > highest(20)[1]");

        result.Unreadable.ShouldBeTrue();
        result.Problem.ShouldBeNull();
    }

    [Fact]
    public void A_lag_and_a_timeframe_qualifier_are_kept()
    {
        Ok("tf(\"1h\").high[2] < tf(\"1h\").vwap[1]").ShouldBe("tf(\"1h\").low[2] > tf(\"1h\").vwap[1]");
    }

    [Fact]
    public void A_condition_that_does_not_parse_is_unreadable_and_has_no_mirror_problem()
    {
        var result = Try("close >");

        result.Unreadable.ShouldBeTrue();
        result.Problem.ShouldBeNull();
    }

    [Fact]
    public void An_unknown_name_is_unreadable_and_the_original_compile_reports_it()
    {
        var result = Try("nonsense(3) > 1");

        result.Unreadable.ShouldBeTrue();
    }

    [Fact]
    public void The_result_of_the_same_text_is_identical_on_repeat()
    {
        Try("close > 100").Problem.ShouldBe(Try("close > 100").Problem);
    }
}
