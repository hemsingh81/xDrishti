using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Application.Tests.Market.Quality.QualityBars;

namespace XDrishti.Application.Tests.Market.Quality;

public sealed class QualityPhaseRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero); // Monday 12:00 India time

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeQualityData _data = new();
    private readonly FakeQualityRuns _runs = new();
    private readonly FakeBarStore _bars = new();
    private readonly FakeBarGaps _gaps = new();
    private readonly FakeDataSettings _settings = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeDataJobStore _jobs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private QualityPhaseRunner Runner => new(_data, _runs, _bars, _gaps, _calendar, _settings, _clock, NullLogger<QualityPhaseRunner>.Instance);

    /// <summary>An instrument with the bars and, unless told otherwise, the aggregates exactly as the bars give them.</summary>
    private QualityTarget Instrument(string symbol, List<Bar>? bars = null, bool stored = true)
    {
        bars ??= FullWindow();
        var target = new QualityTarget(Guid.CreateVersion7(), "NSE:" + symbol, DateTimeOffset.FromUnixTimeSeconds(bars[0].EpochSeconds).AddDays(-60), DateTimeOffset.FromUnixTimeSeconds(bars[^1].EpochSeconds));
        _data.Targets.Add(target);
        _bars.Stored[target.InstrumentId] = bars;
        if (stored)
        {
            _data.Stored[target.InstrumentId] = [.. MinuteAggregator.Build(bars).Select(b => new StoredBucket(b.Key.Timeframe, b.Key.Start, b.Value.Open, b.Value.High, b.Value.Low, b.Value.Close, b.Value.Volume, b.Value.Bars))];
        }

        return target;
    }

    private async Task<(QualityPhase Phase, DataJob Job)> RunAsync(string[]? steps = null, DataJobTrigger trigger = DataJobTrigger.Manual, Guid? instrumentId = null, CancellationToken? token = null)
    {
        var job = DataJob.Start(steps ?? [DataSteps.Quality], false, "owner", Now, trigger, instrumentId: instrumentId);
        _jobs.Jobs[job.Id] = job;
        return (await Runner.RunAsync(job, new JobReporter(job, _jobs, _clock), token ?? Ct), job);
    }

    [Fact]
    public async Task Every_target_is_checked_and_the_run_records_the_window_the_counts_and_the_mean_score()
    {
        Instrument("ABB");
        Instrument("TCS", Sessions(Mon, Tue, Wed, Thu)); // stale: Friday missing
        Instrument("INFY", Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is >= 600 and < 650)).OrderBy(b => b.EpochSeconds).ToList()); // 50 minutes missing

        var (phase, job) = await RunAsync();

        (phase.Status, phase.Done, phase.Total, phase.Passed, phase.Warned, phase.Failed).ShouldBe((PhaseStatus.Succeeded, 3L, 3L, 1L, 1L, 1L));
        phase.RunId.ShouldNotBeNull();
        phase.Message.ShouldBe("1 passed, 1 with warnings, 1 failed.");
        var run = _runs.Runs[phase.RunId.Value];
        (run.Status, run.Sessions, run.Instruments, run.Passed, run.Warned, run.Failed, run.JobId, run.InstrumentId).ShouldBe((QualityRunStatus.Succeeded, 5, 3, 1, 1, 1, job.Id, null));
        run.Score.ShouldNotBeNull();
        _runs.Results.Count.ShouldBe(3);
        _runs.Results.Single(r => r.InstrumentId == _data.Targets[0].InstrumentId).Status.ShouldBe(CheckStatus.Pass);
        _runs.Results.Single(r => r.InstrumentId == _data.Targets[1].InstrumentId).Status.ShouldBe(CheckStatus.Fail);
        _runs.Results.Single(r => r.InstrumentId == _data.Targets[2].InstrumentId).Status.ShouldBe(CheckStatus.Warning);
        _runs.Results.ShouldAllBe(r => r.RunId == run.Id);
    }

    [Fact]
    public async Task A_tampered_aggregate_fails_the_instrument()
    {
        var target = Instrument("ABB");
        var stored = _data.Stored[target.InstrumentId].ToList();
        var day = stored.First(b => b.Timeframe == Timeframe.D1);
        stored[stored.IndexOf(day)] = day with { Close = day.Close + 1 };
        _data.Stored[target.InstrumentId] = stored;

        var (phase, _) = await RunAsync();

        phase.Failed.ShouldBe(1);
        var result = _runs.Results.Single();
        (result.Status, result.Checks.Aggregates.Differences).ShouldBe((CheckStatus.Fail, 1));
    }

    [Fact]
    public async Task A_day_marked_unfillable_in_the_gap_records_makes_its_missing_minutes_a_warning()
    {
        var target = Instrument("ABB", Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is 700 or 701)).OrderBy(b => b.EpochSeconds).ToList());
        var gap = BarGap.Detect(target.InstrumentId, Wed, 375, 373, Now);
        gap.RecordRefetch(373, 373, Now);
        _gaps.Rows[(target.InstrumentId, Wed)] = gap;

        await RunAsync();

        var completeness = _runs.Results.Single().Checks.Completeness;
        (completeness.Status, completeness.UnfillableMinutes).ShouldBe((CheckStatus.Warning, 2));
    }

    [Fact]
    public async Task An_open_gap_is_not_unfillable()
    {
        var target = Instrument("ABB", Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is 700 or 701)).OrderBy(b => b.EpochSeconds).ToList());
        _gaps.Rows[(target.InstrumentId, Wed)] = BarGap.Detect(target.InstrumentId, Wed, 375, 373, Now);

        await RunAsync();

        var completeness = _runs.Results.Single().Checks.Completeness;
        (completeness.Status, completeness.UnfillableMinutes).ShouldBe((CheckStatus.Pass, 0));
    }

    [Theory]
    [InlineData(DataJobTrigger.Daily, new[] { "dhan", "aggregate", "quality" }, "daily")]
    [InlineData(DataJobTrigger.Manual, new[] { "quality" }, "manual")]
    [InlineData(DataJobTrigger.Manual, new[] { "dhan", "aggregate", "quality" }, "after_job")]
    [InlineData(DataJobTrigger.Manual, new[] { "aggregate", "quality" }, "after_job")]
    public async Task The_run_is_recorded_with_the_trigger_that_the_job_implies(DataJobTrigger jobTrigger, string[] steps, string expected)
    {
        Instrument("ABB");

        var (phase, _) = await RunAsync(steps, jobTrigger);

        _runs.Runs[phase.RunId!.Value].Trigger.ToCode().ShouldBe(expected);
    }

    [Fact]
    public async Task A_job_about_one_instrument_checks_only_that_instrument_and_records_it_on_the_run()
    {
        var one = Instrument("ABB");
        Instrument("TCS");

        var (phase, _) = await RunAsync(instrumentId: one.InstrumentId);

        _data.Asked.ShouldBe([one.InstrumentId]);
        phase.Total.ShouldBe(1);
        _runs.Runs[phase.RunId!.Value].InstrumentId.ShouldBe(one.InstrumentId);
        _runs.Results.Single().InstrumentId.ShouldBe(one.InstrumentId);
    }

    [Fact]
    public async Task Without_targets_the_phase_is_skipped_and_no_run_is_recorded()
    {
        var (phase, _) = await RunAsync();

        (phase.Status, phase.RunId).ShouldBe((PhaseStatus.Skipped, null));
        phase.Message.ShouldBe("No tracked instrument has bars: nothing to check.");
        _runs.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_instrument_that_does_not_qualify_says_so()
    {
        var (phase, _) = await RunAsync(instrumentId: Guid.NewGuid());

        (phase.Status, phase.Message).ShouldBe((PhaseStatus.Skipped, "The instrument is not tracked or has no bars: nothing to check."));
    }

    [Fact]
    public async Task Another_run_in_progress_fails_the_phase_without_a_second_run()
    {
        Instrument("ABB");
        _runs.Runs[Guid.NewGuid()] = QualityRun.Start(QualityTrigger.Manual, 5, 1, null, null, Now.AddMinutes(-1));

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Code).ShouldBe((PhaseStatus.Failed, "data.job_running"));
        _runs.Results.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_instrument_whose_checks_crash_is_recorded_as_failed_and_the_others_are_still_checked()
    {
        var broken = Instrument("ABB");
        Instrument("TCS");
        _data.Failing.Add(broken.InstrumentId);

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Done, phase.Passed, phase.Failed).ShouldBe((PhaseStatus.Succeeded, 2L, 1L, 1L));
        var result = _runs.Results.Single(r => r.InstrumentId == broken.InstrumentId);
        (result.Status, result.Score).ShouldBe((CheckStatus.Fail, 0));
        result.Checks.Aggregates.Status.ShouldBe(CheckStatus.Fail);
    }

    [Fact]
    public async Task Results_are_saved_in_batches_so_progress_survives_an_interruption()
    {
        for (var i = 0; i < 45; i++)
        {
            Instrument("S" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), Sessions(Fri));
        }

        await RunAsync();

        _runs.SaveBatches.ShouldBe([20, 20, 5]);
        _runs.Published.Count.ShouldBeGreaterThanOrEqualTo(3);
        _runs.Published[^1].Status.ShouldBe(QualityRunStatus.Succeeded);
    }

    [Fact]
    public async Task Stopping_the_application_fails_the_run_keeps_what_was_saved_and_rethrows()
    {
        Instrument("ABB");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await RunAsync(token: source.Token));

        var run = _runs.Runs.Values.Single();
        (run.Status, run.Error).ShouldBe((QualityRunStatus.Failed, "The quality run was interrupted because the application is stopping."));
    }

    [Fact]
    public async Task An_unexpected_failure_fails_the_run_with_a_safe_message_and_rethrows()
    {
        Instrument("ABB");
        _runs.SaveFailure = new InvalidOperationException("secret connection detail");

        await Should.ThrowAsync<InvalidOperationException>(async () => await RunAsync());

        var run = _runs.Runs.Values.Single();
        (run.Status, run.Error).ShouldBe((QualityRunStatus.Failed, "The quality run failed unexpectedly. See the API log."));
    }

    [Fact]
    public async Task A_job_that_was_ended_elsewhere_stops_the_run_and_fails_it()
    {
        Instrument("ABB");
        Instrument("TCS");
        var job = DataJob.Start([DataSteps.Quality], false, "owner", Now);
        _jobs.Jobs[job.Id] = job;
        _jobs.EndedElsewhere.Add(job.Id);

        var phase = await Runner.RunAsync(job, new JobReporter(job, _jobs, _clock), Ct);

        phase.Status.ShouldBe(PhaseStatus.Failed);
        _runs.Runs.Values.Single().Status.ShouldBe(QualityRunStatus.Failed);
    }

    [Fact]
    public async Task The_window_is_the_configured_number_of_closed_sessions()
    {
        _settings.QualitySessions = 3;
        Instrument("ABB", Sessions(Wed, Thu, Fri));

        var (phase, _) = await RunAsync();

        _runs.Runs[phase.RunId!.Value].Sessions.ShouldBe(3);
        var result = _runs.Results.Single();
        (result.Status, result.BarsChecked, result.Checks.Completeness.ExpectedMinutes).ShouldBe((CheckStatus.Pass, 1125, 1125));
    }

    [Fact]
    public async Task The_thresholds_of_the_settings_are_used()
    {
        _settings.QualityMaxMissingPercent = 0.01;
        Instrument("ABB", Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m == 600)).OrderBy(b => b.EpochSeconds).ToList());

        await RunAsync();

        _runs.Results.Single().Checks.Completeness.Status.ShouldBe(CheckStatus.Warning);
    }

    [Fact]
    public async Task The_phase_is_published_at_its_start_and_its_end()
    {
        Instrument("ABB");

        var (_, job) = await RunAsync();

        _jobs.Published.ShouldContain(p => p.Phase == "quality");
        job.CurrentPhase.ShouldBe("quality");
    }
}
