using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Depth;
using XDrishti.Application.Live;
using XDrishti.Application.OptionChain;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.OptionChain.ChainSamples;
using static XDrishti.Application.Tests.OptionChain.OptionChainSupervisorWorld;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>The option-chain recorder's run owner (P14-02 criteria 6, 7, 8): three gates, the window, the day's underlyings, the run record, the disk guard before a start, the waits, crash recovery, the day's end row.</summary>
public sealed class OptionChainSupervisorTests
{
    private static async Task<string> FingerprintAsync(OptionChainSupervisorWorld world) =>
        FeedCredentialFingerprint.Of((await world.Credentials.GetCredentialsAsync(TestContext.Current.CancellationToken))!);

    // ---- the three gates ----

    [Fact]
    public async Task With_the_deployment_flag_off_nothing_is_read_or_started()
    {
        var world = new OptionChainSupervisorWorld(deployment: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Targets.Loads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldBeEmpty();
        world.LastState.ShouldBe(DepthStates.Disabled);
        world.Heartbeats.Reports[^1].Details.DeploymentEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_stored_switch_row_the_recorder_is_off()
    {
        var world = new OptionChainSupervisorWorld(switchOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    [Fact]
    public async Task The_service_starts_paused_and_a_paused_service_keeps_the_recorder_off()
    {
        OptionChainRecorderService.Definition.Defaults.Enabled.ShouldBeFalse();
        var world = new OptionChainSupervisorWorld(serviceOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    [Fact]
    public async Task Outside_the_window_nothing_starts()
    {
        var world = new OptionChainSupervisorWorld(8, 0);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Closed);
    }

    [Fact]
    public async Task A_switch_that_cannot_be_read_is_a_closed_gate_and_stops_a_running_session()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        world.Switch.Fail = true;
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Paused);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    [Fact]
    public async Task Turning_the_switch_off_stops_the_session_and_closes_the_run()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();

        world.Switch.Set(false);
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    // ---- the start ----

    [Fact]
    public async Task With_all_gates_open_the_session_starts_with_the_indices_that_have_a_rule_and_the_other_underlyings_as_stocks()
    {
        var world = new OptionChainSupervisorWorld(9, 14);
        world.SetClock(At(12, 9, 14, 40));

        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);
        world.Session.LastDay!.Indices.Select(t => t.Symbol).ShouldBe(["NSE:NIFTY", "NSE:BANKNIFTY"], "FINNIFTY has no expiry rule and is not asked");
        world.Session.LastDay.Stocks.Select(t => t.Symbol).ShouldBe(["NSE:RELIANCE"]);
        world.Credentials.Reads.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Recording);
    }

    [Fact]
    public async Task With_no_underlying_it_skips_once_for_the_day_and_never_reads_credentials()
    {
        var world = new OptionChainSupervisorWorld();
        world.Targets.Targets.Clear();

        await world.TickAsync();
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
        world.LastState.ShouldBe(DepthStates.NoInstruments);
    }

    [Fact]
    public async Task Without_a_token_it_skips_once_and_waits()
    {
        var world = new OptionChainSupervisorWorld();
        world.Credentials.Token = null;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionChainSupervisor.NoTokenMessage);
        world.LastState.ShouldBe(DepthStates.NoToken);
    }

    // ---- ends ----

    [Fact]
    public async Task At_the_end_of_the_window_the_run_succeeds_and_the_days_row_carries_the_counts()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Stats = new ChainRunStats(75, 515, 500, 15, 58_620);

        world.SetClock(At(12, 15, 31));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        var day = world.Store.Sessions.ShouldHaveSingleItem();
        (day.EndReason, day.Slots, day.Requests, day.Ok, day.Gaps, day.Rows).ShouldBe(("window_end", 75, 515, 500, 15, 58_620L));
    }

