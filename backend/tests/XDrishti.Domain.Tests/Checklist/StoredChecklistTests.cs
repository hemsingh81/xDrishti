using XDrishti.Domain.Checklist;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Checklist;

/// <summary>Spec P7-10 (ADR 0095): the checklist rebuilt from the standings a plan stored never turns an unreadable or missing record into a pass.</summary>
public sealed class StoredChecklistTests
{
    private const string Account = "acc-1";

    [Fact]
    public void No_standings_means_every_check_passed_and_the_ticket_can_be_approved()
    {
        var result = StoredChecklist.Build(Account, "AAA", true, [])!;

        result.Checks.Select(c => c.Code).ShouldBe(ChecklistCodes.All);
        result.Checks.ShouldAllBe(c => c.Outcome == FilterOutcome.Pass);
        result.Review().CanApprove.ShouldBeTrue();
    }

    [Fact]
    public void An_unavailable_standing_needs_a_confirmation_and_a_named_one_covers_only_that_check_on_that_account()
    {
        var result = StoredChecklist.Build(Account, "AAA", true, ["checklist:data_quality:unavailable", "checklist:fno_ban:fail", "allocation_entry_is_indicative"])!;
        var alone = result.Review();
        var one = result.Review(OwnerConfirmation.Create("owner:hem", "checked", Account, "AAA", [ChecklistCodes.DataQuality]).Value);
        var both = result.Review(OwnerConfirmation.Create("owner:hem", "checked", Account, "AAA", [ChecklistCodes.DataQuality, ChecklistCodes.FnoBan]).Value);
        var otherAccount = result.Review(OwnerConfirmation.Create("owner:hem", "checked", "acc-2", "AAA", [ChecklistCodes.DataQuality, ChecklistCodes.FnoBan]).Value);

        alone.CanApprove.ShouldBeFalse();
        one.CanApprove.ShouldBeFalse();
        both.CanApprove.ShouldBeTrue();
        both.ConfirmedDespiteCodes.ShouldBe(new[] { ChecklistCodes.DataQuality, ChecklistCodes.FnoBan });
        otherAccount.CanApprove.ShouldBeFalse();
    }

    [Fact]
    public void A_warn_policy_failure_is_shown_and_does_not_block()
    {
        var result = StoredChecklist.Build(Account, "AAA", true, ["checklist:holdings_conflict:fail"])!;

        var review = result.Review();

        review.CanApprove.ShouldBeTrue();
        review.Checks.Single(c => c.Check.Code == ChecklistCodes.HoldingsConflict).Standing.ShouldBe(CheckStanding.Warned);
    }

    [Theory]
    [InlineData("checklist:no_trading_account")]
    [InlineData("checklist:made_up:fail")]
    [InlineData("checklist:fno_ban:maybe")]
    [InlineData("checklist:fno_ban:fail:extra")]
    public void A_standing_that_cannot_be_read_gives_no_result(string standing)
    {
        StoredChecklist.Build(Account, "AAA", true, [standing]).ShouldBeNull();
    }

    [Fact]
    public void A_non_paper_allocation_is_refused_and_no_confirmation_can_cover_it()
    {
        var result = StoredChecklist.Build(Account, "AAA", false, [])!;

        var review = result.Review(OwnerConfirmation.Create("owner:hem", "please", Account, "AAA", [ChecklistCodes.DataQuality]).Value);

        review.CanApprove.ShouldBeFalse();
        review.Checks.Single().Standing.ShouldBe(CheckStanding.Refused);
        result.Refused.ShouldBeTrue();
    }

    [Fact]
    public void The_gate_standing_of_the_plan_is_not_read_for_a_paper_allocation()
    {
        var result = StoredChecklist.Build(Account, "AAA", true, ["checklist:account_paper:fail"])!;

        result.Refused.ShouldBeFalse();
        result.Review().CanApprove.ShouldBeTrue();
    }

    [Fact]
    public void The_approval_wording_is_pinned()
    {
        PlanWording.ApprovedForWatching.ShouldBe("Approved for watching: no order is placed and none exists.");
    }
}
