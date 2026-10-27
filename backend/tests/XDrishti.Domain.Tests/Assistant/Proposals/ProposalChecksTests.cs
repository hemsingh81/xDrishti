using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Domain.Tests.Assistant.Proposals;

/// <summary>Spec P9-08 AC 4, 5, 7: each rule fails alone as a failing row; an unavailable input never counts as a pass.</summary>
public sealed class ProposalChecksTests
{
    private static HypothesisProposalFacts Good() => new(true, "Parameter", true, [], "2", "2.5", 50, 120, 30, 0, 3, 0, 3, false, false);

    private static BasketProposalFacts GoodBasket() => new(true, true, true, false, true, true, false, "value", 40, 15, false, false);

    [Fact]
    public void A_proposal_that_meets_every_rule_passes_every_row()
    {
        ProposalChecks.AllOk(ProposalChecks.Hypothesis(Good())).ShouldBeTrue();
        ProposalChecks.AllOk(ProposalChecks.Basket(ProposalKind.BasketAdd, GoodBasket())).ShouldBeTrue();
        ProposalChecks.FirstRefusal(ProposalChecks.Hypothesis(Good())).ShouldBeNull();
    }

    public static TheoryData<string, HypothesisProposalFacts, string> HypothesisFailures() => new()
    {
        { "unresolved", Good() with { EvidenceResolved = false }, ProposalCodes.EvidenceUnresolved },
        { "exit knob", Good() with { KnobKind = "Exit" }, ProposalCodes.KnobKindNotSupported },
        { "slippage knob", Good() with { KnobKind = "Slippage" }, ProposalCodes.KnobKindNotSupported },
        { "no baseline run", Good() with { BaselineRunFound = false }, ProposalCodes.CheckBaselineRun },
        { "baseline differs", Good() with { BaselineProblems = ["the baseline run used 3 for 'rr', not 2"] }, ProposalCodes.CheckBaselineRun },
        { "too far", Good() with { CandidateValue = "3.5" }, ProposalCodes.CheckKnobBounds },
        { "sign", Good() with { CandidateValue = "-2.5" }, ProposalCodes.CheckKnobBounds },
        { "thin cell", Good() with { CellN = 29 }, ProposalCodes.EvidenceInsufficient },
        { "cell unavailable", Good() with { CellN = null }, ProposalCodes.EvidenceInsufficient },
        { "three open", Good() with { OpenHypotheses = 3 }, ProposalCodes.PendingLimit },
        { "three pending", Good() with { PendingHypotheses = 3 }, ProposalCodes.PendingLimit },
        { "duplicate", Good() with { DuplicateOpenOrPending = true }, ProposalCodes.DuplicatePending },
        { "blocked", Good() with { RepeatBlocked = true }, ProposalCodes.RepeatBlocked },
    };

    [Theory]
    [MemberData(nameof(HypothesisFailures))]
    public void Each_hypothesis_rule_fails_on_its_own(string name, HypothesisProposalFacts facts, string refusal)
    {
        var checks = ProposalChecks.Hypothesis(facts);

        ProposalChecks.AllOk(checks).ShouldBeFalse(name);
        ProposalChecks.FirstRefusal(checks).ShouldBe(refusal, name);
    }

    public static TheoryData<string, string, BasketProposalFacts, string> BasketFailures() => new()
    {
        { "unresolved", ProposalKind.BasketAdd, GoodBasket() with { EvidenceResolved = false }, ProposalCodes.EvidenceUnresolved },
        { "no basket", ProposalKind.BasketAdd, GoodBasket() with { BasketFound = false, BasketIsManual = false }, ProposalCodes.CheckBasketFound },
        { "archived", ProposalKind.BasketAdd, GoodBasket() with { BasketArchived = true }, ProposalCodes.CheckBasketFound },
        { "system", ProposalKind.BasketAdd, GoodBasket() with { BasketIsManual = false }, ProposalCodes.BasketSystem },
        { "not tracked", ProposalKind.BasketAdd, GoodBasket() with { InstrumentTracked = false }, ProposalCodes.CheckInstrumentTracked },
        { "already member", ProposalKind.BasketAdd, GoodBasket() with { IsMember = true }, ProposalCodes.CheckMembership },
        { "thin", ProposalKind.BasketAdd, GoodBasket() with { TradeN = 14 }, ProposalCodes.EvidenceInsufficient },
        { "report unavailable", ProposalKind.BasketAdd, GoodBasket() with { TradeEvidenceState = "unavailable", TradeN = null }, ProposalCodes.EvidenceInsufficient },
        { "duplicate", ProposalKind.BasketAdd, GoodBasket() with { DuplicatePending = true }, ProposalCodes.DuplicatePending },
        { "blocked", ProposalKind.BasketAdd, GoodBasket() with { RepeatBlocked = true }, ProposalCodes.RepeatBlocked },
        { "remove a non member", ProposalKind.BasketRemove, GoodBasket() with { IsMember = false }, ProposalCodes.CheckMembership },
        { "remove thin", ProposalKind.BasketRemove, GoodBasket() with { IsMember = true, TradeN = 3 }, ProposalCodes.EvidenceInsufficient },
    };

    [Theory]
    [MemberData(nameof(BasketFailures))]
    public void Each_basket_rule_fails_on_its_own(string name, string kind, BasketProposalFacts facts, string refusal)
    {
        var checks = ProposalChecks.Basket(kind, facts);

        ProposalChecks.AllOk(checks).ShouldBeFalse(name);
        ProposalChecks.FirstRefusal(checks).ShouldBe(refusal, name);
    }

    [Fact]
    public void A_removal_needs_a_member_and_a_system_basket_is_refused_for_both_kinds()
    {
        ProposalChecks.AllOk(ProposalChecks.Basket(ProposalKind.BasketRemove, GoodBasket() with { IsMember = true })).ShouldBeTrue();
        ProposalChecks.AllOk(ProposalChecks.Basket(ProposalKind.BasketRemove, GoodBasket() with { IsMember = true, BasketIsManual = false })).ShouldBeFalse();
    }

    [Fact]
    public void The_check_ids_are_unique_in_a_row_set()
    {
        ProposalChecks.Hypothesis(Good()).Select(c => c.Code).Distinct().Count().ShouldBe(ProposalChecks.Hypothesis(Good()).Count);
        ProposalChecks.Basket(ProposalKind.BasketAdd, GoodBasket()).Select(c => c.Code).Distinct().Count().ShouldBe(ProposalChecks.Basket(ProposalKind.BasketAdd, GoodBasket()).Count);
    }
}
