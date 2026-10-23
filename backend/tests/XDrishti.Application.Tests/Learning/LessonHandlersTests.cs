using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Spec P6-16: open from the replayed monitors (idempotent), link a fix, close with the stored experiment's numbers, dismiss; illegal moves are refused.</summary>
public sealed class LessonHandlersTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static readonly string Key = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeLog _log = new();

    private readonly IHypothesisLog _hypotheses = Substitute.For<IHypothesisLog>();

    private readonly IExperimentLedger _ledger = Substitute.For<IExperimentLedger>();

    private readonly IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>> _tags = Substitute.For<IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>>>();

    private readonly IQueryHandler<ReplayDriftQuery, Result<DriftReport>> _drift = Substitute.For<IQueryHandler<ReplayDriftQuery, Result<DriftReport>>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));

    private OpenLessonsHandler Open(int recent = 6, string? cell = null)
    {
        var subject = cell ?? Cell;
        var growing = new GrowingTag(subject, LossTagKind.GaveBackProfit, recent, 2, 10);
        _tags.HandleAsync(Arg.Any<AnalyzeLossTagsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new LossTagReport(new LossTagOptions(), 40, 20, [new CellLossTags(subject, 40, 20, [0, 6, 0, 0, 0, 0, 0], 3, [growing])], [], 1, [])));
        _drift.HandleAsync(Arg.Any<ReplayDriftQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DriftReport(new DriftOptions(), 0, [new CellDrift(Cell, 50, 20, null, null, [new DriftEvent(DriftDetectorKind.Cusum, Cell, true, 100, 4, 5, 3, "cusum 5.00 over 3.00")])], null, [], [])));
        return new OpenLessonsHandler(_tags, _drift, _log, _time, NullLogger<OpenLessonsHandler>.Instance);
    }

    private static OpenLessonsCommand Scan(bool dry = false) => new([new CellSource(Key, "intraday", "FIXED_RR")], new DriftOptions(), new LossTagOptions(), 6, null, dry, "cli:xd");

    private static StoredExperiment Experiment(long id, ExperimentOutcome outcome, double? bMean = 0.10, double? cMean = 0.14) => new(id, new ExperimentToStore(
        new string('b', 64), new ExperimentKnob("slippage", ExperimentKnobKind.Slippage, "1", "2"), Key, new string('c', 64), new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1), null, outcome, 1, 3, 3, 3, 0.02, 100, 90, bMean, cMean, 12.5, 14.0, 0.05, 1, string.Empty, "report", "p", DateTimeOffset.UnixEpoch, "t"));

    private async Task<long> OpenOneAsync()
    {
        var outcome = await Open().HandleAsync(Scan(), Ct);
        return outcome.Value.Items.First(i => i.Monitor == "growing_tag").LessonId;
    }

    private LinkLessonHandler Link() => new(_log, _hypotheses, _ledger, _time, NullLogger<LinkLessonHandler>.Instance);

    private CloseLessonHandler Close() => new(_log, _hypotheses, _ledger, _time, NullLogger<CloseLessonHandler>.Instance);

    private DismissLessonHandler Dismiss() => new(_log, _time, NullLogger<DismissLessonHandler>.Instance);

    [Fact]
    public async Task A_scan_opens_one_lesson_per_finding_with_its_numbers_and_lists_what_is_not_built()
    {
        var result = await Open().HandleAsync(Scan(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(i => (i.Monitor, i.Status)).ShouldBe([("growing_tag", "opened"), ("cusum", "opened")]);
        result.Value.Items[0].Problem.ShouldContain("2 of 10 to 6 of 10");
        result.Value.NotBuilt.ShouldContain(n => n.StartsWith("execution_gap", StringComparison.Ordinal));
        _log.Rows.ShouldAllBe(r => r.Row.State == "open" && r.Row.Actor == "cli:xd");
    }

    [Fact]
    public async Task The_same_finding_scanned_again_opens_no_second_lesson()
    {
        var first = await Open().HandleAsync(Scan(), Ct);

        var second = await Open().HandleAsync(Scan(), Ct);

        second.Value.Items.ShouldAllBe(i => i.Status == "existing");
        second.Value.Items.Select(i => i.LessonId).ShouldBe(first.Value.Items.Select(i => i.LessonId));
        _log.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_persisting_growing_tag_with_other_counts_opens_no_new_lesson_while_the_lesson_is_not_final_and_a_fresh_one_after()
    {
        var id = await OpenOneAsync();
        var again = await Open(recent: 8).HandleAsync(Scan(), Ct);
        var dry = await Open(recent: 9).HandleAsync(Scan(dry: true), Ct);
        await Link().HandleAsync(new LinkLessonCommand(id, "experiment", 5, "tried", "cli:xd"), Ct);
        _ledger.FindAsync(5, Arg.Any<CancellationToken>()).Returns(Experiment(5, ExperimentOutcome.Inconclusive));
        var whileLinked = await Open(recent: 9).HandleAsync(Scan(), Ct);
        await Dismiss().HandleAsync(new DismissLessonCommand(id, "no", "cli:xd"), Ct);
        var after = await Open(recent: 9).HandleAsync(Scan(), Ct);

        again.Value.Items.First(i => i.Monitor == "growing_tag").ShouldBe(new LessonScanItem("growing_tag", Cell, again.Value.Items[0].Problem, "existing", id));
        dry.Value.Items[0].Status.ShouldBe("existing");
        whileLinked.Value.Items[0].Status.ShouldBe("existing");
        after.Value.Items[0].Status.ShouldBe("opened");
        after.Value.Items[0].LessonId.ShouldNotBe(id);
        after.Value.Items[0].Problem.ShouldContain("2 of 10 to 9 of 10");
    }

    [Fact]
    public async Task A_finding_whose_subject_does_not_fit_its_column_is_skipped_and_the_scan_goes_on()
    {
        var result = await Open(cell: new string('x', 401)).HandleAsync(Scan(), Ct);

        result.Value.Items.Select(i => (i.Monitor, i.Status)).ShouldBe([("growing_tag", "skipped"), ("cusum", "opened")]);
        result.Value.Items[0].Subject.Length.ShouldBe(400);
        _log.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing_and_marks_known_findings()
    {
        var dry = await Open().HandleAsync(Scan(dry: true), Ct);
        await Open().HandleAsync(Scan(), Ct);
        var dryAgain = await Open().HandleAsync(Scan(dry: true), Ct);

        dry.Value.Items.ShouldAllBe(i => i.Status == "found" && i.LessonId == 0);
        dryAgain.Value.Items.ShouldAllBe(i => i.Status == "existing");
        _log.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_scan_with_a_refused_monitor_fails_and_writes_nothing()
    {
        var handler = Open();
        _drift.HandleAsync(Arg.Any<ReplayDriftQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<DriftReport>(LearningErrors.RunNotFound(Key)));

        var result = await handler.HandleAsync(Scan(), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lesson_is_opened_linked_and_closed_with_the_before_and_after_of_the_stored_experiment()
    {
        var id = await OpenOneAsync();
        _hypotheses.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new HypothesisRow(7, new NewHypothesisRow(null, "judged", "running", "owner", null, "slippage", "Slippage", "1", "2", "r", Key, new string('c', 64), 42, "judged", "cli:xd", DateTimeOffset.UnixEpoch)));
        _ledger.FindAsync(42, Arg.Any<CancellationToken>()).Returns(Experiment(42, ExperimentOutcome.Better));

        var linked = await Link().HandleAsync(new LinkLessonCommand(id, "hypothesis", 7, "tried a dearer fill", "cli:xd"), Ct);
        var closed = await Close().HandleAsync(new CloseLessonCommand(id, "better after the change", "cli:xd"), Ct);

        linked.IsSuccess.ShouldBeTrue();
        closed.IsSuccess.ShouldBeTrue();
        var row = _log.Rows.Last().Row;
        (row.State, row.Result, row.ExperimentId, row.ExperimentOutcome).ShouldBe(("closed", "improved", 42L, "Better"));
        (row.BeforeMeanR, row.AfterMeanR, row.BeforeTotalR, row.AfterTotalR).ShouldBe((0.10, 0.14, 12.5, 14.0));
        row.HypothesisRef.ShouldBe(7);
        _log.Rows.Select(r => r.Row.State).ShouldBe(["open", "open", "linked", "closed"]);
    }

    [Theory]
    [InlineData(ExperimentOutcome.Inconclusive)]
    [InlineData(ExperimentOutcome.NotBetter)]
    [InlineData(ExperimentOutcome.CheaperAssumption)]
    public async Task A_lesson_whose_experiment_was_not_Better_closes_as_no_measurable_change_with_the_numbers_as_stored(ExperimentOutcome outcome)
    {
        var id = await OpenOneAsync();
        _ledger.FindAsync(5, Arg.Any<CancellationToken>()).Returns(Experiment(5, outcome, bMean: null, cMean: null));
        await Link().HandleAsync(new LinkLessonCommand(id, "experiment", 5, "tried", "cli:xd"), Ct);

        var closed = await Close().HandleAsync(new CloseLessonCommand(id, "nothing moved", "cli:xd"), Ct);

        closed.IsSuccess.ShouldBeTrue();
        closed.Value.Text.ShouldContain("no measurable change");
        var row = _log.Rows.Last().Row;
        (row.Result, row.ExperimentOutcome, row.BeforeMeanR, row.AfterMeanR, row.BeforeTotalR, row.AfterTotalR).ShouldBe(("no_measurable_change", outcome.ToString(), null, null, 12.5, 14.0));
    }

    [Fact]
    public async Task A_lesson_linked_to_a_hypothesis_not_yet_judged_cannot_be_closed()
    {
        var id = await OpenOneAsync();
        _hypotheses.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new HypothesisRow(7, new NewHypothesisRow(null, "running", "approved", "owner", null, "slippage", "Slippage", "1", "2", "r", Key, new string('c', 64), null, "started", "cli:xd", DateTimeOffset.UnixEpoch)));
        await Link().HandleAsync(new LinkLessonCommand(id, "hypothesis", 7, "tried", "cli:xd"), Ct);

        var closed = await Close().HandleAsync(new CloseLessonCommand(id, "done", "cli:xd"), Ct);

        closed.Error.Code.ShouldBe(LessonErrors.MoveRefusedCode);
        _log.Rows.Last().Row.State.ShouldBe("linked");
    }

    [Fact]
    public async Task Illegal_moves_are_refused_naming_the_legal_ones()
    {
        var id = await OpenOneAsync();
        await Dismiss().HandleAsync(new DismissLessonCommand(id, "not worth it", "cli:xd"), Ct);

        var linkAfter = await Link().HandleAsync(new LinkLessonCommand(id, "experiment", 5, "late", "cli:xd"), Ct);
        var closeAfter = await Close().HandleAsync(new CloseLessonCommand(id, "late", "cli:xd"), Ct);
        var dismissAgain = await Dismiss().HandleAsync(new DismissLessonCommand(id, "again", "cli:xd"), Ct);
        var closeOpen = await Close().HandleAsync(new CloseLessonCommand(await OpenSecondAsync(), "skip", "cli:xd"), Ct);

        new[] { linkAfter.Error, closeAfter.Error, dismissAgain.Error, closeOpen.Error }.ShouldAllBe(e => e.Code == LessonErrors.MoveRefusedCode);
        linkAfter.Error.Message.ShouldContain("final");
        closeOpen.Error.Message.ShouldContain("linked");
    }

    private async Task<long> OpenSecondAsync()
    {
        var outcome = await Open().HandleAsync(Scan(), Ct);
        return outcome.Value.Items.First(i => i.Monitor == "cusum").LessonId;
    }

    [Fact]
    public async Task Only_the_owner_moves_a_lesson_and_a_fix_that_does_not_exist_is_not_linked()
    {
        var id = await OpenOneAsync();

        var system = await Link().HandleAsync(new LinkLessonCommand(id, "experiment", 5, "auto", "worker:scan"), Ct);
        var missing = await Link().HandleAsync(new LinkLessonCommand(id, "experiment", 99, "tried", "cli:xd"), Ct);
        var unknownLesson = await Dismiss().HandleAsync(new DismissLessonCommand(999, "x", "cli:xd"), Ct);
        var badKind = await Link().HandleAsync(new LinkLessonCommand(id, "magic", 1, "x", "cli:xd"), Ct);
        var noReason = await Dismiss().HandleAsync(new DismissLessonCommand(id, " ", "cli:xd"), Ct);

        system.Error.Code.ShouldBe(LessonErrors.MoveRefusedCode);
        missing.Error.Code.ShouldBe(LessonErrors.FixNotFoundCode);
        unknownLesson.Error.Code.ShouldBe(LessonErrors.NotFoundCode);
        badKind.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        noReason.Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }

    [Fact]
    public async Task A_lesson_moved_meanwhile_is_stale()
    {
        var id = await OpenOneAsync();
        _log.ForceStale = true;

        var result = await Dismiss().HandleAsync(new DismissLessonCommand(id, "x", "cli:xd"), Ct);

        result.Error.Code.ShouldBe(LessonErrors.StaleCode);
    }

    [Fact]
    public async Task The_views_show_history_next_states_and_the_closing_numbers_and_refuse_bad_filters()
    {
        var id = await OpenOneAsync();
        var openedAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromHours(2));
        _ledger.FindAsync(5, Arg.Any<CancellationToken>()).Returns(Experiment(5, ExperimentOutcome.Better));
        await Link().HandleAsync(new LinkLessonCommand(id, "experiment", 5, "tried", "cli:xd"), Ct);
        await Close().HandleAsync(new CloseLessonCommand(id, "ok", "cli:xd"), Ct);

        var one = await new GetLessonHandler(_log).HandleAsync(new GetLessonQuery(id), Ct);
        var list = await new ListLessonsHandler(_log).HandleAsync(new ListLessonsQuery("closed", 10), Ct);
        var bad = await new ListLessonsHandler(_log).HandleAsync(new ListLessonsQuery("nope", 10), Ct);
        var missing = await new GetLessonHandler(_log).HandleAsync(new GetLessonQuery(404), Ct);

        one.Value.History.Select(h => h.State).ShouldBe(["open", "linked", "closed"]);
        one.Value.NextStates.ShouldBeEmpty();
        (one.Value.Result, one.Value.BeforeMeanR, one.Value.AfterMeanR).ShouldBe(("improved", 0.10, 0.14));
        list.Value.Single().Id.ShouldBe(id);
        list.Value.Single().CreatedAt.ShouldBe(openedAt);
        list.Value.Single().UpdatedAt.ShouldBeGreaterThan(openedAt);
        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        missing.Error.Code.ShouldBe(LessonErrors.NotFoundCode);
    }

    private sealed class FakeLog : ILessonLog
    {
        private long _next;

        public List<LessonRow> Rows { get; } = [];

        public bool ForceStale { get; set; }

        public ValueTask<LessonAppend> OpenAsync(NewLessonRow row, CancellationToken cancellationToken)
        {
            if (Known(row.MonitorKind, row.Subject, row.EvidenceKey) is { } known)
            {
                return ValueTask.FromResult(new LessonAppend(known, LessonAppendStatus.Existing));
            }

            Rows.Add(new LessonRow(++_next, row));
            return ValueTask.FromResult(new LessonAppend(_next, LessonAppendStatus.Created));
        }

        public ValueTask<LessonAppend> AppendAsync(NewLessonRow row, CancellationToken cancellationToken)
        {
            if (ForceStale)
            {
                return ValueTask.FromResult(new LessonAppend(0, LessonAppendStatus.Stale));
            }

            Rows.Add(new LessonRow(++_next, row));
            return ValueTask.FromResult(new LessonAppend(_next, LessonAppendStatus.Created));
        }

        public ValueTask<long?> FindIdByEvidenceAsync(string monitorKind, string subject, string evidenceKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Known(monitorKind, subject, evidenceKey));

        // The rule of the real log: a growing tag blocks while its lesson is open or linked, any other finding in any state.
        private long? Known(string monitorKind, string subject, string evidenceKey) =>
            Rows.FirstOrDefault(r => r.Row.LessonId is null && r.Row.MonitorKind == monitorKind && r.Row.Subject == subject && r.Row.EvidenceKey == evidenceKey
                && (monitorKind != "growing_tag" || Rows.Last(x => x.LessonId == r.Id).Row.State is "open" or "linked"))?.Id;

        public ValueTask<LessonRow?> FindAsync(long lessonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.Where(r => r.LessonId == lessonId).LastOrDefault());

        public ValueTask<IReadOnlyList<LessonRow>> HistoryAsync(long lessonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<LessonRow>>([.. Rows.Where(r => r.LessonId == lessonId)]);

        public ValueTask<IReadOnlyList<LessonListed>> ListAsync(string? state, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<LessonListed>>([.. Rows.GroupBy(r => r.LessonId).Select(g => new LessonListed(g.Last(), g.First().Row.CreatedAt)).Where(r => state is null || r.Latest.Row.State == state).Take(limit)]);
    }
}
