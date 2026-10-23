using XDrishti.Domain.Accounts;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Autonomy;

/// <summary>Spec P7-12: the hand table of the auto-approval rule. Anything missing, unavailable or short of the terms declines (leaves the ticket for the owner); nothing is a pass by default.</summary>
public sealed class AutoApprovalPolicyTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Before = Cutoff.AddMinutes(-10);

    private static readonly AutonomySettings On = new(7, new string('a', 64), true, false, 0.58, 3);

    private static AutoApprovalTicket Ticket(TicketGroup group = TicketGroup.Primary, TicketState state = TicketState.Proposed, double? pwin = 0.62, string[]? unavailable = null, string[]? accounts = null) =>
        new(11, "AAA", group, state, pwin, unavailable ?? [], accounts ?? ["acc-1"]);

    private static ChecklistResult Check(string account = "acc-1", string symbol = "AAA", ChecklistFacts? facts = null, TradingMode mode = TradingMode.Paper) =>
        PreTradeChecklist.Evaluate(
            new ChecklistTicket(symbol, "BUY", 100, 10, 0.5, "IT"),
            new ChecklistAccount(account, mode, true, 1.0, 3.0, 2, 2.0, 10000, 1.0, 0, 0),
            facts ?? new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
            ChecklistLimits.Default,
            ChecklistPolicy.Default);

    private static AutoApprovalDecision Run(AutoApprovalTicket? ticket = null, IReadOnlyList<ChecklistResult>? checks = null, AutonomySettings? settings = null, DateTimeOffset? now = null, int today = 0) =>
        AutoApprovalPolicy.Decide(ticket ?? Ticket(), checks ?? [Check()], settings ?? On, new AutoApprovalContext(now ?? Before, Cutoff, today));

    private static string[] Codes(AutoApprovalDecision d) => [.. d.Reasons.Select(r => r.Code)];

    [Fact]
    public void A_primary_proposed_ticket_above_the_threshold_with_every_check_passed_before_the_cutoff_is_approved()
    {
        var d = Run();

        d.Approved.ShouldBeTrue();
        d.Reasons.ShouldBeEmpty();
        d.Summary.ShouldStartWith(AutoApprovalWording.Marker);
        d.SettingsId.ShouldBe(7);
        d.SettingsHash.ShouldBe(On.Hash);
        d.PolicyVersion.ShouldBe("auto-approval/1");
    }

    [Fact]
    public void The_wording_and_names_are_pinned()
    {
        AutoApprovalWording.Actor.ShouldBe("rule:autonomy");
        AutoApprovalWording.Actor.ShouldNotStartWith(OwnerConfirmation.OwnerPrefix);
        AutoApprovalWording.Kind.ShouldBe("auto");
        AutoApprovalWording.Marker.ShouldBe("auto-approved by rule, no order exists");
        AutonomySettings.AutoApproveKey.ShouldBe("autonomy.auto_approve_tickets");
        AutonomySettings.PromoteFromReserveKey.ShouldBe("autonomy.promote_from_reserve");
    }

    [Fact]
    public void The_defaults_have_both_switches_off_the_design_threshold_and_no_stored_row()
    {
        var d = AutonomySettings.Defaults;

        d.AutoApproveTickets.ShouldBeFalse();
        d.PromoteFromReserve.ShouldBeFalse();
        d.MinPWin.ShouldBe(0.58);
        d.MaxAutoPerDay.ShouldBe(3);
        d.Id.ShouldBeNull();
        Run(settings: AutonomySettings.Defaults).Approved.ShouldBeFalse();
    }

    [Fact]
    public void With_the_switch_off_the_only_reason_is_that_approval_is_manual()
    {
        var d = Run(settings: On with { AutoApproveTickets = false });

        d.Approved.ShouldBeFalse();
        Codes(d).ShouldBe([AutoDeclineCodes.SettingOff]);
    }

    [Fact]
    public void A_switch_on_without_a_stored_row_declines()
    {
        Codes(Run(settings: new AutonomySettings(null, null, true, false, 0.58, 3))).ShouldContain(AutoDeclineCodes.SettingsNotStored);
        Codes(Run(settings: On with { Hash = null })).ShouldContain(AutoDeclineCodes.SettingsNotStored);
    }

    [Theory]
    [InlineData(TicketGroup.Reserve)]
    [InlineData(TicketGroup.Rejected)]
    public void Only_a_primary_ticket_is_ever_auto_approved_never_a_reserve(TicketGroup group)
    {
        Codes(Run(Ticket(group))).ShouldBe([AutoDeclineCodes.NotPrimary]);
    }

    [Theory]
    [InlineData(TicketState.Modified)]
    [InlineData(TicketState.Approved)]
    [InlineData(TicketState.Rejected)]
    [InlineData(TicketState.Lapsed)]
    [InlineData(TicketState.Armed)]
    public void A_ticket_the_owner_touched_or_that_is_decided_is_left_alone(TicketState state)
    {
        Codes(Run(Ticket(state: state))).ShouldBe([AutoDeclineCodes.NotProposed]);
    }

    [Fact]
    public void At_the_cutoff_and_after_it_the_rule_declines_and_just_before_it_approves()
    {
        Run(now: Cutoff.AddTicks(-1)).Approved.ShouldBeTrue();
        Codes(Run(now: Cutoff)).ShouldBe([AutoDeclineCodes.AfterCutoff]);
        Codes(Run(now: Cutoff.AddHours(1))).ShouldBe([AutoDeclineCodes.AfterCutoff]);
    }

    [Theory]
    [InlineData(0.58, true)]
    [InlineData(0.5800001, true)]
    [InlineData(0.5799, false)]
    [InlineData(0.0, false)]
    public void P_win_at_the_threshold_approves_and_below_it_declines(double pwin, bool approved)
    {
        var d = Run(Ticket(pwin: pwin));

        d.Approved.ShouldBe(approved);
        if (!approved)
        {
            Codes(d).ShouldBe([AutoDeclineCodes.PWinBelowThreshold]);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void An_unknown_or_impossible_p_win_is_never_a_pass(double? pwin)
    {
        Codes(Run(Ticket(pwin: pwin))).ShouldBe([AutoDeclineCodes.PWinUnavailable]);
    }

    [Fact]
    public void The_threshold_is_the_one_of_the_stored_settings()
    {
        Run(Ticket(pwin: 0.62), settings: On with { MinPWin = 0.65 }).Approved.ShouldBeFalse();
        Run(Ticket(pwin: 0.62), settings: On with { MinPWin = 0.60 }).Approved.ShouldBeTrue();
    }

    [Fact]
    public void A_ticket_that_lists_an_unavailable_input_is_never_approved()
    {
        var d = Run(Ticket(unavailable: ["checklist", "smart_rules"]));

        Codes(d).ShouldBe([AutoDeclineCodes.InputsUnavailable]);
        d.Reasons[0].Detail.ShouldContain("smart_rules");
    }

    [Fact]
    public void A_ticket_with_no_account_cannot_pass_a_checklist()
    {
        Codes(Run(Ticket(accounts: []), checks: [])).ShouldBe([AutoDeclineCodes.NoAccount]);
    }

    [Fact]
    public void No_checklist_at_all_declines()
    {
        Codes(Run(checks: [])).ShouldBe([AutoDeclineCodes.ChecklistMissing]);
    }

    [Fact]
    public void A_checklist_for_another_account_or_another_symbol_does_not_count()
    {
        Codes(Run(checks: [Check(account: "acc-2")])).ShouldBe([AutoDeclineCodes.ChecklistMissing]);
        Codes(Run(checks: [Check(symbol: "BBB")])).ShouldBe([AutoDeclineCodes.ChecklistMissing]);
        Codes(Run(checks: [Check(), Check()])).ShouldBe([AutoDeclineCodes.ChecklistMissing]);
    }

    [Fact]
    public void A_live_account_is_refused_by_the_checklist_and_declines()
    {
        Codes(Run(checks: [Check(mode: TradingMode.ManualLive)])).ShouldBe([AutoDeclineCodes.ChecklistMissing]);
    }

    [Fact]
    public void An_unavailable_check_declines_and_names_the_check()
    {
        var d = Run(checks: [Check(facts: new ChecklistFacts(DataQualityOk: null, HeldLongAnyAccount: false, OpenTradeExists: false, DaysToResults: 10, InFnoBan: false, UnderSurveillance: false, PriceBandLower: 90, PriceBandUpper: 110, AverageDailyVolume: 10000, MaxCorrelation: 0.3))]);

        Codes(d).ShouldBe([$"{AutoDeclineCodes.CheckUnavailable}:{ChecklistCodes.DataQuality}"]);
    }

    [Fact]
    public void Every_unavailable_input_of_the_checklist_declines_so_a_ticket_with_no_data_sources_is_never_approved()
    {
        var d = Run(checks: [Check(facts: new ChecklistFacts())]);

        d.Approved.ShouldBeFalse();
        Codes(d).ShouldAllBe(c => c.StartsWith(AutoDeclineCodes.CheckUnavailable + ":", StringComparison.Ordinal));
        Codes(d).ShouldContain($"{AutoDeclineCodes.CheckUnavailable}:{ChecklistCodes.FnoBan}");
    }

    [Fact]
    public void A_failed_check_declines_even_if_the_owner_could_have_confirmed_it()
    {
        var banned = new ChecklistFacts(true, false, false, 10, true, false, 90, 110, 10000, 0.3);

        Codes(Run(checks: [Check(facts: banned)])).ShouldBe([$"{AutoDeclineCodes.CheckFailed}:{ChecklistCodes.FnoBan}"]);
    }

    [Fact]
    public void With_two_accounts_every_one_must_pass()
    {
        var ticket = Ticket(accounts: ["acc-1", "acc-2"]);
        var banned = new ChecklistFacts(true, false, false, 10, true, false, 90, 110, 10000, 0.3);

        Run(ticket, [Check("acc-1"), Check("acc-2")]).Approved.ShouldBeTrue();
        Codes(Run(ticket, [Check("acc-1"), Check("acc-2", facts: banned)])).ShouldBe([$"{AutoDeclineCodes.CheckFailed}:{ChecklistCodes.FnoBan}"]);
        Codes(Run(ticket, [Check("acc-1")])).ShouldBe([AutoDeclineCodes.ChecklistMissing]);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(9, false)]
    public void The_daily_cap_stops_the_rule_at_the_stored_number(int today, bool approved)
    {
        var d = Run(today: today);

        d.Approved.ShouldBe(approved);
        if (!approved)
        {
            Codes(d).ShouldBe([AutoDeclineCodes.CapReached]);
        }
    }

    [Fact]
    public void Every_reason_that_applies_is_reported_not_only_the_first()
    {
        var d = Run(Ticket(TicketGroup.Reserve, TicketState.Modified, pwin: 0.1, unavailable: ["x"]), checks: [], now: Cutoff, today: 3);

        Codes(d).ShouldBe([AutoDeclineCodes.NotPrimary, AutoDeclineCodes.NotProposed, AutoDeclineCodes.AfterCutoff, AutoDeclineCodes.PWinBelowThreshold, AutoDeclineCodes.InputsUnavailable, AutoDeclineCodes.ChecklistMissing, AutoDeclineCodes.CapReached]);
        d.Summary.ShouldStartWith("left for the owner");
    }

    [Fact]
    public void A_decline_carries_the_settings_snapshot_too()
    {
        var d = Run(Ticket(pwin: 0.1));

        d.SettingsId.ShouldBe(7);
        d.SettingsHash.ShouldBe(On.Hash);
    }

    [Theory]
    [InlineData(0.5, 1, true)]
    [InlineData(0.99, 20, true)]
    [InlineData(0.49, 3, false)]
    [InlineData(1.0, 3, false)]
    [InlineData(0.58, 0, false)]
    [InlineData(0.58, 21, false)]
    [InlineData(double.NaN, 3, false)]
    public void The_ranges_of_the_values_are_those_the_database_checks(double min, int cap, bool valid)
    {
        AutonomySettings.ValuesAreValid(min, cap).ShouldBe(valid);
    }
}
