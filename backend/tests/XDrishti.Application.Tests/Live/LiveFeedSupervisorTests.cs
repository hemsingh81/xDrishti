using XDrishti.Application.Live;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Live.SupervisorWorld;

namespace XDrishti.Application.Tests.Live;

/// <summary>The feed's own window and run owner (ADR 0024 §1, P2-10 criterion 15): one run per window, its end reason, the heartbeat at every look.</summary>
public sealed class LiveFeedSupervisorTests
{
    // ---- the window ----

    [Fact]
    public async Task Before_the_window_nothing_runs_but_the_heartbeat_says_closed()
    {
        var world = new SupervisorWorld();

        await world.TickAsync();

        world.Runs.ShouldBeEmpty();
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(FeedStates.Closed);
        world.Heartbeats.Reports[0].Instance.ShouldBe("test");
    }

    [Fact]
    public async Task At_the_start_of_the_window_one_scheduled_run_opens_and_the_session_starts()
    {
        var world = new SupervisorWorld();
        world.SetClock(At(12, 9, 0));

        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Running);
        run.Trigger.ShouldBe(ServiceRunTrigger.Scheduled);
        world.Session.Starts.ShouldBe(1);
        world.LastState.ShouldBe(FeedStates.Connected); // the state of the session
    }

    [Fact]
    public async Task Later_looks_in_the_window_open_no_second_run()
    {
        var world = new SupervisorWorld(9, 0);
        await world.TickAsync();

        world.SetClock(At(12, 9, 0, 15));
        await world.TickAsync();
        world.SetClock(At(12, 12, 0));
        await world.TickAsync();

        world.Runs.Count.ShouldBe(1);
        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task At_the_end_of_the_window_the_session_is_stopped_and_the_run_closes_as_succeeded_with_the_reason()
    {
        var world = new SupervisorWorld(9, 0);
        await world.TickAsync();

        world.SetClock(At(12, 15, 35));
        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Succeeded);
        run.Message.ShouldBe("Window ended");
        run.FinishedAt.ShouldBe(At(12, 15, 35));
        world.Session.Running.ShouldBeFalse();
        world.LastState.ShouldBe(FeedStates.Closed);
    }

    [Fact]
    public async Task A_weekend_has_no_window()
    {
        var world = new SupervisorWorld(10, 0, day: 17); // Saturday

        await world.TickAsync();

        world.Runs.ShouldBeEmpty();
        world.LastState.ShouldBe(FeedStates.Closed);
    }

    [Fact]
    public async Task The_next_trading_day_opens_a_new_run()
    {
        var world = new SupervisorWorld(9, 0);
        await world.TickAsync();
        world.SetClock(At(12, 15, 36));
        await world.TickAsync();

        world.SetClock(At(13, 9, 0));
        await world.TickAsync();

        world.Runs.Count.ShouldBe(2);
        world.Runs[1].Status.ShouldBe(ServiceRunStatus.Running);
    }

    [Fact]
    public async Task An_edited_window_counts_at_the_next_look()
    {
        var world = new SupervisorWorld(9, 30);
        world.Data.Store(LiveFeedService.Id, LiveFeedService.Definition.Defaults with { Schedule = Schedule.Window(new TimeOnly(10, 0), new TimeOnly(15, 35)) });

        await world.TickAsync();
        world.Runs.ShouldBeEmpty();

        world.SetClock(At(12, 10, 0));
        await world.TickAsync();
        world.Runs.Count.ShouldBe(1);
    }

    // ---- pause and manual ----

    [Fact]
    public async Task Pausing_in_the_window_stops_the_session_and_closes_the_run_as_paused()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();

        world.Data.Store(LiveFeedService.Id, LiveFeedService.Definition.Defaults with { Enabled = false });
        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Succeeded);
        run.Message.ShouldBe("Paused");
        world.LastState.ShouldBe(FeedStates.Paused);
    }

    [Fact]
    public async Task A_paused_service_does_not_start_in_its_window()
    {
        var world = new SupervisorWorld(10, 0);
        world.Data.Store(LiveFeedService.Id, LiveFeedService.Definition.Defaults with { Enabled = false });

        await world.TickAsync();

        world.Runs.ShouldBeEmpty();
        world.LastState.ShouldBe(FeedStates.Paused);
    }

    [Fact]
    public async Task A_manual_marker_in_force_opens_a_manual_run_outside_the_window_and_it_ends_with_the_marker()
    {
        var world = new SupervisorWorld(18, 0);
        world.Manual.Until = At(12, 19, 0);

        await world.TickAsync();
        var run = world.Runs.ShouldHaveSingleItem();
        run.Trigger.ShouldBe(ServiceRunTrigger.Manual);

        world.SetClock(At(12, 19, 0, 1));
        await world.TickAsync();
        run.Status.ShouldBe(ServiceRunStatus.Succeeded);
        run.Message.ShouldBe("Window ended");
    }

    // ---- the token ----

    [Fact]
    public async Task Without_a_usable_token_one_skipped_run_is_recorded_for_the_day_and_nothing_connects()
    {
        var world = new SupervisorWorld(9, 0);
        world.Credentials.Token = null;

        await world.TickAsync();
        world.SetClock(At(12, 9, 1));
        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Skipped);
        run.Message.ShouldBe("No usable token");
        world.Session.Starts.ShouldBe(0);
        world.LastState.ShouldBe(FeedStates.NoToken);
    }

    [Fact]
    public async Task When_the_token_arrives_later_in_the_window_the_feed_starts()
    {
        var world = new SupervisorWorld(9, 0);
        world.Credentials.Token = null;
        await world.TickAsync();

        world.Credentials.Token = "token-one";
        world.SetClock(At(12, 9, 30));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
        world.Runs[^1].Status.ShouldBe(ServiceRunStatus.Running);
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_and_nothing_is_tried_until_the_token_changes()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();

        world.Session.Finish(FeedSessionEnd.TokenRefused, FeedCredentialFingerprint.Of(await TokenAsync(world)));
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 0, 15));
        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Failed);
        run.Message.ShouldStartWith("Token refused");
        world.LastState.ShouldBe(FeedStates.TokenRefused);

        // the same token, looked at once a minute: still nothing
        world.SetClock(At(12, 10, 2));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        // a new token: the next look (at most a minute later) starts a new run
        world.Credentials.Token = "token-two";
        world.SetClock(At(12, 10, 4));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
        world.Runs.Count.ShouldBe(2);
        world.Runs[1].Status.ShouldBe(ServiceRunStatus.Running);
    }

    [Fact]
    public async Task The_credentials_are_looked_at_only_once_a_minute_while_a_refused_token_is_waited_out()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();
        world.Session.Finish(FeedSessionEnd.TokenRefused, FeedCredentialFingerprint.Of(await TokenAsync(world)));
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 0, 15));
        await world.TickAsync(); // closes the run; the token is not looked at again before a minute has passed

        world.Credentials.Token = "token-two";
        world.SetClock(At(12, 10, 0, 30));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_window_starts_with_a_fresh_attempt_even_with_the_old_token()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();
        world.Session.Finish(FeedSessionEnd.TokenRefused, FeedCredentialFingerprint.Of(await TokenAsync(world)));
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 0, 15));
        await world.TickAsync();
        world.SetClock(At(12, 16, 0));
        await world.TickAsync(); // outside the window: every wait is over

        world.SetClock(At(13, 9, 0));
        await world.TickAsync();

        world.Session.Starts.ShouldBe(2);
    }

    // ---- failures ----

    [Fact]
    public async Task Flapping_fails_the_run_and_pauses_new_attempts_for_five_minutes()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();

        world.Session.Finish(FeedSessionEnd.Flapping);
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 0, 15));
        await world.TickAsync();

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Failed);
        run.Message.ShouldStartWith("Flapping");
        world.LastState.ShouldBe(FeedStates.Flapping);

        world.SetClock(At(12, 10, 4));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);

        world.SetClock(At(12, 10, 5, 16));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
        world.Runs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_table_of_live_quotes_is_emptied_once_per_window_not_once_per_session()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();
        world.Quotes.Clears.ShouldBe(1);

        world.Session.Finish(FeedSessionEnd.Flapping);
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 0, 15));
        await world.TickAsync();
        world.SetClock(At(12, 10, 5, 16));
        await world.TickAsync(); // a second run, the same day
        world.Session.Crash();
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 6));
        await world.TickAsync();
        world.SetClock(At(12, 10, 7));
        await world.TickAsync(); // a third

        world.Session.Starts.ShouldBe(3);
        world.Quotes.Clears.ShouldBe(1);

        world.Session.Finish(FeedSessionEnd.Stopped);
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 18, 0));
        await world.TickAsync();
        world.SetClock(At(13, 9, 1)); // the next day's window
        await world.TickAsync();

        world.Quotes.Clears.ShouldBe(2);
    }

    [Fact]
    public async Task A_heartbeat_goes_out_at_every_look_even_when_the_look_fails()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();
        var before = world.Heartbeats.Reports.Count;

        world.Data.OnList = () => throw new InvalidOperationException("database unavailable");
        await world.TickAsync();

        world.Heartbeats.Reports.Count.ShouldBe(before + 1);
        world.LastState.ShouldBe(FeedStates.Connected); // the state of the session that is up
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);
        world.Data.OnList = null;
        await world.TickAsync(); // and the next look works
        world.Heartbeats.Reports.Count.ShouldBe(before + 2);
    }

    [Fact]
    public async Task A_failing_first_look_still_reports_a_heartbeat_with_a_neutral_state()
    {
        var world = new SupervisorWorld(10, 0);
        world.Data.OnList = () => throw new InvalidOperationException("database unavailable");

        await world.TickAsync();

        world.LastState.ShouldBe(FeedStates.Closed);
    }

    [Fact]
    public async Task A_session_that_does_not_end_when_asked_keeps_its_run_open_and_no_second_session_starts_until_it_has_ended()
    {
        var world = new SupervisorWorld(10, 0);
        world.Session.IgnoreCancel = true;
        await world.TickAsync();
        world.Data.Store(LiveFeedService.Id, LiveFeedService.Definition.Defaults with { Enabled = false });

        var pausing = world.TickAsync();
        await world.Clock.WhenWaitingForAsync(LiveFeedSupervisor.StopWait);
        world.Clock.Advance(LiveFeedSupervisor.StopWait);
        await pausing;

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);
        world.Data.Store(LiveFeedService.Id, LiveFeedService.Definition.Defaults); // resumed while the old connection still hangs
        await world.TickAsync();
        world.Session.Starts.ShouldBe(1);
        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Running);

        world.Session.Finish(FeedSessionEnd.Stopped);
        await UntilEndedAsync(world.Supervisor.SessionTask);
        await world.TickAsync();

        world.Runs[0].Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.Runs[0].Message.ShouldBe("Paused"); // the reason it was asked to stop
        world.Runs.Count.ShouldBe(2); // the window is on again: a new run and session
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_crashed_session_fails_the_run_and_is_started_again_after_a_short_pause()
    {
        var world = new SupervisorWorld(10, 0);
        await world.TickAsync();

        world.Session.Crash();
        await UntilEndedAsync(world.Supervisor.SessionTask);
        world.SetClock(At(12, 10, 0, 15));
        await world.TickAsync();

        world.Runs.ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Failed);
        world.Runs[0].Message!.ShouldContain("unexpectedly");

        world.SetClock(At(12, 10, 1));
        await world.TickAsync();
        world.Session.Starts.ShouldBe(2);
    }

    [Fact]
    public async Task A_run_left_open_by_the_previous_process_is_closed_as_interrupted_at_the_first_look()
    {
        var world = new SupervisorWorld(10, 0);
        var left = ServiceRun.Start(LiveFeedService.Id, ServiceRunTrigger.Scheduled, 1, At(12, 9, 0));
        await world.Data.TryStartAsync(left, TimeSpan.FromHours(8), TestContext.Current.CancellationToken);

        await world.TickAsync();

        left.Status.ShouldBe(ServiceRunStatus.Failed);
        left.Message.ShouldStartWith("Interrupted");
        world.Runs.Count.ShouldBe(2); // and the window, still on, has a new run
        world.Runs[1].Status.ShouldBe(ServiceRunStatus.Running);
    }

    [Fact]
    public async Task A_failure_of_a_look_is_logged_and_the_next_look_goes_on()
    {
        var world = new SupervisorWorld(10, 0);
        world.Data.OnList = () => throw new InvalidOperationException("database down");

        await world.TickAsync();
        world.Data.OnList = null;
        await world.TickAsync();

        world.Log.Events.ShouldContain(e => e.Event == 4119);
        world.Runs.Count.ShouldBe(1);
    }

    // ---- heartbeat and shutdown ----

    [Fact]
    public async Task Every_look_sends_a_heartbeat_in_and_out_of_the_window_with_the_figures()
    {
        var world = new SupervisorWorld(8, 0);
        world.Metrics.SetConnected(true);
        world.Metrics.SetSubscribed(7);

        await world.TickAsync();
        world.SetClock(At(12, 8, 0, 15));
        await world.TickAsync();
        world.SetClock(At(12, 9, 0));
        await world.TickAsync();

        world.Heartbeats.Reports.Count.ShouldBe(3);
        world.Heartbeats.Reports.Select(r => r.State).ShouldBe([FeedStates.Closed, FeedStates.Closed, FeedStates.Connected]);
        world.Heartbeats.Reports[0].Details.Subscribed.ShouldBe(7);
    }

    [Fact]
    public async Task When_the_process_stops_the_session_ends_and_the_run_closes_as_stopped()
    {
        var world = new SupervisorWorld(10, 0);
        using var stop = new CancellationTokenSource();
        var running = world.Supervisor.RunAsync(stop.Token);
        await FeedWorld.UntilAsync(() => world.Runs.Count == 1);

        await stop.CancelAsync();
        await running;

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Succeeded);
        run.Message.ShouldBe("Stopped");
        world.Session.Running.ShouldBeFalse();
    }

    [Fact]
    public async Task A_session_that_ended_by_itself_keeps_its_failure_when_the_process_stops_before_the_next_look()
    {
        var world = new SupervisorWorld(10, 0);
        using var stop = new CancellationTokenSource();
        var running = world.Supervisor.RunAsync(stop.Token);
        await FeedWorld.UntilAsync(() => world.Runs.Count == 1);

        world.Session.Finish(FeedSessionEnd.TokenRefused, "fingerprint");
        await UntilEndedAsync(world.Supervisor.SessionTask);
        await stop.CancelAsync();
        await running;

        var run = world.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Failed);
        run.Message.ShouldStartWith("Token refused");
    }

    [Fact]
    public async Task When_the_process_stops_the_last_heartbeat_says_stopped()
    {
        var world = new SupervisorWorld(10, 0);
        using var stop = new CancellationTokenSource();
        var running = world.Supervisor.RunAsync(stop.Token);
        await FeedWorld.UntilAsync(() => world.Runs.Count == 1);

        await stop.CancelAsync();
        await running;

        world.LastState.ShouldBe(FeedStates.Stopped);
        FeedStates.Stopped.ShouldBe("stopped");
    }

    [Fact]
    public async Task A_process_that_stops_outside_the_window_also_says_stopped()
    {
        var world = new SupervisorWorld(8, 0);
        using var stop = new CancellationTokenSource();
        var running = world.Supervisor.RunAsync(stop.Token);
        await FeedWorld.UntilAsync(() => world.Heartbeats.Reports.Count == 1);

        await stop.CancelAsync();
        await running;

        world.Runs.ShouldBeEmpty();
        world.Heartbeats.Reports.Select(r => r.State).ShouldBe([FeedStates.Closed, FeedStates.Stopped]);
    }

    private static async Task<XDrishti.Application.Abstractions.Market.Bars.DataAccountCredentials> TokenAsync(SupervisorWorld world) =>
        (await world.Credentials.GetCredentialsAsync(TestContext.Current.CancellationToken))!;
}
