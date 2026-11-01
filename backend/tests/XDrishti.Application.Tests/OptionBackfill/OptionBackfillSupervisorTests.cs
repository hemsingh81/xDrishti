using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Depth;
using XDrishti.Application.Live;
using XDrishti.Application.OptionBackfill;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.OptionBackfill.BackfillSamples;
using static XDrishti.Application.Tests.OptionBackfill.OptionBackfillSupervisorWorld;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>The backfill's run owner (P14-03 criteria 5, 6, 7, 9): four gates, the night window, the run record, the disk guard before a start, the waits. Every look is one explicit tick on the fake clock; nothing sleeps.</summary>
public sealed class OptionBackfillSupervisorTests
{
    private static async Task<string> FingerprintAsync(OptionBackfillSupervisorWorld world) =>
        FeedCredentialFingerprint.Of((await world.Credentials.GetCredentialsAsync(TestContext.Current.CancellationToken))!);

    // ---- the four gates ----

    [Fact]
    public async Task With_the_deployment_flag_off_nothing_is_read_or_started()
    {
        var world = new OptionBackfillSupervisorWorld(deployment: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Targets.Loads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldBeEmpty();
        world.LastState.ShouldBe(DepthStates.Disabled);
        world.Heartbeats.Reports[^1].Details.DeploymentEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_stored_switch_row_the_backfill_is_off()
    {
        var world = new OptionBackfillSupervisorWorld(switchOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    [Fact]
    public async Task The_service_starts_paused_and_a_paused_service_keeps_the_backfill_off()
    {
        OptionBackfillService.Definition.Defaults.Enabled.ShouldBeFalse();
        var world = new OptionBackfillSupervisorWorld(serviceOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    [Fact]
    public async Task Without_the_owners_acceptance_of_the_probe_result_it_refuses_to_run_with_the_other_three_gates_open()
    {
        var world = new OptionBackfillSupervisorWorld(accepted: false);

        await world.TickAsync();
        world.SetClock(At(12, 20, 1));
        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldBeEmpty();
        world.LastState.ShouldBe(BackfillStates.ProbeNotAccepted);

        world.Ack.Accept();
        world.SetClock(At(12, 20, 2));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task Outside_the_night_window_nothing_starts_and_nothing_is_read()
    {
        var world = new OptionBackfillSupervisorWorld(10, 0);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(BackfillStates.OutsideWindow);
    }

    [Theory]
    [InlineData(16, 29, 0)]
    [InlineData(16, 30, 1)]
    [InlineData(0, 30, 1)]
    [InlineData(8, 29, 1)]
    [InlineData(8, 30, 0)]
    public async Task The_window_boundaries_are_1630_and_0830_every_day_including_a_sunday(int hour, int minute, int starts)
    {
        var world = new OptionBackfillSupervisorWorld(hour, minute);
        world.SetClock(new DateTimeOffset(2026, 10, 18, hour, minute, 0, Ist)); // a Sunday

        await world.TickAsync();

        world.Session.Starts.ShouldBe(starts);
    }

    [Fact]
    public async Task A_gate_that_cannot_be_read_is_a_closed_gate_and_stops_a_running_session()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        world.Switch.Fail = true;
        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.Paused);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    [Fact]
    public async Task An_acceptance_that_cannot_be_read_is_a_closed_gate_too()
    {
        var world = new OptionBackfillSupervisorWorld();
        world.Ack.Fail = true;

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.Paused);
    }

    [Fact]
    public async Task Turning_the_switch_off_stops_the_session_and_closes_the_run()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();

        world.Switch.Set(false);
        world.SetClock(At(12, 20, 1));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.LastState.ShouldBe(DepthStates.SwitchOff);
    }

    [Fact]
    public async Task The_end_of_the_night_window_stops_the_session()
    {
        var world = new OptionBackfillSupervisorWorld(8, 0);
        world.SetClock(new DateTimeOffset(2026, 10, 13, 8, 0, 0, Ist));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        world.SetClock(new DateTimeOffset(2026, 10, 13, 8, 30, 0, Ist));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionBackfillService.WindowEnded);
        world.LastState.ShouldBe(BackfillStates.OutsideWindow);
    }

    // ---- the start ----

    [Fact]
    public async Task With_all_gates_open_the_session_starts_with_the_loaded_targets_and_one_run_record()
    {
        var world = new OptionBackfillSupervisorWorld();

        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);
        world.Session.LastTargets!.Select(t => t.Symbol).ShouldBe(["NSE:NIFTY", "NSE:BANKNIFTY", "NSE:RELIANCE"]);
        world.Credentials.Reads.ShouldBe(1);
        world.LastState.ShouldBe(BackfillStates.Backfilling);
    }

    [Fact]
    public async Task With_nothing_to_plan_it_skips_once_for_the_day_and_never_reads_credentials()
    {
        var world = new OptionBackfillSupervisorWorld();
        world.Targets.Targets.Clear();

        await world.TickAsync();
        world.SetClock(At(12, 20, 1));
        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
        world.LastState.ShouldBe(DepthStates.NoInstruments);
    }

    [Fact]
    public async Task Without_a_token_it_skips_once_and_waits()
    {
        var world = new OptionBackfillSupervisorWorld();
        world.Credentials.Token = null;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionBackfillSupervisor.NoTokenMessage);
        world.LastState.ShouldBe(DepthStates.NoToken);
    }

    // ---- ends ----

    [Fact]
    public async Task A_completed_backfill_is_not_started_again_the_same_day_and_the_heartbeat_says_done()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(BackfillSessionEnd.Completed);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.SetClock(At(12, 20, 2));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.LastState.ShouldBe(BackfillStates.Done);
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_and_waits_for_a_changed_token()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(BackfillSessionEnd.TokenRefused);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionBackfillSupervisor.TokenRefusedMessage);
        world.SetClock(At(12, 20, 3));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.TokenRefused);

        world.Credentials.Token = "a-new-token";
        world.SetClock(At(12, 20, 5));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
        (await FingerprintAsync(world)).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Not_subscribed_fails_the_run_and_ends_the_day()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(BackfillSessionEnd.NotSubscribed);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.Credentials.Token = "a-new-token";
        world.SetClock(At(12, 21, 0));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionBackfillSupervisor.NotSubscribedMessage);
        world.Session.Starts.ShouldBe(1, "the day is over for the backfill, whatever the token");
        world.LastState.ShouldBe(DepthStates.NotSubscribed);
    }

    [Fact]
    public async Task The_daily_cap_ends_the_run_for_the_india_day_and_the_next_day_goes_on()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(BackfillSessionEnd.DailyCap);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.SetClock(At(12, 23, 0));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(BackfillStates.DailyCap);

        world.SetClock(At(13, 0, 5));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_stored_size_over_the_own_cap_stops_the_start_without_reading_credentials_and_stays_stopped()
    {
        var world = new OptionBackfillSupervisorWorld();
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;

        await world.TickAsync();
        world.Store.Stored = 0;
        world.SetClock(At(12, 20, 1));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(0, "it stays stopped until the owner resumes");
        world.Credentials.Reads.ShouldBe(0);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Skipped);
        world.LastState.ShouldBe(DepthStates.DiskBudget);

        world.Switch.Set(false); // the owner pauses, then resumes: the guard agrees now
        world.SetClock(At(12, 20, 2));
        await world.TickAsync();
        world.Switch.Set(true);
        world.SetClock(At(12, 20, 3));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task Free_space_under_the_floor_stops_the_start()
    {
        var world = new OptionBackfillSupervisorWorld();
        world.Store.Free = world.Settings.MinFreeBytes - 1;

        await world.TickAsync();

        world.Session.Starts.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(0);
        world.LastState.ShouldBe(DepthStates.DiskFree);
    }

    [Fact]
    public async Task A_disk_stop_inside_the_session_fails_the_run_and_latches()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(BackfillSessionEnd.DiskBudget);
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.SetClock(At(12, 20, 2));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Message.ShouldBe(OptionBackfillSupervisor.DiskBudgetMessage);
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(DepthStates.DiskBudget);
    }

    [Fact]
    public async Task All_gates_open_freeze_the_ranked_shares_once_and_a_closed_gate_never_does()
    {
        var closed = new OptionBackfillSupervisorWorld(accepted: false);
        await closed.TickAsync();
        closed.Targets.Freezes.ShouldBe(0);

        var open = new OptionBackfillSupervisorWorld();
        await open.TickAsync();
        open.Targets.Freezes.ShouldBe(1);
    }

    [Theory]
    [InlineData("StoreFailed", 5, true)]
    [InlineData("RateLimited", 15, true)]
    [InlineData("TooManyGivenUp", 0, false)]
    [InlineData("BadRequest", 0, false)]
    public async Task A_breaker_end_fails_the_run_and_waits_as_the_adr_says(string end, int waitMinutes, bool resumesTonight)
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(Enum.Parse<BackfillSessionEnd>(end));
        await UntilEndedAsync(world.Supervisor.SessionTask);

        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Failed);
        world.SetClock(At(12, 20, 1).AddMinutes(Math.Max(waitMinutes - 1, 1)));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1, "still waiting");

        world.SetClock(At(12, 21, 30));
        world.Credentials.Token = "same-or-new";
        await world.TickAsync();
        world.Session.Starts.ShouldBe(resumesTonight ? 2 : 1);
    }

    [Theory]
    [InlineData("TooManyGivenUp")]
    [InlineData("BadRequest")]
    [InlineData("NotSubscribed")]
    [InlineData("Completed")]
    public async Task An_end_for_the_night_holds_after_midnight_and_the_next_night_starts_again(string end)
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();
        world.Session.Finish(Enum.Parse<BackfillSessionEnd>(end));
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 20, 1));
        await world.TickAsync();
        world.Credentials.Token = "a-new-token";

        world.SetClock(At(13, 0, 30));
        await world.TickAsync();
        world.SetClock(At(13, 8, 29));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1, "the window crosses midnight but the night is the same");

        world.SetClock(At(13, 16, 30));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task The_heartbeat_carries_the_progress_counts_and_the_rate_and_nothing_else()
    {
        var world = new OptionBackfillSupervisorWorld();
        await world.TickAsync();

        var details = world.Heartbeats.Reports[^1].Details;

        details.RequestsPerSecond.ShouldBe(1);
        details.Progress!.Planned.ShouldBe(Keys(world.Settings, Nifty, BankNifty));
        (details.Progress.Done, details.Progress.GivenUp, details.Progress.Left).ShouldBe((0, 0, details.Progress.Planned));
    }

    [Fact]
    public void The_service_is_a_continuous_window_checked_in_code_run_by_xd_feed_and_the_heartbeat_row_is_its_own()
    {
        OptionBackfillService.Definition.RunsAs.ShouldBe("xd-feed");
        OptionBackfillService.Definition.Id.ShouldBe("options-backfill");
        OptionBackfillService.Definition.Defaults.ShouldSatisfyAllConditions(d => d.Enabled.ShouldBeFalse(), d => d.Retries.ShouldBe(0), d => d.NotifyOnFailure.ShouldBeTrue(), d => d.CatchUp.ShouldBeFalse());
        IOptionBackfillHeartbeats.ServiceName.ShouldBe("xd-backfill");
        IOptionBackfillHeartbeats.ServiceName.ShouldNotBe("xd-options");
    }
}
