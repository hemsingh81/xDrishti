#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Paper;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-03: the review handlers with fakes. Sources that fail show as unavailable, storing follows the digest's rule and is idempotent, and the only reachable data port is read only.</summary>
public sealed class PaperReviewHandlersTests
{
    private static readonly Guid Account = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a04");

    private static readonly DateOnly Tuesday = new(2026, 11, 24);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public IPaperReviewSource Source { get; } = Substitute.For<IPaperReviewSource>();

        public IPaperReviewStore Store { get; } = Substitute.For<IPaperReviewStore>();

        public IReviewComparisonLine Comparison { get; } = Substitute.For<IReviewComparisonLine>();

        public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-11-28T10:30:00+05:30", System.Globalization.CultureInfo.InvariantCulture));

        public Rig(bool epoch = true)
        {
            var summary = new EpochSummary(new EpochRow(7, new DateOnly(2026, 11, 2), DateTimeOffset.UnixEpoch, "first", "the default epoch", "system"), null, 0, 0, 1, 2);
            Source.EpochsAsync(Ct).ReturnsForAnyArgs(epoch ? [summary] : []);
            Source.TicketsAsync(default, Ct).ReturnsForAnyArgs(ci => ci.Arg<DateOnly>() == Tuesday ? [Ticket()] : []);
            Source.TradesAsync(default, Ct).ReturnsForAnyArgs([Trade()]);
            Source.SkipsAsync(default, Ct).ReturnsForAnyArgs([]);
            Source.DiscretionaryAsync(default, Ct).ReturnsForAnyArgs([]);
            Comparison.LineAsync(default, Ct).ReturnsForAnyArgs("unavailable: not built");
            Store.FindAsync(default!, default, Ct).ReturnsForAnyArgs((StoredPaperReview?)null);
            Store.InsertAsync(default!, Ct).ReturnsForAnyArgs(ci => (new StoredPaperReview(5, ci.Arg<NewPaperReview>()), true));
        }

        public BuildPaperReviewHandler Build()
        {
            var calendars = Substitute.For<ITradingCalendar>();
            calendars.GetAsync(Ct).ReturnsForAnyArgs(new TradingCalendar());
            var gatherer = new PaperReviewGatherer(Source, calendars, Comparison, Time, NullLogger<PaperReviewGatherer>.Instance);
            return new BuildPaperReviewHandler(gatherer, Store, Time, NullLogger<BuildPaperReviewHandler>.Instance);
        }

