using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criteria 1, 2, 5, 7, 8 and 9 of P3-04d: the section "conditions at the decision bar" of a card.</summary>
public sealed class ConditionsSectionTests
{
    private const string OrbText = "close > or_high(or_min) and close[1] <= or_high(or_min)";

    private static FeatureReadTrace Read(string name, int lag, double? value) => new(name, Timeframe.M15, lag, value);

    /// <summary>The ORB BUY of the criterion 2 day: opening range high 101.20, previous 15m close 100.90, trigger bar close 101.60.</summary>
    private static TradeExplanation OrbBuy() => new(
        Day.Epoch(CardFixtures.Date, 600),
        Timeframe.M15,
        [
            new ConditionSectionTrace("trigger", "all", [new ConditionItemTrace("buy.trigger[0]", OrbText, TruthState.True, [Read("close", 0, 101.6), Read("or_high(15)", 0, 101.2), Read("close", 1, 100.9), Read("or_high(15)", 0, 101.2)])]),
        ]);

    private static string Render(TradeExplanation? explanation)
    {
        var (_, run, data) = CardFixtures.Goldens()[0];
        return CardFixtures.Render(run, data with { Explanation = explanation });
    }

    [Fact]
    public void An_orb_buy_shows_the_trigger_true_with_the_four_reads_at_their_lags()
    {
        var html = Render(OrbBuy());

        html.ShouldContain("<h2>conditions at the decision bar</h2>");
        html.ShouldContain("<h3>trigger (all of)</h3>");
        html.ShouldContain(Html.Text(OrbText));
        html.ShouldContain("close (15m, lag 0) = 101.6; or_high(15) (15m, lag 0) = 101.2; close (15m, lag 1) = 100.9; or_high(15) (15m, lag 0) = 101.2");
        html.ShouldContain(">true<");
        html.ShouldNotContain("<h3>setup");
        html.ShouldNotContain("<h3>invalidate");
        html.ShouldContain("10:00");
        html.ShouldContain("never fires");
        Golden.CheckCard("card-orb-buy-conditions", html);
    }

    [Fact]
    public void The_old_line_is_gone_and_a_stored_run_says_it_was_not_recomputed()
    {
        var withSection = Render(OrbBuy());
        var stored = Render(null);

        withSection.ShouldNotContain("not in this export");
        stored.ShouldNotContain("not in this export");
        stored.ShouldContain("conditions: not recomputed for a stored run");
    }

    [Fact]
    public void An_unknown_condition_prints_unknown_and_not_ready_for_the_feature()
    {
        var explanation = new TradeExplanation(
            Day.Epoch(CardFixtures.Date, 600),
            Timeframe.M15,
            [
                new ConditionSectionTrace("trigger", "any", [new ConditionItemTrace("buy.trigger[0]", "close > vwap", TruthState.True, [Read("close", 0, 101), Read("vwap", 0, 100)])]),
                new ConditionSectionTrace("invalidate", "any", [new ConditionItemTrace("buy.invalidate[0]", "rsi(14) > 80", TruthState.Unknown, [Read("rsi(14)", 0, null)])]),
            ]);

        var html = Render(explanation);

        html.ShouldContain("<h3>invalidate (any of)</h3>");
        html.ShouldContain(">unknown<");
        html.ShouldContain("rsi(14) (15m, lag 0) = not ready");
    }

    [Fact]
    public void A_value_has_at_most_six_significant_digits_and_a_condition_without_a_read_says_so()
    {
        var explanation = new TradeExplanation(
            Day.Epoch(CardFixtures.Date, 600),
            Timeframe.M15,
            [new ConditionSectionTrace("trigger", "all", [new ConditionItemTrace("t[0]", "close > 1", TruthState.True, [Read("close", 0, 1234.56789)]), new ConditionItemTrace("t[1]", "time >= \"09:45\"", TruthState.True, [])])]);

        var html = Render(explanation);

        html.ShouldContain("= 1234.57");
        html.ShouldContain("no feature read");
    }

    [Fact]
    public void A_hostile_condition_text_is_escaped_and_a_cut_read_list_says_so()
    {
        var explanation = new TradeExplanation(
            Day.Epoch(CardFixtures.Date, 600),
            Timeframe.M15,
            [new ConditionSectionTrace("trigger", "all", [new ConditionItemTrace("t[0]", "<script>alert(1)</script> & \"x\"", TruthState.False, [Read("close", 0, 1)], ReadsOmitted: true)])]);

        var html = Render(explanation);

        html.ShouldNotContain("<script");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldContain("more reads not listed");
    }

    [Fact]
    public void The_largest_allowed_condition_lists_keep_the_card_far_below_the_page_limit_and_two_renders_are_byte_identical()
    {
        var reads = Enumerable.Range(0, 64).Select(i => Read("ema(" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", i % 5, 100.123456 + i)).ToList();
        var items = Enumerable.Range(0, 100).Select(i => new ConditionItemTrace("buy.trigger[" + i + "]", "ema(1) > ema(2) and ema(3) > ema(4)", TruthState.True, reads)).ToList();
        var explanation = new TradeExplanation(Day.Epoch(CardFixtures.Date, 600), Timeframe.M15, [new ConditionSectionTrace("trigger", "all", items)]);
        var (_, run, data) = CardFixtures.Goldens()[0];
        var withSection = data with { Explanation = explanation };

        var first = Sinks.Bytes(s => TradeCardHtml.Write(s, withSection, run, CardFixtures.Labels(run)));
        var second = Sinks.Bytes(s => TradeCardHtml.Write(s, withSection, run, CardFixtures.Labels(run)));

        second.ShouldBe(first);
        first.Length.ShouldBeGreaterThan(Sinks.Bytes(s => TradeCardHtml.Write(s, data, run, CardFixtures.Labels(run))).Length);
        // the compiler allows 100 conditions of 64 reads; the card lists 12 reads of each and says it cut the rest, so the page stays under its limit (rendering throws above it)
        first.Length.ShouldBeLessThan(150 * 1024);
        Encoding.UTF8.GetString(first).ShouldContain("more reads not listed");
    }
}
