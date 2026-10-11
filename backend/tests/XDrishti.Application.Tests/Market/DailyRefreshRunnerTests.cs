using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Refresh;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

/// <summary>
/// The daily schedule driven by a fake clock: switch off, time of day (waited for in short slices), catch-up after downtime, the same command as the
/// endpoint, a busy or failing start does not end the loop.
/// </summary>
public sealed class DailyRefreshRunnerTests : IDisposable
{
    private const int ScheduledEvent = 3008;

    private const int CatchUpEvent = 3012;

    private readonly SlicedClock _clock = new(new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero)); // 07:30 IST

    private readonly FakeRefreshSettings _settings = new();

    private readonly CapturingLogger<DailyRefreshRunner> _log = new();

    private readonly InMemoryMarketData _data = new();

    private readonly List<StartRefreshCommand> _started = [];

    private readonly SemaphoreSlim _startedSignal = new(0);

    private Func<StartRefreshCommand, Result<RefreshStarted>> _answer = _ => Result.Success(new RefreshStarted(Guid.NewGuid()));

    public DailyRefreshRunnerTests() => FinishedAt(_clock.GetUtcNow().AddHours(-1)); // 06:30 IST today: after yesterday's 08:45, nothing is missed

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _startedSignal.Dispose();

    private void FinishedAt(DateTimeOffset startedAt)
    {
        _data.Runs.Clear();
        var run = RefreshRun.Start(RefreshTrigger.Daily, false, "x", startedAt);
        run.Fail(startedAt.AddSeconds(5), "earlier");
        _data.Runs.Add(run);
    }

    private DailyRefreshRunner Runner()
    {
        var handler = new FakeStartHandler(this);
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<StartRefreshCommand, RefreshStarted>)).Returns(handler);
        provider.GetService(typeof(IRefreshRunStore)).Returns(_data);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);
        return new DailyRefreshRunner(scopes, _settings, _clock, _log);
    }

    [Fact]
    public async Task When_switched_off_it_ends_at_once_and_starts_nothing_not_even_a_catch_up()
    {
        _settings.AutoRefreshEnabled = false;
        _data.Runs.Clear();

        await Runner().RunAsync(Ct);

        _clock.Advance(TimeSpan.FromDays(3));
        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_starts_nothing_before_the_time()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token); // the first timer exists when this returns

        _clock.Advance(TimeSpan.FromMinutes(74)); // 08:44 IST

        _started.ShouldBeEmpty();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task It_starts_one_daily_refresh_at_the_time_with_the_same_command_as_the_endpoint()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _clock.Advance(TimeSpan.FromMinutes(75)); // 08:45 IST
        await _startedSignal.WaitAsync(Ct);

        var command = _started.ShouldHaveSingleItem();
        (command.Trigger, command.Force, command.RequestedBy).ShouldBe((RefreshTrigger.Daily, false, DailyRefreshRunner.Requester));
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task The_wait_is_in_slices_of_at_most_a_minute_that_read_the_clock_again()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _clock.Advance(TimeSpan.FromMinutes(75)); // one jump past 08:45 IST: the slice that ends ends the wait
        await _startedSignal.WaitAsync(Ct);

        _clock.Waits[0].ShouldBe(DailyRefreshRunner.MaxSlice);
        _clock.Waits.ShouldAllBe(w => w <= DailyRefreshRunner.MaxSlice && w > TimeSpan.Zero);
        _started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task The_last_slice_ends_exactly_at_the_scheduled_time()
    {
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 11, 3, 14, 30, TimeSpan.Zero)); // 08:44:30 IST, nothing missed
        FinishedAt(_clock.GetUtcNow().AddHours(-1));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _clock.Waits.ShouldBe([TimeSpan.FromSeconds(30)]);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await _startedSignal.WaitAsync(Ct);

        _started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task After_a_run_the_next_one_is_scheduled_for_the_next_day()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _clock.Advance(TimeSpan.FromMinutes(75));
        await _log.WaitForAsync(ScheduledEvent, 2);

        _log.Events.Count(e => e.Event == ScheduledEvent).ShouldBe(2);
        _log.Events[0].Message.ShouldContain("2026-10-11");
        _log.Events.Last(e => e.Event == ScheduledEvent).Message.ShouldContain("2026-10-12");
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_refresh_that_is_already_running_does_not_end_the_schedule()
    {
        _answer = _ => Result.Failure<RefreshStarted>(MarketErrors.RefreshRunning);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _clock.Advance(TimeSpan.FromMinutes(75));
        await _log.WaitForAsync(ScheduledEvent, 2);

        task.IsCompleted.ShouldBeFalse();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_end_the_schedule()
    {
        _answer = _ => throw new InvalidOperationException("database down");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _clock.Advance(TimeSpan.FromMinutes(75));
        await _log.WaitForAsync(ScheduledEvent, 2);

        task.IsCompleted.ShouldBeFalse();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Stopping_the_application_ends_the_wait_quietly()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        await stop.CancelAsync();
        await task;

        task.IsCompletedSuccessfully.ShouldBeTrue();
        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Started_just_after_the_time_with_that_days_refresh_done_it_waits_for_tomorrow()
    {
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 11, 3, 16, 0, TimeSpan.Zero)); // 08:46 IST
        FinishedAt(new DateTimeOffset(2026, 10, 11, 3, 15, 2, TimeSpan.Zero)); // the 08:45 run, a little late
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _log.Events.Single(e => e.Event == ScheduledEvent).Message.ShouldContain("2026-10-12");
        _started.ShouldBeEmpty();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Started_after_the_time_when_that_days_refresh_was_missed_it_refreshes_at_once()
    {
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 11, 3, 16, 0, TimeSpan.Zero)); // 08:46 IST
        FinishedAt(new DateTimeOffset(2026, 10, 10, 3, 15, 2, TimeSpan.Zero)); // yesterday's run; today's 08:45 was missed
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        var command = _started.ShouldHaveSingleItem();
        (command.Trigger, command.Force, command.RequestedBy).ShouldBe((RefreshTrigger.Daily, false, DailyRefreshRunner.Requester));
        _log.Events.ShouldContain(e => e.Event == CatchUpEvent);
        _log.Events.Single(e => e.Event == ScheduledEvent).Message.ShouldContain("2026-10-12"); // and the schedule goes on
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Started_before_the_time_after_a_day_of_downtime_it_refreshes_at_once()
    {
        FinishedAt(new DateTimeOffset(2026, 10, 9, 3, 15, 2, TimeSpan.Zero)); // two days ago; yesterday's 08:45 was missed
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_list_that_was_never_refreshed_is_refreshed_at_start()
    {
        _data.Runs.Clear();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _started.ShouldHaveSingleItem();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_refresh_that_ran_since_the_scheduled_time_is_not_repeated_even_if_it_failed_or_ran_manually()
    {
        FinishedAt(new DateTimeOffset(2026, 10, 10, 3, 15, 0, TimeSpan.Zero)); // exactly at yesterday's scheduled time
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _started.ShouldBeEmpty();
        await stop.CancelAsync();
        await task;
    }

    [Fact]
    public async Task A_refresh_that_is_running_is_not_started_again_by_the_catch_up()
    {
        _data.Runs.Clear();
        _answer = _ => Result.Failure<RefreshStarted>(MarketErrors.RefreshRunning);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = Runner().RunAsync(stop.Token);

        _started.ShouldHaveSingleItem(); // asked once; the answer was "already running"
        task.IsCompleted.ShouldBeFalse();
        await stop.CancelAsync();
        await task;
    }

    private sealed class FakeStartHandler(DailyRefreshRunnerTests test) : ICommandHandler<StartRefreshCommand, RefreshStarted>
    {
        public ValueTask<Result<RefreshStarted>> HandleAsync(StartRefreshCommand command, CancellationToken cancellationToken)
        {
            test._started.Add(command);
            test._startedSignal.Release();
            return ValueTask.FromResult(test._answer(command));
        }
    }

    /// <summary>A fake clock that remembers how long each timer was asked to wait.</summary>
    private sealed class SlicedClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        public List<TimeSpan> Waits { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Add(dueTime);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
