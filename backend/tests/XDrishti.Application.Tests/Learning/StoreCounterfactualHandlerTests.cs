using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Spec P8-12: the counterfactual is stored once per input hash, the basis follows the run headers, the views never carry NaN, and the Journey reads only stored records.</summary>
public sealed class StoreCounterfactualHandlerTests
{
    private static readonly string KeyA = new('a', 64);

    private static readonly string KeyB = new('b', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly IChargeSheetConfirmationSource _confirmations = Substitute.For<IChargeSheetConfirmationSource>();

    private readonly IQueryHandler<EvaluateCounterfactualQuery, Result<CounterfactualReport>> _evaluate = Substitute.For<IQueryHandler<EvaluateCounterfactualQuery, Result<CounterfactualReport>>>();

    private readonly MemoryStore _store = new();

    private StoreCounterfactualHandler Handler() =>
        new(_runs, _confirmations, _evaluate, _store, new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero)), NullLogger<StoreCounterfactualHandler>.Instance);

    private static StoredRunHeader Header(string key, string? sheet = null, string? rates = null) =>
        new(1, key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2025, 1, 1), new DateOnly(2026, 8, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", sheet, rates, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private void Runs(params StoredRunHeader[] headers)
    {
        foreach (var h in headers)
        {
            _runs.FindAsync(h.RunKey, Arg.Any<CancellationToken>()).Returns(h);
        }
    }

    private static CounterfactualSide Side(double logLoss, int kept, double sumKept) =>
        new(new ProbabilityMetrics(100, 50, logLoss, 0.25, 0.55, 3, null), new MetaPolicyTotals(kept, 100 - kept, sumKept, -2));

    internal static CounterfactualReport Report(CounterfactualOptions? options = null, double nan = 0.69)
    {
        var w1 = new CounterfactualWindow(1, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 40, 20, 5, 100, 100, false, new CounterfactualPooled(40, Side(nan, 20, 4), Side(nan, 20, 4), 0, null));
        var w2 = new CounterfactualWindow(2, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), 40, 20, 5, 100, 140, true, new CounterfactualPooled(40, Side(0.70, 20, 4), Side(double.NaN, 20, 10.4), 0.01, double.NaN));
        return new CounterfactualReport(
            CounterfactualOutcome.Inconclusive, ["Only 1 judged window."], options ?? new CounterfactualOptions(), new DateOnly(2025, 12, 31), new DateOnly(2026, 6, 30), 6, 100, [w1, w2],
            new CounterfactualPooled(40, Side(0.70, 20, 4), Side(0.69, 20, 10.4), 0.01, 0.02), new CounterfactualPooled(80, Side(0.70, 40, 8), Side(0.69, 40, 14.4), 0.005, 0.02),
            ["Replayed history.", "Model recipe: fake (p); training set ts1."]);
    }

    private static StoreCounterfactualCommand Command(params string[] keys) =>
        new(new EvaluateCounterfactualQuery([.. keys.Select(k => new CellSource(k, "intraday", "x"))], new CellStatsOptions(), null, new ValidationOptions(), new CounterfactualOptions(), 6), "cli:xd");

    [Fact]
    public async Task New_inputs_are_stored_and_the_same_inputs_again_add_no_row()
    {
        Runs(Header(KeyA));
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Report()));

        var first = await Handler().HandleAsync(Command(KeyA), Ct);
        var second = await Handler().HandleAsync(Command(KeyA), Ct);

        first.Value.Created.ShouldBeTrue();
        second.Value.Created.ShouldBeFalse();
        second.Value.Id.ShouldBe(first.Value.Id);
        _store.Rows.Count.ShouldBe(1);
        _store.Rows[0].Report.Basis.ShouldBe("gross");
        _store.Rows[0].Report.Text.ShouldContain("limit:");
        _store.Rows[0].Report.InputHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task A_report_without_the_model_recipe_line_fails_loudly_and_stores_nothing()
    {
        Runs(Header(KeyA));
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Report() with { Limits = ["Replayed history."] }));

        await Should.ThrowAsync<InvalidOperationException>(async () => await Handler().HandleAsync(Command(KeyA), Ct));

        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Any_changed_knob_is_another_row()
    {
        Runs(Header(KeyA));
        var changed = Command(KeyA) with { Query = Command(KeyA).Query with { Counterfactual = new CounterfactualOptions(RetrainMonths: 2, Trials: 2) } };
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Report()));

        await Handler().HandleAsync(Command(KeyA), Ct);
        var again = await Handler().HandleAsync(changed, Ct);

        again.Value.Created.ShouldBeTrue();
        _store.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Gross_and_costed_runs_are_never_mixed()
    {
        Runs(Header(KeyA), Header(KeyB, "sheet", "rates"));

        var result = await Handler().HandleAsync(Command(KeyA, KeyB), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        result.Error.Message.ShouldContain("never mixed");
        _store.Rows.ShouldBeEmpty();
        await _evaluate.DidNotReceiveWithAnyArgs().HandleAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Costed_runs_are_unconfirmed_until_the_owner_confirmed_that_sheet()
    {
        Runs(Header(KeyA, "sheet", "rates"));
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Report()));

        await Handler().HandleAsync(Command(KeyA), Ct);
        _confirmations.FindAsync(Arg.Any<CancellationToken>()).Returns(new ChargeSheetConfirmation("sheet", "rates", new DateOnly(2026, 10, 1)));
        await Handler().HandleAsync(Command(KeyA) with { Query = Command(KeyA).Query with { HoldOutMonths = 3 } }, Ct);

        _store.Rows.Select(r => r.Report.Basis).ShouldBe(["costed_unconfirmed", "net_confirmed"]);
    }

    [Fact]
    public async Task A_refused_evaluation_or_an_unknown_run_stores_nothing()
    {
        Runs(Header(KeyA));
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<CounterfactualReport>(LearningErrors.Invalid([new FieldError("x", "bad")])));

        var refused = await Handler().HandleAsync(Command(KeyA), Ct);
        var unknown = await Handler().HandleAsync(Command(new string('f', 64)), Ct);

        refused.IsFailure.ShouldBeTrue();
        unknown.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_stored_view_has_no_nan_and_the_uplift_of_an_unjudged_window_is_null()
    {
        Runs(Header(KeyA));
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Report()));
        await Handler().HandleAsync(Command(KeyA), Ct);

        var view = await new GetCounterfactualHandler(_store).HandleAsync(new GetCounterfactualQuery(null), Ct);

        view.IsSuccess.ShouldBeTrue();
        view.Value.Windows[0].TakeRUplift.ShouldBeNull();
        view.Value.Windows[0].Note.ShouldNotBeNull();
        view.Value.Windows[1].Retrained.LogLoss.ShouldBeNull();
        view.Value.Windows[1].GainStandardError.ShouldBeNull();
        view.Value.Windows[1].TakeRUplift.ShouldNotBeNull().ShouldBe(40 * ((10.4 / 100) - (4.0 / 100)), 1e-9);
        view.Value.JudgedWindows.ShouldBe(1);
        view.Value.Id.ShouldBe(_store.Rows[0].Id);
        view.Value.SameRunsStored.ShouldBe(1);
    }

    [Fact]
    public async Task Get_is_not_found_when_nothing_or_an_unknown_id_is_stored_and_list_checks_its_limit()
    {
        var get = new GetCounterfactualHandler(_store);

        (await get.HandleAsync(new GetCounterfactualQuery(null), Ct)).Error.Code.ShouldBe(LearningErrors.CounterfactualNotFoundCode);
        (await get.HandleAsync(new GetCounterfactualQuery(99), Ct)).Error.Code.ShouldBe(LearningErrors.CounterfactualNotFoundCode);
        (await new ListCounterfactualsHandler(_store).HandleAsync(new ListCounterfactualsQuery(51), Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
        (await new ListCounterfactualsHandler(_store).HandleAsync(new ListCounterfactualsQuery(0), Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }

    [Fact]
    public async Task The_journey_without_a_counterfactual_is_empty_and_the_live_line_is_not_evaluable()
    {
        var grades = Substitute.For<IGradeStore>();
        grades.ReportItemsAsync(null, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new GradedPage([], false));

        var journey = await new GetJourneyHandler(_store, grades).HandleAsync(new GetJourneyQuery(), Ct);

        journey.Value.Counterfactual.ShouldBeNull();
        journey.Value.PlanPrice.State.ShouldBe("empty");
        journey.Value.PlanPrice.Basis.ShouldBe("simulated on plan prices");
        journey.Value.Live.State.ShouldBe("not_evaluable");
        journey.Value.Live.N.ShouldBe(0);
    }

    [Fact]
    public async Task The_journey_carries_the_newest_header_the_evidence_and_the_weeks_while_live_stays_not_evaluable()
    {
        Runs(Header(KeyA));
        _evaluate.HandleAsync(Arg.Any<EvaluateCounterfactualQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Report()));
        await Handler().HandleAsync(Command(KeyA), Ct);
        var grades = Substitute.For<IGradeStore>();
        var items = Enumerable.Range(0, 5).Select(_ => new GradedItem(1, new DateOnly(2026, 10, 19), 1, TicketGroup.Primary, TicketState.Closed, new TicketGrade(GradeStatus.Graded, 1.0, null, null, null, null, null, TicketGrade.Basis, "t"))).ToList();
        grades.ReportItemsAsync(null, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new GradedPage(items, false));

        var journey = await new GetJourneyHandler(_store, grades).HandleAsync(new GetJourneyQuery(), Ct);

        journey.Value.Counterfactual.ShouldNotBeNull().Outcome.ShouldBe("Inconclusive");
        journey.Value.Evidence.Population.ShouldBe([KeyA]);
        journey.Value.Evidence.N.ShouldBe(80);
        journey.Value.PlanPrice.Weeks.Single().MeanR.ShouldBe(1.0);
        journey.Value.PlanPrice.TotalGraded.ShouldBe(5);
        journey.Value.Live.State.ShouldBe("not_evaluable");
    }

    private sealed class MemoryStore : ILearningReportStore
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
