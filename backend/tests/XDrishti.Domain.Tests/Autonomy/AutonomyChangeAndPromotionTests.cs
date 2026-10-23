using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Autonomy;

/// <summary>Spec P7-12: only the owner changes the switches, and the reserve is never promoted unless its own switch is on and the ticket meets every term of an auto approval.</summary>
public sealed class AutonomyChangeAndPromotionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 3, 40, 0, TimeSpan.Zero);

    private static readonly AutoApprovalContext Ctx = new(Now, Now.AddMinutes(-5), 0);

    private static readonly AutonomySettings Promote = new(4, new string('b', 64), false, true, 0.58, 3);

    private static ChecklistResult Check(string symbol) =>
        PreTradeChecklist.Evaluate(
            new ChecklistTicket(symbol, "BUY", 100, 10, 0.5, "IT"),
            new ChecklistAccount("acc-1", Domain.Accounts.TradingMode.Paper, true, 1.0, 3.0, 2, 2.0, 10000, 1.0, 0, 0),
            new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
            ChecklistLimits.Default,
            ChecklistPolicy.Default);

    private static PromotionCandidate Reserve(long id, int rank, double? pwin = 0.7, string[]? unavailable = null, bool checklist = true) =>
        new(new AutoApprovalTicket(id, "S" + id, TicketGroup.Reserve, TicketState.Lapsed, pwin, unavailable ?? [], ["acc-1"]), rank, checklist ? [Check("S" + id)] : []);

    [Theory]
    [InlineData("owner:hem@example.com", true)]
    [InlineData("rule:autonomy", false)]
    [InlineData("system:nightly", false)]
    [InlineData("cli:xd", false)]
    [InlineData("owner:", false)]
    [InlineData("owner:   ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_an_owner_actor_can_make_a_change(string? actor, bool allowed)
    {
        var result = AutonomyChange.Create(actor, "turn it on", true, false, 0.58, 3);

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            result.Error.Code.ShouldBe("autonomy.owner_only");
        }
    }

    [Fact]
    public void A_change_needs_a_reason_and_values_in_range()
    {
        AutonomyChange.Create("owner:a", "  ", true, false, 0.58, 3).Error.Code.ShouldBe("autonomy.reason_invalid");
        AutonomyChange.Create("owner:a", new string('x', 501), true, false, 0.58, 3).Error.Code.ShouldBe("autonomy.reason_invalid");
        AutonomyChange.Create("owner:a", "ok", true, false, 0.2, 3).Error.Code.ShouldBe("autonomy.values_invalid");
        AutonomyChange.Create("owner:a", "ok", true, false, 0.58, 0).Error.Code.ShouldBe("autonomy.values_invalid");
        var good = AutonomyChange.Create("owner:a", " ok ", true, true, 0.6, 5).Value;
        good.Reason.ShouldBe("ok");
        good.AutoApproveTickets.ShouldBeTrue();
        good.PromoteFromReserve.ShouldBeTrue();
    }

    [Fact]
    public void With_the_switch_off_nothing_is_ever_promoted()
    {
        ReservePromotion.Select(Promote with { PromoteFromReserve = false }, 5, [Reserve(1, 1), Reserve(2, 2)], Ctx).ShouldBeEmpty();
        ReservePromotion.Select(AutonomySettings.Defaults, 5, [Reserve(1, 1)], Ctx).ShouldBeEmpty();
    }

    [Fact]
    public void The_auto_approval_switch_does_not_turn_promotion_on()
    {
        ReservePromotion.Select(Promote with { PromoteFromReserve = false, AutoApproveTickets = true }, 5, [Reserve(1, 1)], Ctx).ShouldBeEmpty();
    }

    [Fact]
    public void With_the_switch_on_the_best_ranked_qualifying_reserve_tickets_fill_the_freed_slots()
    {
        var chosen = ReservePromotion.Select(Promote, 2, [Reserve(3, 3), Reserve(1, 1), Reserve(2, 2)], Ctx);

        chosen.ShouldBe([1, 2]);
        ReservePromotion.Select(Promote, 0, [Reserve(1, 1)], Ctx).ShouldBeEmpty();
    }

    [Fact]
    public void A_reserve_ticket_with_an_unavailable_input_no_checklist_or_a_low_p_win_is_skipped()
    {
        var chosen = ReservePromotion.Select(Promote, 3, [Reserve(1, 1, unavailable: ["smart_rules"]), Reserve(2, 2, checklist: false), Reserve(3, 3, pwin: 0.4), Reserve(4, 4)], Ctx);

        chosen.ShouldBe([4]);
    }

    [Fact]
    public void Promotion_counts_against_the_daily_cap()
    {
        ReservePromotion.Select(Promote, 5, [Reserve(1, 1), Reserve(2, 2), Reserve(3, 3)], Ctx with { AutoApprovedToday = 2 }).ShouldBe([1]);
        ReservePromotion.Select(Promote, 5, [Reserve(1, 1)], Ctx with { AutoApprovedToday = 3 }).ShouldBeEmpty();
    }

    [Fact]
    public void A_primary_ticket_is_not_a_promotion_candidate()
    {
        var primary = new PromotionCandidate(new AutoApprovalTicket(9, "S9", TicketGroup.Primary, TicketState.Proposed, 0.9, [], ["acc-1"]), 1, [Check("S9")]);

        ReservePromotion.Select(Promote, 1, [primary], Ctx).ShouldBeEmpty();
    }
}
