using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;

namespace XDrishti.Application.Tests.Market.Bars.Eod;

/// <summary>
/// The scheduled end-of-day run as a service (ADR 0017 §2, ADR 0018), the tests of the old hosted runner pointed at the scheduler: the run time, once per day, trading days only, the special-session run time,
/// a running job or an account that cannot fetch (a skipped run with the reason), the catch-up after a restart that waits for the cleanup of interrupted jobs, a switched-off run. Monday 2026-10-12, India time.
/// </summary>
public sealed class EodServiceTests : IDisposable
{
    private static readonly string[] EodSteps = ["dhan", "aggregate", "quality"];

    private readonly FakeDataSettings _settings = new();
    private readonly DataJobRecovery _recovery = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly IDataJobChannel _queue = Substitute.For<IDataJobChannel>();
    private readonly ServicesWorld _world;
    private SettableClock _clock = null!;
    private Action<DataJob>? _jobWork;

    public EodServiceTests()
    {
        _recovery.MarkCompleted(); // the cleanup of interrupted jobs is over unless a test says otherwise
        _jobWork = job => job.Finish(_clock.GetUtcNow()); // what the data worker does with a registered job: here it succeeds at once unless a test says otherwise
        _queue.When(q => q.Enqueue(Arg.Any<DataJobTicket>())).Do(call => _jobWork?.Invoke(_jobs.Jobs[call.Arg<DataJobTicket>().JobId]));
        _world = new ServicesWorld(
            (world, scopes) =>
            {
                _clock = world.Clock;
                return [new EodService(scopes, _settings, _recovery, world.Clock, NullLogger<EodService>.Instance)];
            },
            provider => provider
                .Singleton<IDataJobStore>(_jobs)
                .Singleton<IIntradayBarSource>(_source)
                .Singleton<IDataAccountStatus>(_account)
                .Singleton<ICommandHandler<StartDataJobCommand, DataJobView>>(new StartDataJobHandler(_jobs, _queue, Substitute.For<IInstrumentCatalog>(), new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task StartUpAtAsync(int day, int hour, int minute)
    {
        _world.SetClock(At(day, hour, minute));
        _world.Application.StartedAt = At(day, hour, minute);
        await _world.Scheduler.StartUpAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private async Task TickAtAsync(int day, int hour, int minute)
    {
        _world.SetClock(At(day, hour, minute));
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private IEnumerable<ServiceRun> Runs => _world.Data.RunsOf("eod");

    private DataJob AddDailyJob(DateTimeOffset startedAt, bool failed = false)
    {
        var job = DataJob.Start(["dhan", "aggregate"], false, EodService.Requester, startedAt, DataJobTrigger.Daily);
        if (failed)
        {
            job.Fail(startedAt.AddMinutes(3), "broken");
        }
        else
        {
            job.Finish(startedAt.AddMinutes(3));
        }

        _jobs.Jobs[job.Id] = job;
        return job;
    }

    // ---- the run ----

    [Fact]
    public async Task Before_the_run_time_nothing_is_started()
    {
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 14, 59);

        _jobs.Jobs.ShouldBeEmpty();
        Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_the_run_time_it_starts_one_job_with_dhan_aggregate_and_quality_the_daily_trigger_and_the_system_requester()
    {
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        var job = _jobs.Started.ShouldHaveSingleItem();
        job.Steps.ShouldBe(EodSteps);
        (job.Trigger, job.RequestedBy, job.ForceCsv, job.RefetchGaps).ShouldBe((DataJobTrigger.Daily, "system:data-eod", false, false));
        _queue.Received(1).Enqueue(Arg.Is<DataJobTicket>(t => t.JobId == job.Id));
        var run = Runs.ShouldHaveSingleItem();
        (run.Trigger, run.Status, run.DataJobId).ShouldBe((ServiceRunTrigger.Scheduled, ServiceRunStatus.Succeeded, job.Id));
    }

    [Fact]
    public async Task It_runs_once_per_day_and_after_midnight_it_is_a_new_day()
    {
        await StartUpAtAsync(12, 10, 0);
        await TickAtAsync(12, 15, 50);

        await TickAtAsync(12, 16, 30);
        await TickAtAsync(12, 23, 59);
        await TickAtAsync(13, 15, 50);

        _jobs.Started.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_scheduled_run_is_skipped_when_an_automatic_job_exists_for_today_already()
    {
        AddDailyJob(At(12, 15, 50, 3));
        await StartUpAtAsync(12, 10, 0);

        await _world.Runner.StartAsync("eod", ServiceRunTrigger.Scheduled, "system:scheduler", Ct);
        await _world.Runner.WhenIdleAsync();

        var run = Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, EodService.AlreadyRanToday));
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_manual_job_of_the_day_does_not_count_as_the_automatic_run()
    {
        var manual = DataJob.Start(["dhan"], false, "owner", At(12, 13, 0));
        manual.Finish(At(12, 14, 0));
        _jobs.Jobs[manual.Id] = manual;
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        _jobs.Started.ShouldHaveSingleItem().Trigger.ShouldBe(DataJobTrigger.Daily);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public async Task A_weekend_never_starts_a_run(int day)
    {
        await StartUpAtAsync(day, 9, 0);

        await TickAtAsync(day, 16, 0);

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holiday_never_starts_a_run()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 12), CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        await StartUpAtAsync(12, 9, 0);

        await TickAtAsync(12, 16, 0);

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_special_session_day_runs_after_the_session_ends_not_at_15_50()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Special(new DateOnly(2026, 10, 12), 18 * 60 + 15, 19 * 60 + 15, CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        await StartUpAtAsync(12, 9, 0);
        await TickAtAsync(12, 16, 0);
        _jobs.Jobs.ShouldBeEmpty();

        await TickAtAsync(12, 19, 15);

        _jobs.Started.ShouldHaveSingleItem();
    }

    // ---- skipped runs ----

    [Fact]
    public async Task When_a_job_is_running_the_run_is_skipped_and_recorded_with_the_reason_once_for_the_day()
    {
        var running = DataJob.Start(["csv"], false, "owner", At(12, 15, 40));
        _jobs.Jobs[running.Id] = running;
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);
        await TickAtAsync(12, 15, 51);
        await TickAtAsync(12, 16, 30);

        var skipped = _jobs.Skipped.ShouldHaveSingleItem();
        (skipped.Status, skipped.Trigger, skipped.Error).ShouldBe((DataJobStatus.Skipped, DataJobTrigger.Daily, "Another data update was running."));
        skipped.Steps.ShouldBe(EodSteps);
        _jobs.Started.ShouldBeEmpty();
        var run = Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, "Another data update was running."));
    }

    [Theory]
    [InlineData(DataAccountStatuses.None, "There is no primary Data account. Add one under Accounts.")]
    [InlineData(DataAccountStatuses.NotConnected, "Connect the Data account under Accounts.")]
    [InlineData(DataAccountStatuses.Expired, "The sign-in of the Data account has expired. Reconnect it under Accounts.")]
    public async Task When_the_data_account_cannot_fetch_the_run_is_skipped_and_recorded_with_the_accounts_reason(string status, string reason)
    {
        _account.State = new DataAccountState(Guid.NewGuid(), "Primary", status, reason);
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        _jobs.Skipped.ShouldHaveSingleItem().Error.ShouldBe(reason);
        _jobs.Started.ShouldBeEmpty();
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
        var run = Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, reason));
    }

    [Fact]
    public async Task When_the_dhan_fetch_is_not_available_the_run_is_skipped_with_its_reason()
    {
        _source.Availability = new SourceAvailability(false, "The Dhan Data API fetch is not available yet.");
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        _jobs.Skipped.ShouldHaveSingleItem().Error.ShouldBe("The Dhan Data API fetch is not available yet.");
    }

    [Fact]
    public async Task A_run_of_the_day_that_appeared_while_starting_is_not_followed_by_a_skipped_job()
    {
        var running = DataJob.Start(["csv"], false, "owner", At(12, 15, 40));
        _jobs.Jobs[running.Id] = running;
        _jobs.OnTryStart = () =>
        {
            _jobs.OnTryStart = null;
            AddDailyJob(At(12, 15, 50)); // a second instance started the run of the day first
        };
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        _jobs.Skipped.ShouldBeEmpty();
        Runs.ShouldHaveSingleItem().Message.ShouldBe(EodService.AlreadyRanToday);
    }

    [Fact]
    public async Task A_database_failure_fails_the_run_and_records_nothing_else()
    {
        _jobs.LastByTriggerFailure = new InvalidOperationException("database down");
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        var run = Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "The service failed unexpectedly. See the API log."));
        _jobs.Skipped.ShouldBeEmpty();
        _jobs.Started.ShouldBeEmpty();
    }

