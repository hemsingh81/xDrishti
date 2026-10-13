using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The scheduler with a fake clock and the rows in memory (ADR 0018 §3, §4): a start at the scheduled time and once per day, trading days only, an edit without a restart, catch-up after a restart,
/// the recovery of interrupted runs, the first-start settings, pause and run-after. The clock starts on Monday 2026-10-12, 10:00 India time.
/// </summary>
public sealed class ServiceSchedulerTests : IDisposable
{
    private static readonly string[] Runs = ["scheduled"];

    private readonly TestService _eod = Eod();

    private readonly ServicesWorld _world;

    public ServiceSchedulerTests() => _world = new ServicesWorld(_eod);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task TickAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private async Task StartUpAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        _world.Application.StartedAt = instant;
        await _world.Scheduler.StartUpAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private IEnumerable<string> Triggers(string id = "eod") => _world.Data.RunsOf(id).Select(r => r.Trigger.ToCode());

    // ---- when it runs ----

    [Fact]
    public async Task Nothing_starts_before_the_scheduled_time_and_one_run_starts_at_it()
    {
        await StartUpAtAsync(At(12, 10, 0));

        await TickAtAsync(At(12, 15, 49));
        _world.Data.Runs.ShouldBeEmpty();

        await TickAtAsync(At(12, 15, 50));

        var run = _world.Data.Runs.ShouldHaveSingleItem();
        (run.ServiceId, run.Trigger, run.Attempt, run.Status).ShouldBe(("eod", ServiceRunTrigger.Scheduled, 1, ServiceRunStatus.Succeeded));
        Triggers().ShouldBe(Runs);
    }

