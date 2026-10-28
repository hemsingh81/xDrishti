using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-01 criteria 4, 6, 8 and 9: each of the nine outcomes by a hand table, the flags at their edges, the unavailable rule.</summary>
public sealed class TicketOutcomeClassifierTests
{
    private static readonly MatchTolerances Tol = MatchTolerances.Default;
    private static readonly TicketPlan Reliance = new(Side.Buy, 40, 1250.00m, 1240.00m);
    private static readonly PlanGradeFacts Graded = new(PlanGradeFacts.Graded, 1.20);
    private static readonly PlanGradeFacts Pending = new(PlanGradeFacts.Pending, null);

    private static TicketOutcome? Run(TicketPlan plan, IReadOnlyList<LiveFill> fills, IReadOnlyList<LiveOrder>? orders, PlanGradeFacts grade, string protection = OutcomeCodes.Protected, bool expired = false) =>
        TicketOutcomeClassifier.Classify(plan, fills, orders, grade, protection, Tol, expired);

    private static LiveFill Buy(long qty, decimal price) => LiveFixtureDay.Fill(1, "RELIANCE", Side.Buy, qty, price, "09:41:10");

    [Fact]
    public void The_fixture_ticket_T1_is_filled_with_forty_shares_and_no_flag()
    {
        var o = Run(Reliance, [LiveFixtureDay.Fills[0], LiveFixtureDay.Fills[1]], [LiveFixtureDay.Orders[0]], new PlanGradeFacts(PlanGradeFacts.Graded, 0.5))!;

        o.Outcome.ShouldBe(OutcomeCodes.Filled);
        o.EntryQty.ShouldBe(40);
        o.OrderedQty.ShouldBe(40);
        o.Flags.ShouldBeEmpty();
        o.MissedR.ShouldBeNull();
    }

    [Fact]
    public void T2_is_rejected_with_the_brokers_text() =>
        Run(new TicketPlan(Side.Buy, 10, 3400m, null), [], [LiveFixtureDay.Orders[2]], Graded)!.ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(OutcomeCodes.Rejected),
            o => o.RejectText.ShouldBe("insufficient margin"),
            o => o.MissedR.ShouldBeNull());

    [Fact]
    public void T3_is_missed_no_order_with_its_plan_grade_R() =>
        Run(new TicketPlan(Side.Buy, 30, 1500m, 1480m), [], [], Graded)!.ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(OutcomeCodes.MissedNoOrder),
            o => o.MissedR.ShouldBe(1.20));

    [Fact]
    public void T4_is_not_triggered_with_no_missed_R() =>
        Run(new TicketPlan(Side.Buy, 25, 1600m, null), [], [], new PlanGradeFacts(PlanGradeFacts.NotTriggered, null))!.ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(OutcomeCodes.NotTriggered),
            o => o.MissedR.ShouldBeNull());

    [Fact]
    public void An_order_that_never_filled_is_missed_unfilled_with_the_R()
    {
        var pending = LiveFixtureDay.Order("o", "INFY", Side.Buy, 30, "expired", 0, "09:30:00", 1499m);

        var o = Run(new TicketPlan(Side.Buy, 30, 1500m, 1480m), [], [pending], Graded)!;

        o.Outcome.ShouldBe(OutcomeCodes.MissedUnfilled);
        o.MissedR.ShouldBe(1.20);
    }

    [Fact]
    public void Without_order_evidence_a_traded_replay_is_missed_unknown_never_missed_no_order()
    {
        var o = Run(new TicketPlan(Side.Buy, 30, 1500m, 1480m), [], null, Graded)!;

        o.Outcome.ShouldBe(OutcomeCodes.MissedUnknown);
        o.MissedR.ShouldBeNull();
        o.OrderEvidence.ShouldBe(OutcomeCodes.EvidenceUnavailable);
    }

    [Fact]
    public void Without_order_evidence_a_replay_that_took_nothing_is_still_not_triggered() =>
        Run(Reliance, [], null, new PlanGradeFacts(PlanGradeFacts.NotTriggered, null))!.Outcome.ShouldBe(OutcomeCodes.NotTriggered);

    [Fact]
    public void A_pending_plan_grade_writes_nothing_until_five_sessions_have_passed()
    {
        Run(Reliance, [], [], Pending).ShouldBeNull();
        Run(Reliance, [], [], Pending, expired: true)!.Outcome.ShouldBe(OutcomeCodes.Unknown);
    }

    [Fact]
    public void A_ticket_that_cannot_be_graded_and_has_no_fill_is_unknown() =>
        Run(Reliance, [], [], new PlanGradeFacts(PlanGradeFacts.NotGradable, null))!.Outcome.ShouldBe(OutcomeCodes.Unknown);

    [Fact]
    public void A_fill_with_a_pending_grade_is_still_an_outcome() =>
        Run(Reliance, [Buy(40, 1250m)], [], Pending)!.Outcome.ShouldBe(OutcomeCodes.Filled);

    [Fact]
    public void One_share_above_the_allocation_is_over_allocation_and_flagged()
    {
        var o = Run(Reliance, [Buy(41, 1250m)], [], Graded)!;

        o.Outcome.ShouldBe(OutcomeCodes.OverAllocation);
        o.Flags.ShouldContain(OutcomeCodes.FlagOverAllocation);
    }

    [Fact]
    public void Below_the_allocation_with_an_order_that_part_filled_is_partial_and_not_flagged()
    {
        var part = LiveFixtureDay.Order("o", "RELIANCE", Side.Buy, 40, "part_traded", 20, "09:40:00", 1251m);

        var o = Run(Reliance, [Buy(20, 1250m)], [part], Graded)!;

        o.Outcome.ShouldBe(OutcomeCodes.Partial);
        o.Flags.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(36, false)]
    [InlineData(35, true)]
    public void Below_ninety_percent_with_no_explaining_order_is_flagged(long qty, bool flagged)
    {
        var o = Run(Reliance, [Buy(qty, 1250m)], null, Graded)!;

        o.Outcome.ShouldBe(OutcomeCodes.Filled);
        o.Flags.Contains(OutcomeCodes.FlagSizeBelowAllocation).ShouldBe(flagged);
    }

    [Theory]
    [InlineData(1252.50, false)] // exactly 0.25 R
    [InlineData(1252.60, true)] // 0.26 R
    [InlineData(1249.00, false)] // better than the plan
    public void A_chased_entry_is_beyond_a_quarter_of_the_planned_risk(double price, bool flagged) =>
        Run(Reliance, [Buy(40, (decimal)price)], [], Graded)!.Flags.Contains(OutcomeCodes.FlagChasedEntry).ShouldBe(flagged);

    [Theory]
    [InlineData(1010.00, false)] // exactly 1.0 %
    [InlineData(1010.50, true)]
    public void Without_a_stop_the_entry_is_chased_beyond_one_percent(double price, bool flagged) =>
        Run(new TicketPlan(Side.Buy, 10, 1000m, null), [Buy(10, (decimal)price)], [], Graded)!.Flags.Contains(OutcomeCodes.FlagChasedEntry).ShouldBe(flagged);

    [Fact]
    public void An_unprotected_entry_carries_the_flag_and_an_unavailable_protection_does_not()
    {
        Run(Reliance, [Buy(40, 1250m)], [], Graded, OutcomeCodes.Unprotected)!.Flags.ShouldContain(OutcomeCodes.FlagUnprotected);
        Run(Reliance, [Buy(40, 1250m)], null, Graded, OutcomeCodes.Unavailable)!.Flags.ShouldBeEmpty();
    }
}
