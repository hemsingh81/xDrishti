using XDrishti.Domain.Accounts;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-04 criterion 5: a ticket with a live allocation needs both switches ON in stored snapshots.</summary>
public sealed class LiveAutoApprovalTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 3, 35, 0, TimeSpan.Zero);

    private static readonly AutonomySettings On = new(7, new string('a', 64), true, false, 0.58, 3);

    private static readonly LiveSettings LiveOn = new(3, new string('b', 64), true, false);

    private static ChecklistResult LiveCheck() => PreTradeChecklist.Evaluate(
        new ChecklistTicket("AAA", "BUY", 100, 10, 0.1, "IT"),
        new ChecklistAccount("acc-1", TradingMode.ManualLive, true, 0.5, 3.0, 2, 2.0, 10000, 1.0, 0, 0, new LiveReadyFacts(true, true, true, true)),
        new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
        ChecklistLimits.Default,
        ChecklistPolicy.Default);

    private static AutoApprovalTicket LiveTicket() => new(11, "AAA", TicketGroup.Primary, TicketState.Proposed, 0.62, [], ["acc-1"], ["acc-1"]);

    private static AutoApprovalDecision Run(LiveSettings? live, AutoApprovalTicket? ticket = null, ChecklistResult? check = null) =>
        AutoApprovalPolicy.Decide(ticket ?? LiveTicket(), [check ?? LiveCheck()], On, new AutoApprovalContext(Cutoff.AddMinutes(-10), Cutoff, 0), live);

    [Fact]
    public void Both_switches_on_with_a_passing_live_checklist_approves_and_carries_both_snapshots()
    {
        var d = Run(LiveOn);

        d.Approved.ShouldBeTrue();
        (d.SettingsId, d.LiveSettingsId, d.LiveSettingsHash).ShouldBe((7, 3, LiveOn.Hash));
        d.Summary.ShouldContain("live settings 3");
    }

    [Fact]
    public void The_live_switch_off_or_not_stored_declines_with_auto_live_off()
    {
        Run(null).Reasons.Select(r => r.Code).ShouldContain(AutoDeclineCodes.AutoLiveOff);
        Run(LiveSettings.Defaults).Reasons.Select(r => r.Code).ShouldContain(AutoDeclineCodes.AutoLiveOff);
        Run(LiveOn with { AutoApproveTickets = false }).Approved.ShouldBeFalse();
        Run(LiveOn with { Id = null }).Approved.ShouldBeFalse();
    }

    [Fact]
    public void An_unavailable_live_gate_never_counts_as_a_pass()
    {
        var unknown = PreTradeChecklist.Evaluate(
            new ChecklistTicket("AAA", "BUY", 100, 10, 0.1, "IT"),
            new ChecklistAccount("acc-1", TradingMode.ManualLive, true, 0.5, 3.0, 2, 2.0, 10000, 1.0, 0, 0, new LiveReadyFacts(true, null, null, null)),
            new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
            ChecklistLimits.Default,
            ChecklistPolicy.Default);

        var d = Run(LiveOn, check: unknown);

        d.Approved.ShouldBeFalse();
        d.Reasons.Select(r => r.Code).ShouldContain($"{AutoDeclineCodes.CheckUnavailable}:{ChecklistCodes.AccountLiveReady}");
    }

    [Fact]
    public void A_paper_ticket_ignores_the_live_switch()
    {
        var paperCheck = PreTradeChecklist.Evaluate(
            new ChecklistTicket("AAA", "BUY", 100, 10, 0.1, "IT"),
            new ChecklistAccount("acc-1", TradingMode.Paper, true, 0.5, 3.0, 2, 2.0, 10000, 1.0, 0, 0),
            new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
            ChecklistLimits.Default,
            ChecklistPolicy.Default);

        var d = Run(null, new AutoApprovalTicket(11, "AAA", TicketGroup.Primary, TicketState.Proposed, 0.62, [], ["acc-1"]), paperCheck);

        d.Approved.ShouldBeTrue();
        d.LiveSettingsId.ShouldBeNull();
    }

    [Fact]
    public void A_live_account_with_the_paper_check_list_is_a_missing_checklist()
    {
        var paper = PreTradeChecklist.Evaluate(
            new ChecklistTicket("AAA", "BUY", 100, 10, 0.1, "IT"),
            new ChecklistAccount("acc-1", TradingMode.Paper, true, 0.5, 3.0, 2, 2.0, 10000, 1.0, 0, 0),
            new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
            ChecklistLimits.Default,
            ChecklistPolicy.Default);

        Run(LiveOn, check: paper).Reasons.Select(r => r.Code).ShouldContain(AutoDeclineCodes.ChecklistMissing);
    }

    [Fact]
    public void The_stored_checklist_of_a_live_allocation_starts_with_the_live_gate()
    {
        var ok = StoredChecklist.Build("acc-1", "AAA", paperMode: false, ["checklist:account_live_ready:unavailable"], liveMode: true)!;

        ok.Checks.Select(c => c.Code).ShouldBe(ChecklistCodes.LiveAll);
        ok.Checks[0].Outcome.ShouldBe(XDrishti.Domain.Selection.FilterOutcome.Unavailable);
        StoredChecklist.Build("acc-1", "AAA", paperMode: false, [], liveMode: false)!.Refused.ShouldBeTrue();
    }
}
