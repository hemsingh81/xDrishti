using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The first interval service (ADR 0019 §5, ADR 0018 §8): a run at 09:15, 09:20 … 15:30 India time on trading days and nothing else, a tick that falls while the service still runs is dropped (log, no run record),
/// a missed tick is never made up, and Run now works at any time. Monday 2026-10-12 is a trading day, Saturday the 10th is not.
/// </summary>
public sealed class IntervalSchedulerTests : IDisposable
{
    private const int TickDroppedEvent = 3418;

    private readonly CapturingLogger<ServiceRunner> _log = new();
    private readonly TestService _quotes = Make("holdings-quotes", Schedule.Interval(5, T(9, 15), T(15, 30)), catchUp: true, timeout: 240, allowed: [ScheduleType.Interval]);
    private readonly ServicesWorld _world;

    public IntervalSchedulerTests() =>
        _world = new ServicesWorld((_, _) => [_quotes], provider => provider.Singleton<Microsoft.Extensions.Logging.ILogger<ServiceRunner>>(_log));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task StartUpAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        _world.Application.StartedAt = instant;
        await _world.Scheduler.StartUpAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private async Task TickAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private IReadOnlyList<ServiceRun> Runs => [.. _world.Data.RunsOf("holdings-quotes")];

    /// <summary>Looks at the schedules every minute from the start to the end, like the scheduler's own wake-ups.</summary>
    private async Task LookEveryMinuteAsync(int day, TimeOnly from, TimeOnly to)
    {
        for (var at = At(day, from.Hour, from.Minute); at <= At(day, to.Hour, to.Minute); at = at.AddMinutes(1))
        {
            await TickAtAsync(at);
        }
    }

    [Fact]
    public async Task A_trading_day_runs_at_0915_every_five_minutes_and_at_1530_and_not_at_1531_or_before_0915()
    {
        await StartUpAtAsync(At(12, 8, 0));

        await LookEveryMinuteAsync(12, T(8, 0), T(18, 0));

        Runs.Count.ShouldBe(76); // 09:15 … 15:30 inclusive
        Runs[0].StartedAt.ShouldBe(At(12, 9, 15));
        Runs[1].StartedAt.ShouldBe(At(12, 9, 20));
        Runs[^1].StartedAt.ShouldBe(At(12, 15, 30));
        Runs.ShouldAllBe(r => r.Trigger == ServiceRunTrigger.Scheduled && r.Status == ServiceRunStatus.Succeeded);
        Runs.ShouldNotContain(r => r.StartedAt > At(12, 15, 30));
    }

    [Fact]
    public async Task Nothing_starts_before_0915_and_nothing_new_after_the_1530_run()
    {
        await StartUpAtAsync(At(12, 9, 0));

        await TickAtAsync(At(12, 9, 14, 59));
        Runs.ShouldBeEmpty();
        await TickAtAsync(At(12, 15, 30));
        await TickAtAsync(At(12, 15, 31));
        await TickAtAsync(At(12, 15, 35));
        await TickAtAsync(At(12, 18, 0));

        Runs.ShouldHaveSingleItem().StartedAt.ShouldBe(At(12, 15, 30));
    }

    [Theory]
    [InlineData(10)] // Saturday
    [InlineData(11)] // Sunday
    public async Task A_weekend_runs_nothing(int day)
    {
        await StartUpAtAsync(At(day, 8, 0));

        await LookEveryMinuteAsync(day, T(9, 0), T(16, 0));

        Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holiday_runs_nothing()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 12), CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        await StartUpAtAsync(At(12, 8, 0));

        await LookEveryMinuteAsync(12, T(9, 0), T(16, 0));

        Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_special_session_on_a_weekend_counts_as_a_trading_day_and_runs_the_configured_window()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Special(new DateOnly(2026, 10, 10), 9 * 60 + 15, 10 * 60 + 15, CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        await StartUpAtAsync(At(10, 8, 0));

        await TickAtAsync(At(10, 9, 15));
        await TickAtAsync(At(10, 9, 20));

        Runs.Select(r => r.StartedAt).ShouldBe([At(10, 9, 15), At(10, 9, 20)]);
    }

