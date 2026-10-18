using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public class FillLimitsAndLabelsTests
{
    [Fact]
    public void The_nine_limits_are_listed_in_order_with_text()
    {
        FillLimits.NotModelled.Select(l => l.Code).ShouldBe(
        [
            "auction_open", "circuit_locks", "touch_is_fill", "no_queue_or_impact", "grid_approximate",
            "minute_path", "hole_is_no_trade", "one_minute_entry", "no_latency",
        ]);
        FillLimits.NotModelled.ShouldAllBe(l => !string.IsNullOrWhiteSpace(l.Text));
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