    [Fact]
    public async Task A_completed_day_is_not_started_again_before_tomorrow()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(ChainSessionEnd.Completed);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.SetClock(At(12, 9, 22));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.LastState.ShouldBe(ChainStates.DayDone);
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_and_waits_for_a_changed_token()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(ChainSessionEnd.TokenRefused);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionChainSupervisor.TokenRefusedMessage);
        world.SetClock(At(12, 9, 23));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.TokenRefused);

        world.Credentials.Token = "a-new-token";
        world.SetClock(At(12, 9, 25));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
        (await FingerprintAsync(world)).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Not_subscribed_fails_the_run_and_ends_the_day()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(ChainSessionEnd.NotSubscribed);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.Credentials.Token = "a-new-token";
        world.SetClock(At(12, 9, 40));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionChainSupervisor.NotSubscribedMessage);
        world.Session.Starts.ShouldBe(1, "the day is over for this recorder, whatever the token");
        world.LastState.ShouldBe(DepthStates.NotSubscribed);
    }

    [Fact]
    public async Task A_rate_limit_pause_fails_the_run_so_the_owner_is_told_and_waits_fifteen_minutes()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(ChainSessionEnd.RatePaused);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        var run = world.Runs.ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, OptionChainSupervisor.RatePausedMessage));
        world.SetClock(At(12, 9, 34));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(ChainStates.RatePaused);

        world.SetClock(At(12, 9, 37));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_stored_size_over_the_own_cap_stops_the_start_without_reading_credentials_and_stays_stopped()
    {
        var world = new OptionChainSupervisorWorld();
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;

        await world.TickAsync();
        world.Store.Stored = 0;
        world.SetClock(At(12, 9, 21));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(0, "it stays stopped until the owner resumes");
        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
        world.LastState.ShouldBe(DepthStates.DiskBudget);
    }

    [Fact]
    public async Task Free_space_under_the_floor_stops_the_start()
    {
        var world = new OptionChainSupervisorWorld();
        world.Store.Free = world.Settings.MinFreeBytes - 1;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.DiskFree);
    }

    [Fact]
    public async Task A_disk_stop_inside_the_session_fails_the_run_and_holds_until_the_owner_pauses_and_resumes()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(ChainSessionEnd.DiskBudget);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 21));
        await world.TickAsync();
        world.SetClock(At(12, 9, 22));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionChainSupervisor.DiskBudgetMessage);
        world.Session.Starts.ShouldBe(1);

        // the owner pauses the service (the recorder is not wanted for a look), then resumes
        world.Switch.Set(false);
        world.SetClock(At(12, 9, 23));
        await world.TickAsync();
        world.Switch.Set(true);
        world.SetClock(At(12, 9, 24));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_past_day_with_request_rows_and_no_end_row_is_closed_as_crashed_and_an_open_run_as_interrupted()
    {
        var world = new OptionChainSupervisorWorld(8, 0);
        var yesterday = new DateOnly(2026, 10, 9);
        world.Store.Requests.Add(new ChainRequestRecord(At(9, 9, 20), "chain", Guid.NewGuid(), new DateOnly(2026, 10, 13), "ok", 200, null, 5, 100, At(9, 9, 20)));
        var left = ServiceRun.Start(OptionChainRecorderService.Id, ServiceRunTrigger.Scheduled, 1, At(9, 9, 14));
        (await ((IServiceRunStore)world.Data).TryStartAsync(left, TimeSpan.FromHours(8), TestContext.Current.CancellationToken)).ShouldBeTrue();

        await world.TickAsync();

        var row = world.Store.Sessions.ShouldHaveSingleItem();
        (row.Date, row.EndReason).ShouldBe((yesterday, "crashed"));
        world.Runs.ShouldHaveSingleItem().IsInterrupted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_crashed_session_fails_its_run_and_is_tried_again_after_thirty_seconds()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Crash();
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 9, 20, 15));
        await world.TickAsync();
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionChainSupervisor.CrashMessage);
        world.Session.Starts.ShouldBe(1);

        world.SetClock(At(12, 9, 20, 50));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task The_heartbeat_is_its_own_row_and_carries_counts_only()
    {
        var world = new OptionChainSupervisorWorld();
        await world.TickAsync();
        world.Session.Stats = new ChainRunStats(3, 20, 18, 2, 1_000);

        world.SetClock(At(12, 9, 20, 15));
        await world.TickAsync();

        IOptionChainHeartbeats.ServiceName.ShouldBe("xd-options");
        var details = world.Heartbeats.Reports[^1].Details;
        (details.Slots, details.Requests, details.Ok, details.Gaps, details.Rows).ShouldBe((3, 20, 18, 2, 1_000L));
        typeof(ChainHeartbeat).GetProperties().Select(p => p.PropertyType).ShouldAllBe(t => t == typeof(bool) || t == typeof(int) || t == typeof(long));
    }

    [Fact]
    public async Task The_process_stopping_ends_the_session_closes_the_run_and_says_stopped()
    {
        var world = new OptionChainSupervisorWorld();
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

    [Fact]
    public void The_service_is_a_window_service_of_the_feed_host_started_paused_with_no_retries()
    {
        var defaults = OptionChainRecorderService.Definition.Defaults;
        (defaults.Enabled, defaults.Retries, defaults.NotifyOnFailure).ShouldBe((false, 0, true));
        OptionChainRecorderService.RunsAs.ShouldBe("xd-feed");
        OptionChainRecorderService.Id.ShouldBe("option-chain-recorder");
    }
}