        public GetDisciplineHandler Discipline()
        {
            var calendars = Substitute.For<ITradingCalendar>();
            calendars.GetAsync(Ct).ReturnsForAnyArgs(new TradingCalendar());
            return new GetDisciplineHandler(new PaperReviewGatherer(Source, calendars, Comparison, Time, NullLogger<PaperReviewGatherer>.Instance), NullLogger<GetDisciplineHandler>.Instance);
        }
    }

    private static ReviewTicket Ticket() => new(1, 1, Tuesday, TicketGroup.Primary, "AAA", "BUY", TicketState.Closed, "owner", false, null, [new ReviewAllocation(Account, 100, 100)]);

    private static ReviewTrade Trade() =>
        new(1, 1, Account, "AAA", "BUY", "primary", "k1", true, 100, 100, DateTimeOffset.Parse("2026-11-24T10:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture),
            new ReviewExit(DateTimeOffset.Parse("2026-11-24T14:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture), "target", 200m, null, null, 2.0, 2.0, null, 2.0));

    [Fact]
    public async Task A_dry_run_builds_the_review_from_stored_facts_and_writes_nothing()
    {
        var rig = new Rig();

        var result = await rig.Build().HandleAsync(new BuildPaperReviewCommand("2026-W48", null, false, "cli:xd"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Stored.ShouldBeFalse();
        result.Value.Text.ShouldStartWith("Paper only: no order was placed and none exists.");
        result.Value.Text.ShouldContain("Followed: approved at full size, entered and closed: n 1");
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
    }

    [Fact]
    public async Task Storing_inserts_once_and_a_second_build_returns_the_stored_one_and_writes_nothing()
    {
        var rig = new Rig();
        var first = await rig.Build().HandleAsync(new BuildPaperReviewCommand(null, null, true, "system:paper-review"), Ct);
        first.Value.Stored.ShouldBeTrue();
        first.Value.Week.ShouldBe("2026-W48");
        await rig.Store.Received(1).InsertAsync(Arg.Is<NewPaperReview>(r => r.Week == "2026-W48" && r.EpochId == 7 && r.BuiltBy == "system:paper-review" && r.Text.StartsWith("Paper only: no order was placed and none exists.", StringComparison.Ordinal)), Ct);

        var stored = new StoredPaperReview(5, new NewPaperReview("2026-W48", 7, rig.Time.GetUtcNow(), "system:paper-review", "text", []));
        rig.Store.FindAsync("2026-W48", 7, Ct).Returns(stored);
        rig.Store.ClearReceivedCalls();
        var second = await rig.Build().HandleAsync(new BuildPaperReviewCommand(null, null, true, "system:paper-review"), Ct);

        second.Value.Repeated.ShouldBeTrue();
        second.Value.Text.ShouldBe("text");
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
    }

    [Theory]
    [InlineData("2026-W49", "paper.invalid")] // a future week
    [InlineData("2026-W40", "paper.invalid")] // an older week cannot be stored
    public async Task A_future_week_is_refused_and_an_old_week_cannot_be_stored(string week, string code)
    {
        var rig = new Rig();

        var result = await rig.Build().HandleAsync(new BuildPaperReviewCommand(week, null, true, "cli:xd"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
    }

    [Fact]
    public async Task An_old_week_is_a_dry_run_that_says_the_state_is_todays()
    {
        var rig = new Rig();

        var result = await rig.Build().HandleAsync(new BuildPaperReviewCommand("2026-W45", null, false, "cli:xd"), Ct);

        result.Value.Text.ShouldContain("State as of 2026-11-28, not as of the end of the week");
    }

    [Fact]
    public async Task A_malformed_week_and_an_unknown_epoch_are_refused()
    {
        var rig = new Rig();

        (await rig.Build().HandleAsync(new BuildPaperReviewCommand("2026-48", null, false, "cli:xd"), Ct)).Error.Code.ShouldBe("paper.invalid");
        (await rig.Build().HandleAsync(new BuildPaperReviewCommand(null, 99, false, "cli:xd"), Ct)).Error.Code.ShouldBe("paper.not_found");
    }

    [Fact]
    public async Task With_no_epoch_the_review_says_so_and_cannot_be_stored()
    {
        var rig = new Rig(epoch: false);

        var dry = await rig.Build().HandleAsync(new BuildPaperReviewCommand(null, null, false, "cli:xd"), Ct);
        var store = await rig.Build().HandleAsync(new BuildPaperReviewCommand(null, null, true, "cli:xd"), Ct);

        dry.Value.Text.ShouldContain("No paper epoch exists yet");
        store.IsFailure.ShouldBeTrue();
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
    }

    [Fact]
    public async Task Failing_sources_show_unavailable_sections_and_the_rest_is_still_produced()
    {
        var rig = new Rig();
        rig.Source.TradesAsync(default, Ct).ThrowsAsyncForAnyArgs(new TimeoutException("secret detail"));
        rig.Comparison.LineAsync(default, Ct).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));

        var result = await rig.Build().HandleAsync(new BuildPaperReviewCommand("2026-W48", null, false, "cli:xd"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Text.ShouldContain("unavailable: the paper record could not be read (TimeoutException)");
        result.Value.Text.ShouldContain("unavailable: the backtest comparison could not be read (InvalidOperationException)");
        result.Value.Text.ShouldNotContain("secret detail");
        result.Value.Sections.Single(s => s.Id == "config").Lines.ShouldContain(l => l.StartsWith("Noted in the current epoch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreadable_journal_shows_the_added_line_unavailable_and_the_rest_is_still_produced()
    {
        var rig = new Rig();
        rig.Source.DiscretionaryAsync(default, Ct).ThrowsAsyncForAnyArgs(new TimeoutException());

        var result = await rig.Build().HandleAsync(new BuildPaperReviewCommand("2026-W48", null, false, "cli:xd"), Ct);

        result.Value.Text.ShouldContain("unavailable: the journal could not be read (TimeoutException)");
        result.Value.Text.ShouldContain("Followed: approved at full size");
    }

    [Fact]
    public async Task The_journal_is_asked_once_per_session_of_the_window()
    {
        var rig = new Rig();

        await rig.Build().HandleAsync(new BuildPaperReviewCommand("2026-W48", null, false, "cli:xd"), Ct);

        // Mon 23 to Fri 27 November: five sessions, all closed by Saturday 28.
        await rig.Source.ReceivedWithAnyArgs(5).DiscretionaryAsync(default, Ct);
        foreach (var day in new[] { 23, 24, 25, 26, 27 })
        {
            await rig.Source.Received(1).DiscretionaryAsync(new DateOnly(2026, 11, day), Ct);
        }
    }

    [Fact]
    public async Task The_discipline_report_covers_the_epoch_to_the_last_closed_session()
    {
        var rig = new Rig();

        var result = await rig.Discipline().HandleAsync(new GetDisciplineQuery(null, null, null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EpochId.ShouldBe(7);
        result.Value.Evidence.PrimaryTickets.ShouldBe(1);
        result.Value.Lines.Select(l => l.Category).Take(7).ShouldBe(DisciplineCategory.All);
        result.Value.Lines.Single(l => l.Category == "followed").N.ShouldBe(1);
        (await rig.Discipline().HandleAsync(new GetDisciplineQuery(null, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 3)), Ct)).Error.Code.ShouldBe("paper.invalid");
    }

    [Fact]
    public void The_review_handlers_hold_no_write_port()
    {
        var forbidden = new[] { typeof(IPlanStore), typeof(IGradeStore), typeof(IJournalStore), typeof(IPaperStore), typeof(IGovernanceStore) };
        foreach (var type in new[] { typeof(BuildPaperReviewHandler), typeof(GetPaperReviewHandler), typeof(ListPaperReviewsHandler), typeof(GetDisciplineHandler), typeof(PaperReviewGatherer) })
        {
            type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Intersect(forbidden).ShouldBeEmpty(type.Name);
        }

        typeof(IPaperReviewSource).GetMethods().ShouldAllBe(m => m.Name.EndsWith("Async", StringComparison.Ordinal));
        typeof(IPaperReviewStore).GetMethods().Select(m => m.Name).ShouldBe(["InsertAsync", "FindAsync", "ListAsync"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_source_calls_only_read_members_of_the_plan_grade_journal_governance_and_paper_ports()
    {
        var plans = Substitute.For<IPlanStore>();
        var grades = Substitute.For<IGradeStore>();
        var journal = Substitute.For<IJournalStore>();
        var governance = Substitute.For<IGovernanceStore>();
        var data = Substitute.For<IPaperReviewData>();
        plans.ListAsync(default, default, default, Ct).ReturnsForAnyArgs([]);
        governance.ListEpochsAsync(Ct).ReturnsForAnyArgs([]);
        journal.DiscretionaryAsync(default, Ct).ReturnsForAnyArgs([]);
        data.TradesAsync(default, Ct).ReturnsForAnyArgs([]);
        data.SkipsAsync(default, Ct).ReturnsForAnyArgs([]);
        var source = new PaperReviewSource(plans, grades, journal, governance, data);

        await source.EpochsAsync(Ct);
        await source.TicketsAsync(Tuesday, Ct);
        await source.TradesAsync(7, Ct);
        await source.SkipsAsync(7, Ct);
        await source.DiscretionaryAsync(Tuesday, Ct);

        var read = new[] { "ListAsync", "ItemsAsync", "ApprovalsAsync", "ForPlansAsync", "DiscretionaryAsync", "ListEpochsAsync", "TradesAsync", "SkipsAsync" };
        var called = plans.ReceivedCalls().Concat(grades.ReceivedCalls()).Concat(journal.ReceivedCalls()).Concat(governance.ReceivedCalls()).Concat(data.ReceivedCalls()).Select(c => c.GetMethodInfo().Name);
        called.ShouldAllBe(name => read.Contains(name));
    }
}
