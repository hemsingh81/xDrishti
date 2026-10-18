using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public class FillLimitsAndLabelsTests
{
    [Fact]
    public void The_ten_limits_are_listed_in_order_with_text()
    {
        FillLimits.NotModelled.Select(l => l.Code).ShouldBe(
        [
            "auction_open", "circuit_locks", "touch_is_fill", "no_queue_or_impact", "grid_approximate",
            "minute_path", "hole_is_no_trade", "one_minute_entry", "no_latency", "dividends_not_modelled",
        ]);
        FillLimits.NotModelled.ShouldAllBe(l => !string.IsNullOrWhiteSpace(l.Text));
    }

    /// <summary>The words that the banner of an export may not use (P3-04b1, criterion 6): no performance statistic in a limit text.</summary>
    private static readonly string[] ForbiddenWords =
        ["win", "wins", "winner", "winners", "winning", "loss", "losses", "loser", "losers", "losing", "hit", "hits", "expectancy", "profit", "profits", "profitable", "drawdown", "average", "mean", "sum", "total", "percent", "percentage", "rate", "ratio"];

    [Theory]
    [InlineData("auction_open")]
    [InlineData("dividends_not_modelled")]
    public void The_texts_of_the_overnight_limits_say_what_is_not_modelled_and_use_no_word_of_a_statistic(string code)
    {
        // P3-03d D15: the correction of auction_open and the new code are neutral. (The text of touch_is_fill is P3-04b1's to reword.)
        var text = FillLimits.NotModelled.Single(l => l.Code == code).Text;

        System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z]+").Select(m => m.Value.ToLowerInvariant()).Intersect(ForbiddenWords).ShouldBeEmpty(code);
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\d R\b").ShouldBeFalse(code);
        text.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_auction_open_text_names_the_gap_exit_of_a_position_held_overnight_and_no_longer_says_that_no_strategy_fills_there()
    {
        var text = FillLimits.NotModelled.Single(l => l.Code == "auction_open").Text;

        text.ShouldContain("held overnight");
        text.ShouldContain("a stop at the open, a target at its level");
        text.ShouldNotContain("no P3 strategy");
        FillLimits.NotModelled[^1].Code.ShouldBe("dividends_not_modelled", "the new code is appended at the end");
    }

    [Fact]
    public void Default_rules_are_modelled_with_a_tenth_of_a_percent()
    {
        FillDescription.Labels(FillRules.Default).ShouldBe(["fills: modelled", "entry limit: +0.1 %"]);
    }

    [Fact]
    public void Without_slippage_the_fills_are_exact()
    {
        var rules = FillRules.Create(0.1, SlippageConfig.None).Value;

        FillDescription.Labels(rules)[0].ShouldBe("fills: exact");
    }

    [Fact]
    public void The_percent_is_in_the_shortest_form_under_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CommaCulture();
            var rules = FillRules.Create(0.25, SlippageConfig.Default).Value;

            FillDescription.Labels(rules)[1].ShouldBe("entry limit: +0.25 %");
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // The test host runs in globalization-invariant mode, so de-DE cannot be loaded: an invariant clone with German separators stands in for it.
    private static CultureInfo CommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }
}
