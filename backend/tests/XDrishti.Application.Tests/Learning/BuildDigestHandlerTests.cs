#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Spec P7-15: the digest is built from stored records through read calls only, stored once per ISO week, and applies nothing.</summary>
public sealed class BuildDigestHandlerTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static readonly DateTimeOffset Saturday = new(2026, 10, 24, 10, 0, 0, TimeSpan.FromMinutes(330));

    private static readonly DateTimeOffset InWeek = new(2026, 10, 22, 9, 0, 0, TimeSpan.FromMinutes(330));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ICellStatsStore _cells = Substitute.For<ICellStatsStore>();

    private readonly ILearningChangelog _changelog = Substitute.For<ILearningChangelog>();

    private readonly ILessonLog _lessons = Substitute.For<ILessonLog>();

    private readonly IHypothesisLog _hypotheses = Substitute.For<IHypothesisLog>();

    private readonly IExperimentLedger _experiments = Substitute.For<IExperimentLedger>();

    private readonly IModelRegistry _models = Substitute.For<IModelRegistry>();

    private readonly IGradeStore _grades = Substitute.For<IGradeStore>();

    private readonly IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>> _phase = Substitute.For<IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>>>();

    private readonly IDigestStore _store = Substitute.For<IDigestStore>();

    private FakeTimeProvider _time = new(Saturday);

    public BuildDigestHandlerTests()
    {
        _cells.NewestAsOfAsync(Ct).Returns(new DateOnly(2026, 10, 23));
        _changelog.ListAsync("cell", null, Arg.Any<int>(), Ct).Returns([new ChangelogEntry(9, new NewChangelogEntry("transition", "cell", Cell, "research", "candidate", "enough trades", "owner", [], null, null, null, null, InWeek))]);
        _lessons.ListAsync(null, Arg.Any<int>(), Ct).Returns([
            new LessonListed(new LessonRow(3, Lesson("cusum", "open")), InWeek),
            new LessonListed(new LessonRow(4, Lesson("growing_tag", "open")), InWeek),
        ]);
        _hypotheses.ListAsync(null, Arg.Any<int>(), Ct).Returns([new HypothesisRow(5, new NewHypothesisRow(null, "proposed", null, "owner", Cell, "slippage", "Slippage", "1", "2", "fills dearer", "run", null, null, "why", "cli:xd", InWeek))]);
        _experiments.ListAsync(Arg.Any<int>(), Ct).Returns([]);
        _models.ListAsync(null, Arg.Any<int>(), Ct).Returns([]);
        _grades.ReportItemsAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<int>(), Ct).Returns(new GradedPage([], false));
        _phase.HandleAsync(Arg.Any<EvaluatePhaseCheckQuery>(), Ct).Returns(Result.Success(new PhaseCheckView(PhaseCheck.Evaluate(null, null), string.Empty)));
        _store.FindByWeekAsync(Arg.Any<string>(), Ct).Returns((StoredDigest?)null);
        _store.InsertAsync(Arg.Any<NewDigest>(), Ct).Returns(c => (new StoredDigest(1, c.Arg<NewDigest>().Digest, c.Arg<NewDigest>().BuiltBy, c.Arg<NewDigest>().CreatedAt), true));
    }

    private static NewLessonRow Lesson(string monitor, string state) =>
        new(null, state, null, monitor, Cell, "k", "mean R fell", null, null, null, null, null, null, null, null, "found", "system", InWeek);

    private BuildDigestHandler Handler() => new(_cells, _changelog, _lessons, _hypotheses, _experiments, _models, _grades, _phase, _store, _time, NullLogger<BuildDigestHandler>.Instance);

    [Fact]
    public async Task The_digest_is_built_from_the_stored_records_and_stored_once()
    {
        var result = await Handler().HandleAsync(new BuildDigestCommand(null, true, "system:weekly"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsoWeek.ShouldBe("2026-W43");
        result.Value.Repeated.ShouldBeFalse();
        result.Value.Text.ShouldContain("- #9 transition ORB|15m|BUY|intraday|FIXED_RR|* [research -> candidate] enough trades");
        result.Value.Text.ShouldContain("- #3 cusum ORB|15m|BUY|intraday|FIXED_RR|* [open] mean R fell");
        result.Value.Text.ShouldContain("- #4 growing_tag ORB|15m|BUY|intraday|FIXED_RR|* [open] mean R fell");
        result.Value.Text.ShouldContain("- #5 hypothesis ORB|15m|BUY|intraday|FIXED_RR|* [proposed] slippage 1 -> 2: fills dearer");
        result.Value.Text.ShouldContain("Open lessons: 2; linked to a fix: 0");
        await _store.Received(1).InsertAsync(Arg.Is<NewDigest>(d => d.BuiltBy == "system:weekly" && d.CreatedAt == Saturday), Ct);
    }

    [Fact]
    public async Task Nothing_is_applied_only_read_calls_reach_the_learning_stores()
    {
        await Handler().HandleAsync(new BuildDigestCommand(null, true, "system:weekly"), Ct);

        // The exact read-only calls the digest makes; a call to any other method of these ports is a write (or a read nobody checked).
        var allowed = new[] { "ListAsync", "NewestAsOfAsync", "ReportItemsAsync" };
        foreach (var spy in new object[] { _cells, _changelog, _lessons, _hypotheses, _experiments, _models, _grades })
        {
            foreach (var call in spy.ReceivedCalls())
            {
                allowed.ShouldContain(call.GetMethodInfo().Name, $"{call.GetMethodInfo().DeclaringType!.Name}.{call.GetMethodInfo().Name} is not on the read-only list");
            }
        }

        _store.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().Order(StringComparer.Ordinal).ShouldBe(["FindByWeekAsync", "InsertAsync"]);
    }

    [Fact]
    public void The_handler_holds_no_port_that_approves_orders_or_writes_a_plan()
    {
        var parameters = typeof(BuildDigestHandler).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();

        parameters.ShouldNotContain(n => n.Contains("Plan", StringComparison.Ordinal) || n.Contains("Approval", StringComparison.Ordinal) || n.Contains("Broker", StringComparison.Ordinal) || n.Contains("Order", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_week_with_a_digest_shows_it_and_writes_and_reads_nothing_else()
    {
        var existing = DigestBuilder.Build(new DigestInput(DigestWeek.TryParse("2026-W43")!.Value, Saturday, null, [], [], [], [], 0, 0, [], 0, [], null, [], [], [], []));
        _store.FindByWeekAsync("2026-W43", Ct).Returns(new StoredDigest(8, existing, "cli:xd", Saturday));

        var result = await Handler().HandleAsync(new BuildDigestCommand("2026-W43", true, "system:weekly"), Ct);

        result.Value.Repeated.ShouldBeTrue();
        result.Value.Id.ShouldBe(8);
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
        _lessons.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_concurrent_second_build_returns_the_first_stored_digest()
    {
        var first = DigestBuilder.Build(new DigestInput(DigestWeek.TryParse("2026-W43")!.Value, Saturday, null, [], [], [], [], 0, 0, [], 0, [], null, [], [], [], []));
        _store.InsertAsync(Arg.Any<NewDigest>(), Ct).Returns((new StoredDigest(8, first, "cli:xd", Saturday), false));

        var result = await Handler().HandleAsync(new BuildDigestCommand(null, true, "system:weekly"), Ct);

        result.Value.Repeated.ShouldBeTrue();
        result.Value.Id.ShouldBe(8);
    }

    [Fact]
    public async Task A_dry_run_builds_and_stores_nothing()
    {
        var result = await Handler().HandleAsync(new BuildDigestCommand(null, false, "cli:xd"), Ct);

        result.Value.Id.ShouldBeNull();
        result.Value.Text.ShouldContain(DigestBuilder.NothingApplied);
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("2026-W44")]
    public async Task A_bad_or_future_week_is_refused(string week)
    {
        var result = await Handler().HandleAsync(new BuildDigestCommand(week, true, "cli:xd"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Fact]
    public async Task A_phase_check_that_cannot_be_read_fails_the_build_and_stores_nothing()
    {
        _phase.HandleAsync(Arg.Any<EvaluatePhaseCheckQuery>(), Ct).Returns(Result.Failure<PhaseCheckView>(Error.Upstream("learning.boom", "no")));

        var result = await Handler().HandleAsync(new BuildDigestCommand(null, true, "system:weekly"), Ct);

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Theory]
    [InlineData("2026-10-23T09:00:00", null, true)] // Friday: before the week's Saturday 10:00
    [InlineData("2026-10-24T09:59:00", null, true)]
    [InlineData("2026-10-24T10:00:00", null, false)]
    [InlineData("2026-10-25T10:00:00", null, false)] // the catch-up Sunday
    [InlineData("2026-10-27T10:00:00", "2026-W43", false)] // the most recent completed week, the next Tuesday
    [InlineData("2026-11-03T10:00:00", "2026-W43", true)] // two weeks later: dry run only
    public async Task A_digest_is_stored_only_for_the_current_week_from_saturday_10_or_the_last_completed_week(string now, string? week, bool refused)
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse(now + "+05:30", System.Globalization.CultureInfo.InvariantCulture));

        var result = await Handler().HandleAsync(new BuildDigestCommand(week, true, "cli:xd"), Ct);

        result.IsFailure.ShouldBe(refused);
        if (refused)
        {
            result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        }

        await _store.Received(refused ? 0 : 1).InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Fact]
    public async Task An_old_week_can_be_printed_and_says_its_state_is_the_build_dates()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-03T10:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture));

        var result = await Handler().HandleAsync(new BuildDigestCommand("2026-W43", false, "cli:xd"), Ct);

        result.Value.Text.ShouldContain("State as of 2026-11-03, not as of the end of the week");
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Fact]
    public async Task The_stored_text_carries_the_build_time_in_india_time()
    {
        var result = await Handler().HandleAsync(new BuildDigestCommand(null, true, "system:weekly"), Ct);

        result.Value.Text.ShouldContain("Built at 2026-10-24 10:00 IST.");
    }

    private async Task<string> TextAtAsync(string now)
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));
        return (await Handler().HandleAsync(new BuildDigestCommand(null, false, "cli:xd"), Ct)).Value.Text;
    }

    [Fact]
    public async Task A_record_at_sunday_2359_ist_is_in_the_week_and_one_at_monday_0000_ist_is_not()
    {
        _changelog.ListAsync("cell", null, Arg.Any<int>(), Ct).Returns([
            new ChangelogEntry(20, new NewChangelogEntry("transition", "cell", Cell, "a", "b", "last minute", "owner", [], null, null, null, null, new DateTimeOffset(2026, 10, 25, 23, 59, 0, TimeSpan.FromMinutes(330)))),
            new ChangelogEntry(21, new NewChangelogEntry("transition", "cell", Cell, "b", "c", "next week", "owner", [], null, null, null, null, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromMinutes(330)))),
            new ChangelogEntry(19, new NewChangelogEntry("transition", "cell", Cell, "z", "a", "week start", "owner", [], null, null, null, null, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromMinutes(330)))),
            new ChangelogEntry(18, new NewChangelogEntry("transition", "cell", Cell, "y", "z", "just before", "owner", [], null, null, null, null, new DateTimeOffset(2026, 10, 18, 23, 59, 0, TimeSpan.FromMinutes(330)))),
        ]);

        var text = await TextAtAsync("2026-10-25T20:00:00+05:30");

        text.ShouldContain("#20 transition");
        text.ShouldContain("#19 transition");
        text.ShouldNotContain("#21 transition");
        text.ShouldNotContain("#18 transition");
    }

    [Fact]
    public async Task The_week_is_the_india_week_not_the_utc_week()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero)); // Sunday in UTC, Monday 01:30 in India
        var monday = await Handler().HandleAsync(new BuildDigestCommand(null, false, "cli:xd"), Ct);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.Zero)); // 2027-01-01 in India, still ISO 2026-W53
        var newYear = await Handler().HandleAsync(new BuildDigestCommand(null, false, "cli:xd"), Ct);

        monday.Value.IsoWeek.ShouldBe("2026-W44");
        newYear.Value.IsoWeek.ShouldBe("2026-W53");
        newYear.Value.WeekStart.ShouldBe(new DateOnly(2026, 12, 28));
    }

    [Fact]
    public async Task The_same_stored_records_give_the_same_text_on_two_builds()
    {
        var a = await Handler().HandleAsync(new BuildDigestCommand(null, false, "cli:xd"), Ct);
        var b = await Handler().HandleAsync(new BuildDigestCommand(null, false, "cli:xd"), Ct);

        a.Value.Text.ShouldBe(b.Value.Text);
    }
}
