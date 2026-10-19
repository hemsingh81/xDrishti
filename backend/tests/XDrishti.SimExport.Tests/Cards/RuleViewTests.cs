using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criterion 4 of P3-04c, the rule view (C3): read from the recorded definition text, tolerant, with a raw-text fallback.</summary>
public sealed class RuleViewTests
{
    private static StrategyRecord Record(string json, params (string, double)[] parameters) =>
        new("X", parameters.ToDictionary(p => p.Item1, p => p.Item2), json, new string('d', 64), Timeframe.M15, [Side.Buy, Side.Sell]);

    [Fact]
    public void The_orb_buy_and_sell_views_equal_the_golden_file_golden()
    {
        var orb = CardFixtures.Orb();

        var text = "BUY\n" + string.Join("\n", RuleView.Of(orb, Side.Buy).Lines) + "\n\nSELL\n" + string.Join("\n", RuleView.Of(orb, Side.Sell).Lines) + "\n";

        Golden.CheckCard("rule-view-orb", text);
    }

    [Fact]
    public void The_orb_buy_view_is_the_example_of_the_spec()
    {
        var view = RuleView.Of(CardFixtures.Orb(), Side.Buy);

        view.IsFallback.ShouldBeFalse();
        view.Lines.ShouldBe(
        [
            "strategy: ORB (or_min = 15, rr = 2)",
            "trigger: all of:",
            "  close > or_high(or_min) and close[1] <= or_high(or_min)",
            "entry: at_close",
            "stop: or_opposite (minutes = or_min)",
            "exits: FIXED_RR (rr = 2)",
            "time rules: first entry 09:30, last entry 11:30, square off 15:15",
        ]);
    }

    [Fact]
    public void Setup_invalidate_overlays_filters_the_other_stop_members_and_a_max_hold_are_printed_in_the_order_of_the_spec()
    {
        var json = "{\"buy\":{\"setup\":{\"mode\":\"any\",\"items\":[\"a > b\",\"c < d\"]},\"trigger\":{\"mode\":\"all\",\"items\":[\"x\"]},\"invalidate\":{\"mode\":\"any\",\"items\":[\"y\"]},"
            + "\"entry\":{\"kind\":\"limit_pullback\"},\"stop\":{\"kind\":\"atr_mult\",\"timeframe\":\"15m\",\"mult\":1.5,\"atr_period\":\"n\",\"pct\":null},"
            + "\"exits\":[{\"policy\":\"FIXED_RR\",\"parameters\":[{\"name\":\"rr\",\"values\":[2]}]},{\"policy\":\"PARTIAL_BE\",\"parameters\":[{\"name\":\"at\",\"values\":[1,\"x\"]},{\"name\":\"size\",\"values\":[0.5]}]}],"
            + "\"overlays\":[{\"kind\":\"time_stop\",\"parameters\":[]}]},\"time_rules\":{\"max_hold_sessions\":5,\"square_off\":\"15:15\"},\"filters\":[\"liquid\",\"not_halted\"]}";

        var view = RuleView.Of(Record(json, ("rr", 2), ("a", 1)), Side.Buy);

        view.Lines.ShouldBe(
        [
            "strategy: X (a = 1, rr = 2)",
            "setup: any of:",
            "  a > b",
            "  c < d",
            "trigger: all of:",
            "  x",
            "invalidate: any of:",
            "  y",
            "entry: limit_pullback",
            "stop: atr_mult (timeframe = 15m, mult = 1.5, atr_period = n)",
            "exits: FIXED_RR (rr = 2)",
            "exits: PARTIAL_BE (at = 1, x; size = 0.5)",
            "overlays: time_stop",
            "time rules: square off 15:15, max hold sessions 5",
            "filters: liquid",
            "filters: not_halted",
        ]);
    }

    [Fact]
    public void Unknown_members_are_ignored_and_missing_ones_are_skipped()
    {
        var view = RuleView.Of(Record("{\"future\":1,\"sell\":{\"unknown\":[1],\"entry\":{\"kind\":\"at_close\",\"extra\":true}}}"), Side.Sell);

        view.IsFallback.ShouldBeFalse();
        view.Lines.ShouldBe(["strategy: X", "entry: at_close"]);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"sell\":{}}")]
    [InlineData("{\"buy\":[1]}")]
    [InlineData("{\"buy\":\"x\"}")]
    public void A_definition_that_does_not_parse_or_has_no_block_for_the_side_falls_back_to_the_raw_text_with_its_hash(string json)
    {
        var view = RuleView.Of(Record(json), Side.Buy);

        view.IsFallback.ShouldBeTrue();
        view.Raw.ShouldBe(json);
        view.Sha256.ShouldBe(new string('d', 64));
        view.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_long_raw_definition_is_cut()
    {
        RuleView.Of(Record(new string('x', 50_000)), Side.Buy).Raw.Length.ShouldBe(RuleView.MaxRawChars);
    }

    [Fact]
    public void A_definition_with_a_lone_surrogate_in_a_string_does_not_throw()
    {
        var view = RuleView.Of(Record("{\"buy\":{\"filters\":[],\"entry\":{\"kind\":\"\\ud800\"}}}"), Side.Buy);

        view.IsFallback.ShouldBeTrue();
    }
}