    // ---- switched off, catch-up ----

    [Fact]
    public async Task When_switched_off_in_the_configuration_the_seeded_service_is_paused_and_starts_nothing_not_even_a_catch_up()
    {
        _settings.EodEnabled = false;

        await StartUpAtAsync(12, 17, 0);
        await TickAtAsync(13, 15, 50);

        _jobs.Jobs.ShouldBeEmpty();
        Runs.ShouldBeEmpty();
        (await _world.ViewAsync("eod")).Status.ShouldBe("paused");
    }

    [Fact]
    public async Task The_configured_time_is_the_first_time_of_the_service_and_the_edit_on_the_screen_wins_after_a_restart()
    {
        _settings.EodTimeIst = new TimeOnly(16, 5);
        await StartUpAtAsync(12, 10, 0);
        (await _world.ViewAsync("eod")).Schedule.Time.ShouldBe("16:05");

        (await _world.SaveAsync("eod", Svc.Input(Schedule.Daily(new TimeOnly(16, 30)), revision: _world.Data.Rows["eod"].Revision))).IsSuccess.ShouldBeTrue();
        _settings.EodTimeIst = new TimeOnly(9, 0);
        await StartUpAtAsync(12, 10, 5);

        (await _world.ViewAsync("eod")).Schedule.Time.ShouldBe("16:30");
    }

