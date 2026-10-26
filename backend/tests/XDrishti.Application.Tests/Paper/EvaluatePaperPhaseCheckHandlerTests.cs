#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Paper;

/// <summary>
/// Spec P10-06 over fake ports: a seeded 12-week epoch (a stub comparison stands in for the owner's confirmed charge sheet) reads MET with its caveats, an empty record reads NOT EVALUABLE everywhere, a Sunday end date rounds down to the Friday,
/// bad dates and epochs are refused, an unconfirmed sheet keeps C3 and C4 not evaluable, and every call on every store is a read.
/// </summary>
public sealed class EvaluatePaperPhaseCheckHandlerTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);

    // Monday 2027-01-25, 16:00 India time: the session has closed.
    private static readonly DateTimeOffset Now = new(2027, 1, 25, 10, 30, 0, TimeSpan.Zero);

    private static readonly EpochRow Epoch = new(2, Start, new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), "owner_restart", "start", "owner");

    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();
    private readonly IBacktestPaperData _data = Substitute.For<IBacktestPaperData>();
    private readonly IPaperPhaseData _phase = Substitute.For<IPaperPhaseData>();
    private readonly IGovernanceStore _governance = Substitute.For<IGovernanceStore>();
    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();
    private readonly IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>> _comparison = Substitute.For<IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>>>();
    private readonly IQueryHandler<GetDisciplineQuery, Result<DisciplineView>> _discipline = Substitute.For<IQueryHandler<GetDisciplineQuery, Result<DisciplineView>>>();
    private readonly IQueryHandler<EvaluatePlanPhaseCheckQuery, Result<PlanPhaseCheckView>> _p7 = Substitute.For<IQueryHandler<EvaluatePlanPhaseCheckQuery, Result<PlanPhaseCheckView>>>();
    private readonly IServiceScheduleReader _services = Substitute.For<IServiceScheduleReader>();
    private readonly IOpenBookSettings _openBook = Substitute.For<IOpenBookSettings>();
    private readonly IAutonomySettingsStore _autonomy = Substitute.For<IAutonomySettingsStore>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    public EvaluatePaperPhaseCheckHandlerTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _ledger.EpochAsync(Arg.Any<long?>(), Ct).Returns(c => ValueTask.FromResult<EpochRow?>(c.Arg<long?>() is { } id && id != 2 ? null : Epoch));
        _governance.ListEpochsAsync(Ct).Returns(ValueTask.FromResult<IReadOnlyList<EpochSummary>>([new EpochSummary(Epoch, "h", 61, 0, 2, 0)]));
        _governance.ListChangesAsync(Arg.Any<long?>(), Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredChange>>(
            [new StoredChange(1, 1, 2, "k", "risk_down", "1", "2", "noted", 2, null, new DateOnly(2026, 12, 1)), new StoredChange(2, 2, 3, "k", "risk_down", "2", "3", "noted", 2, null, new DateOnly(2026, 12, 8))]));
        _services.GetAsync(Arg.Any<string>(), Ct).Returns(ValueTask.FromResult<ServiceScheduleInfo?>(new ServiceScheduleInfo(true, null, "daily", null)));
        _autonomy.CurrentAsync(Ct).Returns(AutonomySettings.Defaults);
        _discipline.HandleAsync(Arg.Any<GetDisciplineQuery>(), Ct).Returns(ValueTask.FromResult(Result.Failure<DisciplineView>(PaperErrors.NotFound("discipline"))));
        _p7.HandleAsync(Arg.Any<EvaluatePlanPhaseCheckQuery>(), Ct).Returns(ValueTask.FromResult(Result.Success(new PlanPhaseCheckView(
            new PlanPhaseReport(new DateOnly(2027, 1, 25), 10, "MET", "MET", [], [], [], [], [], [], ["limit"]), "text"))));
        Seed(61, 52);
        _comparison.HandleAsync(Arg.Any<GetBacktestVsPaperQuery>(), Ct).Returns(ValueTask.FromResult(Result.Success(View(true))));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EvaluatePaperPhaseCheckHandler Handler() =>
        new(_calendar, _ledger, _data, _phase, _governance, _plans, _comparison, _discipline, _p7, _services, _openBook, _autonomy, new FakeTimeProvider(Now), NullLogger<EvaluatePaperPhaseCheckHandler>.Instance);

    private static List<DateOnly> Weekdays() =>
        [.. Enumerable.Range(0, 85).Select(i => Start.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];

    private void Seed(int trades, int covered, int integrity = 0)
    {
        var days = Weekdays();
        var runs = days.Select((d, i) => new PlanRunRow(i + 1, 1, new NewPlanRun(d, "k", "published", null, "ok", i + 1, false, Now, Now))).ToList();
        var plans = days.Select((d, i) => new PlanRow(i + 1, new NewPlan("k", d, Now, 1, "h", "b", false, "t", Now, []), PlanState.Published, 2, 0, 0)).ToList();
        _plans.ListRunsAsync(null, Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<PlanRunRow>>([.. runs.AsEnumerable().Reverse()]));
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<PlanRow>>(plans));
        var captures = days.Select((d, i) => new CaptureRunView(i + 1, d, Now, Now, "done", 2, 0, 0, 0, 0, i == 1 ? integrity : 0, false, "scheduled run")).Take(covered).ToList();
        _phase.CaptureRunsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<CaptureRunView>>(captures));
        var t0 = new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(1, trades).Select(i => new PaperCompareRow(i, i, "primary", "measured", "cell", "k" + i, t0.AddDays(i), t0.AddDays(i).AddHours(2), 0.1, "net", "flat-10")).ToList();
        _data.ReadAsync(2, Ct).Returns(ValueTask.FromResult(new PaperCompareData(rows, 61, null)));
    }

    private static MetricView Num(double v) => new("value", v, null);

    private static BacktestVsPaperView View(bool confirmed)
    {
        var none = new MetricView("insufficient", null, "x");
        CompareVerdictView met = new("met", "ok");
        CompareVerdictView gone = new("not_evaluable", "not evaluable: costs not confirmed");
        return new BacktestVsPaperView(
            2, 61, new CompareCountsView(61, 0, 0, 0, 0), "net", new CompareChargeSheetView("flat-10", confirmed ? new DateOnly(2026, 10, 30) : null),
            new ComparePaperView(Num(0.1), new CompareIntervalView(Num(-0.12), Num(0.51)), Num(2)), new CompareBandView(none, none, none, none, null, 10_000, null), new CompareDrawdownView(none, none, none, null),
            new CompareCriteriaView(confirmed ? met : gone, confirmed ? met : gone), null, [], [], new CompareSensitivityView("simulated on plan prices, all proposals", 70, Num(0.05)),
            new CompareEvidenceView(Start, null, null, null, null, [], "s"), []);
    }

    private async Task<PaperPhaseReport> RunAsync(DateOnly? through = null, long? epoch = null)
    {
        var result = await Handler().HandleAsync(new EvaluatePaperPhaseCheckQuery(through, epoch), Ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Text.ShouldContain("limit: Simulated fills");
        return result.Value.Report;
    }

    [Fact]
    public async Task A_seeded_12_week_epoch_with_a_confirmed_stub_sheet_is_met_with_its_caveats_and_reads_only()
    {
        var report = await RunAsync();

        report.Epoch!.Id.ShouldBe(2);
        report.ElapsedDays.ShouldBe(85);
        report.Through.ShouldBe(new DateOnly(2027, 1, 25));
        report.Criteria.Select(c => c.Verdict).ShouldBe(["MET", "MET", "MET", "MET", "MET"]);
        report.ExitVerdict.ShouldBe("MET");
        report.ExitCriterion.ShouldBe("MET (point estimate; 90% interval [-0.12, +0.51] includes 0; 2 risk-down changes: the population was censored)");
        report.Weeks.Sum(w => w.RiskDownChanges).ShouldBe(2);
        report.Printed.Services.ShouldAllBe(s => s.State == "enabled");
        report.Printed.DisciplineLines.ShouldBe(["unavailable: paper.not_found"]);

        await _plans.DidNotReceiveWithAnyArgs().MoveItemAsync(default!, Ct);
        await _plans.DidNotReceiveWithAnyArgs().MovePlanAsync(default!, Ct);
        await _plans.DidNotReceiveWithAnyArgs().RecordRunAsync(default!, Ct);
        await _plans.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
        await _plans.DidNotReceiveWithAnyArgs().PublishWithRunAsync(default!, default!, Ct);
        await _governance.DidNotReceiveWithAnyArgs().CommitAsync(default!, Ct);
        await _governance.DidNotReceiveWithAnyArgs().StartOwnerEpochAsync(default!, Ct);
        await _governance.DidNotReceiveWithAnyArgs().CreateFirstEpochAsync(default, default, null, Ct);
        await _governance.DidNotReceiveWithAnyArgs().AddNoteAsync(default, null, null, string.Empty, string.Empty, default, Ct);
    }

    [Fact]
    public async Task The_production_default_not_confirmed_fact_keeps_expectancy_and_drawdown_not_evaluable()
    {
        _comparison.HandleAsync(Arg.Any<GetBacktestVsPaperQuery>(), Ct).Returns(ValueTask.FromResult(Result.Success(View(false))));

        var report = await RunAsync();

        report.Criteria.Single(c => c.Id == "C3").Verdict.ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Id == "C3").Label.ShouldContain("costs not confirmed");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
        report.Printed.ChargeSheetConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_sunday_end_date_is_rounded_down_to_the_friday_before()
    {
        var report = await RunAsync(new DateOnly(2027, 1, 24));

        report.Through.ShouldBe(new DateOnly(2027, 1, 22));
        report.ElapsedDays.ShouldBe(82);
        report.Criteria[0].Label.ShouldBe("NOT EVALUABLE: day 82 of 84 (week 12 of 12)");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public async Task Low_coverage_is_not_met_with_the_missing_sessions_listed()
    {
        Seed(61, 40);

        var report = await RunAsync();

        report.Criteria[0].Verdict.ShouldBe("NOT MET");
        report.Criteria[0].Facts.ShouldContain(f => f.StartsWith("missing sessions: ", StringComparison.Ordinal));
        report.ExitVerdict.ShouldBe("NOT MET");
    }

    [Fact]
    public async Task An_integrity_mismatch_in_a_capture_row_is_not_met()
    {
        Seed(61, 52, integrity: 1);

        var report = await RunAsync();

        report.Criteria.Single(c => c.Id == "I").Verdict.ShouldBe("NOT MET");
    }

    [Fact]
    public async Task No_epoch_reads_not_evaluable_everywhere()
    {
        _ledger.EpochAsync(null, Ct).Returns(ValueTask.FromResult<EpochRow?>(null));
        _governance.ListEpochsAsync(Ct).Returns(ValueTask.FromResult<IReadOnlyList<EpochSummary>>([]));

        var report = await RunAsync();

        report.Epoch.ShouldBeNull();
        report.Criteria.ShouldAllBe(c => c.Verdict == "NOT EVALUABLE");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public async Task A_comparison_that_cannot_be_read_is_named_and_never_a_met()
    {
        _comparison.HandleAsync(Arg.Any<GetBacktestVsPaperQuery>(), Ct).Returns(ValueTask.FromResult(Result.Failure<BacktestVsPaperView>(PaperErrors.Invalid("x", "y"))));

        var report = await RunAsync();

        report.Criteria.Single(c => c.Id == "C3").Label.ShouldContain("could not be read");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
    }

    [Theory]
    [InlineData("2027-01-26", "paper.invalid")]
    [InlineData("2026-10-30", "paper.invalid")]
    public async Task A_date_after_the_newest_closed_session_or_before_the_epoch_is_refused(string through, string code)
    {
        var result = await Handler().HandleAsync(new EvaluatePaperPhaseCheckQuery(DateOnly.Parse(through, System.Globalization.CultureInfo.InvariantCulture), null), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public async Task An_unknown_epoch_is_not_found_and_a_non_positive_id_is_invalid()
    {
        var unknown = await Handler().HandleAsync(new EvaluatePaperPhaseCheckQuery(null, 99), Ct);
        var zero = await Handler().HandleAsync(new EvaluatePaperPhaseCheckQuery(null, 0), Ct);

        unknown.Error.Code.ShouldBe("paper.not_found");
        zero.Error.Code.ShouldBe("paper.invalid");
    }
}
