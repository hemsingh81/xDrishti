using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criterion 10: a proposal sentence only with a positive lower bound and enough observations; it names the knob and nothing applies it.</summary>
public sealed class SlippageProposalTests
{
    [Fact]
    public void A_positive_lower_bound_with_enough_observations_gives_the_sentence_with_the_numbers_and_the_knob()
    {
        var text = SlippageProposal.For("L2", "buy", "intraday", ReferenceKind.PlanGrade, 24, 3.4, 0.8, 20).ShouldNotBeNull();
        text.ShouldContain("L2");
        text.ShouldContain("3.4 bp");
        text.ShouldContain("n = 24");
        text.ShouldContain("slippage.ticks");
        text.ShouldContain("nothing is applied automatically");
        text.Length.ShouldBeLessThanOrEqualTo(SlippageProposal.MaxLength);
    }

    [Theory]
    [InlineData(19, 0.8)]
    [InlineData(25, 0.0)]
    [InlineData(25, -0.4)]
    public void Too_few_or_a_lower_bound_at_or_below_zero_gives_none(int n, double lo) =>
        SlippageProposal.For("L1", "buy", "delivery", ReferenceKind.TicketLevel, n, 2.0, lo, 20).ShouldBeNull();
}
