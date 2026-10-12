using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars.Eod;

/// <summary>
/// The scheduled end-of-day run with a fake clock (Monday 2026-10-12, India time): the decision of one look at the schedule, the loop that waits in short slices, the
/// catch-up at start-up, once per day, a running job, an account that is not connected, switched off.
/// </summary>
public sealed class DataEodRunnerTests : IDisposable
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private const int ScheduledEvent = 3319;

    private const int StartedEvent = 3321;

    private static readonly string[] EodSteps = ["dhan", "aggregate", "quality"];

    private readonly SlicedClock _clock = new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, Ist)); // a test moves it forward with At(...)
    private readonly FakeDataSettings _settings = new();
    private readonly DataJobRecovery _recovery = new();
    private readonly CapturingLogger<DataEodRunner> _log = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly IDataJobChannel _queue = Substitute.For<IDataJobChannel>();
    private readonly IServiceScopeFactory _scopes;

    public DataEodRunnerTests()
    {
        _recovery.MarkCompleted(); // the cleanup of interrupted jobs is over unless a test says otherwise
        var catalog = Substitute.For<IInstrumentCatalog>();
        _scopes = Scopes(new Dictionary<Type, object>
        {
            [typeof(ITradingCalendar)] = _calendar,
            [typeof(IDataJobStore)] = _jobs,
            [typeof(IIntradayBarSource)] = _source,
            [typeof(IDataAccountStatus)] = _account,
            [typeof(ICommandHandler<StartDataJobCommand, DataJobView>)] = new StartDataJobHandler(_jobs, _queue, catalog, new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance),
        });
    }

    private static IServiceScopeFactory Scopes(Dictionary<Type, object> services)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(Arg.Any<Type>()).Returns(call => services.GetValueOrDefault(call.Arg<Type>()));
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);
        return scopes;
    }

    public void Dispose()
    {
        // nothing to release: the fakes hold no resources
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DataEodRunner Runner(DataJobRecovery? recovery = null) => new(_scopes, _settings, recovery ?? _recovery, _clock, _log);

    private void At(int hour, int minute, int day = 12) => _clock.SetUtcNow(new DateTimeOffset(2026, 10, day, hour, minute, 0, Ist));

    private DataJob AddDailyJob(DateTimeOffset startedAt, DataJobStatus finishAs = DataJobStatus.Succeeded)
    {
        var job = DataJob.Start([DataSteps.Dhan, DataSteps.Aggregate], false, DataEodRunner.Requester, startedAt, DataJobTrigger.Daily);
        if (finishAs == DataJobStatus.Failed)
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

    [Fact]
    public async Task Before_the_run_time_nothing_is_started()
    {
        At(14, 59);

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.NotDue);

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_the_run_time_it_starts_one_job_with_dhan_aggregate_and_quality_the_daily_trigger_and_the_system_requester()
    {
        At(15, 50);

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.Started);

        var job = _jobs.Started.ShouldHaveSingleItem();
        job.Steps.ShouldBe(EodSteps);
        (job.Trigger, job.RequestedBy, job.ForceCsv, job.RefetchGaps).ShouldBe((DataJobTrigger.Daily, "system:data-eod", false, false));
        _queue.Received(1).Enqueue(Arg.Is<DataJobTicket>(t => t.JobId == job.Id));
    }

    [Fact]
    public async Task It_runs_once_per_day_even_if_asked_again_and_after_midnight_it_is_a_new_day()
    {
        var runner = Runner();
        At(15, 50);
        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.Started);
        _jobs.Jobs.Values.Single().Finish(_clock.GetUtcNow());

        At(16, 30);
        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.AlreadyDone);
        At(23, 59);
        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.AlreadyDone);
        At(15, 50, day: 13);
        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.Started);

        _jobs.Started.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_manual_job_of_the_day_does_not_count_as_the_automatic_run()
    {
        At(15, 50);
        var manual = DataJob.Start(["dhan"], false, "owner", _clock.GetUtcNow().AddHours(-2));
        manual.Finish(_clock.GetUtcNow().AddHours(-1));
        _jobs.Jobs[manual.Id] = manual;

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.Started);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public async Task A_weekend_never_starts_a_run(int day)
    {
        At(16, 0, day);

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.NotDue);

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holiday_never_starts_a_run()
    {
        _calendar.Calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 12), CalendarSource.Manual, CalendarStatus.Confirmed, null, _clock.GetUtcNow())]);
        At(16, 0);

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.NotDue);

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_special_session_day_runs_after_the_session_ends_not_at_15_50()
    {
        _calendar.Calendar = new TradingCalendar([TradingDay.Special(new DateOnly(2026, 10, 12), 18 * 60 + 15, 19 * 60 + 15, CalendarSource.Manual, CalendarStatus.Confirmed, null, _clock.GetUtcNow())]);
        var runner = Runner();
        At(16, 0);
        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.NotDue);

        At(19, 15);

        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.Started);
    }

    [Fact]
    public async Task When_a_job_is_running_the_run_is_skipped_and_recorded_with_the_reason_once_for_the_day()
    {
        var running = DataJob.Start(["csv"], false, "owner", new DateTimeOffset(2026, 10, 12, 15, 40, 0, Ist));
        _jobs.Jobs[running.Id] = running;
        var runner = Runner();
        At(15, 50);

        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.Skipped);
        (await runner.RunDueAsync(Ct)).ShouldBe(EodOutcome.AlreadyDone);

        var skipped = _jobs.Skipped.ShouldHaveSingleItem();
        (skipped.Status, skipped.Trigger, skipped.Error).ShouldBe((DataJobStatus.Skipped, DataJobTrigger.Daily, "Another data update was running."));
        skipped.Steps.ShouldBe(EodSteps);
        _jobs.Started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DataAccountStatuses.None, "There is no primary Data account. Add one under Accounts.")]
    [InlineData(DataAccountStatuses.NotConnected, "Connect the Data account under Accounts.")]
    [InlineData(DataAccountStatuses.Expired, "The sign-in of the Data account has expired. Reconnect it under Accounts.")]
    public async Task When_the_data_account_cannot_fetch_the_run_is_skipped_and_recorded_with_the_accounts_reason(string status, string reason)
    {
        _account.State = new DataAccountState(Guid.NewGuid(), "Primary", status, reason);
        At(15, 50);

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.Skipped);

        _jobs.Skipped.ShouldHaveSingleItem().Error.ShouldBe(reason);
        _jobs.Started.ShouldBeEmpty();
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task When_the_dhan_fetch_is_not_available_the_run_is_skipped_with_its_reason()
    {
        _source.Availability = new SourceAvailability(false, "The Dhan Data API fetch is not available yet.");
        At(15, 50);

        (await Runner().RunDueAsync(Ct)).ShouldBe(EodOutcome.Skipped);

        _jobs.Skipped.ShouldHaveSingleItem().Error.ShouldBe("The Dhan Data API fetch is not available yet.");
    }

    [Fact]
    public async Task A_database_failure_is_reported_as_a_crash_and_nothing_is_recorded()
    {
        _jobs.LastByTriggerFailure = new InvalidOperationException("database down");
        At(15, 50);

        var outcome = await Runner().RunDueAsync(Ct);

        outcome.ShouldBe(EodOutcome.Crashed);
        _jobs.Skipped.ShouldBeEmpty();
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_switched_off_it_ends_at_once_and_starts_nothing_not_even_a_catch_up()
    {
        _settings.EodEnabled = false;
        At(17, 0);

        await Runner().RunAsync(Ct);

        _clock.Advance(TimeSpan.FromDays(3));
        _jobs.Jobs.ShouldBeEmpty();
        _clock.Waits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Started_after_the_run_time_without_a_run_today_it_updates_at_once_and_still_schedules_tomorrow()
    {
        At(17, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        var job = _jobs.Started.ShouldHaveSingleItem();
        job.Trigger.ShouldBe(DataJobTrigger.Daily);
        _log.Events.Single(e => e.Event == ScheduledEvent).Message.ShouldContain("2026-10-13 15:50");
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task The_start_up_catch_up_waits_for_the_recovery_so_a_job_stuck_from_the_previous_process_does_not_make_it_skip_the_day()
    {
        var recovery = new DataJobRecovery();
        var processStart = new DateTimeOffset(2026, 10, 12, 16, 0, 0, Ist);
        var stuck = DataJob.Start(["csv"], false, "owner", new DateTimeOffset(2026, 10, 12, 15, 0, 0, Ist)); // still "running" in the database: the previous process died
        _jobs.Jobs[stuck.Id] = stuck;
        At(17, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner(recovery).RunAsync(stop.Token);
        await _clock.RecoveryWaiting.Task;

        _jobs.Skipped.ShouldBeEmpty(); // not decided yet
        _jobs.Started.ShouldBeEmpty();
        (await _jobs.FailInterruptedAsync(processStart, _clock.GetUtcNow(), Ct)).ShouldBe(1); // what the data worker's recovery does
        recovery.MarkCompleted();
        await _log.WaitForAsync(ScheduledEvent);

        _jobs.Started.ShouldHaveSingleItem().Trigger.ShouldBe(DataJobTrigger.Daily);
        _jobs.Skipped.ShouldBeEmpty();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task When_the_recovery_never_finishes_the_catch_up_goes_on_after_the_wait_and_sees_the_job_as_running()
    {
        var stuck = DataJob.Start(["csv"], false, "owner", new DateTimeOffset(2026, 10, 12, 15, 0, 0, Ist));
        _jobs.Jobs[stuck.Id] = stuck;
        At(17, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner(new DataJobRecovery()).RunAsync(stop.Token);
        await _clock.RecoveryWaiting.Task;
        _clock.Advance(DataEodRunner.RecoveryWait);
        await _log.WaitForAsync(ScheduledEvent);

        _jobs.Skipped.ShouldHaveSingleItem().Error.ShouldBe("Another data update was running.");
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_run_of_the_day_that_appeared_while_starting_is_not_followed_by_a_skipped_row()
    {
        var running = DataJob.Start(["csv"], false, "owner", new DateTimeOffset(2026, 10, 12, 15, 40, 0, Ist));
        _jobs.Jobs[running.Id] = running;
        At(15, 50);
        _jobs.OnTryStart = () =>
        {
            _jobs.OnTryStart = null;
            AddDailyJob(new DateTimeOffset(2026, 10, 12, 15, 50, 0, Ist)); // a second instance started the run of the day first
        };

        var outcome = await Runner().RunDueAsync(Ct);

        outcome.ShouldBe(EodOutcome.AlreadyDone);
        _jobs.Skipped.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-5_000_000_000, 0)]                                    // the run time already passed: never a negative delay
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(300_000_000, 300_000_000)]                             // 30 s
    [InlineData(600_000_000, 600_000_000)]                             // exactly the longest slice
    [InlineData(600_000_001, 600_000_000)]
    [InlineData(36_000_000_000, 600_000_000)]
    public void A_slice_is_what_is_left_at_most_a_minute_and_never_negative(long remainingTicks, long expectedTicks)
    {
        DataEodRunner.Slice(TimeSpan.FromTicks(remainingTicks)).Ticks.ShouldBe(expectedTicks);
    }

    [Fact]
    public async Task A_clock_that_jumps_past_the_run_time_between_two_reads_starts_the_run_and_does_not_end_the_schedule()
    {
        At(14, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);
        _clock.JumpTo = new DateTimeOffset(2026, 10, 12, 15, 55, 0, Ist);
        _clock.JumpAtRead = 2;

        _clock.Advance(TimeSpan.FromMinutes(1)); // the first slice ends; the next read finds the clock far ahead
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _log.WaitForAsync(StartedEvent);

        task.IsFaulted.ShouldBeFalse();
        _jobs.Started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Started_after_the_run_time_when_today_already_ran_it_does_not_run_again()
    {
        At(17, 0);
        AddDailyJob(new DateTimeOffset(2026, 10, 12, 15, 50, 3, Ist));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _jobs.Started.ShouldBeEmpty();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Started_after_the_run_time_when_today_was_skipped_it_does_not_try_again()
    {
        At(17, 0);
        var skipped = DataJob.Skipped(["dhan", "aggregate"], DataJobTrigger.Daily, DataEodRunner.Requester, "not connected", new DateTimeOffset(2026, 10, 12, 15, 50, 0, Ist));
        _jobs.Jobs[skipped.Id] = skipped;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _jobs.Started.ShouldBeEmpty();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Started_before_the_run_time_it_waits_and_does_not_catch_up()
    {
        At(14, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _jobs.Started.ShouldBeEmpty();
        _log.Events.Single(e => e.Event == ScheduledEvent).Message.ShouldContain("2026-10-12 15:50");
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Started_on_a_weekend_it_does_not_catch_up_and_the_next_run_is_monday()
    {
        At(17, 0, 10); // Saturday
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _jobs.Started.ShouldBeEmpty();
        _log.Events.Single(e => e.Event == ScheduledEvent).Message.ShouldContain("2026-10-12 15:50");
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task The_wait_is_in_slices_of_at_most_a_minute_that_read_the_clock_again_and_the_job_starts_at_the_time()
    {
        At(14, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _clock.Advance(TimeSpan.FromMinutes(110)); // one jump past 15:50
        await _log.WaitForAsync(StartedEvent);

        _clock.Waits[0].ShouldBe(DataEodRunner.MaxSlice);
        _clock.Waits.ShouldAllBe(w => w <= DataEodRunner.MaxSlice && w > TimeSpan.Zero);
        _jobs.Started.ShouldHaveSingleItem().Trigger.ShouldBe(DataJobTrigger.Daily);
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task The_last_slice_ends_exactly_at_the_run_time_and_nothing_starts_a_second_earlier()
    {
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 15, 49, 30, Ist));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _clock.Waits.ShouldBe([TimeSpan.FromSeconds(30)]);
        _clock.Advance(TimeSpan.FromSeconds(29));
        _jobs.Started.ShouldBeEmpty();
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _log.WaitForAsync(StartedEvent);

        _jobs.Started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task After_the_run_the_next_one_is_the_next_trading_day_skipping_the_weekend()
    {
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 9, 15, 49, 30, Ist));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await _log.WaitForAsync(ScheduledEvent, 2);

        _log.Events.Where(e => e.Event == ScheduledEvent).Select(e => e.Message).Last().ShouldContain("2026-10-12 15:50");
        _jobs.Started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_job_that_is_running_at_the_run_time_does_not_end_the_schedule()
    {
        var running = DataJob.Start(["csv"], false, "owner", new DateTimeOffset(2026, 10, 12, 15, 0, 0, Ist));
        _jobs.Jobs[running.Id] = running;
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 15, 49, 30, Ist));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await _log.WaitForAsync(ScheduledEvent, 2);

        task.IsCompleted.ShouldBeFalse();
        _jobs.Skipped.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Stopping_the_application_ends_the_wait_quietly()
    {
        At(14, 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);
        await _log.WaitForAsync(ScheduledEvent);

        await stop.CancelAsync();
        await task;

        task.IsCompletedSuccessfully.ShouldBeTrue();
        _jobs.Jobs.ShouldBeEmpty();
    }

    /// <summary>A fake clock that remembers how long each timer was asked to wait.</summary>
    private sealed class SlicedClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _reads;

        public List<TimeSpan> Waits { get; } = [];

        /// <summary>Completes when a timer of the length of the recovery wait was created: the runner is waiting for the cleanup.</summary>
        public TaskCompletionSource RecoveryWaiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>When set, the n-th read of the clock from now on (1-based) finds it at <see cref="JumpTo"/>: it jumped between two reads.</summary>
        public int? JumpAtRead { get; set; }

        public DateTimeOffset JumpTo { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (JumpAtRead is { } at && ++_reads == at)
            {
                SetUtcNow(JumpTo);
            }

            return base.GetUtcNow();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Add(dueTime);
            if (dueTime == DataEodRunner.RecoveryWait)
            {
                RecoveryWaiting.TrySetResult();
            }

            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