    [Fact]
    public async Task It_runs_once_per_day_however_often_it_looks_and_again_the_next_trading_day()
    {
        await StartUpAtAsync(At(12, 10, 0));
        await TickAtAsync(At(12, 15, 50));

        await TickAtAsync(At(12, 15, 51));
        await TickAtAsync(At(12, 16, 30));
        await TickAtAsync(At(12, 23, 59));
        _eod.Calls.Count.ShouldBe(1);

        await TickAtAsync(At(13, 15, 50));
        _eod.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_time_that_passed_unseen_the_same_day_is_made_up_for_once_by_the_catch_up_rules()
    {
        await StartUpAtAsync(At(12, 10, 0));

        await TickAtAsync(At(12, 17, 45)); // the machine slept through 15:50

        var run = _world.Data.Runs.ShouldHaveSingleItem();
        run.Trigger.ShouldBe(ServiceRunTrigger.CatchUp);
        await TickAtAsync(At(12, 17, 46));
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_time_that_passed_unseen_on_an_earlier_day_or_with_catch_up_off_is_not_made_up_for()
    {
        await StartUpAtAsync(At(12, 10, 0));
        await TickAtAsync(At(15, 9, 0)); // slept from Monday morning to Thursday: Tuesday's and Wednesday's runs are gone
        _eod.Calls.ShouldBeEmpty();

        using var world = new ServicesWorld(Eod(catchUp: false));
        world.SetClock(At(12, 10, 0));
        world.Application.StartedAt = At(12, 10, 0);
        await world.Scheduler.StartUpAsync(Ct);
        world.SetClock(At(12, 17, 45));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();
        world.Data.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_most_one_catch_up_per_service_per_start_counts_the_ones_found_by_a_look_too()
    {
        await StartUpAtAsync(At(12, 10, 0));

        await TickAtAsync(At(12, 17, 45)); // slept through Monday's time: made up for once
        await TickAtAsync(At(13, 17, 45)); // slept through Tuesday's time as well: not again in the same start

        _world.Data.RunsOf("eod").Select(r => r.Trigger).ShouldBe([ServiceRunTrigger.CatchUp]);
        await StartUpAtAsync(At(13, 17, 50)); // a new start makes up for it
        _world.Data.RunsOf("eod").Select(r => r.Trigger).ShouldBe([ServiceRunTrigger.CatchUp, ServiceRunTrigger.CatchUp]);
    }

    [Fact]
    public async Task A_time_found_less_than_two_minutes_late_is_an_ordinary_scheduled_start()
    {
        await StartUpAtAsync(At(12, 10, 0));

        await TickAtAsync(At(12, 15, 51, 59));

        _world.Data.Runs.ShouldHaveSingleItem().Trigger.ShouldBe(ServiceRunTrigger.Scheduled);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public async Task A_weekend_starts_nothing(int day)
    {
        await StartUpAtAsync(At(day, 9, 0));

        await TickAtAsync(At(day, 15, 49));
        await TickAtAsync(At(day, 15, 50));
        await TickAtAsync(At(day, 18, 0));

        _world.Data.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holiday_starts_nothing()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 12), CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        await StartUpAtAsync(At(12, 9, 0));

        await TickAtAsync(At(12, 15, 50));
        await TickAtAsync(At(12, 17, 0));

        _world.Data.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_special_evening_session_moves_the_run_to_its_end()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Special(new DateOnly(2026, 10, 12), 18 * 60 + 15, 19 * 60 + 15, CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        await StartUpAtAsync(At(12, 9, 0));

        await TickAtAsync(At(12, 15, 50));
        _world.Data.Runs.ShouldBeEmpty();
        await TickAtAsync(At(12, 19, 14));
        _world.Data.Runs.ShouldBeEmpty();
        await TickAtAsync(At(12, 19, 15));

        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_next_look_is_at_the_next_start_but_at_most_a_minute_ahead()
    {
        await StartUpAtAsync(At(12, 10, 0));

        _world.SetClock(At(12, 12, 0));
        (await _world.Scheduler.TickAsync(Ct)).ShouldBe(At(12, 12, 1));

        _world.SetClock(At(12, 15, 49, 30));
        (await _world.Scheduler.TickAsync(Ct)).ShouldBe(At(12, 15, 50));
    }

    [Theory]
    [InlineData(-5_000_000_000, 0)]
    [InlineData(0, 0)]
    [InlineData(300_000_000, 300_000_000)]
    [InlineData(600_000_000, 600_000_000)]
    [InlineData(36_000_000_000, 600_000_000)]
    public void A_wait_is_what_is_left_at_most_a_minute_and_never_negative(long remainingTicks, long expectedTicks)
    {
        ServiceScheduler.Slice(TimeSpan.FromTicks(remainingTicks)).Ticks.ShouldBe(expectedTicks);
    }

    [Fact]
    public async Task A_clock_set_back_does_not_start_anything_by_itself()
    {
        await StartUpAtAsync(At(12, 16, 0));
        _eod.Calls.Count.ShouldBe(1); // catch-up

        await TickAtAsync(At(12, 9, 0)); // an adjusted clock

        _eod.Calls.Count.ShouldBe(1);
    }

    // ---- an edit takes effect without a restart ----

    [Fact]
    public async Task A_saved_schedule_is_used_at_the_next_look_without_a_restart()
    {
        await StartUpAtAsync(At(12, 10, 0));
        _world.SetClock(At(12, 11, 0));
        (await _world.ViewAsync("eod")).NextRun.ShouldBe(At(12, 15, 50));

        var saved = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 20))));
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Error.Message : null);

        (await _world.ViewAsync("eod")).NextRun.ShouldBe(At(12, 16, 20));
        await TickAtAsync(At(12, 15, 50));
        _eod.Calls.ShouldBeEmpty();
        await TickAtAsync(At(12, 16, 20));
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_next_look_follows_a_schedule_that_was_edited_in_the_store()
    {
        await StartUpAtAsync(At(12, 10, 0));
        _world.Data.Store("eod", Settings(Schedule.Daily(T(15, 55))));

        _world.SetClock(At(12, 15, 54, 30));

        (await _world.Scheduler.TickAsync(Ct)).ShouldBe(At(12, 15, 55));
    }

    [Fact]
    public async Task An_edit_to_a_time_that_has_passed_today_does_not_start_a_run_by_itself()
    {
        var job = Make("job", Schedule.Daily(T(15, 50)));
        using var world = new ServicesWorld(job);
        world.SetClock(At(12, 10, 0));
        world.Application.StartedAt = At(12, 10, 0);
        await world.Scheduler.StartUpAsync(Ct);
        world.SetClock(At(12, 11, 0));
        await world.Scheduler.TickAsync(Ct);

        world.Data.Store("job", Settings(Schedule.Daily(T(10, 30)))); // moved to a time that is over
        world.SetClock(At(12, 11, 1));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();

        job.Calls.ShouldBeEmpty();
        world.SetClock(At(13, 10, 30));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();
        job.Calls.Count.ShouldBe(1);
    }

    // ---- pause ----

    [Fact]
    public async Task A_paused_service_is_not_started_and_a_time_that_passed_is_not_made_up_for_when_it_is_resumed()
    {
        await StartUpAtAsync(At(12, 10, 0));
        (await _world.PauseAsync("eod")).IsSuccess.ShouldBeTrue();

        await TickAtAsync(At(12, 15, 50));
        _eod.Calls.ShouldBeEmpty();
        _world.SetClock(At(12, 16, 30));
        (await _world.ResumeAsync("eod")).IsSuccess.ShouldBeTrue();
        await TickAtAsync(At(12, 16, 31));
        _eod.Calls.ShouldBeEmpty();

        await TickAtAsync(At(13, 15, 50));
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_paused_service_has_no_next_run_and_the_status_paused()
    {
        _world.SetClock(At(12, 11, 0));
        await _world.PauseAsync("eod");

        var view = await _world.ViewAsync("eod");

        (view.Status, view.Enabled, view.NextRun).ShouldBe(("paused", false, null));
    }

    [Fact]
    public async Task A_time_that_passed_while_the_service_was_paused_is_not_made_up_for_after_a_resume_and_a_restart_the_same_day()
    {
        await StartUpAtAsync(At(12, 10, 0));
        _world.SetClock(At(12, 15, 30));
        (await _world.PauseAsync("eod")).IsSuccess.ShouldBeTrue();
        _world.SetClock(At(12, 16, 30));
        (await _world.ResumeAsync("eod")).IsSuccess.ShouldBeTrue(); // after the 15:50 time

        await StartUpAtAsync(At(12, 17, 0)); // restart

        _eod.Calls.ShouldBeEmpty();
        await TickAtAsync(At(13, 15, 50));
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_save_after_the_time_also_counts_as_dealt_with_but_one_before_it_does_not()
    {
        _world.SetClock(At(12, 15, 0));
        (await _world.SaveAsync("eod", Input(Schedule.Daily(T(15, 50)), retries: 1))).IsSuccess.ShouldBeTrue(); // before 15:50
        await StartUpAtAsync(At(12, 17, 0));
        _eod.Calls.Count.ShouldBe(1); // the time was missed since

        using var other = new ServicesWorld(Eod());
        other.SetClock(At(12, 16, 0));
        (await other.SaveAsync("eod", Input(Schedule.Daily(T(15, 50)), retries: 1))).IsSuccess.ShouldBeTrue(); // after 15:50
        other.SetClock(At(12, 17, 0));
        other.Application.StartedAt = At(12, 17, 0);
        await other.Scheduler.StartUpAsync(Ct);
        await other.Runner.WhenIdleAsync();
        other.Services[0].Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_start_settings_are_no_decision_of_the_owner_so_a_catch_up_still_follows_them()
    {
        _eod.Seed = Settings(Schedule.Daily(T(15, 50)));

        await StartUpAtAsync(At(12, 17, 0));

        _world.Data.Rows["eod"].UpdatedBy.ShouldBe(ServiceScheduler.SeedRequester);
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_time_that_comes_during_a_slow_start_up_is_not_lost()
    {
        using var world = new ServicesWorld(Eod(catchUp: false));
        world.SetClock(At(12, 15, 49, 30));
        world.Application.StartedAt = At(12, 15, 49, 30);
        world.Data.OnList = () => world.SetClock(At(12, 15, 50, 20)); // the start-up takes a while: 15:50 passes during it
        await world.Scheduler.StartUpAsync(Ct);
        world.Data.OnList = null;
        world.Services[0].Calls.ShouldBeEmpty(); // catch-up is off

        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();

        world.Data.Runs.ShouldHaveSingleItem().Trigger.ShouldBe(ServiceRunTrigger.Scheduled);
    }

    // ---- catch-up ----

    [Fact]
    public async Task Started_after_the_time_without_a_run_since_it_makes_up_once_with_the_catch_up_trigger()
    {
        await StartUpAtAsync(At(12, 17, 0));

        var run = _world.Data.Runs.ShouldHaveSingleItem();
        (run.Trigger, run.Status).ShouldBe((ServiceRunTrigger.CatchUp, ServiceRunStatus.Succeeded));

        await TickAtAsync(At(12, 17, 1));
        await TickAtAsync(At(12, 18, 0));
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_catch_up_off_nothing_is_made_up_for()
    {
        using var world = new ServicesWorld(Eod(catchUp: false));
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 17, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.Runner.WhenIdleAsync();

        world.Data.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_paused_service_never_catches_up_not_even_when_resumed()
    {
        using var world = new ServicesWorld(Eod(enabled: false));
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 17, 0);
        await world.Scheduler.StartUpAsync(Ct);

        await world.ResumeAsync("eod");
        world.SetClock(At(12, 17, 1));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();

        world.Data.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_that_started_since_the_time_means_nothing_is_made_up_even_if_it_failed_or_was_skipped()
    {
        _world.Data.Runs.Add(ServiceRun.Skipped("eod", ServiceRunTrigger.Scheduled, "The Data account is not connected.", At(12, 15, 50, 2)));

        await StartUpAtAsync(At(12, 17, 0));

        _eod.Calls.ShouldBeEmpty();
        _world.Data.Runs.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_run_from_before_the_time_does_not_stop_the_catch_up()
    {
        _world.Data.Runs.Add(ServiceRun.Skipped("eod", ServiceRunTrigger.Manual, "x", At(12, 14, 0)));

        await StartUpAtAsync(At(12, 17, 0));

        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Started_before_the_time_or_on_a_weekend_nothing_is_made_up()
    {
        await StartUpAtAsync(At(12, 14, 0));
        _eod.Calls.ShouldBeEmpty();

        await StartUpAtAsync(At(11, 17, 0)); // Sunday
        _eod.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Yesterdays_time_is_not_made_up_for_by_a_daily_job_but_is_by_a_service_that_asks_for_the_latest_reach()
    {
        using var world = new ServicesWorld(Eod(reach: CatchUpReach.SameDay), Make("refresh", Schedule.Daily(T(8, 45), false, Weekdays.EveryDay), constraints: new ServiceConstraints { CatchUpReach = CatchUpReach.Latest }));
        world.SetClock(At(13, 7, 30));
        world.Application.StartedAt = At(13, 7, 30);

        await world.Scheduler.StartUpAsync(Ct);
        await world.Runner.WhenIdleAsync();

        world.Services[0].Calls.ShouldBeEmpty();
        world.Services[1].Calls.Count.ShouldBe(1);
        world.Data.RunsOf("refresh").Single().Trigger.ShouldBe(ServiceRunTrigger.CatchUp);
    }

    [Fact]
    public async Task A_service_that_was_never_run_is_made_up_for_at_start_when_it_asks_for_the_latest_reach()
    {
        using var world = new ServicesWorld(Make("refresh", Schedule.Daily(T(8, 45), false, Weekdays.EveryDay), constraints: new ServiceConstraints { CatchUpReach = CatchUpReach.Latest }));
        world.SetClock(At(12, 7, 30));
        world.Application.StartedAt = At(12, 7, 30);

        await world.Scheduler.StartUpAsync(Ct);
        await world.Runner.WhenIdleAsync();

        world.Services[0].Calls.Count.ShouldBe(1);
    }

    // ---- recovery ----

    [Fact]
    public async Task A_run_left_running_by_the_previous_process_is_closed_as_interrupted_before_the_catch_up_decision()
    {
        var stuck = ServiceRun.Start("eod", ServiceRunTrigger.Scheduled, 1, At(12, 15, 0));
        _world.Data.Runs.Add(stuck); // the previous process died while it ran

        await StartUpAtAsync(At(12, 17, 0)); // this process started at 17:00

        (stuck.Status, stuck.Message).ShouldBe((ServiceRunStatus.Failed, ServiceScheduler.InterruptedMessage));
        stuck.FinishedAt.ShouldBe(At(12, 17, 0));
        var catchUp = _world.Data.RunsOf("eod").Last();
        (catchUp.Trigger, catchUp.Status).ShouldBe((ServiceRunTrigger.CatchUp, ServiceRunStatus.Succeeded)); // not refused as "already running"
        _world.Data.Runs.Count(r => r.Status == ServiceRunStatus.Skipped).ShouldBe(0);
    }

    [Fact]
    public async Task A_run_that_started_after_this_process_is_not_touched_by_the_recovery()
    {
        var mine = ServiceRun.Start("eod", ServiceRunTrigger.Manual, 1, At(12, 17, 30));
        _world.Data.Runs.Add(mine);

        await StartUpAtAsync(At(12, 17, 0));

        mine.IsRunning.ShouldBeTrue();
    }

    // ---- the first-start settings ----

    [Fact]
    public async Task The_configuration_seeds_a_row_once_an_edit_is_kept_and_the_configuration_is_ignored_after_it()
    {
        _eod.Seed = Settings(Schedule.Daily(T(16, 5)), enabled: false);

        await StartUpAtAsync(At(12, 10, 0));
        var seeded = _world.Data.Rows["eod"];
        (seeded.Enabled, seeded.Schedule, seeded.UpdatedBy).ShouldBe((false, Schedule.Daily(T(16, 5)), ServiceScheduler.SeedRequester));

        var edited = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 20)), revision: seeded.Revision));
        edited.IsSuccess.ShouldBeTrue();
        _eod.Seed = Settings(Schedule.Daily(T(9, 0)), enabled: true); // the configuration changed meanwhile

        await StartUpAtAsync(At(12, 10, 5)); // restart

        var row = _world.Data.Rows["eod"];
        (row.Enabled, row.Schedule, row.UpdatedBy).ShouldBe((true, Schedule.Daily(T(16, 20)), "owner@example.invalid"));
    }

    [Fact]
    public async Task Until_the_row_exists_the_screen_shows_the_configured_settings()
    {
        _eod.Seed = Settings(Schedule.Daily(T(16, 5)), enabled: false);
        _world.SetClock(At(12, 11, 0));

        var view = await _world.ViewAsync("eod");

        (view.Enabled, view.Schedule.Time, view.Status, view.Revision).ShouldBe((false, "16:05", "paused", 0));
    }

    [Fact]
    public async Task A_service_without_a_seed_has_no_row_and_runs_on_its_defaults()
    {
        await StartUpAtAsync(At(12, 10, 0));

        _world.Data.Rows.ShouldBeEmpty();
        await TickAtAsync(At(12, 15, 50));
        _eod.Calls.Count.ShouldBe(1);
    }

    // ---- run after ----

    [Fact]
    public async Task A_service_set_to_run_after_another_starts_when_it_succeeds_with_the_after_trigger()
    {
        var report = Make("report", Schedule.AfterService("eod"));
        using var world = new ServicesWorld(Eod(), report);
        world.SetClock(At(12, 10, 0));
        world.Application.StartedAt = At(12, 10, 0);
        await world.Scheduler.StartUpAsync(Ct);

        world.SetClock(At(12, 15, 50));
        await world.Scheduler.TickAsync(Ct);
        await report.WhenStartedAsync().WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await world.Runner.WhenIdleAsync();

        var run = world.Data.RunsOf("report").ShouldHaveSingleItem();
        (run.Trigger, run.Status).ShouldBe((ServiceRunTrigger.After, ServiceRunStatus.Succeeded));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("skipped")]
    public async Task A_service_set_to_run_after_another_does_not_start_when_it_does_not_succeed(string outcome)
    {
        var eod = Eod();
        eod.Behavior = (_, _) => ValueTask.FromResult(outcome == "failed" ? XDrishti.Application.Abstractions.Platform.ServiceOutcome.Failed("x") : XDrishti.Application.Abstractions.Platform.ServiceOutcome.Skipped("y"));
        var report = Make("report", Schedule.AfterService("eod"));
        using var world = new ServicesWorld(eod, report);
        world.SetClock(At(12, 10, 0));
        world.Application.StartedAt = At(12, 10, 0);
        await world.Scheduler.StartUpAsync(Ct);

        world.SetClock(At(12, 15, 50));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();

        report.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_paused_service_set_to_run_after_another_does_not_start()
    {
        var report = Make("report", Schedule.AfterService("eod"), enabled: false);
        using var world = new ServicesWorld(Eod(), report);
        world.SetClock(At(12, 10, 0));
        world.Application.StartedAt = At(12, 10, 0);
        await world.Scheduler.StartUpAsync(Ct);

        world.SetClock(At(12, 15, 50));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();

        report.Calls.ShouldBeEmpty();
    }
}
