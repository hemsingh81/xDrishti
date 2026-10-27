using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Domain.Tests.Assistant.Proposals;

/// <summary>Spec P9-08 AC 7: the dedupe key is stable under case and number form, and differs when the idea differs.</summary>
public sealed class ProposalKeyTests
{
    private static readonly string Run = new('a', 64);

    private static HypothesisPayload Hypo(string cell = "ORB|15m|BUY|intraday|FIXED_RR|*", string knob = "rr", string candidate = "2.5") => new(Run, cell, knob, "Parameter", "2", candidate);

    [Fact]
    public void A_trailing_zero_and_case_do_not_change_the_hypothesis_key()
    {
        ProposalKey.Hypothesis(Hypo(candidate: "2.50")).ShouldBe(ProposalKey.Hypothesis(Hypo(candidate: "2.5")));
        ProposalKey.Hypothesis(Hypo(knob: "RR")).ShouldBe(ProposalKey.Hypothesis(Hypo(knob: "rr")));
        ProposalKey.Hypothesis(Hypo(cell: "orb|15m|buy|intraday|fixed_rr|*")).ShouldBe(ProposalKey.Hypothesis(Hypo()));
        ProposalKey.Hypothesis(Hypo()).Length.ShouldBe(64);
    }

    [Fact]
    public void Another_value_cell_or_knob_is_another_key()
    {
        var key = ProposalKey.Hypothesis(Hypo());

        ProposalKey.Hypothesis(Hypo(candidate: "2.4")).ShouldNotBe(key);
        ProposalKey.Hypothesis(Hypo(knob: "atr")).ShouldNotBe(key);
        ProposalKey.Hypothesis(Hypo(cell: "ORB|15m|SELL|intraday|FIXED_RR|*")).ShouldNotBe(key);
    }

    [Fact]
    public void A_basket_key_ignores_the_symbol_case_and_tells_add_from_remove()
    {
        var basket = Guid.NewGuid();

        ProposalKey.Basket(ProposalKind.BasketAdd, new BasketPayload(basket, "evb")).ShouldBe(ProposalKey.Basket(ProposalKind.BasketAdd, new BasketPayload(basket, "EVB")));
        ProposalKey.Basket(ProposalKind.BasketAdd, new BasketPayload(basket, "EVB")).ShouldNotBe(ProposalKey.Basket(ProposalKind.BasketRemove, new BasketPayload(basket, "EVB")));
        ProposalKey.Basket(ProposalKind.BasketAdd, new BasketPayload(basket, "EVB")).ShouldNotBe(ProposalKey.Basket(ProposalKind.BasketAdd, new BasketPayload(Guid.NewGuid(), "EVB")));
    }
}
