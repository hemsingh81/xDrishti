using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;

namespace XDrishti.Application.Tests.Market;

/// <summary>
/// The daily refresh of the instrument master as a service (ADR 0010, ADR 0018), the tests of the old hosted runner pointed at the scheduler: the time of day, the same command as the endpoint, catch-up
/// after downtime of any length, switched off, a refresh that is running already or a start that fails does not end the schedule. Sunday 2026-10-11, India time (the clock of the old tests: 07:30).
/// </summary>
public sealed class InstrumentMasterServiceTests : IDisposable
{
    private readonly FakeRefreshSettings _settings = new();
    private readonly InMemoryMarketData _data = new();
    private readonly List<StartRefreshCommand> _started = [];
    private readonly ServicesWorld _world;
    private SettableClock _clock = null!;
    private Func<StartRefreshCommand, Result<RefreshStarted>>? _answer;
    private Action<RefreshRun>? _work;

    public InstrumentMasterServiceTests()
    {
        _work = run => run.MarkUnchanged(_clock.GetUtcNow(), SourceStamp.None, SourceStamp.None, 10);
        _world = new ServicesWorld(
            (world, scopes) =>
            {
                _clock = world.Clock;
                return [new InstrumentMasterService(scopes, _settings, world.Clock)];
            },
            provider => provider
                .Singleton<IRefreshRunStore>(_data)
                .Singleton<ICommandHandler<StartRefreshCommand, RefreshStarted>>(new FakeStartHandler(this)));
        _world.SetClock(At(11, 7, 30));
        _world.Application.StartedAt = At(11, 7, 30);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private IEnumerable<ServiceRun> Runs => _world.Data.RunsOf("instrument-master");

    private async Task StartUpAsync()
    {
        _world.Application.StartedAt = _world.Clock.GetUtcNow();
        await _world.Scheduler.StartUpAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private async Task TickAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private void RanAt(DateTimeOffset startedAt, ServiceRunStatus status = ServiceRunStatus.Succeeded)
    {
        var run = ServiceRun.Start("instrument-master", ServiceRunTrigger.Scheduled, 1, startedAt);
        if (status == ServiceRunStatus.Failed)
        {
            run.Fail(startedAt.AddSeconds(5), "earlier");
        }
        else
        {
            run.Succeed(startedAt.AddSeconds(5));
        }

        _world.Data.Runs.Add(run);
    }

    [Fact]
    public async Task It_starts_nothing_before_the_time()
    {
        RanAt(At(11, 6, 30)); // after yesterday's 08:45: nothing is missed
        await StartUpAsync();

        await TickAtAsync(At(11, 8, 44));

        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_starts_one_daily_refresh_at_the_time_with_the_same_command_as_the_endpoint()
    {
        RanAt(At(11, 6, 30));
        await StartUpAsync();

        await TickAtAsync(At(11, 8, 45));

        var command = _started.ShouldHaveSingleItem();
        (command.Trigger, command.Force, command.RequestedBy).ShouldBe((RefreshTrigger.Daily, false, InstrumentMasterService.Requester));
        Runs.Last().Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    [Fact]
    public async Task After_a_run_the_next_one_is_the_next_day()
    {
        RanAt(At(11, 6, 30));
        await StartUpAsync();
        await TickAtAsync(At(11, 8, 45));

        (await _world.ViewAsync("instrument-master")).NextRun.ShouldBe(At(12, 8, 45));
    }

    [Fact]
    public async Task Run_now_starts_a_manual_refresh_for_the_owner()
    {
        await _world.RunNowAsync("instrument-master");
        await _world.Runner.WhenIdleAsync();

        var command = _started.ShouldHaveSingleItem();
        (command.Trigger, command.Force, command.RequestedBy).ShouldBe((RefreshTrigger.Manual, false, "owner@example.invalid"));
    }

    [Fact]
    public async Task A_refresh_that_is_already_running_is_a_skipped_run_and_does_not_end_the_schedule()
    {
        RanAt(At(11, 6, 30));
        _answer = _ => Result.Failure<RefreshStarted>(MarketErrors.RefreshRunning);
        await StartUpAsync();

        await TickAtAsync(At(11, 8, 45));

        var run = Runs.Last();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, InstrumentMasterService.AnotherRefreshRunning));
        (await _world.ViewAsync("instrument-master")).NextRun.ShouldBe(At(12, 8, 45));
    }

    [Fact]
    public async Task A_handler_that_throws_fails_the_run_and_does_not_end_the_schedule()
    {
        RanAt(At(11, 6, 30));
        _answer = _ => throw new InvalidOperationException("database down");
        await StartUpAsync();

        await TickAtAsync(At(11, 8, 45));

        Runs.Last().Status.ShouldBe(ServiceRunStatus.Failed);
        (await _world.ViewAsync("instrument-master")).NextRun.ShouldBe(At(12, 8, 45));
    }

    [Fact]
    public async Task The_run_ends_as_the_refresh_does()
    {
        RanAt(At(11, 6, 30));
        await StartUpAsync();
        _work = run => run.Fail(_clock.GetUtcNow(), "Dhan's file could not be read");

        await TickAtAsync(At(11, 8, 45));

        var failed = Runs.Last();
        (failed.Status, failed.Message).ShouldBe((ServiceRunStatus.Failed, "Dhan's file could not be read"));
    }

    [Fact]
    public async Task An_unchanged_file_is_a_successful_run_with_a_note()
    {
        RanAt(At(11, 6, 30));
        await StartUpAsync();

        await TickAtAsync(At(11, 8, 45));

        var run = Runs.Last();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "The source files had not changed."));
    }

