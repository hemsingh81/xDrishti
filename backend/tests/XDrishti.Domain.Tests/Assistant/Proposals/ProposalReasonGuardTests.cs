using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Domain.Tests.Assistant.Proposals;

/// <summary>Spec P9-08 AC 3, 5: the reason is checked against the engine's evidence figures, never against anything the model wrote.</summary>
public sealed class ProposalReasonGuardTests
{
    private static ProposalEvidence Evidence(string state = "value") => new(
        "gross R (before charges)", ["a"], "as of 2026-10-20", 120,
        [
            new EvidenceFigure("Cell trades", "120", "value", 120),
            new EvidenceFigure("Cell expectancy (R)", state == "value" ? "0.21" : string.Empty, state, 120),
        ],
        ["Replayed history."]);

    private const string Subject = "ORB|15m|BUY|intraday|FIXED_RR|*: rr 2 to 2.5";

    [Fact]
    public void A_reason_that_restates_the_evidence_passes()
    {
        ProposalReasonGuard.Check("The cell has n=120 trades and expectancy 0.21.", Subject, Evidence()).ShouldBeEmpty();
    }

    [Fact]
    public void One_figure_off_is_refused_the_worked_case_of_the_spec()
    {
        ProposalReasonGuard.Check("The cell has n=121 trades.", Subject, Evidence()).ShouldContain(ProposalCodes.ReasonNumberNotInEvidence);
    }

    [Fact]
    public void A_computed_or_invented_number_is_refused()
    {
        ProposalReasonGuard.Check("Expectancy of 0.21 is up 35% on last year.", Subject, Evidence()).ShouldContain(ProposalCodes.ReasonNumberNotInEvidence);
    }

    [Fact]
    public void A_figure_for_an_insufficient_input_is_refused()
    {
        ProposalReasonGuard.Check("Cell expectancy (R) is 0.21 on 120 trades.", Subject, Evidence("insufficient")).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Strong momentum is visible in the cell.")]
    [InlineData("It is the best cell with a breakout pattern.")]
    [InlineData("The cell may outperform other cells.")]
    public void A_claim_word_needs_a_figure_of_its_kind(string reason)
    {
        ProposalReasonGuard.Check(reason, Subject, Evidence()).ShouldContain(ProposalCodes.ReasonClaimWord);
    }

    [Fact]
    public void A_symbol_that_is_not_in_the_facts_is_refused()
    {
        ProposalReasonGuard.Check("The cell resembles TCS in the stored runs.", Subject, Evidence()).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Buy it now.")]
    [InlineData("See https://example.com for the cell.")]
    [InlineData("The cell is expected to improve.")]
    public void Advice_addresses_and_forecasts_are_refused(string reason)
    {
        ProposalReasonGuard.Check(reason, Subject, Evidence()).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_claim_word_helper_ignores_a_word_the_evidence_itself_carries()
    {
        ProposalWording.ClaimWordsIn("strong", "relative strong figure").ShouldBeEmpty();
        ProposalWording.ClaimWordsIn("The strongest cell", "n 120").ShouldBe(["strong"]);
    }
}
