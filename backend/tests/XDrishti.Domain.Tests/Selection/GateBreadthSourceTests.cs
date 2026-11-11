using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>BK-08 criterion 9 (the pure part): the breadth the gate reads from a chosen basket, named; a missing, stale, future or insufficient row is unavailable and never the tracked set's figure.</summary>
public sealed class GateBreadthSourceTests
{
    private static readonly DateOnly Decision = new(2026, 11, 11);

    private static readonly DateOnly Prior = new(2026, 11, 10);

    [Fact]
    public void A_row_for_the_prior_session_is_the_breadth_and_the_sentence_names_the_basket()
    {
        var read = GateBreadthSource.FromBasket("Nifty 50", Prior, 0.62, "value", Decision, 4);

        read.Breadth.ShouldBe(0.62);
        read.Note.ShouldBe("breadth of Nifty 50: 0.62");
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("2026-11-11", 0.6, "value")]
    [InlineData("2026-11-12", 0.6, "value")]
    [InlineData("2026-11-06", 0.6, "value")]
    [InlineData("2026-11-10", null, "insufficient")]
    [InlineData("2026-11-10", 0.6, "insufficient")]
    [InlineData("2026-11-10", double.NaN, "value")]
    public void No_row_a_row_not_before_the_day_a_stale_row_or_an_insufficient_one_is_unavailable(string? date, double? b20, string? state)
    {
        var day = date is null ? (DateOnly?)null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        var read = GateBreadthSource.FromBasket("Nifty 50", day, b20, state, Decision, 4);

        read.Breadth.ShouldBeNull();
        read.Note.ShouldContain("Nifty 50");
        read.Note.ShouldContain("unavailable");
        read.Note.ShouldContain("does not fall back");
    }

    [Fact]
    public void The_edge_of_the_maximum_age_is_still_fresh()
    {
        GateBreadthSource.FromBasket("N", Decision.AddDays(-4), 0.5, "value", Decision, 4).Breadth.ShouldBe(0.5);
        GateBreadthSource.FromBasket("N", Decision.AddDays(-5), 0.5, "value", Decision, 4).Breadth.ShouldBeNull();
    }

    [Fact]
    public void An_unavailable_breadth_makes_the_gate_unavailable_and_never_a_pass()
    {
        var read = GateBreadthSource.FromBasket("Nifty 50", null, null, null, Decision, 4);
        var gate = MarketGate.Evaluate(new MarketGateInput(Prior, 14, read.Breadth, false), Decision, new SmartRulesOptions());

        gate.Mode.ShouldBe(GateMode.Unavailable);
        gate.MissingInputs.ShouldBe(["breadth"]);
    }
}
