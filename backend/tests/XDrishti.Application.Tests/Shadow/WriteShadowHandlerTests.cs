using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Planning;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Shadow;
using static XDrishti.Application.Tests.Shadow.ShadowFixture;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>
/// CF-08 part A over fake ports: every item of every group becomes a row once, the set-aside items are recorded and not counted, a second run stores nothing, a session that has opened is a gap and never written, the chain links, and with the switch OFF nothing is read or written.
/// </summary>
public sealed class WriteShadowHandlerTests
{
    private static readonly DateOnly Tuesday = new(2026, 11, 3);

    private static readonly DateOnly Monday = new(2026, 11, 2);

    private readonly IShadowSettings _settings = Substitute.For<IShadowSettings>();

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private readonly IPaperStore _paper = Substitute.For<IPaperStore>();

    private readonly IGovernanceStore _governance = Substitute.For<IGovernanceStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly MemoryStore _store = new();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 15, 40, 0, TimeSpan.Zero)); // Monday 21:10 India time

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public WriteShadowHandlerTests()
    {
        _settings.IsEnabledAsync(Ct).Returns(true);
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _paper.CurrentEpochAsync(Ct).Returns(new EpochRow(3, new DateOnly(2026, 10, 1), DateTimeOffset.UnixEpoch, "first", "default", "system"));
        _governance.LatestSnapshotAsync(true, Ct).Returns(new StoredSnapshot(1, DateTimeOffset.UnixEpoch, Monday, new string('f', 64), "{}", false));
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([Plan(10, Tuesday)]);
        _plans.ListAsync(Tuesday, null, Arg.Any<int>(), Ct).Returns([Plan(10, Tuesday)]);
    }

    private ShadowScorer _scorer = ShadowScorerFake.Empty();

    private WriteShadowHandler Handler() => new(_settings, _plans, _store, _paper, _governance, _calendar, _scorer, new ShadowOptions(), _time, NullLogger<WriteShadowHandler>.Instance);

    private void Items(long planId, params NewPlanItem[] items) =>
        _plans.ItemsAsync(planId, Ct).Returns([.. items.Select((item, i) => Row(100 + i, planId, item))]);

    private async Task<ShadowWriteOutcome> RunAsync(DateOnly? session = null)
    {
        var result = await Handler().HandleAsync(new WriteShadowCommand(session), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private void TheNightlyPlan() => Items(
        10,
        Item(1, TicketGroup.Primary, "NSE:P1"),
        Item(2, TicketGroup.Primary, "NSE:P2"),
        Item(3, TicketGroup.Reserve, "NSE:R1"),
        Item(4, TicketGroup.Reserve, "NSE:R2"),
        Item(5, TicketGroup.Reserve, "NSE:R3"),
        Item(6, TicketGroup.Rejected, "NSE:X1"), // rejected by the fill: reached it
        Item(7, TicketGroup.Rejected, "NSE:D1", reachedFill: false)); // dropped in the merge as a duplicate: never reached the fill

    [Fact]
    public async Task Every_item_of_every_group_becomes_a_row_and_the_duplicate_is_recorded_but_not_counted()
    {
        TheNightlyPlan();

        var outcome = await RunAsync();

        outcome.Sessions.ShouldBe(1);
        outcome.Rows.ShouldBe(7);
        outcome.EdgeRows.ShouldBe(6);
        var rows = _store.Sessions.Single().Entries;
        rows.Select(r => r.TicketGroup).ShouldBe(["primary", "primary", "reserve", "reserve", "reserve", "rejected", "rejected"]);
        rows.Where(r => r.CountsForEdge).Count().ShouldBe(6);
        rows.Single(r => r.Symbol == "NSE:D1").CountsForEdge.ShouldBeFalse();
        rows.ShouldAllBe(r => r.SessionDate == Tuesday && r.PlanId == 10);
    }

    [Fact]
    public async Task A_second_run_stores_nothing()
    {
        TheNightlyPlan();
        await RunAsync();
        var calls = _store.WriteCalls;

        var again = await RunAsync();

        again.Sessions.ShouldBe(0);
        again.Rows.ShouldBe(0);
        _store.WriteCalls.ShouldBe(calls);
        _store.Sessions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_row_carries_the_stored_levels_group_epoch_and_configuration_and_nothing_recomputed()
    {
        Items(10, Item(1, TicketGroup.Primary, "NSE:P1", note: "model P(win) 0.712, advisory only"));

        await RunAsync();

        var row = _store.Sessions.Single().Entries.Single();
        (row.EntryLimit, row.Stop, row.Target).ShouldBe((100m, 98m, 104m));
        row.TimeRule.ShouldBe("09:30-11:30");
        row.CellKey.ShouldBe(Intraday);
        row.StrategyHash.ShouldBe(Hash);
        row.ConfigHash.ShouldBe(new string('f', 64));
        row.EdgeEpoch.ShouldBe(3);
        row.PWin.ShouldBe(0.712);
        (row.ModelId, row.FeatureVersion, row.Features).ShouldBe((null, null, null));
    }

    [Fact]
    public async Task A_plan_without_levels_or_an_advisory_figure_gives_nulls_never_a_guess()
    {
        var bare = Item(1, TicketGroup.Primary, "NSE:P1") with { Levels = null };
        Items(10, bare);

        await RunAsync();

        var row = _store.Sessions.Single().Entries.Single();
        (row.EntryLimit, row.Stop, row.Target, row.PWin).ShouldBe((null, null, null, null));
    }

    [Theory]
    [InlineData("model P(win) 0.712, advisory only", 0.712)]
    [InlineData("model P(win) 1, advisory only", 1.0)]
    [InlineData("model P(win) 1.5, advisory only", null)]
    [InlineData("model P(win) abc, advisory only", null)]
    [InlineData("some other note", null)]
    [InlineData(null, null)]
    public void The_advisory_figure_is_read_only_from_the_exact_note_the_builder_writes(string? note, double? expected)
    {
        ShadowPlanFacts.AdvisoryPWin(Item(1, TicketGroup.Primary, "NSE:P1", note: note)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.7123, "model P(win) 0.712, advisory only", 0.712)]
    [InlineData(0.9996, "model P(win) 1, advisory only", 1.0)]
    [InlineData(0.0004, "model P(win) 0, advisory only", 0.0)]
    [InlineData(0.05, "model P(win) 0.05, advisory only", 0.05)]
    public void The_note_the_plan_builder_writes_is_read_back_by_the_same_format(double figure, string words, double expected)
    {
        var note = SelectionPlanBuilder.AdvisoryNote(figure); // the one place the builder writes the words

        note.ShouldBe(words);
        ShadowPlanFacts.AdvisoryPWin(Item(1, TicketGroup.Primary, "NSE:P1", note: note)).ShouldBe(expected);
    }

    [Fact]
    public void A_note_with_more_decimals_or_other_words_than_the_builder_writes_is_not_read()
    {
        ShadowPlanFacts.AdvisoryPWin(Item(1, TicketGroup.Primary, "NSE:P1", note: "model P(win) 0.7123, advisory only")).ShouldBeNull();
        ShadowPlanFacts.AdvisoryPWin(Item(1, TicketGroup.Primary, "NSE:P1", note: "model P(win) -0.5, advisory only")).ShouldBeNull();
        ShadowPlanFacts.AdvisoryPWin(Item(1, TicketGroup.Primary, "NSE:P1", note: "model P(win) , advisory only")).ShouldBeNull();
    }

    [Fact]
    public async Task The_trade_key_uses_the_decision_day_of_the_cell_as_the_paper_record_does()
    {
        Items(10, Item(1, TicketGroup.Primary, "NSE:P1"), Item(2, TicketGroup.Reserve, "NSE:P2", strategy: "PEAD", cell: Daily));

        await RunAsync();

        var keys = _store.Sessions.Single().Entries.Select(e => e.TradeKey).ToList();
        keys[0].ShouldBe("ORB|NSE:P1|Buy|2026-11-03"); // intraday: decided on the session itself
        keys[1].ShouldBe("PEAD|NSE:P2|Buy|2026-11-02"); // daily: decided at the close of the day before
    }

    [Fact]
    public async Task A_candidate_whose_trade_key_an_earlier_one_has_is_left_out_and_the_primary_one_keeps_it()
    {
        Items(10, Item(1, TicketGroup.Rejected, "NSE:P1"), Item(2, TicketGroup.Primary, "NSE:P1"));

        var outcome = await RunAsync();

        outcome.DroppedSameKey.ShouldBe(1);
        _store.Sessions.Single().Entries.Single().TicketGroup.ShouldBe("primary");
    }

    [Fact]
    public async Task The_session_digest_is_chained_after_the_one_before_and_is_the_digest_of_its_row_hashes()
    {
        TheNightlyPlan();
        await RunAsync();
        var first = _store.Sessions.Single();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 15, 40, 0, TimeSpan.Zero)); // Tuesday evening
        var wednesday = new DateOnly(2026, 11, 4);
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([Plan(11, wednesday), Plan(10, Tuesday)]);
        _plans.ListAsync(wednesday, null, Arg.Any<int>(), Ct).Returns([Plan(11, wednesday)]);
        Items(11, Item(1, TicketGroup.Primary, "NSE:P9"));

        await RunAsync();

        var second = _store.Sessions.Single(s => s.SessionDate == wednesday);
        first.PrevDigest.ShouldBeNull();
        first.Digest.ShouldBe(ShadowDigest.Next(null, first.SessionDate, "ok", first.Entries.Select(e => e.RowHash)));
        second.PrevDigest.ShouldBe(first.Digest);
        second.Digest.ShouldBe(ShadowDigest.Next(first.Digest, second.SessionDate, "ok", second.Entries.Select(e => e.RowHash)));
        ShadowDigest.FirstBroken((await _store.ReadChainAsync(Ct)).ToList()).ShouldBeNull();
    }

    [Fact]
    public async Task A_past_session_writes_no_row_and_records_a_gap()
    {
        TheNightlyPlan();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero)); // 09:30 India time on the session: opened

        var outcome = await RunAsync(Tuesday);

        outcome.Gaps.ShouldBe(1);
        outcome.Rows.ShouldBe(0);
        var gap = _store.Sessions.Single();
        (gap.SessionDate, gap.Status, gap.Entries.Count).ShouldBe((Tuesday, "gap", 0));
        gap.Digest.ShouldBe(ShadowDigest.Next(null, Tuesday, "gap", []));
        await _plans.DidNotReceive().ItemsAsync(Arg.Any<long>(), Ct);
    }

    [Fact]
    public async Task A_session_that_is_over_is_not_back_filled_even_when_a_plan_is_stored_for_it()
    {
        TheNightlyPlan();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 5, 15, 40, 0, TimeSpan.Zero)); // two days later

        await RunAsync(Tuesday);

        _store.Sessions.Single().Status.ShouldBe("gap");
        _store.Sessions.SelectMany(s => s.Entries).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_after_a_missed_night_records_a_gap_for_each_opened_session_and_then_writes_the_next_one()
    {
        TheNightlyPlan();
        await RunAsync(); // Tuesday written on Monday evening
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 5, 15, 40, 0, TimeSpan.Zero)); // Thursday evening: Tuesday's night was the last one run
        var friday = new DateOnly(2026, 11, 6);
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([Plan(12, friday)]);
        _plans.ListAsync(friday, null, Arg.Any<int>(), Ct).Returns([Plan(12, friday)]);
        Items(12, Item(1, TicketGroup.Primary, "NSE:P7"));

        var outcome = await RunAsync();

        outcome.Gaps.ShouldBe(2); // Wednesday and Thursday
        outcome.Sessions.ShouldBe(1);
        _store.Sessions.OrderBy(s => s.SessionDate).Select(s => (s.SessionDate.Day, s.Status)).ShouldBe([(3, "ok"), (4, "gap"), (5, "gap"), (6, "ok")]);
        ShadowDigest.FirstBroken((await _store.ReadChainAsync(Ct)).ToList()).ShouldBeNull();
    }

    [Fact]
    public async Task A_run_for_one_session_first_records_a_gap_for_every_opened_session_the_chain_lacks()
    {
        TheNightlyPlan();
        await RunAsync(); // Tuesday written on Monday evening
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 5, 15, 40, 0, TimeSpan.Zero)); // Thursday evening
        var friday = new DateOnly(2026, 11, 6);
        _plans.ListAsync(friday, null, Arg.Any<int>(), Ct).Returns([Plan(12, friday)]);
        Items(12, Item(1, TicketGroup.Primary, "NSE:P7"));

        var outcome = await RunAsync(friday);

        outcome.Gaps.ShouldBe(2);
        _store.Sessions.OrderBy(s => s.SessionDate).Select(s => (s.SessionDate.Day, s.Status)).ShouldBe([(3, "ok"), (4, "gap"), (5, "gap"), (6, "ok")]);
        ShadowDigest.FirstBroken((await _store.ReadChainAsync(Ct)).ToList()).ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_session_is_never_written_while_an_earlier_session_that_has_not_opened_has_no_row(bool asked)
    {
        var wednesday = new DateOnly(2026, 11, 4); // Tuesday has no plan; Wednesday has one
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([Plan(20, wednesday)]);
        _plans.ListAsync(Tuesday, null, Arg.Any<int>(), Ct).Returns([]);
        _plans.ListAsync(wednesday, null, Arg.Any<int>(), Ct).Returns([Plan(20, wednesday)]);
        Items(20, Item(1, TicketGroup.Primary, "NSE:W1"));

        var outcome = await RunAsync(asked ? wednesday : null);

        outcome.Sessions.ShouldBe(0);
        _store.WriteCalls.ShouldBe(0); // writing Wednesday first would leave Tuesday, still unopened and so no gap, without a row for ever
    }

    [Fact]
    public async Task The_first_run_has_no_history_to_be_a_gap_in()
    {
        Items(10, Item(1, TicketGroup.Primary, "NSE:P1"));

        var outcome = await RunAsync();

        outcome.Gaps.ShouldBe(0);
        _store.Sessions.Single().Status.ShouldBe("ok");
    }

    [Fact]
    public async Task A_session_earlier_than_the_newest_stored_one_is_never_added()
    {
        TheNightlyPlan();
        await RunAsync();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero)); // 08:30 on Tuesday, Tuesday unopened but stored; ask for Monday (opened)

        var outcome = await RunAsync(Monday);

        outcome.Gaps.ShouldBe(0);
        _store.Sessions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_date_without_a_plan_writes_nothing_and_a_draft_plan_is_not_used()
    {
        _plans.ListAsync(Tuesday, null, Arg.Any<int>(), Ct).Returns([Plan(10, Tuesday, state: PlanState.Draft)]);

        var outcome = await RunAsync(Tuesday);

        outcome.Sessions.ShouldBe(0);
        _store.WriteCalls.ShouldBe(0);
    }

    [Fact]
    public async Task The_newest_version_of_the_plan_is_the_one_shadowed()
    {
        _plans.ListAsync(Tuesday, null, Arg.Any<int>(), Ct).Returns([Plan(10, Tuesday), Plan(11, Tuesday, version: 2)]);
        Items(11, Item(1, TicketGroup.Primary, "NSE:NEWEST"));

        await RunAsync(Tuesday);

        var row = _store.Sessions.Single().Entries.Single();
        (row.PlanId, row.Symbol).ShouldBe((11, "NSE:NEWEST"));
    }

    [Fact]
    public async Task With_no_epoch_and_no_snapshot_stored_the_row_says_so_with_nulls_and_nothing_is_created()
    {
        _paper.CurrentEpochAsync(Ct).Returns((EpochRow?)null);
        _governance.LatestSnapshotAsync(true, Ct).Returns((StoredSnapshot?)null);
        Items(10, Item(1, TicketGroup.Primary, "NSE:P1"));

        await RunAsync();

        var row = _store.Sessions.Single().Entries.Single();
        (row.EdgeEpoch, row.ConfigHash).ShouldBe((null, null));
        await _paper.DidNotReceive().EnsureFirstEpochAsync(Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(), Ct);
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_read_or_written()
    {
        _settings.IsEnabledAsync(Ct).Returns(false);
        TheNightlyPlan();

        var outcome = await RunAsync();

        outcome.Enabled.ShouldBeFalse();
        outcome.Rows.ShouldBe(0);
        outcome.Summary.ShouldContain("Switched off");
        _store.WriteCalls.ShouldBe(0);
        await _plans.DidNotReceive().ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Ct);
        await _plans.DidNotReceive().ItemsAsync(Arg.Any<long>(), Ct);
    }

    [Fact]
    public async Task A_date_that_is_not_a_trading_session_is_refused()
    {
        var result = await Handler().HandleAsync(new WriteShadowCommand(new DateOnly(2026, 11, 7)), Ct); // a Saturday

        result.IsFailure.ShouldBeTrue();
        _store.WriteCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_scorer_that_throws_does_not_cost_the_session()
    {
        _scorer = ShadowScorerFake.Throwing();
        TheNightlyPlan();

        var outcome = await RunAsync(Tuesday);

        outcome.Sessions.ShouldBe(1);
        outcome.Rows.ShouldBe(7);
    }

    [Fact]
    public async Task After_the_scoring_deadline_the_scorer_is_not_asked_and_the_session_is_written_without_scores()
    {
        _scorer = ShadowScorerFake.Throwing(); // would fail the run if it were asked; it is not
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 3, 10, 0, TimeSpan.Zero)); // Tuesday 08:40 India time, after 08:30 and before the 09:00 cut-off
        TheNightlyPlan();

        var outcome = await RunAsync(Tuesday);

        outcome.Sessions.ShouldBe(1);
        (_store.Sessions.Single().Scores ?? []).ShouldBeEmpty();
    }
}
