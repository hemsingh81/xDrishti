using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Spec P8-13 criteria 8 and 9 (P8-13b): entry-window studies and meta-policy evaluations are stored once per input hash, a NoPolicy result is stored like any other, and the reads return what was stored with the row's own id.</summary>
public sealed class StoreLearningReportHandlerTests
{
    private static readonly string KeyA = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly IChargeSheetConfirmationSource _confirmations = Substitute.For<IChargeSheetConfirmationSource>();

    private readonly IQueryHandler<ProposeEntryWindowsQuery, Result<EntryWindowReport>> _propose = Substitute.For<IQueryHandler<ProposeEntryWindowsQuery, Result<EntryWindowReport>>>();

    private readonly IQueryHandler<EvaluateMetaPolicyQuery, Result<MetaPolicyReport>> _evaluate = Substitute.For<IQueryHandler<EvaluateMetaPolicyQuery, Result<MetaPolicyReport>>>();

    private readonly IModelTrainer _trainer = Substitute.For<IModelTrainer>();

    private readonly MemoryReportStore _store = new();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));

    public StoreLearningReportHandlerTests()
    {
        var factory = Substitute.For<IScorerFactory>();
        factory.Name.Returns("fake");
        _trainer.Candidate.Returns(factory);
        _trainer.Parameters.Returns("p");
        _runs.FindAsync(KeyA, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(1, KeyA, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2025, 1, 1), new DateOnly(2026, 8, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t"));
    }

    private static IReadOnlyList<CellSource> Sources => [new CellSource(KeyA, "intraday", "x")];

    private static EntryWindowReport Windows(EntryWindowStatus status = EntryWindowStatus.Proposed, double? lift = 0.08)
    {
        var cell = new EntryWindowProposal("ORB|intraday|x", status, 120, 0.5, 555, 645, 40, 24, 0.58, lift, 0.2, double.NaN, 0.33, false, 3, 3, 2, 0.06, 1, 0, [], [], ["held in 3 of 3 folds"]);
        return new EntryWindowReport(new EntryWindowOptions(), new ValidationOptions(), 120, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 30), 3, 0, [cell], ["Optimistic where chosen on all scores."]);
    }

    private static MetaPolicyReport Policy(bool noPolicy = false) =>
        noPolicy
            ? MetaPolicyReport.NoPolicy("There is no champion model.", new MetaPolicyOptions(), ["Advisory: nothing uses a policy until P7."])
            : new MetaPolicyReport(
                MetaPolicyOutcome.DoesNotHelp, ["Taking everything is as good."], new MetaPolicyOptions(), 300, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 30), 4, [],
                new MetaPolicyTotals(80, 20, 12.5, -3), new MetaPolicyTotals(60, 40, 5, -2), [new MetaPolicyThreshold("*", 0.55, 300, 0.8)], ["Champion: model 7 (abcdef123456), verdict ok."]);

    private StoreEntryWindowsHandler WindowsHandler() => new(_runs, _confirmations, _propose, _store, _time, NullLogger<StoreEntryWindowsHandler>.Instance);

    private StoreMetaPolicyHandler PolicyHandler() => new(_runs, _confirmations, _evaluate, _trainer, _store, _time, NullLogger<StoreMetaPolicyHandler>.Instance);

    private static StoreEntryWindowsCommand WindowsCommand(EntryWindowOptions? options = null) =>
        new(new ProposeEntryWindowsQuery(Sources, options ?? new EntryWindowOptions(), new ValidationOptions(), 6), "cli:xd");

    private static StoreMetaPolicyCommand PolicyCommand(MetaPolicyOptions? options = null) =>
        new(new EvaluateMetaPolicyQuery(Sources, new CellStatsOptions(), null, new ValidationOptions(), options ?? new MetaPolicyOptions(), 6), "cli:xd");

    [Fact]
    public async Task An_entry_window_study_is_stored_once_per_input_hash_and_another_knob_is_another_row()
    {
        _propose.HandleAsync(Arg.Any<ProposeEntryWindowsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Windows()));

        var first = await WindowsHandler().HandleAsync(WindowsCommand(), Ct);
        var again = await WindowsHandler().HandleAsync(WindowsCommand(), Ct);
        var other = await WindowsHandler().HandleAsync(WindowsCommand(new EntryWindowOptions(BucketMinutes: 15)), Ct);

        first.Value.Created.ShouldBeTrue();
        again.Value.Created.ShouldBeFalse();
        again.Value.Id.ShouldBe(first.Value.Id);
        other.Value.Created.ShouldBeTrue();
        _store.Rows.Count.ShouldBe(2);
        _store.Rows[0].Report.Kind.ShouldBe("entry_windows");
        _store.Rows[0].Report.Basis.ShouldBe("gross");
        _store.Rows[0].Report.Text.ShouldContain("limit:");
    }

    [Fact]
    public async Task The_read_returns_the_stored_view_with_the_window_as_clock_text_and_no_nan()
    {
        _propose.HandleAsync(Arg.Any<ProposeEntryWindowsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Windows()));
        var stored = await WindowsHandler().HandleAsync(WindowsCommand(), Ct);

        var view = (await new GetEntryWindowsHandler(_store).HandleAsync(new GetEntryWindowsQuery(null), Ct)).Value;

        view.Id.ShouldBe(stored.Value.Id);
        view.BuiltBy.ShouldBe("cli:xd");
        view.Outcome.ShouldBe("Proposed");
        var cell = view.Cells.Single();
        (cell.WindowFrom, cell.WindowTo).ShouldBe(("09:15", "10:45"));
        cell.MeanROutside.ShouldBeNull();
        (cell.FoldsHeld, cell.FoldsEvaluable, cell.FoldsRequired).ShouldBe((3, 3, 2));
    }

    [Fact]
    public async Task A_status_other_than_proposed_is_the_outcome_and_is_kept()
    {
        _propose.HandleAsync(Arg.Any<ProposeEntryWindowsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Windows(EntryWindowStatus.Inconclusive)));
        await WindowsHandler().HandleAsync(WindowsCommand(), Ct);

        var view = (await new GetEntryWindowsHandler(_store).HandleAsync(new GetEntryWindowsQuery(null), Ct)).Value;

        view.Outcome.ShouldBe("NoWindowProposed");
        view.Cells.Single().Status.ShouldBe("Inconclusive");
    }

    [Fact]
    public async Task A_no_policy_result_is_stored_like_any_other_and_repeats_add_no_row()
    {
        _evaluate.HandleAsync(Arg.Any<EvaluateMetaPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Policy(noPolicy: true)));

        var first = await PolicyHandler().HandleAsync(PolicyCommand(), Ct);
        var again = await PolicyHandler().HandleAsync(PolicyCommand(), Ct);

        first.Value.Created.ShouldBeTrue();
        again.Value.Created.ShouldBeFalse();
        _store.Rows.Single().Report.Outcome.ShouldBe("NoPolicy");
        var view = (await new GetMetaPolicyHandler(_store).HandleAsync(new GetMetaPolicyQuery(first.Value.Id), Ct)).Value;
        view.Outcome.ShouldBe("NoPolicy");
        view.From.ShouldBeNull();
        view.Reasons.ShouldBe(["There is no champion model."]);
    }

    [Fact]
    public async Task A_policy_with_a_champion_differs_from_the_no_policy_row_and_reads_back_totals_and_thresholds()
    {
        _evaluate.HandleAsync(Arg.Any<EvaluateMetaPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Policy(noPolicy: true)), Result.Success(Policy()));
        await PolicyHandler().HandleAsync(PolicyCommand(), Ct);
        await PolicyHandler().HandleAsync(PolicyCommand(), Ct);

        var view = (await new GetMetaPolicyHandler(_store).HandleAsync(new GetMetaPolicyQuery(null), Ct)).Value;

        _store.Rows.Count.ShouldBe(2);
        view.Outcome.ShouldBe("DoesNotHelp");
        (view.Pooled.Kept, view.Pooled.Skipped).ShouldBe((80, 20));
        view.Pooled.MeanSkipped.ShouldBe(-0.15);
        view.Policy.Single().Threshold.ShouldBe(0.55);
    }

    [Fact]
    public async Task Nothing_stored_is_a_404_with_the_kinds_code_and_a_refused_evaluation_stores_nothing()
    {
        var windows = await new GetEntryWindowsHandler(_store).HandleAsync(new GetEntryWindowsQuery(null), Ct);
        var policy = await new GetMetaPolicyHandler(_store).HandleAsync(new GetMetaPolicyQuery(5), Ct);
        _propose.HandleAsync(Arg.Any<ProposeEntryWindowsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<EntryWindowReport>(LearningErrors.Invalid([new FieldError("x", "bad")])));
        var refused = await WindowsHandler().HandleAsync(WindowsCommand(), Ct);

        windows.Error.Code.ShouldBe("learning.entry_windows_not_found");
        policy.Error.Code.ShouldBe("learning.meta_policy_not_found");
        refused.IsFailure.ShouldBeTrue();
        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_of_another_kind_is_not_returned_by_id()
    {
        _propose.HandleAsync(Arg.Any<ProposeEntryWindowsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Windows()));
        var stored = await WindowsHandler().HandleAsync(WindowsCommand(), Ct);

        var wrong = await new GetMetaPolicyHandler(_store).HandleAsync(new GetMetaPolicyQuery(stored.Value.Id), Ct);

        wrong.IsFailure.ShouldBeTrue();
    }

    private sealed class MemoryReportStore : ILearningReportStore
    {
        public List<StoredLearningReport> Rows { get; } = [];

        public ValueTask<(StoredLearningReport Stored, bool Created)> InsertAsync(NewLearningReport report, CancellationToken cancellationToken)
        {
            var existing = Rows.FirstOrDefault(r => r.Report.Kind == report.Kind && r.Report.InputHash == report.InputHash);
            if (existing is not null)
            {
                return ValueTask.FromResult((existing, false));
            }

            var row = new StoredLearningReport(Rows.Count + 1, report);
            Rows.Add(row);
            return ValueTask.FromResult((row, true));
        }

        public ValueTask<StoredLearningReport?> FindAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public ValueTask<StoredLearningReport?> NewestAsync(string kind, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.LastOrDefault(r => r.Report.Kind == kind));

        public ValueTask<IReadOnlyList<StoredLearningReport>> ListAsync(string kind, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredLearningReport>>([.. Enumerable.Reverse(Rows).Where(r => r.Report.Kind == kind).Take(limit)]);
    }
}
