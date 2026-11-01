using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

/// <summary>
/// The P12-03 amendment "a Soft-only rejection is not final while a confirm ticket is open" (P12-08 criterion 12, ADR 0166 amendment): <c>checking&gt;rejected</c> is made at once for a Hard non-pass (Fail or Unavailable), a read mode other than
/// <c>semi_auto</c> (an unread mode moves nothing), and a Soft-only rejection without an open ticket; it waits only while the newest ticket is open by the database clock; a confirmed ticket continues to the hand-off unless the Soft set changed.
/// </summary>
public sealed class RejectionFinalityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 4, 16, 0, TimeSpan.Zero);
    private static readonly ModeAnswer SemiAuto = new(ExecMode.SemiAuto, true);

    private static RejectionRun SoftOnly(params string[] soft) => new(91, CheckVerdict.Rejected, [], soft.Length == 0 ? [CheckCodes.AdvShare] : soft);

    private static RejectionTicket Ticket(string? outcome, double expiresInSeconds = 60, params string[] soft) =>
        new(Guid.NewGuid(), outcome, Now.AddSeconds(expiresInSeconds), soft.Length == 0 ? [CheckCodes.AdvShare] : soft);

    private static void Expect(RejectionDecision d, RejectionStep step, string reason, IReadOnlyList<string> codes)
    {
        d.Step.ShouldBe(step);
        d.Reason.ShouldBe(reason);
        d.Codes.ShouldBe(codes);
    }

    [Fact]
    public void A_soft_only_rejection_in_semi_auto_with_an_open_ticket_waits()
    {
        var decision = RejectionFinality.Decide(SoftOnly(), SemiAuto, Ticket(null), Now);

        decision.Step.ShouldBe(RejectionStep.AwaitTicket);
        decision.Reason.ShouldBe(RejectionReasons.TicketOpen);
    }

    [Fact]
    public void A_soft_only_rejection_without_a_ticket_is_final()
    {
        var decision = RejectionFinality.Decide(SoftOnly(CheckCodes.AdvShare, CheckCodes.FnoBan), SemiAuto, null, Now);

        Expect(decision, RejectionStep.RejectNow, RejectionReasons.NoTicket, [CheckCodes.AdvShare, CheckCodes.FnoBan]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TicketOutcomes.Confirmed)]
    public void A_hard_failure_is_final_whatever_the_ticket_says(string? outcome)
    {
        var run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.LtpCollar], [CheckCodes.AdvShare]);

        var decision = RejectionFinality.Decide(run, SemiAuto, Ticket(outcome), Now);

        Expect(decision, RejectionStep.RejectNow, RejectionReasons.HardCheck, [CheckCodes.LtpCollar, CheckCodes.AdvShare]);
    }

    [Fact]
    public void An_unavailable_hard_input_is_a_hard_non_pass_and_is_final_with_an_open_ticket()
    {
        // The stored run lists every Hard check that was not Pass (Fail and Unavailable alike); unavailable is never a pass.
        var run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.FeedFresh, CheckCodes.MarginFunds], []);

        var decision = RejectionFinality.Decide(run, SemiAuto, Ticket(null), Now);

        decision.Step.ShouldBe(RejectionStep.RejectNow);
        decision.Reason.ShouldBe(RejectionReasons.HardCheck);
    }

    [Theory]
    [InlineData(TicketOutcomes.Skipped, RejectionReasons.TicketSkipped)]
    [InlineData(TicketOutcomes.Expired, RejectionReasons.TicketExpired)]
    [InlineData(TicketOutcomes.Refused, RejectionReasons.TicketRefused)]
    [InlineData("something_new", RejectionReasons.TicketClosed)]
    public void A_ticket_the_owner_skipped_or_the_system_closed_makes_the_soft_only_rejection_final(string outcome, string reason)
    {
        var decision = RejectionFinality.Decide(SoftOnly(), SemiAuto, Ticket(outcome), Now);

        Expect(decision, RejectionStep.RejectNow, reason, [CheckCodes.AdvShare]);
    }

    [Fact]
    public void An_open_ticket_past_its_expiry_by_the_database_clock_is_final_before_the_expiry_loop_has_closed_it()
    {
        RejectionFinality.Decide(SoftOnly(), SemiAuto, Ticket(null, expiresInSeconds: 0.001), Now).Step.ShouldBe(RejectionStep.AwaitTicket);

        var atExpiry = RejectionFinality.Decide(SoftOnly(), SemiAuto, Ticket(null, expiresInSeconds: 0), Now);
        var after = RejectionFinality.Decide(SoftOnly(), SemiAuto, Ticket(null, expiresInSeconds: -5), Now);

        Expect(atExpiry, RejectionStep.RejectNow, RejectionReasons.TicketExpired, [CheckCodes.AdvShare]);
        after.Reason.ShouldBe(RejectionReasons.TicketExpired);
    }

    [Fact]
    public void A_confirmed_ticket_for_the_same_soft_set_continues_to_the_hand_off_and_a_changed_set_is_final()
    {
        var same = RejectionFinality.Decide(SoftOnly(CheckCodes.AdvShare, CheckCodes.FnoBan), SemiAuto, Ticket(TicketOutcomes.Confirmed, 60, CheckCodes.FnoBan, CheckCodes.AdvShare), Now);
        var changed = RejectionFinality.Decide(SoftOnly(CheckCodes.AdvShare, CheckCodes.FnoBan), SemiAuto, Ticket(TicketOutcomes.Confirmed, 60, CheckCodes.AdvShare), Now);

        Expect(same, RejectionStep.None, RejectionReasons.Confirmed, []);
        Expect(changed, RejectionStep.RejectNow, RejectionReasons.SoftSetChanged, [CheckCodes.AdvShare, CheckCodes.FnoBan]);
    }

    [Fact]
    public void An_open_ticket_whose_soft_set_differs_still_waits_because_the_tap_refuses_it_and_closes_the_ticket()
    {
        RejectionFinality.Decide(SoftOnly(CheckCodes.AdvShare, CheckCodes.FnoBan), SemiAuto, Ticket(null, 60, CheckCodes.AdvShare), Now).Step.ShouldBe(RejectionStep.AwaitTicket);
    }

    [Theory]
    [InlineData(ExecMode.Paper)]
    [InlineData(ExecMode.ManualLive)]
    [InlineData(ExecMode.Assisted)]
    [InlineData(ExecMode.AutoLive)]
    public void A_soft_only_rejection_outside_semi_auto_is_final_even_with_an_open_ticket(ExecMode mode)
    {
        var decision = RejectionFinality.Decide(SoftOnly(), new ModeAnswer(mode, true), Ticket(null), Now);

        Expect(decision, RejectionStep.RejectNow, RejectionReasons.NotSemiAuto, [CheckCodes.AdvShare]);
    }

    [Fact]
    public void A_mode_that_could_not_be_read_moves_nothing_for_a_soft_only_rejection_but_a_hard_failure_still_rejects()
    {
        Expect(RejectionFinality.Decide(SoftOnly(), null, Ticket(null), Now), RejectionStep.None, RejectionReasons.ModeUnavailable, []);
        Expect(RejectionFinality.Decide(new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.NoHalt], []), null, Ticket(null), Now), RejectionStep.RejectNow, RejectionReasons.HardCheck, [CheckCodes.NoHalt]);
    }

    [Fact]
    public void A_rejected_run_without_any_non_pass_is_final_and_a_run_that_is_not_rejected_or_absent_moves_nothing()
    {
        Expect(RejectionFinality.Decide(new RejectionRun(91, CheckVerdict.Rejected, [], []), SemiAuto, Ticket(null), Now), RejectionStep.RejectNow, RejectionReasons.CheckRejected, []);
        RejectionFinality.Decide(new RejectionRun(91, CheckVerdict.Cleared, [], []), SemiAuto, null, Now).Step.ShouldBe(RejectionStep.None);
        RejectionFinality.Decide(new RejectionRun(91, CheckVerdict.ClearedByConfirmation, [], [CheckCodes.AdvShare]), SemiAuto, null, Now).Step.ShouldBe(RejectionStep.None);
        Expect(RejectionFinality.Decide(null, SemiAuto, null, Now), RejectionStep.None, RejectionReasons.NoRun, []);
    }

    [Fact]
    public void The_wait_never_happens_for_any_hard_code_mode_or_closed_outcome_over_the_whole_table()
    {
        ModeAnswer?[] modes = [null, .. Enum.GetValues<ExecMode>().Select(m => new ModeAnswer(m, true))];
        RejectionTicket?[] tickets = [null, Ticket(null), Ticket(null, -1), .. TicketOutcomes.All.Select(o => Ticket(o))];
        var hards = CheckCodes.All.Where(c => CheckCodes.ClassOf(c) == CheckClass.Hard).Select(c => (IReadOnlyList<string>)[c]).Prepend([]);

        foreach (var hard in hards)
        {
            foreach (var mode in modes)
            {
                foreach (var ticket in tickets)
                {
                    var d = RejectionFinality.Decide(new RejectionRun(91, CheckVerdict.Rejected, hard, [CheckCodes.AdvShare]), mode, ticket, Now);
                    var waits = d.Step == RejectionStep.AwaitTicket;
                    var mayWait = hard.Count == 0 && mode?.EffectiveMode == ExecMode.SemiAuto && ticket is { Outcome: null } t && Now < t.ExpiresAt;
                    waits.ShouldBe(mayWait, $"hard={string.Join(',', hard)} mode={mode?.EffectiveMode} ticket={ticket?.Outcome ?? (ticket is null ? "none" : "open")}");
                    if (hard.Count > 0)
                    {
                        d.Step.ShouldBe(RejectionStep.RejectNow);
                    }
                }
            }
        }
    }
}
