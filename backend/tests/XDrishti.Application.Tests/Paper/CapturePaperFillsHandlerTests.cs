using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Application.Research;
using XDrishti.Application.Tests.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Paper;

/// <summary>
/// Spec P10-01: the capture use case over fake ports and the real replayer. Only armed tickets, once per allocation at the owner's quantity, the grade's prices and R, pending is not stored, final facts are skips, idempotent, no ticket moves.
/// </summary>
public sealed class CapturePaperFillsHandlerTests
{
    private static readonly DateOnly F = PaperFixture.Session;

    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*";

    private static readonly string Hash = new('a', 64);

    private static readonly DateTimeOffset Armed = new(2026, 11, 3, 9, 10, 0, TimeSpan.FromMinutes(330));

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private readonly IPaperStore _paper = Substitute.For<IPaperStore>();

    private readonly IEpochGate _gate = Substitute.For<IEpochGate>();

    private readonly IGradeSettings _settings = Substitute.For<IGradeSettings>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));

    private SessionWrite? _written;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CapturePaperFillsHandlerTests()
    {
        _strategies.GetByHashAsync(Hash, Ct).Returns(new StoredVersion("ORB", 1, 1, Hash, "text", "{}", null, null, DateTimeOffset.UnixEpoch));
        var basket = Guid.NewGuid();
        _baskets.ListAsync(false, Ct).Returns([new BasketView(basket, "B", "#000", "p", null, 0, 1, DateTimeOffset.UnixEpoch, null, "manual", false, null, null)]);
        var member = new BasketMemberView(Guid.NewGuid(), RunFixture.Summary(Instrument, "NSE:AAA", null, false), 0, DateTimeOffset.UnixEpoch, null, null, "owner", null, null);
        _baskets.MembersAsync(basket, null, null, Ct).Returns(new BasketMembers(basket, "B", null, null, 1, [member]));
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        var epoch = new EpochRow(1, new DateOnly(2026, 11, 2), DateTimeOffset.UnixEpoch, "first", "default", "system");
        _gate.ResolveAsync(Arg.Any<IReadOnlyList<DateOnly>>(), Ct).Returns(call => new EpochResolution(epoch, call.Arg<IReadOnlyList<DateOnly>>().Distinct().ToDictionary(s => s, _ => "measured")));
        _paper.PairsAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<(long, Guid), StoredPair>());
        _paper.CoverageAsync(Ct).Returns(new SessionCoverage(new HashSet<DateOnly>(), new HashSet<DateOnly>()));
        _paper.WriteSessionAsync(Arg.Any<SessionWrite>(), Ct).Returns(call =>
        {
            _written = call.Arg<SessionWrite>();
            return new SessionWritten(_written.Entries.Count, _written.Entries.Count(e => e.Exit is not null) + _written.Exits.Count, _written.Skips.Count);
        });
        _settings.Capital.Returns(100_000m);
        _plans.ListAsync(F, null, Arg.Any<int>(), Ct).Returns([new PlanRow(1, new NewPlan("k", F, new DateTimeOffset(2026, 11, 3, 3, 35, 0, TimeSpan.Zero), 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), PlanState.Armed, 1, 0, 0)]);
        _plans.HistoryAsync(1, Ct).Returns([Event(11, "armed", Armed)]);
        _plans.ApprovalsAsync(1, Ct).Returns([Approval(101, 11, "approve", "owner", null)]);
    }

    private static PlanEventRow Event(long item, string to, DateTimeOffset at) => new(item * 10, item, "approved", to, "system:preopen", "r", null, at);

    private static ApprovalRow Approval(long id, long item, string decision, string kind, string? modifications) =>
        new(id, item, kind, decision, kind == "owner" ? "owner:me" : "rule:autonomy", "r", null, modifications, DateTimeOffset.UnixEpoch);

    private static PlanItemRow Item(long id, TicketState state = TicketState.Expired, TicketGroup group = TicketGroup.Primary, int qty = 100) =>
        new(id, 1, new NewPlanItem((int)id, group, "NSE:AAA", "BUY", "ORB", Hash, "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"], new PlanEvidence(Cell, 30, "r", "b", null, null, [], null), [new NewAllocation(Account, "paper", qty)]), state);

    private CapturePaperFillsHandler Handler() =>
        new(_plans, _paper, _gate, _settings, _sheets, _calendar, new TicketReplayer(_strategies, _baskets, _simulation), _time, NullLogger<CapturePaperFillsHandler>.Instance);

    private void Replay(IReadOnlyList<SimTrade> trades, IReadOnlyList<SimTrade>? open = null, bool costed = false) =>
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Success(FabricatedRun.Assemble(
            FabricatedRun.List((Instrument, "NSE:AAA")), [FabricatedRun.Ran(Instrument, "NSE:AAA", F.AddDays(-30), F)], trades, open, from: F, to: F, costs: costed ? new CostRecord(PaperFixture.SheetId, new string('a', 64), "{}", null, 100_000m, SizingProfile.Standard, 1m) : null)));

    private static string ModificationText(int qty) => TicketModification.Serialize([new QuantityChange(Account, qty)]);

    [Fact]
    public async Task A_session_of_an_earlier_epoch_that_still_has_work_is_revisited_and_written_under_its_own_epoch()
    {
        var first = new EpochRow(1, new DateOnly(2026, 11, 2), DateTimeOffset.UnixEpoch, "first", "default", "system");
        var second = new EpochRow(2, new DateOnly(2026, 11, 4), DateTimeOffset.UnixEpoch, "detected_change", "changed", "system");
        _gate.ResolveAsync(Arg.Any<IReadOnlyList<DateOnly>>(), Ct).Returns(call => new EpochResolution(second, call.Arg<IReadOnlyList<DateOnly>>().Distinct().ToDictionary(s => s, _ => "measured"), [second, first]));
        _paper.CoverageAsync(Ct).Returns(new SessionCoverage(new HashSet<DateOnly> { F }, new HashSet<DateOnly> { F }));
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        Replay([PaperFixture.Trade(Instrument)]);
        var writes = new List<SessionWrite>();
        _paper.WriteSessionAsync(Arg.Any<SessionWrite>(), Ct).Returns(call =>
        {
            writes.Add(call.Arg<SessionWrite>());
            return new SessionWritten(0, 0, 0);
        });

        await Handler().HandleAsync(new CapturePaperFillsCommand(null, false), Ct);

        writes.Single(w => w.Run.SessionDate == F).EpochId.ShouldBe(1);
        writes.Where(w => w.Run.SessionDate != F).ShouldAllBe(w => w.EpochId == 2);
    }

    [Fact]
    public async Task An_armed_ticket_becomes_one_entry_and_exit_at_the_owners_size_with_the_hand_worked_numbers()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _plans.ApprovalsAsync(1, Ct).Returns([Approval(100, 11, "modify", "owner", ModificationText(60)), Approval(101, 11, "approve", "owner", null)]);
        Replay([PaperFixture.Trade(Instrument)]);

        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        result.Value.EntriesWritten.ShouldBe(1);
        result.Value.ExitsWritten.ShouldBe(1);
        var written = _written!.Entries.Single();
        written.Entry.QtyAllocated.ShouldBe(100);
        written.Entry.Qty.ShouldBe(60);
        written.Entry.EntryAt.ShouldBe(new DateTimeOffset(2026, 11, 3, 9, 20, 0, TimeSpan.FromMinutes(330)));
        written.Entry.EntryPrice.ShouldBe(100.15m);
        written.Entry.EntryExact.ShouldBe(100.10m);
        written.Entry.StopPrice.ShouldBe(99.00m);
        written.Entry.TargetPrice.ShouldBe(102.30m);
        written.Entry.ApprovalKind.ShouldBe("owner");
        written.Entry.TicketGroup.ShouldBe("primary");
        written.Entry.TradeKey.ShouldBe("ORB|ABC|Buy|2026-11-03");
        written.Exit!.GrossPnl.ShouldBe(129.00m);
        written.Exit.Charges.ShouldBeNull();
        written.Exit.NetPnl.ShouldBeNull();
        written.Exit.RSim.ShouldBe(1.954545, 1e-6);
        written.Exit.RBasis.ShouldBe("gross");
        _written.Accounts.Single().Capital.ShouldBe(100_000m);
        _written.Run.Outcome.ShouldBe("done");
    }

    [Fact]
    public async Task A_stored_sheet_charges_the_two_orders_at_the_account_quantity_and_r_is_the_replays_net_r()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _plans.ApprovalsAsync(1, Ct).Returns([Approval(100, 11, "modify", "owner", ModificationText(60)), Approval(101, 11, "approve", "owner", null)]);
        _settings.CostSheetId.Returns(PaperFixture.SheetId);
        _sheets.FindAsync(PaperFixture.SheetId, Ct).Returns(Result.Success(PaperFixture.FlatSheet));
        Replay([PaperFixture.Trade(Instrument, netR: 1.80)], costed: true);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        var exit = _written!.Entries.Single().Exit!;
        exit.Charges.ShouldBe(20.00m);
        exit.NetPnl.ShouldBe(109.00m);
        exit.RSim.ShouldBe(1.80);
        exit.RBasis.ShouldBe("net");
        await _simulation.Received().HandleAsync(Arg.Is<RunSimulationQuery>(q => q.Request.Costs.Equals(CostChoice.Sheet(PaperFixture.SheetId, 100_000m))), Ct);
    }

    [Fact]
    public async Task The_paper_entry_equals_the_grade_of_the_same_ticket_because_both_use_the_one_replayer()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        Replay([PaperFixture.Trade(Instrument)]);
        var grades = Substitute.For<IGradeStore>();
        grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade>());
        IReadOnlyList<NewGrade>? graded = null;
        grades.InsertAsync(Arg.Do<IReadOnlyList<NewGrade>>(g => graded = g), Ct).Returns(1);
        var grader = new GradePlansHandler(_plans, grades, _settings, _strategies, _baskets, _sheets, _calendar, _simulation, _time, NullLogger<GradePlansHandler>.Instance);

        await grader.HandleAsync(new GradePlansCommand(F), Ct);
        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        var grade = graded!.Single();
        var entry = _written!.Entries.Single();
        entry.Entry.EntryExact.ShouldBe((decimal)grade.Grade.EntryPrice!.Value);
        entry.Exit!.RSim.ShouldBe(grade.Grade.R!.Value);
        entry.Exit.ExitReason.ShouldBe(grade.Grade.ExitReason);
        entry.Entry.RunKey.ShouldBe(grade.RunKey);
    }

    [Theory]
    [InlineData(TicketState.Proposed)]
    [InlineData(TicketState.Approved)]
    [InlineData(TicketState.Rejected)]
    [InlineData(TicketState.Lapsed)]
    [InlineData(TicketState.Invalidated)]
    [InlineData(TicketState.Modified)]
    public async Task A_ticket_that_is_not_armed_has_no_entry_and_costs_no_replay(TicketState state)
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, state)]);
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Entries.ShouldBeEmpty();
        _written.Skips.ShouldBeEmpty();
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
    }

    [Fact]
    public async Task An_approved_ticket_that_expired_without_an_armed_row_is_not_captured()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _plans.HistoryAsync(1, Ct).Returns([Event(11, "expired", Armed)]);
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Entries.ShouldBeEmpty();
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
    }

    [Fact]
    public async Task An_entry_minute_before_the_ticket_was_armed_is_stored_as_a_skip_not_an_entry()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _plans.HistoryAsync(1, Ct).Returns([Event(11, "armed", new DateTimeOffset(2026, 11, 3, 9, 25, 0, TimeSpan.FromMinutes(330)))]);
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Entries.ShouldBeEmpty();
        _written.Skips.Single().Code.ShouldBe(SkipCodes.EnteredBeforeArmed);
    }

    [Fact]
    public async Task An_unreadable_stored_modification_skips_the_pair_and_never_uses_the_larger_size()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _plans.ApprovalsAsync(1, Ct).Returns([Approval(100, 11, "modify", "owner", "garbage"), Approval(101, 11, "approve", "owner", null)]);
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Entries.ShouldBeEmpty();
        _written.Skips.Single().Code.ShouldBe(SkipCodes.ModificationUnreadable);
    }

    [Fact]
    public async Task An_auto_approved_reserve_ticket_is_captured_and_labelled()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11, group: TicketGroup.Reserve)]);
        _plans.ApprovalsAsync(1, Ct).Returns([Approval(101, 11, "approve", "auto", null)]);
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        var entry = _written!.Entries.Single().Entry;
        entry.TicketGroup.ShouldBe("reserve");
        entry.ApprovalKind.ShouldBe("auto");
    }

    [Fact]
    public async Task No_entry_in_the_replay_is_a_not_triggered_skip()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        Replay([]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Skips.Single().Code.ShouldBe(SkipCodes.NotTriggered);
        _written.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trade_with_legs_is_a_legs_not_supported_skip()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        var trade = PaperFixture.Trade(Instrument) with { Legs = new ValueList<Leg>([new Leg(PaperFixture.EntryEpoch, 100.10, 100.15, 1m, LegKind.Entry, string.Empty)]) };
        Replay([trade]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Skips.Single().Code.ShouldBe(SkipCodes.LegsNotSupported);
    }

    [Fact]
    public async Task A_refused_replay_is_pending_nothing_is_stored_and_the_session_row_is_partial()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Ct).Returns(Result.Failure<SimulationRun>(Error.Conflict("x", "later")));

        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        result.Value.Pending.ShouldBe(1);
        _written!.Entries.ShouldBeEmpty();
        _written.Skips.ShouldBeEmpty();
        _written.Run.Outcome.ShouldBe("partial");
        _written.Run.Pending.ShouldBe(1);
    }

    [Fact]
    public async Task A_pair_with_an_entry_and_exit_or_a_skip_is_left_alone_and_not_replayed_again()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _paper.PairsAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<(long, Guid), StoredPair> { [(11, Account)] = new(null, null, false, true) });
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Entries.ShouldBeEmpty();
        _written.Exits.ShouldBeEmpty();
        _written.Skips.ShouldBeEmpty();
        await _simulation.DidNotReceive().HandleAsync(Arg.Any<RunSimulationQuery>(), Ct);
    }

    [Fact]
    public async Task An_open_swing_entry_is_written_without_an_exit_and_a_later_run_writes_the_one_exit()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        Replay([], [PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        var opened = _written!.Entries.Single();
        opened.Exit.ShouldBeNull();

        var stored = opened.Entry;
        _paper.PairsAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<(long, Guid), StoredPair> { [(11, Account)] = new(7, stored, false, false) });
        Replay([PaperFixture.Trade(Instrument)]);

        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        _written!.Entries.ShouldBeEmpty();
        var exit = _written.Exits.Single();
        exit.EntryId.ShouldBe(7);
        exit.Exit.GrossPnl.ShouldBe(Math.Round((102.30m - 100.15m) * 100, 2));
    }

    [Fact]
    public async Task A_later_replay_that_shows_a_different_entry_overwrites_nothing_and_is_counted_as_integrity()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        Replay([], [PaperFixture.Trade(Instrument)]);
        await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);
        var stored = _written!.Entries.Single().Entry with { EntryPrice = 100.20m };
        _paper.PairsAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<(long, Guid), StoredPair> { [(11, Account)] = new(7, stored, false, false) });
        Replay([PaperFixture.Trade(Instrument)]);

        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        result.Value.Integrity.ShouldBe(1);
        _written!.Entries.ShouldBeEmpty();
        _written.Exits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_configured_sheet_that_is_not_stored_fails_the_run_and_writes_nothing()
    {
        _plans.ItemsAsync(1, Ct).Returns([Item(11)]);
        _settings.CostSheetId.Returns("nse-test");
        _sheets.FindAsync("nse-test", Ct).Returns(Result.Failure<CostSheet>(Error.NotFound("x", "none")));

        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        result.Error.Code.ShouldBe(PaperErrors.SheetMissingCode);
        await _paper.DidNotReceive().WriteSessionAsync(Arg.Any<SessionWrite>(), Ct);
    }

    [Fact]
    public async Task A_session_that_is_not_past_is_refused()
    {
        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(new DateOnly(2026, 11, 10), true), Ct);

        result.Error.Code.ShouldBe(PaperErrors.InvalidCode);
        await _plans.DidNotReceive().ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Ct);
    }

    [Fact]
    public async Task A_session_before_the_epoch_is_refused()
    {
        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(new DateOnly(2026, 10, 30), true), Ct);

        result.Error.Code.ShouldBe(PaperErrors.InvalidCode);
    }

    [Fact]
    public async Task Every_session_since_the_epoch_gets_a_coverage_row_even_with_no_armed_ticket_and_the_capture_never_moves_a_ticket()
    {
        _plans.ListAsync(Arg.Any<DateOnly?>(), null, Arg.Any<int>(), Ct).Returns([]);
        var rows = new List<SessionWrite>();
        _paper.WriteSessionAsync(Arg.Do<SessionWrite>(rows.Add), Ct).Returns(new SessionWritten(0, 0, 0));

        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(null, false), Ct);

        rows.Select(r => r.Run.SessionDate).ShouldBe([new DateOnly(2026, 11, 2), F, new DateOnly(2026, 11, 4)]);
        rows.ShouldAllBe(r => r.Run.Seen == 0 && r.Run.Outcome == "done" && !r.Run.Manual);
        result.Value.Sessions.ShouldBe(3);
        await _plans.DidNotReceive().MoveItemAsync(Arg.Any<ItemMove>(), Ct);
        await _plans.DidNotReceive().MovePlanAsync(Arg.Any<PlanMove>(), Ct);
    }

    [Fact]
    public async Task A_session_that_throws_is_recorded_as_failed_and_the_result_says_so()
    {
        _plans.ListAsync(F, null, Arg.Any<int>(), Ct).Returns<IReadOnlyList<PlanRow>>(_ => throw new InvalidOperationException("boom"));

        var result = await Handler().HandleAsync(new CapturePaperFillsCommand(F, true), Ct);

        result.Value.FailedSessions.ShouldBe(1);
        _written!.Run.Outcome.ShouldBe("failed");
    }
}
