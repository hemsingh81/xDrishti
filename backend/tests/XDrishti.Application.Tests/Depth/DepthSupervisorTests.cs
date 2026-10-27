using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Depth;
using XDrishti.Application.Live;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Depth.DepthSupervisorWorld;

namespace XDrishti.Application.Tests.Depth;

/// <summary>The depth recorder's run owner (P7-16 criteria 6, 7, 8, 10): three gates, the window, the run record, the disk guard before a start, the waits, crash recovery and the day's end row.</summary>
public sealed class DepthSupervisorTests
{
    private static async Task<string> FingerprintAsync(DepthSupervisorWorld world) =>
        FeedCredentialFingerprint.Of((await world.Credentials.GetCredentialsAsync(TestContext.Current.CancellationToken))!);

    // ---- the three gates: with any one closed nothing reads credentials, builds a URL or opens a socket ----

    [Fact]
    public async Task With_the_deployment_flag_off_nothing_is_read_or_started_whatever_else_says()
    {
        var world = new DepthSupervisorWorld(deployment: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldBeEmpty();
        world.Universe.Calls.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Disabled);
        world.Heartbeats.Reports[^1].Details.DeploymentEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_stored_switch_row_the_recorder_is_off()
    {
        var world = new DepthSupervisorWorld(switchOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    [Fact]
    public async Task With_the_newest_switch_row_off_the_recorder_is_off()
    {
        var world = new DepthSupervisorWorld();
        world.Switch.Set(false);

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    [Fact]
    public async Task The_service_starts_paused_and_a_paused_service_keeps_the_recorder_off()
    {
        DepthRecorderService.Definition.Defaults.Enabled.ShouldBeFalse();
        var world = new DepthSupervisorWorld(serviceOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    [Fact]
    public async Task Outside_the_window_all_gates_open_still_start_nothing()
    {
        var world = new DepthSupervisorWorld(8, 0);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Closed);
    }

    [Fact]
    public async Task A_weekend_has_no_window()
    {
        var world = new DepthSupervisorWorld();
        world.SetClock(At(17, 10, 0)); // Saturday

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
    }

    // ---- start ----

    [Fact]
    public async Task With_all_three_gates_open_inside_the_window_one_scheduled_run_opens_and_the_session_starts_with_the_days_set()
    {
        var world = new DepthSupervisorWorld(9, 14);

        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Running);
        run.Trigger.ShouldBe(ServiceRunTrigger.Scheduled);
        world.Session.Starts.ShouldBe(1);
        world.Session.LastSet!.Select(e => (e.Symbol, e.Reason)).ShouldBe([("ARMED", "armed"), ("PINNED", "pinned"), ("LIQUID", "turnover")]);
        world.Store.Sets[Today].Count.ShouldBe(3);
        world.Credentials.Reads.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Recording);
    }

    [Fact]
    public async Task Later_looks_open_no_second_run()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();

        world.SetClock(At(12, 9, 20, 15));
        await world.TickAsync();
        world.SetClock(At(12, 12, 0));
        await world.TickAsync();

        world.Runs.Count.ShouldBe(1);
        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task The_set_is_fixed_for_the_day_a_restart_reuses_the_stored_one()
    {
        var world = new DepthSupervisorWorld();
        var stored = new List<DepthSetEntry> { new(Guid.NewGuid(), 4444, "STORED", "pinned", 1) };
        world.Store.Sets[Today] = stored;

        await world.TickAsync();

        world.Session.LastSet.ShouldBe(stored);
        world.Universe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_set_records_one_skipped_run_per_day_and_reads_no_credentials()
    {
        var world = new DepthSupervisorWorld();
        world.Armed.Tickets.Clear();
        world.Universe.Rows.Clear();
        world.Securities.Known.Clear();

        await world.TickAsync();
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
        world.LastState.ShouldBe(DepthStates.NoInstruments);
    }

    [Fact]
    public async Task Without_a_usable_token_one_skipped_run_is_recorded_per_day()
    {
        var world = new DepthSupervisorWorld();
        world.Credentials.Token = null;

        await world.TickAsync();
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(DepthRecorderSupervisor.NoTokenMessage);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.NoToken);
    }

    // ---- stop ----

    [Fact]
    public async Task At_the_end_of_the_window_the_session_stops_the_run_succeeds_and_the_days_row_is_written_as_window_end()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Stats = new DepthRunStats(1000, 5000, 2, 7, 1);

        world.SetClock(At(12, 15, 31));
        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Succeeded);
        run.Message.ShouldBe("Window ended");
        world.Session.Running.ShouldBeFalse();
        var day = world.Store.Sessions.ShouldHaveSingleItem();
        day.EndReason.ShouldBe("window_end");
        day.Date.ShouldBe(Today);
        day.Instruments.ShouldBe(3);
        day.Snapshots.ShouldBe(1000);
        day.GapSeconds.ShouldBe(7);
        world.LastState.ShouldBe(DepthStates.Closed);
    }

    [Fact]
    public async Task Turning_the_switch_off_stops_the_session_the_run_says_paused_and_the_day_row_says_paused_when_the_day_is_over()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();

        world.Switch.Set(false);
        world.SetClock(At(12, 10, 0));
        await world.TickAsync();

        world.Session.Running.ShouldBeFalse();
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe("Paused");
        world.LastState.ShouldBe(DepthStates.SwitchOff);
        world.Store.Sessions.ShouldBeEmpty(); // the day is not over yet

        world.SetClock(At(12, 15, 31));
        await world.TickAsync();
        world.Store.Sessions.ShouldHaveSingleItem().EndReason.ShouldBe("paused");
    }

