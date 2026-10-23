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

    private readonly FakeTimeProvider _time = new(Saturday);

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
        var result = await Handler().HandleAsync(new BuildDigestCommand(null, false, "system:weekly"), Ct);

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
        await Handler().HandleAsync(new BuildDigestCommand(null, false, "system:weekly"), Ct);

        var allowed = new[] { "List", "Find", "History", "NewestAsOf", "Count", "ReportItems", "ForPlans" };
        foreach (var spy in new object[] { _cells, _changelog, _lessons, _hypotheses, _experiments, _models, _grades })
        {
            foreach (var call in spy.ReceivedCalls())
            {
                allowed.Any(a => call.GetMethodInfo().Name.StartsWith(a, StringComparison.Ordinal)).ShouldBeTrue($"{call.GetMethodInfo().DeclaringType!.Name}.{call.GetMethodInfo().Name} is a write");
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
        var existing = DigestBuilder.Build(new DigestInput(DigestWeek.TryParse("2026-W43")!.Value, null, [], [], [], [], 0, 0, [], 0, [], null, [], null, null, [], []));
        _store.FindByWeekAsync("2026-W43", Ct).Returns(new StoredDigest(8, existing, "cli:xd", Saturday));

        var result = await Handler().HandleAsync(new BuildDigestCommand("2026-W43", false, "system:weekly"), Ct);

        result.Value.Repeated.ShouldBeTrue();
        result.Value.Id.ShouldBe(8);
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
        _lessons.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_concurrent_second_build_returns_the_first_stored_digest()
    {
        var first = DigestBuilder.Build(new DigestInput(DigestWeek.TryParse("2026-W43")!.Value, null, [], [], [], [], 0, 0, [], 0, [], null, [], null, null, [], []));
        _store.InsertAsync(Arg.Any<NewDigest>(), Ct).Returns((new StoredDigest(8, first, "cli:xd", Saturday), false));

        var result = await Handler().HandleAsync(new BuildDigestCommand(null, false, "system:weekly"), Ct);

        result.Value.Repeated.ShouldBeTrue();
        result.Value.Id.ShouldBe(8);
    }

    [Fact]
    public async Task A_dry_run_builds_and_stores_nothing()
    {
        var result = await Handler().HandleAsync(new BuildDigestCommand(null, true, "cli:xd"), Ct);

        result.Value.Id.ShouldBeNull();
        result.Value.Text.ShouldContain(DigestBuilder.NothingApplied);
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("2026-W44")]
    public async Task A_bad_or_future_week_is_refused(string week)
    {
        var result = await Handler().HandleAsync(new BuildDigestCommand(week, false, "cli:xd"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _store.DidNotReceive().InsertAsync(Arg.Any<NewDigest>(), Ct);
    }

    [Fact]
    public async Task The_same_stored_records_give_the_same_text_on_two_builds()
    {
        var a = await Handler().HandleAsync(new BuildDigestCommand(null, true, "cli:xd"), Ct);
        var b = await Handler().HandleAsync(new BuildDigestCommand(null, true, "cli:xd"), Ct);

        a.Value.Text.ShouldBe(b.Value.Text);
    }
}