    [Fact]
    public async Task Started_after_the_run_time_without_a_run_today_it_updates_at_once()
    {
        await StartUpAtAsync(12, 17, 0);

        var job = _jobs.Started.ShouldHaveSingleItem();
        job.Trigger.ShouldBe(DataJobTrigger.Daily);
        Runs.ShouldHaveSingleItem().Trigger.ShouldBe(ServiceRunTrigger.CatchUp);
        (await _world.ViewAsync("eod")).NextRun.ShouldBe(At(13, 15, 50)); // and the schedule goes on
    }

    [Fact]
    public async Task Started_after_the_run_time_when_today_already_ran_or_was_skipped_it_does_not_run_again()
    {
        AddDailyJob(At(12, 15, 50, 3));
        await StartUpAtAsync(12, 17, 0);
        _jobs.Started.ShouldBeEmpty();

        var skipped = DataJob.Skipped(["dhan", "aggregate"], DataJobTrigger.Daily, EodService.Requester, "not connected", At(13, 15, 50));
        _jobs.Jobs[skipped.Id] = skipped;
        _world.Data.Runs.Add(ServiceRun.Skipped("eod", ServiceRunTrigger.Scheduled, "not connected", At(13, 15, 50)));
        await StartUpAtAsync(13, 17, 0);
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_start_up_catch_up_waits_for_the_cleanup_so_a_job_stuck_from_the_previous_process_does_not_make_it_skip_the_day()
    {
        var recovery = new DataJobRecovery();
        using var world = new ServicesWorld(
            (w, scopes) =>
            {
                _clock = w.Clock;
                return [new EodService(scopes, _settings, recovery, w.Clock, NullLogger<EodService>.Instance)];
            },
            provider => provider
                .Singleton<IDataJobStore>(_jobs)
                .Singleton<IIntradayBarSource>(_source)
                .Singleton<IDataAccountStatus>(_account)
                .Singleton<ICommandHandler<StartDataJobCommand, DataJobView>>(new StartDataJobHandler(_jobs, _queue, Substitute.For<IInstrumentCatalog>(), new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance)));
        var stuck = DataJob.Start(["csv"], false, "owner", At(12, 15, 0)); // still "running" in the database: the previous process died
        _jobs.Jobs[stuck.Id] = stuck;
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 16, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.Clock.WhenWaitingForAsync(EodService.RecoveryWait);

        _jobs.Skipped.ShouldBeEmpty(); // not decided yet
        _jobs.Started.ShouldBeEmpty();
        (await _jobs.FailInterruptedAsync(At(12, 16, 0), world.Clock.GetUtcNow(), Ct)).ShouldBe(1); // what the data worker's recovery does
        recovery.MarkCompleted();
        await world.Runner.WhenIdleAsync();

        _jobs.Started.ShouldHaveSingleItem().Trigger.ShouldBe(DataJobTrigger.Daily);
        _jobs.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_cleanup_never_finishes_the_catch_up_goes_on_after_the_wait_and_sees_the_job_as_running()
    {
        using var world = new ServicesWorld(
            (w, scopes) =>
            {
                _clock = w.Clock;
                return [new EodService(scopes, _settings, new DataJobRecovery(), w.Clock, NullLogger<EodService>.Instance)];
            },
            provider => provider
                .Singleton<IDataJobStore>(_jobs)
                .Singleton<IIntradayBarSource>(_source)
                .Singleton<IDataAccountStatus>(_account)
                .Singleton<ICommandHandler<StartDataJobCommand, DataJobView>>(new StartDataJobHandler(_jobs, _queue, Substitute.For<IInstrumentCatalog>(), new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance)));
        var stuck = DataJob.Start(["csv"], false, "owner", At(12, 15, 0));
        _jobs.Jobs[stuck.Id] = stuck;
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 16, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.Clock.WhenWaitingForAsync(EodService.RecoveryWait);
        world.Clock.Advance(EodService.RecoveryWait);
        await world.Runner.WhenIdleAsync();

        _jobs.Skipped.ShouldHaveSingleItem().Error.ShouldBe("Another data update was running.");
    }

    // ---- the job's outcome ----

    [Fact]
    public async Task A_data_job_that_fails_fails_the_run_with_the_jobs_error()
    {
        _jobWork = job => job.Fail(_clock.GetUtcNow(), "Dhan went away");
        await StartUpAtAsync(12, 10, 0);

        await TickAtAsync(12, 15, 50);

        var run = Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "Dhan went away"));
    }