    // ---- switched off and catch-up ----

    [Fact]
    public async Task When_switched_off_in_the_configuration_nothing_starts_not_even_a_catch_up()
    {
        _settings.AutoRefreshEnabled = false;

        await StartUpAsync();
        await TickAtAsync(At(14, 8, 45));

        _started.ShouldBeEmpty();
        (await _world.ViewAsync("instrument-master")).Status.ShouldBe("paused");
    }

    [Fact]
    public async Task The_configured_time_seeds_the_service_once()
    {
        _settings.AutoRefreshTimeIst = new TimeOnly(7, 5);
        RanAt(At(11, 6, 0));
        await StartUpAsync();
        _settings.AutoRefreshTimeIst = new TimeOnly(9, 0);
        await StartUpAsync();

        (await _world.ViewAsync("instrument-master")).Schedule.Time.ShouldBe("07:05");
    }

    [Fact]
    public async Task Started_after_the_time_when_that_days_refresh_was_missed_it_refreshes_at_once()
    {
        _world.SetClock(At(11, 8, 46));
        RanAt(At(10, 8, 45, 2)); // yesterday's run; today's 08:45 was missed

        await StartUpAsync();

        _started.ShouldHaveSingleItem().Trigger.ShouldBe(RefreshTrigger.Daily);
        Runs.Last().Trigger.ShouldBe(ServiceRunTrigger.CatchUp);
    }

    [Fact]
    public async Task Started_just_after_the_time_with_that_days_refresh_done_it_waits_for_tomorrow()
    {
        _world.SetClock(At(11, 8, 46));
        RanAt(At(11, 8, 45, 2));

        await StartUpAsync();

        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Started_before_the_time_after_a_day_of_downtime_it_refreshes_at_once()
    {
        RanAt(At(9, 8, 45, 2)); // two days ago; yesterday's 08:45 was missed

        await StartUpAsync();

        _started.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_list_that_was_never_refreshed_is_refreshed_at_start()
    {
        await StartUpAsync();

        _started.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(ServiceRunStatus.Succeeded)]
    [InlineData(ServiceRunStatus.Failed)]
    public async Task A_run_that_started_since_the_scheduled_time_is_not_repeated_even_if_it_failed(ServiceRunStatus status)
    {
        RanAt(At(10, 8, 45), status); // exactly at yesterday's scheduled time

        await StartUpAsync();

        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refresh_that_is_running_is_not_started_again_by_the_catch_up()
    {
        _answer = _ => Result.Failure<RefreshStarted>(MarketErrors.RefreshRunning);

        await StartUpAsync();

        _started.ShouldHaveSingleItem(); // asked once; the answer was "already running"
        Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
    }

    [Fact]
    public void The_defaults_are_a_daily_refresh_at_08_45_every_day_that_makes_up_for_any_missed_run()
    {
        var definition = InstrumentMasterService.Definition;

        (definition.Defaults.Schedule.StartTime, definition.Defaults.Schedule.TradingDaysOnly, definition.Defaults.Schedule.DaySet).ShouldBe((new TimeOnly(8, 45), false, Weekdays.EveryDay));
        definition.Constraints.CatchUpReach.ShouldBe(CatchUpReach.Latest);
    }

    private sealed class FakeStartHandler(InstrumentMasterServiceTests test) : ICommandHandler<StartRefreshCommand, RefreshStarted>
    {
        public ValueTask<Result<RefreshStarted>> HandleAsync(StartRefreshCommand command, CancellationToken cancellationToken)
        {
            test._started.Add(command);
            if (test._answer is { } answer)
            {
                return ValueTask.FromResult(answer(command));
            }

            var run = RefreshRun.Start(command.Trigger, command.Force, command.RequestedBy, test._clock.GetUtcNow());
            test._data.Runs.Add(run);
            test._work?.Invoke(run);
            return ValueTask.FromResult(Result.Success(new RefreshStarted(run.Id)));
        }
    }

    // ---- timeout ----

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task A_run_that_times_out_while_its_refresh_goes_on_says_so_and_is_never_retried(int retries)
    {
        RanAt(At(11, 6, 30));
        _world.Data.Store("instrument-master", XDrishti.Application.Tests.Platform.Services.Svc.Settings(Schedule.Daily(new TimeOnly(8, 45), false, Weekdays.EveryDay), retries: retries, timeout: 30));
        _work = null; // the refresh stays running
        await StartUpAsync();
        _world.SetClock(At(11, 8, 45));
        await _world.Scheduler.TickAsync(Ct);
        await _world.Clock.WhenWaitingForAsync(JobPolling.FirstWait);

        _world.Clock.Advance(TimeSpan.FromMinutes(30));
        await _world.Runner.WhenIdleAsync();

        var run = Runs.Last();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "Timed out after 30 min (the job is still running)."));
        _world.Runner.NextRetryAt.ShouldBeNull();
        _data.Runs.Single(r => r.IsRunning).ShouldNotBeNull(); // the refresh has no cancel: it goes on in the refresh worker
    }
}
