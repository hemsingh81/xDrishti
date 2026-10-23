using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Autonomy;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Autonomy;

/// <summary>Spec P7-12 over fake stores: with the switch OFF the pass reads the switch and nothing else; ON, only a ticket that meets every term is written; the settings are the owner's act only.</summary>
public sealed class AutonomyHandlersTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 3, 35, 0, TimeSpan.Zero);

    private static readonly Guid Account = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a01");

    private static readonly AutonomySettings On = new(7, new string('a', 64), true, false, 0.58, 3);

    private readonly IAutonomySettingsStore _settings = Substitute.For<IAutonomySettingsStore>();

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private readonly IAutoApprovalStore _auto = Substitute.For<IAutoApprovalStore>();

    private readonly IChecklistSource _checklists = Substitute.For<IChecklistSource>();

    private readonly FakeTimeProvider _time = new(Cutoff.AddMinutes(-30));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanItemRow Item(long id, TicketState state = TicketState.Proposed, TicketGroup group = TicketGroup.Primary, double? pwin = 0.7, string[]? unavailable = null) =>
        new(id, 1, new NewPlanItem((int)id, group, "ABC", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"], new PlanEvidence("c", 30, "r", "b", 0.6, null, unavailable ?? [], null, pwin), [new NewAllocation(Account, "paper", 10)]), state);

    private static PlanRow Plan(PlanState state = PlanState.Published) =>
        new(1, new NewPlan("k", new DateOnly(2026, 10, 26), Cutoff, 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), state, 2, 0, 0);

    private static ChecklistResult Passing() =>
        PreTradeChecklist.Evaluate(
            new ChecklistTicket("ABC", "BUY", 100, 10, 0.5, "IT"),
            new ChecklistAccount(Account.ToString(), TradingMode.Paper, true, 1.0, 3.0, 2, 2.0, 10000, 1.0, 0, 0),
            new ChecklistFacts(true, false, false, 10, false, false, 90, 110, 10000, 0.3),
            ChecklistLimits.Default,
            ChecklistPolicy.Default);

    private AutoApprovePlanHandler Pass() => new(_settings, _plans, _auto, _checklists, _time, NullLogger<AutoApprovePlanHandler>.Instance);

    private void Arrange(AutonomySettings settings, params PlanItemRow[] items)
    {
        _settings.CurrentAsync(Ct).Returns(settings);
        _plans.FindAsync(1, Ct).Returns(Plan());
        _plans.ItemsAsync(1, Ct).Returns(items);
        _auto.CountAutoApprovalsAsync(Arg.Any<DateOnly>(), Ct).Returns(0);
        _checklists.ForTicketAsync(Arg.Any<PlanItemRow>(), Ct).Returns([Passing()]);
        _auto.AutoApproveAsync(Arg.Any<AutoApproveMove>(), Ct).Returns(MoveStatus.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_the_switch_off_or_nothing_stored_the_pass_reads_the_switch_and_does_nothing_else(bool storedRow)
    {
        _settings.CurrentAsync(Ct).Returns(storedRow ? On with { AutoApproveTickets = false } : AutonomySettings.Defaults);

        var result = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Value.SwitchOn.ShouldBeFalse();
        result.Value.Approved.ShouldBe(0);
        await _plans.DidNotReceive().FindAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _plans.DidNotReceive().ItemsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _auto.DidNotReceive().AutoApproveAsync(Arg.Any<AutoApproveMove>(), Arg.Any<CancellationToken>());
        await _auto.DidNotReceive().CountAutoApprovalsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _checklists.DidNotReceive().ForTicketAsync(Arg.Any<PlanItemRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_the_switch_on_a_qualifying_primary_ticket_is_approved_by_the_rule_with_the_settings_snapshot()
    {
        Arrange(On, Item(5));

        var result = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Value.Approved.ShouldBe(1);
        await _auto.Received(1).AutoApproveAsync(Arg.Is<AutoApproveMove>(m => m.ItemId == 5 && m.SettingsId == 7 && m.SettingsHash == On.Hash && m.PolicyVersion == "auto-approval/1" && m.Reason.StartsWith(AutoApprovalWording.Marker, StringComparison.Ordinal)), Ct);
    }

    [Fact]
    public async Task With_no_checklist_source_nothing_is_ever_approved()
    {
        Arrange(On, Item(5));
        var none = new AutoApprovePlanHandler(_settings, _plans, _auto, new NullChecklistSource(), _time, NullLogger<AutoApprovePlanHandler>.Instance);

        var result = await none.HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Value.Approved.ShouldBe(0);
        result.Value.Declined.Single().Codes.ShouldContain(AutoDeclineCodes.ChecklistMissing);
        await _auto.DidNotReceive().AutoApproveAsync(Arg.Any<AutoApproveMove>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reserve_decided_unavailable_and_low_p_win_tickets_are_never_written()
    {
        Arrange(On, Item(1, group: TicketGroup.Reserve), Item(2, state: TicketState.Modified), Item(3, unavailable: ["smart_rules"]), Item(4, pwin: 0.5), Item(5, pwin: null));

        var result = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Value.Approved.ShouldBe(0);
        result.Value.Declined.Select(d => d.ItemId).ShouldBe([3, 4, 5]);
        await _auto.DidNotReceive().AutoApproveAsync(Arg.Any<AutoApproveMove>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_daily_cap_counts_what_is_already_approved_and_each_new_approval()
    {
        Arrange(On with { MaxAutoPerDay = 2 }, Item(1), Item(2), Item(3));
        _auto.CountAutoApprovalsAsync(Arg.Any<DateOnly>(), Ct).Returns(1);

        var result = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Value.Approved.ShouldBe(1);
        result.Value.Declined.Select(d => d.ItemId).ShouldBe([2, 3]);
    }

    [Fact]
    public async Task When_the_owner_decided_first_the_store_says_stale_and_the_ticket_is_left_as_the_owner_left_it()
    {
        Arrange(On, Item(5));
        _auto.AutoApproveAsync(Arg.Any<AutoApproveMove>(), Ct).Returns(MoveStatus.Stale);

        var result = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Value.Approved.ShouldBe(0);
        result.Value.Declined.Single().Codes.ShouldBe(["store_stale"]);
    }

    [Fact]
    public async Task At_the_cutoff_the_pass_is_refused_and_writes_nothing()
    {
        Arrange(On, Item(5));
        _time.SetUtcNow(Cutoff);

        var late = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        late.IsFailure.ShouldBeTrue();
        await _auto.DidNotReceive().AutoApproveAsync(Arg.Any<AutoApproveMove>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task For_a_plan_that_is_not_open_the_pass_is_refused_and_writes_nothing()
    {
        Arrange(On, Item(5));
        _plans.FindAsync(1, Ct).Returns(Plan(PlanState.Approved));

        var closed = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        closed.IsFailure.ShouldBeTrue();
        await _auto.DidNotReceive().AutoApproveAsync(Arg.Any<AutoApproveMove>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_plan_is_not_found()
    {
        _settings.CurrentAsync(Ct).Returns(On);
        _plans.FindAsync(1, Ct).Returns((PlanRow?)null);

        var result = await Pass().HandleAsync(new AutoApprovePlanCommand(1), Ct);

        result.Error.Code.ShouldBe("plans.not_found");
    }

    [Theory]
    [InlineData("rule:autonomy")]
    [InlineData("system:nightly")]
    [InlineData("cli:xd")]
    [InlineData("")]
    public async Task A_change_by_anything_but_an_owner_is_refused_before_the_store_is_touched(string actor)
    {
        var handler = new ChangeAutonomySettingsHandler(_settings, NullLogger<ChangeAutonomySettingsHandler>.Instance);

        var result = await handler.HandleAsync(new ChangeAutonomySettingsCommand(actor, "turn on", true, false, 0.58, 3), Ct);

        result.Error.Code.ShouldBe("autonomy.owner_only");
        await _settings.DidNotReceive().AppendAsync(Arg.Any<AutonomyChange>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_owner_change_is_appended_against_the_row_the_owner_saw_and_returned_with_the_history()
    {
        var row = new AutonomyChangeRow(8, new string('c', 64), true, false, 0.6, 2, "owner:hem", "go", Cutoff);
        _settings.CurrentAsync(Ct).Returns(On);
        _settings.AppendAsync(Arg.Any<AutonomyChange>(), 7, Ct).Returns(row);
        _settings.HistoryAsync(Arg.Any<int>(), Ct).Returns([row]);
        var handler = new ChangeAutonomySettingsHandler(_settings, NullLogger<ChangeAutonomySettingsHandler>.Instance);

        var result = await handler.HandleAsync(new ChangeAutonomySettingsCommand("owner:hem", "go", true, false, 0.6, 2), Ct);

        result.Value.Id.ShouldBe(8);
        result.Value.MinPWin.ShouldBe(0.6);
        result.Value.History.Single().Actor.ShouldBe("owner:hem");
        result.Value.Notice.ShouldBe(PlanWording.NoOrder);
    }

    [Fact]
    public async Task A_change_that_lost_the_race_is_stale()
    {
        _settings.CurrentAsync(Ct).Returns(On);
        _settings.AppendAsync(Arg.Any<AutonomyChange>(), 7, Ct).Returns((AutonomyChangeRow?)null);
        var handler = new ChangeAutonomySettingsHandler(_settings, NullLogger<ChangeAutonomySettingsHandler>.Instance);

        var result = await handler.HandleAsync(new ChangeAutonomySettingsCommand("owner:hem", "go", true, false, 0.6, 2), Ct);

        result.Error.Code.ShouldBe("autonomy.stale");
    }

    [Fact]
    public async Task Reading_with_nothing_stored_shows_everything_off()
    {
        _settings.CurrentAsync(Ct).Returns(AutonomySettings.Defaults);
        _settings.HistoryAsync(Arg.Any<int>(), Ct).Returns([]);

        var view = await new GetAutonomySettingsHandler(_settings).HandleAsync(new GetAutonomySettingsQuery(), Ct);

        view.AutoApproveTickets.ShouldBeFalse();
        view.PromoteFromReserve.ShouldBeFalse();
        view.Id.ShouldBeNull();
        view.History.ShouldBeEmpty();
    }
}