    [Fact]
    public async Task Pausing_the_service_stops_the_session()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();

        world.Data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = false });
        world.SetClock(At(12, 10, 0));
        await world.TickAsync();

        world.Session.Running.ShouldBeFalse();
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    // ---- the ends that wait ----

    [Fact]
    public async Task A_refused_token_fails_the_run_and_waits_for_a_changed_token()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(DepthSessionEnd.TokenRefused, await FingerprintAsync(world));
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Failed);
        world.Runs[0].Message.ShouldBe(DepthRecorderSupervisor.TokenRefusedMessage);
        world.LastState.ShouldBe(DepthStates.TokenRefused);

        // the same token: still nothing; a new token: a new run
        world.SetClock(At(12, 9, 23));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.Credentials.Token = "a-new-token";
        world.SetClock(At(12, 9, 25));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_806_fails_the_run_as_not_subscribed_and_does_not_retry_with_the_same_credentials()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(DepthSessionEnd.NotSubscribed, await FingerprintAsync(world));
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.SetClock(At(12, 11, 0));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(DepthRecorderSupervisor.NotSubscribedMessage);
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.NotSubscribed);
    }

    [Fact]
    public async Task Flapping_fails_the_run_and_pauses_attempts_for_five_minutes()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(DepthSessionEnd.Flapping);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.Runs[0].Message.ShouldBe(DepthRecorderSupervisor.FlappingMessage);
        world.SetClock(At(12, 9, 24));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Flapping);

        world.SetClock(At(12, 9, 27));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_crashed_session_fails_its_run_and_is_tried_again_after_thirty_seconds()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Crash();
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 20, 15));
        await world.TickAsync();
        world.Runs[0].Message.ShouldBe(DepthRecorderSupervisor.CrashMessage);
        world.Session.Starts.ShouldBe(1);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(2);
    }

    // ---- the disk guard ----

    [Fact]
    public async Task Before_a_start_a_stored_size_over_the_cap_stops_the_start_and_records_one_skipped_run_without_reading_credentials()
    {
        var world = new DepthSupervisorWorld();
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;

        await world.TickAsync();
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(DepthRecorderSupervisor.DiskBudgetMessage);
        world.LastState.ShouldBe(DepthStates.DiskBudget);
    }

    [Fact]
    public async Task Before_a_start_free_space_under_the_floor_stops_the_start()
    {
        var world = new DepthSupervisorWorld();
        world.Store.Free = world.Settings.MinFreeBytes - 1;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.DiskFree);
    }

    [Fact]
    public async Task A_stopped_recorder_stays_stopped_when_the_condition_clears_until_a_look_finds_it_not_wanted_then_the_owners_resume_starts_it()
    {
        var world = new DepthSupervisorWorld();
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;
        await world.TickAsync();
        world.Store.Stored = 0; // cleared

        world.SetClock(At(12, 9, 25));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(0); // still stopped

        world.Data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = false }); // the owner pauses ...
        world.SetClock(At(12, 9, 26));
        await world.TickAsync();
        world.Data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = true }); // ... and resumes
        world.SetClock(At(12, 9, 27));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task A_session_stopped_by_the_guard_fails_the_run_with_the_message_and_the_day_row_names_the_reason()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(DepthSessionEnd.DiskBudget);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.SetClock(At(12, 15, 32));
        await world.TickAsync();

        world.Runs[0].Status.ShouldBe(ServiceRunStatus.Failed);
        world.Runs[0].Message.ShouldBe(DepthRecorderSupervisor.DiskBudgetMessage);
        world.Session.Starts.ShouldBe(1);
        world.Store.Sessions.ShouldHaveSingleItem().EndReason.ShouldBe("disk_budget");
    }

    // ---- recovery, isolation, shutdown ----

    [Fact]
    public async Task A_day_with_a_stored_set_and_no_end_row_is_closed_as_crashed_at_the_first_look_and_a_leftover_run_is_interrupted()
    {
        var world = new DepthSupervisorWorld(8, 0);
        var yesterday = new DateOnly(2026, 10, 9);
        world.Store.Sets[yesterday] = [new DepthSetEntry(Guid.NewGuid(), 1, "OLD", "armed", 1)];
        var left = ServiceRun.Start(DepthRecorderService.Id, ServiceRunTrigger.Scheduled, 1, At(9, 9, 14));
        (await ((IServiceRunStore)world.Data).TryStartAsync(left, TimeSpan.FromHours(8), TestContext.Current.CancellationToken)).ShouldBeTrue();

        await world.TickAsync();

        var row = world.Store.Sessions.ShouldHaveSingleItem();
        row.Date.ShouldBe(yesterday);
        row.EndReason.ShouldBe("crashed");
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Failed);
        world.Runs[0].IsInterrupted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failing_look_does_not_throw_the_heartbeat_still_goes_out_and_the_next_look_tries_again()
    {
        var world = new DepthSupervisorWorld();
        world.Armed.Fail = true;

        await Should.NotThrowAsync(world.TickAsync);

        world.Heartbeats.Reports.ShouldHaveSingleItem();
        world.Session.Starts.ShouldBe(0);
        world.Armed.Fail = false;
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task A_switch_that_cannot_be_read_is_a_closed_gate_and_stops_a_running_session()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();
        world.Session.Running.ShouldBeTrue();

        world.Switch.Fail = true;
        world.SetClock(At(12, 9, 21));
        await Should.NotThrowAsync(world.TickAsync);

        world.Session.Running.ShouldBeFalse();
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.LastState.ShouldBe(DepthStates.Paused);
        world.Switch.Fail = false;
        world.SetClock(At(12, 9, 22));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2); // the gate reads again: the recorder is wanted again
    }

    [Fact]
    public async Task Service_settings_that_cannot_be_read_are_a_closed_gate_and_stop_a_running_session()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();

        world.Data.OnList = () => throw new InvalidOperationException("the database is away");
        world.SetClock(At(12, 9, 21));
        await Should.NotThrowAsync(world.TickAsync);

        world.Session.Running.ShouldBeFalse();
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    [Fact]
    public async Task A_restart_in_the_middle_of_the_day_does_not_close_todays_day_as_crashed_but_earlier_days_still_are()
    {
        var world = new DepthSupervisorWorld(8, 0);
        var yesterday = new DateOnly(2026, 10, 9);
        world.Store.Sets[yesterday] = [new DepthSetEntry(Guid.NewGuid(), 1, "OLD", "armed", 1)];
        world.Store.Sets[Today] = [new DepthSetEntry(Guid.NewGuid(), 2, "NEW", "armed", 1)];

        await world.TickAsync();

        world.Store.Sessions.ShouldHaveSingleItem().Date.ShouldBe(yesterday);
        (await ((IDepthStore)world.Store).RecordSessionAsync(new DepthSessionRecord(Today, At(12, 8, 0), At(12, 15, 31), "window_end", 1, 0, 0, 0, 0, null, null), TestContext.Current.CancellationToken)).ShouldBeTrue("the day's real end row can still be written");
    }

    [Fact]
    public async Task The_process_stopping_ends_the_session_closes_the_run_and_says_stopped_in_a_last_heartbeat()
    {
        var world = new DepthSupervisorWorld();
        using var stop = new CancellationTokenSource();
        var loop = world.Supervisor.RunAsync(stop.Token);
        for (var i = 0; i < 1_000_000 && world.Session.Starts == 0; i++)
        {
            await Task.Yield();
        }

        await stop.CancelAsync();
        await loop;

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.Runs[0].Message.ShouldBe("Stopped");
        world.LastState.ShouldBe(DepthStates.Stopped);
    }

    [Fact]
    public async Task The_heartbeat_carries_counts_only()
    {
        var world = new DepthSupervisorWorld();
        await world.TickAsync();

        var details = world.Heartbeats.Reports[^1].Details;

        details.DeploymentEnabled.ShouldBeTrue();
        details.Instruments.ShouldBe(3);
        details.FreeSpaceReadable.ShouldBeTrue();
    }
}