    [Fact]
    public async Task The_run_stays_running_while_the_data_job_runs_and_ends_with_it()
    {
        _jobWork = null;
        await StartUpAtAsync(12, 10, 0);
        _world.SetClock(At(12, 15, 50));
        await _world.Scheduler.TickAsync(Ct);
        await _world.Clock.WhenWaitingForAsync(JobPolling.FirstWait);

        var job = _jobs.Started.ShouldHaveSingleItem();
        Runs.Single().IsRunning.ShouldBeTrue();
        (await _world.ViewAsync("eod")).Status.ShouldBe("running");
        job.Finish(_world.Clock.GetUtcNow());
        _world.Clock.Advance(JobPolling.FirstWait);
        await _world.Runner.WhenIdleAsync();

        Runs.Single().Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    // ---- run now, retries ----

    [Fact]
    public async Task Run_now_starts_a_manual_data_job_for_the_owner_even_when_a_job_ran_today_already()
    {
        AddDailyJob(At(12, 15, 50, 3));
        _world.SetClock(At(12, 16, 30));

        var run = await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();

        run.IsSuccess.ShouldBeTrue();
        var job = _jobs.Started.ShouldHaveSingleItem();
        (job.Trigger, job.RequestedBy).ShouldBe((DataJobTrigger.Manual, "owner@example.invalid"));
        Runs.Single().Trigger.ShouldBe(ServiceRunTrigger.Manual);
    }

    [Fact]
    public async Task Run_now_with_the_data_account_not_connected_is_a_skipped_run_and_no_skipped_job_that_would_block_the_days_run()
    {
        _account.State = new DataAccountState(null, null, DataAccountStatuses.None, "No primary Data account.");
        _world.SetClock(At(12, 16, 30));

        await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();

        Runs.Single().Status.ShouldBe(ServiceRunStatus.Skipped);
        _jobs.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_retry_after_a_failed_job_is_not_stopped_by_the_failed_job_of_the_day()
    {
        _world.Data.Store("eod", Svc.Settings(Schedule.Daily(new TimeOnly(15, 50)), retries: 1, timeout: 240));
        var attempts = 0;
        _jobWork = job =>
        {
            if (++attempts == 1)
            {
                job.Fail(_clock.GetUtcNow(), "Dhan went away");
            }
            else
            {
                job.Finish(_clock.GetUtcNow());
            }
        };
        await StartUpAtAsync(12, 10, 0);
        await TickAtAsync(12, 15, 50);

        await TickAtAsync(12, 15, 51); // the retry is due after 30 seconds

        _jobs.Started.Count.ShouldBe(2);
        Runs.Select(r => (r.Attempt, r.Status)).ShouldBe([(1, ServiceRunStatus.Failed), (2, ServiceRunStatus.Succeeded)]);
    }

    // ---- timeout ----

    private async Task TimeOutTheRunAsync(int retries)
    {
        _world.Data.Store("eod", Svc.Settings(Schedule.Daily(new TimeOnly(15, 50)), retries: retries, timeout: 60));
        _jobWork = null; // the data job stays running
        await StartUpAtAsync(12, 10, 0);
        _world.SetClock(At(12, 15, 50));
        await _world.Scheduler.TickAsync(Ct);
        await _world.Clock.WhenWaitingForAsync(JobPolling.FirstWait);

        _world.Clock.Advance(TimeSpan.FromMinutes(60));
        await _world.Runner.WhenIdleAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task A_run_that_times_out_while_its_data_job_goes_on_says_so_and_is_never_retried(int retries)
    {
        await TimeOutTheRunAsync(retries);

        var run = Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "Timed out after 60 min (the job is still running)."));
        _world.Runner.NextRetryAt.ShouldBeNull();
        _jobs.Jobs.Values.Single().IsRunning.ShouldBeTrue(); // the job has no cancel: it goes on in the data worker
        _jobs.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_later_attempt_finding_the_runs_own_job_still_running_writes_no_skipped_job_that_would_block_or_hide_it()
    {
        await TimeOutTheRunAsync(retries: 2);
        _world.SetClock(At(12, 17, 0));

        _world.Runner.ResumeRetry("eod", ServiceRunTrigger.Scheduled, "system:retry", 2, At(12, 17, 0)); // an attempt 2 (it skips the once-a-day rule)
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();

        var run = Runs.Last();
        (run.Attempt, run.Status, run.Message).ShouldBe((2, ServiceRunStatus.Skipped, EodService.OwnJobRunning));
        _jobs.Skipped.ShouldBeEmpty();
        _jobs.Started.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_manual_run_while_the_timed_out_job_still_runs_is_skipped_and_writes_no_job()
    {
        await TimeOutTheRunAsync(retries: 0);
        _world.SetClock(At(12, 17, 0));

        await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();

        Runs.Last().Status.ShouldBe(ServiceRunStatus.Skipped);
        _jobs.Skipped.ShouldBeEmpty();
        _jobs.Started.ShouldHaveSingleItem();
    }
}
