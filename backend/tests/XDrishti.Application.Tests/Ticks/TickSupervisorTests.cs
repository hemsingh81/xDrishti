using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Application.Live;
using XDrishti.Application.Ticks;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Ticks.TickSupervisorWorld;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>The tick recorder's run owner (P7-16b criteria 5, 6, 8, 9, 11): three gates, the window, the day's set, the run record, the disk guard before a start, the waits, crash recovery, the day's end row and no depth table.</summary>
public sealed class TickSupervisorTests
{
    private static async Task<string> FingerprintAsync(TickSupervisorWorld world) =>
        FeedCredentialFingerprint.Of((await world.Credentials.GetCredentialsAsync(TestContext.Current.CancellationToken))!);

    // ---- the three gates ----

    [Fact]
    public async Task With_the_deployment_flag_off_nothing_is_read_or_started()
    {
        var world = new TickSupervisorWorld(deployment: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldBeEmpty();
        world.Store.Sets.ShouldBeEmpty();
        world.LastState.ShouldBe(DepthStates.Disabled);
        world.Heartbeats.Reports[^1].Details.DeploymentEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_stored_switch_row_the_recorder_is_off()
    {
        var world = new TickSupervisorWorld(switchOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    [Fact]
    public async Task The_service_starts_paused_and_a_paused_service_keeps_the_recorder_off()
    {
        TickRecorderService.Definition.Defaults.Enabled.ShouldBeFalse();
        var world = new TickSupervisorWorld(serviceOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    [Fact]
    public async Task Outside_the_window_nothing_starts()
    {
        var world = new TickSupervisorWorld(8, 0);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Closed);
    }

    [Fact]
    public async Task A_switch_that_cannot_be_read_is_a_closed_gate_and_stops_a_running_session()
    {
        var world = new TickSupervisorWorld();
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        world.Switch.Fail = true;
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.LastState.ShouldBe(DepthStates.Paused);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    // ---- the day's set ----

    [Fact]
    public async Task With_all_gates_open_the_depth_set_is_copied_as_the_days_tick_set_and_the_session_starts()
    {
        var world = new TickSupervisorWorld(9, 14);
        world.SetClock(At(12, 9, 14, 40));

        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);
        world.Session.LastSet!.Select(e => (e.Symbol, e.Source)).ShouldBe([("DEPTH1", "depth_set"), ("DEPTH2", "depth_set")]);
        world.Store.Sets[Today].Count.ShouldBe(2);
        world.Universe.Calls.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Recording);
    }

    [Fact]
    public async Task Without_a_depth_set_before_09_14_30_it_waits_without_reading_credentials_then_builds_the_set_the_same_way()
    {
        var world = new TickSupervisorWorld(9, 14, depthSetStored: false);

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(0);
        world.Store.Sets.ShouldBeEmpty();
        world.LastState.ShouldBe(TickSetChoice.WaitingForSet);

        world.SetClock(At(12, 9, 14, 30));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
        world.Session.LastSet!.Select(e => (e.Symbol, e.Source, e.Reason)).ShouldBe([("ARMED", "built", "armed"), ("PINNED", "built", "pinned"), ("LIQUID", "built", "turnover")]);
    }

    [Fact]
    public async Task The_set_is_fixed_for_the_day_a_depth_set_stored_later_does_not_change_it_and_a_restart_reuses_it()
    {
        var world = new TickSupervisorWorld(10, 0, depthSetStored: false);
        await world.TickAsync();
        world.Session.LastSet!.ShouldAllBe(e => e.Source == "built");

        world.Store.DepthSets[Today] = [new TickSetEntry(Guid.NewGuid(), 9999, "LATE", "depth_set", "armed", 1)];
        world.Session.Finish(DepthSessionEnd.Stopped);
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 1));
        await world.TickAsync();
        world.SetClock(At(12, 10, 2));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(2);
        world.Session.LastSet!.Select(e => e.Symbol).ShouldBe(["ARMED", "PINNED", "LIQUID"]);
    }

    // ---- ends ----

    [Fact]
    public async Task At_the_end_of_the_window_the_run_succeeds_and_the_days_row_carries_the_counts_and_the_gap()
    {
        var world = new TickSupervisorWorld();
        await world.TickAsync();
        world.Session.Stats = new TickRunStats(9_000, 1_200, 7_000, 3, 42, 2);

        world.SetClock(At(12, 15, 31));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        var day = world.Store.Sessions.ShouldHaveSingleItem();
        (day.EndReason, day.Instruments, day.Packets, day.Rows, day.Unchanged, day.DroppedRows, day.GapSeconds).ShouldBe(("window_end", 2, 9_000L, 1_200L, 7_000L, 3L, 42));
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_and_waits_for_a_changed_token()
    {
        var world = new TickSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(DepthSessionEnd.TokenRefused, await FingerprintAsync(world));
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(TickRecorderSupervisor.TokenRefusedMessage);
        world.SetClock(At(12, 9, 23));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        world.Credentials.Token = "a-new-token";
        world.SetClock(At(12, 9, 25));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task Flapping_fails_the_run_and_pauses_attempts_for_five_minutes()
    {
        var world = new TickSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(DepthSessionEnd.Flapping);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.SetClock(At(12, 9, 25));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Flapping);

        world.SetClock(At(12, 9, 27));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task Before_a_start_a_stored_size_over_the_own_cap_stops_the_start_without_reading_credentials()
    {
        var world = new TickSupervisorWorld();
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
        world.LastState.ShouldBe(DepthStates.DiskBudget);
    }

    [Fact]
    public async Task Before_a_start_free_space_under_the_shared_floor_stops_the_start()
    {
        var world = new TickSupervisorWorld();
        world.Store.Free = world.Depth.MinFreeBytes - 1;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.DiskFree);
    }

    [Fact]
    public async Task A_past_day_with_a_tick_set_and_no_end_row_is_closed_as_crashed_in_the_tick_table_only()
    {
        var world = new TickSupervisorWorld(8, 0);
        var yesterday = new DateOnly(2026, 10, 9);
        world.Store.Sets[yesterday] = [new TickSetEntry(Guid.NewGuid(), 1, "OLD", "built", "armed", 1)];
        world.Store.DepthSets[yesterday] = [new TickSetEntry(Guid.NewGuid(), 2, "D", "depth_set", "armed", 1)];
        var left = ServiceRun.Start(TickRecorderService.Id, ServiceRunTrigger.Scheduled, 1, At(9, 9, 14));
        (await ((IServiceRunStore)world.Data).TryStartAsync(left, TimeSpan.FromHours(8), TestContext.Current.CancellationToken)).ShouldBeTrue();

        await world.TickAsync();

        var row = world.Store.Sessions.ShouldHaveSingleItem();
        (row.Date, row.EndReason).ShouldBe((yesterday, "crashed"));
        world.Runs.ShouldHaveSingleItem().IsInterrupted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_crashed_session_fails_its_run_and_is_tried_again_after_thirty_seconds()
    {
        var world = new TickSupervisorWorld();
        await world.TickAsync();
        world.Session.Crash();
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 20, 15));
        await world.TickAsync();
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(TickRecorderSupervisor.CrashMessage);
        world.Session.Starts.ShouldBe(1);

        world.SetClock(At(12, 9, 20, 50));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task The_heartbeat_is_its_own_row_and_carries_counts_only()
    {
        var world = new TickSupervisorWorld();
        await world.TickAsync();
        world.Session.Stats = new TickRunStats(10, 4, 6, 0, 1, 0);

        world.SetClock(At(12, 9, 20, 15));
        await world.TickAsync();

        ITickHeartbeats.ServiceName.ShouldBe("xd-ticks");
        var details = world.Heartbeats.Reports[^1].Details;
        (details.Packets, details.Rows, details.Unchanged, details.Instruments).ShouldBe((10L, 4L, 6L, 2));
        typeof(TickHeartbeat).GetProperties().Select(p => p.PropertyType).ShouldAllBe(t => t == typeof(bool) || t == typeof(int) || t == typeof(long));
    }

    [Fact]
    public async Task The_process_stopping_ends_the_session_closes_the_run_and_says_stopped()
    {
        var world = new TickSupervisorWorld();
        using var stop = new CancellationTokenSource();
        var loop = world.Supervisor.RunAsync(stop.Token);
        for (var i = 0; i < 1_000 && world.Session.Starts == 0; i++)
        {
            await Task.Yield();
        }

        await stop.CancelAsync();
        await loop;

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.LastState.ShouldBe(DepthStates.Stopped);
    }
}