    [Fact]
    public async Task A_tick_that_falls_while_the_previous_run_still_runs_is_dropped_with_a_log_line_and_no_run_record()
    {
        await StartUpAtAsync(At(12, 9, 0));
        var gate = _quotes.Gate();
        await TickAtStartedAsync(At(12, 9, 15));
        await _quotes.WhenStartedAsync();

        await TickAtStartedAsync(At(12, 9, 20)); // still running
        await TickAtStartedAsync(At(12, 9, 25)); // still running

        Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);
        _quotes.Calls.Count.ShouldBe(1);
        _log.Events.Count(e => e.Event == TickDroppedEvent).ShouldBe(2);
        _log.Events.First(e => e.Event == TickDroppedEvent).Message.ShouldContain("service.tick_dropped service=holdings-quotes");

        gate.SetResult(XDrishti.Application.Abstractions.Platform.ServiceOutcome.Succeeded());
        await _world.Runner.WhenIdleAsync();
        await TickAtAsync(At(12, 9, 30));

        Runs.Select(r => r.StartedAt).ShouldBe([At(12, 9, 15), At(12, 9, 30)]);
        Runs.ShouldAllBe(r => r.Status == ServiceRunStatus.Succeeded);
    }

    /// <summary>Looks at the schedules without waiting for the runs to end (a gated run does not).</summary>
    private async Task TickAtStartedAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        await _world.Scheduler.TickAsync(Ct);
    }

    [Fact]
    public async Task A_daily_service_that_is_busy_still_records_a_skipped_run()
    {
        using var world = new ServicesWorld(Eod(catchUp: false));
        var eod = world.Services[0];
        var gate = eod.Gate();
        world.SetClock(At(12, 10, 0));
        world.Application.StartedAt = At(12, 10, 0);
        await world.Scheduler.StartUpAsync(Ct);
        world.SetClock(At(12, 15, 50));
        await world.Scheduler.TickAsync(Ct);

        var manual = await world.RunNowAsync("eod");

        manual.IsFailure.ShouldBeTrue();
        world.Data.RunsOf("eod").Count(r => r.Status == ServiceRunStatus.Skipped).ShouldBe(1);
        gate.SetResult(XDrishti.Application.Abstractions.Platform.ServiceOutcome.Succeeded());
        await world.Runner.WhenIdleAsync();
    }

    [Fact]
    public async Task A_missed_tick_is_not_made_up_even_with_catch_up_on_and_the_next_aligned_tick_runs()
    {
        await StartUpAtAsync(At(12, 9, 16));
        await TickAtAsync(At(12, 9, 17));
        _quotes.Calls.ShouldBeEmpty();

        await TickAtAsync(At(12, 10, 3)); // the machine slept: 09:20 … 10:00 passed unseen

        Runs.ShouldBeEmpty();
        await TickAtAsync(At(12, 10, 5));
        Runs.ShouldHaveSingleItem().StartedAt.ShouldBe(At(12, 10, 5));
    }

    [Fact]
    public async Task A_start_up_inside_the_window_makes_nothing_up()
    {
        await StartUpAtAsync(At(12, 11, 2));

        Runs.ShouldBeEmpty();
        await TickAtAsync(At(12, 11, 5));
        Runs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_paused_service_runs_no_ticks_and_resuming_does_not_make_them_up()
    {
        await StartUpAtAsync(At(12, 9, 0));
        (await _world.PauseAsync("holdings-quotes")).IsSuccess.ShouldBeTrue();

        await TickAtAsync(At(12, 9, 15));
        await TickAtAsync(At(12, 9, 20));
        (await _world.ResumeAsync("holdings-quotes")).IsSuccess.ShouldBeTrue();
        await TickAtAsync(At(12, 9, 21));

        Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_now_outside_the_window_starts_a_manual_run()
    {
        _world.SetClock(At(12, 20, 0));

        var started = await _world.RunNowAsync("holdings-quotes");
        await _world.Runner.WhenIdleAsync();

        started.IsSuccess.ShouldBeTrue();
        Runs.ShouldHaveSingleItem().Trigger.ShouldBe(ServiceRunTrigger.Manual);
    }
}
