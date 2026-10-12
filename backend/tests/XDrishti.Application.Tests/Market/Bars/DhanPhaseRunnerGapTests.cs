using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>The gap backfill inside the Dhan phase (ADR 0017 §3): extra windows in the plan, bars through the same merge, the counters of the phase, failures that retry next time.</summary>
public sealed class DhanPhaseRunnerGapTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 50, 0, Ist);

    private static readonly DateOnly Friday = new(2026, 10, 9);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly FakeFetchTargets _targets = new();
    private readonly FakeBarStore _bars = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly FakeDataSettings _settings = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeBarGaps _gaps = new();
    private readonly FakeBarDayCounts _counts;
    private readonly FetchTarget _target;

    public DhanPhaseRunnerGapTests()
    {
        _counts = new FakeBarDayCounts(_bars);
        _target = new FetchTarget(Guid.CreateVersion7(), "NSE:ABB", "share", "100", "NSE_EQ", new DateTimeOffset(2026, 10, 12, 15, 29, 0, Ist), new DateTimeOffset(2026, 8, 3, 9, 15, 0, Ist));
        _targets.Targets.Add(_target);
        var days = new Dictionary<DateOnly, int>();
        for (var day = new DateOnly(2026, 10, 12); day > new DateOnly(2026, 8, 20); day = day.AddDays(-1))
        {
            days[day] = _calendar.Calendar.ExpectedMinutes(day);
        }

        days[Friday] = 200;
        _counts.Preset[_target.InstrumentId] = days;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<Bar> FridayBars(int from, int count) =>
        Enumerable.Range(from, count).Select(i => new Bar(new DateTimeOffset(2026, 10, 9, 9, 15, 0, Ist).ToUnixTimeSeconds() + i * 60L, 100, 101, 99, 100.5, 10));

    private async Task<(DhanPhase Phase, DataJob Job)> RunAsync(bool refetchGaps = false)
    {
        var job = DataJob.Start([DataSteps.Dhan], false, "owner", Now, DataJobTrigger.Manual, refetchGaps);
        _jobs.Jobs[job.Id] = job;
        var runner = new DhanPhaseRunner(
            _source, _account, _targets, _bars, _settings, _calendar, new GapBackfill(_counts, _gaps, _settings, _clock), _clock, NullLogger<DhanPhaseRunner>.Instance);
        return (await runner.RunAsync(new JobReporter(job, _jobs, _clock), Ct), job);
    }

    [Fact]
    public async Task The_plan_gets_an_extra_window_for_the_partial_session_before_the_ordinary_one_and_the_bars_go_through_the_same_merge()
    {
        _source.Respond = t => new IntradayResult(t.InstrumentId, [.. FridayBars(200, 175)], 2);

        var (phase, _) = await RunAsync();

        var windows = _source.Plans.ShouldHaveSingleItem().Targets.ShouldHaveSingleItem().Windows;
        windows.Count.ShouldBe(2);
        windows[0].ShouldBe(new FetchWindow(new DateTimeOffset(2026, 10, 9, 0, 0, 0, Ist), new DateTimeOffset(2026, 10, 10, 0, 0, 0, Ist)));
        windows[1].From.ShouldBe(_target.LastBar!.Value.AddDays(-2));
        var session = _bars.Sessions.ShouldHaveSingleItem();
        (session.Source, session.Completed, session.Bars.Count).ShouldBe((BarSource.Dhan, true, 175));
        (phase.GapsFound, phase.GapsFilled, phase.GapsUnfillable).ShouldBe((1, 1, 0));
        var gap = _gaps.Rows[(_target.InstrumentId, Friday)];
        (gap.Status, gap.Found, gap.Attempts).ShouldBe((GapStatus.Filled, 375, 1));
    }

    [Fact]
    public async Task A_refetch_that_adds_nothing_ends_unfillable_and_leaves_no_warning_about_open_gaps()
    {
        var (phase, job) = await RunAsync();

        (phase.Status, phase.GapsFound, phase.GapsFilled, phase.GapsUnfillable).ShouldBe((PhaseStatus.Succeeded, 1, 0, 1));
        _gaps.Rows[(_target.InstrumentId, Friday)].Status.ShouldBe(GapStatus.Unfillable);
        job.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refetch_that_adds_some_bars_leaves_the_gap_open_and_the_job_warns()
    {
        _source.Respond = t => new IntradayResult(t.InstrumentId, [.. FridayBars(200, 100)], 2);

        var (phase, job) = await RunAsync();

        (phase.GapsFound, phase.GapsFilled, phase.GapsUnfillable).ShouldBe((1, 0, 0));
        _gaps.Rows[(_target.InstrumentId, Friday)].Status.ShouldBe(GapStatus.Open);
        job.Warnings.ShouldHaveSingleItem().ShouldBe("1 sessions still have missing bars after the fetch; the next update asks for them again.");
    }

    [Fact]
    public async Task An_instrument_that_could_not_be_fetched_keeps_its_gap_open_without_an_attempt_so_the_next_run_retries()
    {
        _source.Respond = t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "timeout"));

        var (phase, _) = await RunAsync();

        phase.InstrumentsFailed.ShouldBe(1);
        (phase.GapsFound, phase.GapsFilled, phase.GapsUnfillable).ShouldBe((0, 0, 0));
        var gap = _gaps.Rows[(_target.InstrumentId, Friday)];
        (gap.Status, gap.Attempts, gap.LastAttemptAt).ShouldBe((GapStatus.Open, 0, null));
    }

    [Fact]
    public async Task Unfillable_sessions_are_left_alone_by_default_and_asked_for_again_when_the_job_re_fetches_gaps()
    {
        await RunAsync(); // Friday becomes unfillable
        _source.Plans.Clear();

        var (plain, _) = await RunAsync();
        var plainWindows = _source.Plans.Single().Targets.Single().Windows;
        _source.Plans.Clear();
        _counts.Preset[_target.InstrumentId][Friday] = 200;
        _source.Respond = t => new IntradayResult(t.InstrumentId, [.. FridayBars(200, 175)], 2);
        var (retry, _) = await RunAsync(refetchGaps: true);
        var retryWindows = _source.Plans.Single().Targets.Single().Windows;

        plainWindows.Count.ShouldBe(1);
        (plain.GapsFound, plain.GapsUnfillable).ShouldBe((0, 0));
        retryWindows.Count.ShouldBe(2);
        (retry.GapsFound, retry.GapsFilled).ShouldBe((1, 1));
        _gaps.Rows[(_target.InstrumentId, Friday)].Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Gaps_that_cannot_be_worked_out_do_not_stop_the_fetch_and_are_reported_once()
    {
        var failing = new FailingGaps();
        var job = DataJob.Start([DataSteps.Dhan], false, "owner", Now);
        _jobs.Jobs[job.Id] = job;
        var runner = new DhanPhaseRunner(
            _source, _account, _targets, _bars, _settings, _calendar, new GapBackfill(_counts, failing, _settings, _clock), _clock, NullLogger<DhanPhaseRunner>.Instance);

        var phase = await runner.RunAsync(new JobReporter(job, _jobs, _clock), Ct);

        (phase.Status, phase.InstrumentsFetched, phase.GapsFound).ShouldBe((PhaseStatus.Succeeded, 1, 0));
        _source.Plans.Single().Targets.Single().Windows.Count.ShouldBe(1);
        job.Warnings.ShouldHaveSingleItem().ShouldBe("Missing bars could not be looked for in 1 instruments; they were fetched as usual.");
    }

    [Fact]
    public async Task An_instrument_without_a_first_bar_is_fetched_as_before_and_never_examined()
    {
        _targets.Targets.Clear();
        _targets.Targets.Add(_target with { FirstBar = null, LastBar = null });

        var (phase, _) = await RunAsync();

        (phase.GapsFound, _gaps.Rows.Count, _counts.Asked.Count).ShouldBe((0, 0, 0));
    }

    private sealed class FailingGaps : IBarGaps
    {
        public ValueTask<IReadOnlyList<BarGap>> ListForAsync(Guid instrumentId, DateOnly since, CancellationToken cancellationToken) => throw new InvalidOperationException("database down");

        public ValueTask SaveAsync(IReadOnlyCollection<BarGap> gaps, CancellationToken cancellationToken) => throw new InvalidOperationException("database down");

        public ValueTask<GapSummary> GetSummaryAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("database down");

        public ValueTask<GapPage> ListAsync(GapSearch search, CancellationToken cancellationToken) => throw new InvalidOperationException("database down");
    }
}
